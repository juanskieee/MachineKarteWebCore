using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Repositories
{
    public class MachineSummaryRepository
    {
        private readonly string _connStr;

        public MachineSummaryRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DBConnection")
                       ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        public async Task<List<string>> GetMachinesAsync(
            string process, string location, DateTime from, DateTime to, bool isTe)
        {
            if (!MachineSummaryConfig.IsValidProcess(process, isTe))
                throw new ArgumentException($"Unknown process '{process}'.", nameof(process));

            var sql = isTe ? TeSql : StandardSql(process);

            return await QueryMachinesAsync(
                sql, process, location, from, to,
                mcnoColumn: isTe ? "TesterNo" : "Mcno",
                includeProcessParameter: isTe);
        }

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
            ORDER BY a.Mcno DESC";

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
            ORDER BY a.TesterNo DESC";

        private async Task<List<string>> QueryMachinesAsync(
            string sql, string process, string location,
            DateTime from, DateTime to, string mcnoColumn, bool includeProcessParameter)
        {
            var result = new List<string>();

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn) { CommandType = CommandType.Text };
            cmd.Parameters.AddWithValue("@Range1",
                from.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@Range2",
                to.ToString("MM/dd/yyyy HH:mm:ss", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@Location", location);

            if (includeProcessParameter)
                cmd.Parameters.AddWithValue("@Process", process);

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var value = rdr[mcnoColumn]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(value))
                    result.Add(value);
            }

            return result;
        }
    }
}
