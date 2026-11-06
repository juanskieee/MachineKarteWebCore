using System.Data;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

/// <summary>
/// Fetches raw BM rows for one process+location combination.
/// Query logic mirrors Form12.vb → LoadMCName().
/// </summary>
public class BmAnalyticsRepository
{
    private readonly string _connectionString;

    public BmAnalyticsRepository(IConfiguration configuration) =>
        _connectionString = RepositorySql.GetConnectionString(configuration);
    // EEE query  (per-process table e.g. DCBMMaintenance + BMCase)
    private const string EeeSql = @"
        SELECT
            a.Devicename,
            CASE WHEN EXISTS (SELECT 1
                    FROM MachineKarte_MCD
                    WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','')
                      AND isactive = 1) THEN 'Yes' ELSE 'No' END AS isMCD,
            a.process,
            a.location,
            a.Mcno                                      AS Mcno,
            CONVERT(char(10),a.Timeofrequest,101) + ' ' + CONVERT(char(8),a.Timeofrequest,108) AS Timeofrequest,
            b.BMCase                                    AS BMCase,
            a.bmid,
            CASE WHEN EXISTS (SELECT 1
                    FROM MachineKarte_ActionCard
                    WHERE bmid = a.bmID AND machineno = a.mcno) THEN 'Yes' ELSE 'No' END AS HaveAction,
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
        OUTER APPLY (SELECT TOP (1) ac.mcstatus FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard] ac
                     WHERE ac.bmID = a.bmID AND ac.machineno = a.mcno ORDER BY ac.Seqno DESC) c
        WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
          AND  a.Pmcategoryid = '1'
          AND  a.BMStatusID NOT IN ('30','31')
          AND  a.bmcaseid <> 0
          AND  a.Location = @Location
        ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.BMCase";
    // TE query  (teBMMaintenance + BMTEUnit + MachineKarte_ActionCard_TE)
    private const string TeSql = @"
        SELECT
            a.Devicename,
            CASE WHEN EXISTS (SELECT 1
                    FROM MachineKarte_MCD
                    WHERE REPLACE(Devicename,' ','') = REPLACE(a.devicename,' ','')
                      AND isactive = 1) THEN 'Yes' ELSE 'No' END AS isMCD,
            a.process,
            a.location,
            a.testerNo                                  AS Mcno,
            CONVERT(char(10),a.Timeofrequest,101) + ' ' + CONVERT(char(8),a.Timeofrequest,108) AS Timeofrequest,
            b.bmteunit                                  AS BMCase,
            a.bmid,
            CASE WHEN EXISTS (SELECT 1
                    FROM MachineKarte_ActionCard_TE
                    WHERE bmid = a.bmID AND machineno = a.testerno) THEN 'Yes' ELSE 'No' END AS HaveAction,
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
        OUTER APPLY (SELECT TOP (1) ac.mcstatus FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard_TE] ac
                     WHERE ac.bmID = a.bmID AND ac.machineno = a.testerno ORDER BY ac.Seqno DESC) c
        WHERE (a.timeofrequest BETWEEN @Range1 AND @Range2)
          AND (a.Pmcategoryid = '1' OR a.Pmcategoryid = '4')
          AND  a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
          AND  a.bmunitid <> 0
          AND  a.Location = @Location
          AND  a.process  = @Process
        ORDER BY a.mcno DESC, a.Timeofrequest DESC, b.bmteunitid";
    public async Task<List<BmAnalyticsRow>> GetRowsAsync(
        string process, string location, string mode,
        DateTime dateFrom, DateTime dateTo, CancellationToken cancellationToken = default)
    {
        if (!BmAnalyticsMappings.GetProcesses(mode).Contains(process, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));
        var rows = new List<BmAnalyticsRow>();

        string sql = mode == "TE"
            ? TeSql
            : string.Format(EeeSql, process);

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new SqlCommand(sql, conn)
        {
            CommandType = CommandType.Text,
            CommandTimeout = 60
        };
        RepositorySql.AddDateRange(cmd, dateFrom, dateTo);
        RepositorySql.AddVarChar(cmd, "@Location", location, 20);
        RepositorySql.AddVarChar(cmd, "@Process", process, 20);

        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
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
