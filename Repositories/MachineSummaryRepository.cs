using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Repositories
{
    public class MachineSummaryRepository
    {
        private readonly string _conn;

        public MachineSummaryRepository(IConfiguration cfg)
        {
            _conn = cfg["DBConnection"]!;
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  EEE mode — one table per process: [process]BMMaintenance
        //  Logic mirrors NewLoadColor(): isMCD = Yes AND HaveAction = No,
        //  pushed entirely into SQL using EXISTS / NOT EXISTS.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<List<string>> GetMachinesAsync(
            string process, string location, DateTime from, DateTime to)
        {
            // Table name is safe: only called with values from MachineSummaryConfig.EeeProcessOrder
            var sql = $@"
                SELECT DISTINCT a.Mcno
                FROM [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
                WHERE EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] mcd
                    WHERE REPLACE(mcd.Devicename,' ','') = REPLACE(a.Devicename,' ','')
                      AND mcd.IsActive = 1
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard] ac
                    WHERE ac.bmID    = a.bmID
                      AND ac.machineno = a.Mcno
                )
                AND  a.timeofrequest BETWEEN @Range1 AND @Range2
                AND  a.Pmcategoryid = '1'
                AND  a.BMStatusID   NOT IN (30, 31)
                AND  a.bmcaseid     <> 0
                AND  a.Location     = @Location
                ORDER BY a.Mcno";

            return await QueryMachinesAsync(sql, process, location, from, to, mcnoColumn: "Mcno");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  TE mode — single table teBMMaintenance, filtered by a.process column.
        // ─────────────────────────────────────────────────────────────────────────
        public async Task<List<string>> GetMachinesTEAsync(
            string process, string location, DateTime from, DateTime to)
        {
            const string sql = @"
                SELECT DISTINCT a.TesterNo
                FROM [eMaintenanceSystemDB].[dbo].[teBMMaintenance] a
                WHERE EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] mcd
                    WHERE REPLACE(mcd.Devicename,' ','') = REPLACE(a.Devicename,' ','')
                      AND mcd.IsActive = 1
                )
                AND NOT EXISTS (
                    SELECT 1
                    FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] ac
                    WHERE ac.bmID      = a.bmID
                      AND ac.machineno = a.TesterNo
                )
                AND  a.timeofrequest BETWEEN @Range1 AND @Range2
                AND  a.process       = @Process
                AND (a.Pmcategoryid  = '1' OR a.Pmcategoryid = '4')
                AND  a.BMStatusID    NOT IN (30, 31, 1, 2, 3, 7, 17)
                AND  a.bmunitid      <> 0
                AND  a.Location      = @Location
                ORDER BY a.TesterNo";

            return await QueryMachinesAsync(sql, process, location, from, to, mcnoColumn: "TesterNo");
        }

        // ─────────────────────────────────────────────────────────────────────────
        //  Shared ADO.NET execution
        // ─────────────────────────────────────────────────────────────────────────
        private async Task<List<string>> QueryMachinesAsync(
            string sql, string process, string location,
            DateTime from, DateTime to, string mcnoColumn)
        {
            var result = new List<string>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn) { CommandType = CommandType.Text };
            cmd.Parameters.AddWithValue("@Range1", from.ToString("MM/dd/yyyy HH:mm:ss"));
            cmd.Parameters.AddWithValue("@Range2", to.ToString("MM/dd/yyyy HH:mm:ss"));
            cmd.Parameters.AddWithValue("@Location", location);
            if (sql.Contains("@Process"))
                cmd.Parameters.AddWithValue("@Process", process);

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                var val = rdr[mcnoColumn]?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(val))
                    result.Add(val);
            }
            return result;
        }
    }
}