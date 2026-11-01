using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Repositories
{
    /// <summary>
    /// Data access for Form7 (MCD Monitoring).
    ///
    /// Form7's active path (NewLoadColor → InputToLabel) counts a row when:
    ///     isMCD = "Yes" AND HaveAction = "No"
    /// Both conditions are pushed into SQL, and the returned machine numbers
    /// are distinct – the same effect as Form7's RemoveDuplicates().
    /// </summary>
    public class McdMonitoringRepository
    {
        private readonly string _connStr;

        public McdMonitoringRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DBConnection")
                       ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        public async Task<List<string>> GetMachinesAsync(
            string process, string location, DateTime dateFrom, DateTime dateTo, bool isTe)
        {
            ValidateProcess(process);

            string sql = isTe ? TeSql : StandardSql(process);

            return await QueryMachinesAsync(
                sql, process, location, dateFrom, dateTo,
                mcnoColumn: isTe ? "TesterNo" : "Mcno",
                includeProcessParameter: isTe);
        }

        // ─── Standard mode: [process]BMMaintenance ────────────────────────────

        private static string StandardSql(string process) => $@"
            SELECT DISTINCT a.Mcno
            FROM [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
            INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b
                ON a.BMCaseID = b.BMCaseID
            WHERE EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] mcd
                    WHERE REPLACE(mcd.Devicename, ' ', '') = REPLACE(a.Devicename, ' ', '')
                      AND mcd.IsActive = 1
                  )
              AND NOT EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard] ac
                    WHERE ac.bmID = a.bmID
                      AND ac.machineno = a.Mcno
                  )
              AND a.timeofrequest BETWEEN @Range1 AND @Range2
              AND a.Pmcategoryid = '1'
              AND a.BMStatusID NOT IN (30, 31)
              AND a.bmcaseid <> 0
              AND a.Location = @Location
            ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.BMCase";

        // ─── TE mode: teBMMaintenance ─────────────────────────────────────────

        private const string TeSql = @"
            SELECT DISTINCT a.TesterNo
            FROM [eMaintenanceSystemDB].[dbo].[teBMMaintenance] a
            INNER JOIN [eMaintenanceSystemDB].[dbo].[BMTEUnit] b
                ON a.bmunitid = b.bmteunitid
            WHERE EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] mcd
                    WHERE REPLACE(mcd.Devicename, ' ', '') = REPLACE(a.Devicename, ' ', '')
                      AND mcd.IsActive = 1
                  )
              AND NOT EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] ac
                    WHERE ac.bmID = a.bmID
                      AND ac.machineno = a.TesterNo
                  )
              AND a.timeofrequest BETWEEN @Range1 AND @Range2
              AND a.process = @Process
              AND (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
              AND a.BMStatusID NOT IN (30, 31, 1, 2, 3, 7, 17)
              AND a.bmunitid <> 0
              AND a.Location = @Location
            ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.bmteunitid";

        // ─── Shared ADO.NET execution ─────────────────────────────────────────

        private async Task<List<string>> QueryMachinesAsync(
            string sql, string process, string location,
            DateTime dateFrom, DateTime dateTo,
            string mcnoColumn, bool includeProcessParameter)
        {
            var result = new List<string>();

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Range1",
                dateFrom.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@Range2",
                dateTo.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@Location", location);

            if (includeProcessParameter)
                cmd.Parameters.AddWithValue("@Process", process);

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                string? value = rdr[mcnoColumn]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(value))
                    result.Add(value);
            }

            return result;
        }

        private static void ValidateProcess(string process)
        {
            if (!McdMonitoringConfig.EeeProcesses.Contains(process) &&
                !McdMonitoringConfig.TeProcesses.Contains(process))
            {
                throw new ArgumentException($"Unknown process '{process}'.", nameof(process));
            }
        }
    }
}
