using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories;

/// <summary>
/// Fetches raw BM rows for one process+location combination.
/// Query logic mirrors Form12.vb → LoadMCName().
/// </summary>
public class BmAnalyticsRepository
{
    private readonly string _connStr;

    public BmAnalyticsRepository(IConfiguration cfg) =>
        _connStr = cfg.GetConnectionString("DBConnection")!;

    // ------------------------------------------------------------------
    // EEE query  (per-process table e.g. DCBMMaintenance + BMCase)
    // ------------------------------------------------------------------
    private const string EeeSql = @"
        SELECT
            a.Devicename,
            ISNULL((SELECT 'Yes'
                    FROM MachineKarte_MCD
                    WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','')
                      AND isactive = 1), 'No')          AS isMCD,
            a.process,
            a.location,
            a.Mcno                                      AS Mcno,
            FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
            b.BMCase                                    AS BMCase,
            a.bmid,
            ISNULL((SELECT 'Yes'
                    FROM MachineKarte_ActionCard
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
                END, 0)                                 AS BMContentID,
            (SELECT BMContent FROM BMContent
             WHERE BMContentID = a.BMContentID AND process = @Process) AS BMContent,
            c.mcstatus                                  AS MCStatus,
            'None'                                      AS FontColor
        FROM [eMaintenanceSystemDB].[dbo].[{0}BMMaintenance] a
        INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b
            ON a.BMCaseID = b.BMCaseID
        LEFT JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard] c
            ON a.bmID = c.bmID AND a.mcno = c.machineno
        WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
          AND  a.Pmcategoryid = '1'
          AND  a.BMStatusID NOT IN ('30','31')
          AND  a.bmcaseid <> 0
          AND  a.Location = @Location
        ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.BMCase";

    // ------------------------------------------------------------------
    // TE query  (teBMMaintenance + BMTEUnit + MachineKarte_ActionCard_TE)
    // ------------------------------------------------------------------
    private const string TeSql = @"
        SELECT
            a.Devicename,
            ISNULL((SELECT 'Yes'
                    FROM MachineKarte_MCD
                    WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','')
                      AND isactive = 1), 'No')          AS isMCD,
            a.process,
            a.location,
            a.testerNo                                  AS Mcno,
            FORMAT(a.Timeofrequest,'MM/dd/yyyy HH:mm:ss') AS Timeofrequest,
            b.bmteunit                                  AS BMCase,
            a.bmid,
            ISNULL((SELECT 'Yes'
                    FROM MachineKarte_ActionCard_TE
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
                END, 0)                                 AS BMContentID,
            (SELECT BMContent FROM BMContent
             WHERE BMContentID = a.BMContentID AND process = @Process) AS BMContent,
            c.mcstatus                                  AS MCStatus,
            'None'                                      AS FontColor
        FROM [eMaintenanceSystemDB].[dbo].[teBMMaintenance] a
        INNER JOIN [eMaintenanceSystemDB].[dbo].[BMTEUnit] b
            ON a.bmunitid = b.bmteunitid
        LEFT JOIN [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] c
            ON a.bmID = c.bmID AND a.testerno = c.machineno
        WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
          AND (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
          AND  a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
          AND  a.bmunitid <> 0
          AND  a.Location = @Location
          AND  a.process  = @Process
        ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.bmteunitid";

    // ------------------------------------------------------------------

    public async Task<List<BmAnalyticsRow>> GetRowsAsync(
        string process, string location, string mode,
        DateTime dateFrom, DateTime dateTo)
    {
        var rows = new List<BmAnalyticsRow>();

        string sql = mode == "TE"
            ? TeSql
            : string.Format(EeeSql, process);

        await using var conn = new SqlConnection(_connStr);
        await conn.OpenAsync();

        await using var cmd = new SqlCommand(sql, conn)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 60
        };
        cmd.Parameters.AddWithValue("@Range1", dateFrom.ToString("MM/dd/yyyy HH:mm:ss"));
        cmd.Parameters.AddWithValue("@Range2", dateTo.ToString("MM/dd/yyyy HH:mm:ss"));
        cmd.Parameters.AddWithValue("@Location", location);
        cmd.Parameters.AddWithValue("@Process", process);

        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            rows.Add(new BmAnalyticsRow
            {
                Devicename = rdr["Devicename"].ToString() ?? string.Empty,
                IsMCD = rdr["isMCD"].ToString() ?? "No",
                Process = rdr["process"].ToString() ?? string.Empty,
                Location = rdr["location"].ToString() ?? string.Empty,
                Mcno = rdr["Mcno"].ToString() ?? string.Empty,
                Timeofrequest = rdr["Timeofrequest"].ToString() ?? string.Empty,
                BMCase = rdr["BMCase"].ToString() ?? string.Empty,
                BmId = rdr["bmid"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["bmid"]),
                HaveAction = rdr["HaveAction"].ToString() ?? "No",
                BMContentID = rdr["BMContentID"] == DBNull.Value ? 0 : Convert.ToInt32(rdr["BMContentID"]),
                BMContent = rdr["BMContent"].ToString() ?? string.Empty,
                MCStatus = rdr["MCStatus"].ToString() ?? string.Empty,
            });
        }
        return rows;
    }
}