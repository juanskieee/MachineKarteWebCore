using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Repositories
{
    public class McdMonitoringRepository
    {
        private readonly string _connStr;

        public McdMonitoringRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DBConnection")!;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Fetch raw BM rows for a single process + location combination.
        // Mirrors the two SQL branches in VB's LoadMCName() (TE vs standard).
        // ─────────────────────────────────────────────────────────────────────

        public async Task<List<McdBmRow>> GetBmRowsAsync(
            string process, string location, string dateFrom, string dateTo, bool isTe)
        {
            var rows = new List<McdBmRow>();

            string sql = isTe ? BuildTeSql(process) : BuildStandardSql(process);

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Range1", dateFrom);
            cmd.Parameters.AddWithValue("@Range2", dateTo);
            cmd.Parameters.AddWithValue("@Location", location);

            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                rows.Add(new McdBmRow
                {
                    DeviceName = rdr["Devicename"].ToString()!,
                    IsMcd = rdr["isMCD"].ToString()!,
                    Process = rdr["process"].ToString()!,
                    Location = rdr["location"].ToString()!,
                    Mcno = rdr["Mcno"].ToString()!,
                    TimeOfRequest = rdr["Timeofrequest"].ToString()!,
                    BmCase = rdr["BMCase"].ToString()!,
                    BmId = Convert.ToInt32(rdr["bmid"]),
                    HaveAction = rdr["HaveAction"].ToString()!,
                    BmContentId = Convert.ToInt32(rdr["BMContentID"]),
                    BmContent = rdr["BMContent"]?.ToString() ?? string.Empty,
                    McStatus = rdr["MCStatus"]?.ToString() ?? string.Empty,
                    FontColor = "None",
                    Color = string.Empty
                });
            }

            return rows;
        }

        // ─── TE SQL (teBMMaintenance) ────────────────────────────────────────

        private static string BuildTeSql(string process) => $@"
            SELECT a.Devicename,
                   ISNULL((SELECT 'Yes' FROM MachineKarte_MCD
                           WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','')
                             AND isactive = 1), 'No')   AS isMCD,
                   a.process,
                   a.location,
                   a.testerNo                           AS Mcno,
                   FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
                   b.bmteunit                           AS BMCase,
                   a.bmid,
                   ISNULL((SELECT 'Yes' FROM MachineKarte_ActionCard_TE
                           WHERE bmid = a.bmID AND machineno = a.testerno), 'No') AS HaveAction,
                   COALESCE(
                       CASE
                           WHEN a.AcredBMContentID <> a.BMContentID THEN
                               CASE
                                   WHEN a.AcredBMContentID = ''   THEN a.BMContentID
                                   WHEN a.AcredBMContentID = '0'  THEN a.BMContentID
                                   WHEN a.AcredBMContentID IS NULL THEN a.BMContentID
                                   ELSE a.AcredBMContentID
                               END
                           ELSE a.BMContentID
                       END, 0)                          AS BMContentID,
                   (SELECT BMContent FROM BMContent
                    WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
                   c.mcstatus                           AS MCStatus,
                   'None'                               AS FontColor
            FROM [eMaintenanceSystemDB].[dbo].[teBMMaintenance] a
            INNER JOIN [eMaintenanceSystemDB].[dbo].BMTEUnit b
                ON a.bmunitid = b.bmteunitid
            LEFT JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] c
                ON a.bmID = c.bmID AND a.testerno = c.machineno
            WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
              AND a.process = '{process}'
              AND (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
              AND (a.BMStatusID NOT IN ('30','31','1','2','3','7','17'))
              AND (a.bmunitid <> 0)
              AND (a.Location = @Location)
            ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.bmteunitid";

        // ─── Standard SQL ({process}BMMaintenance) ───────────────────────────

        private static string BuildStandardSql(string process) => $@"
            SELECT a.Devicename,
                   ISNULL((SELECT 'Yes' FROM MachineKarte_MCD
                           WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','')
                             AND isactive = 1), 'No')   AS isMCD,
                   a.process,
                   a.location,
                   a.Mcno                               AS Mcno,
                   FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
                   b.BMCase                             AS BMCase,
                   a.bmid,
                   ISNULL((SELECT 'Yes' FROM MachineKarte_ActionCard
                           WHERE bmid = a.bmID AND machineno = a.mcno), 'No') AS HaveAction,
                   COALESCE(
                       CASE
                           WHEN a.AcredBMContentID <> a.BMContentID THEN
                               CASE
                                   WHEN a.AcredBMContentID = ''   THEN a.BMContentID
                                   WHEN a.AcredBMContentID = '0'  THEN a.BMContentID
                                   WHEN a.AcredBMContentID IS NULL THEN a.BMContentID
                                   ELSE a.AcredBMContentID
                               END
                           ELSE a.BMContentID
                       END, 0)                          AS BMContentID,
                   (SELECT BMContent FROM BMContent
                    WHERE BMContentID = a.BMContentID AND process = '{process}') AS BMContent,
                   c.mcstatus                           AS MCStatus,
                   'None'                               AS FontColor
            FROM [eMaintenanceSystemDB].[dbo].[{process}BMMaintenance] a
            INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b
                ON a.BMCaseID = b.BMCaseID
            LEFT JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard] c
                ON a.bmID = c.bmID AND a.mcno = c.machineno
            WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
              AND (a.Pmcategoryid = '1')
              AND (a.BMStatusID NOT IN ('30','31'))
              AND (a.bmcaseid <> 0)
              AND (a.Location = @Location)
            ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.BMCase";
    }
}