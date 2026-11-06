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
        ;WITH LatestAction AS
        (
            SELECT
                ac.bmID,
                ac.machineno,
                ac.mcstatus,
                ROW_NUMBER() OVER
                (
                    PARTITION BY ac.bmID, ac.machineno
                    ORDER BY ac.Seqno DESC
                ) AS rn
            FROM dbo.MachineKarte_ActionCard ac
        ),
        ActionExists AS
        (
            SELECT DISTINCT
                bmID,
                machineno
            FROM dbo.MachineKarte_ActionCard
        ),
        MCDDevices AS
        (
            SELECT DISTINCT
                REPLACE(Devicename,' ','') AS DevicenameKey
            FROM dbo.MachineKarte_MCD
            WHERE isactive = 1
        ),
        BMData AS
        (
            SELECT
                a.Devicename,
                a.process,
                a.location,
                a.Mcno,
                a.Timeofrequest,
                a.BMCaseID,
                a.bmid,
                a.AcredBMContentID,
                a.BMContentID
            FROM dbo.{0}BMMaintenance a
            WHERE
                a.Timeofrequest >= @Range1
                AND a.Timeofrequest < DATEADD(DAY,1,@Range2)
                AND a.Pmcategoryid = '1'
                AND a.BMStatusID NOT IN ('30','31')
                AND a.bmcaseid <> 0
                AND a.Location = @Location
        )
        SELECT
            a.Devicename,

            CASE
                WHEN mcd.DevicenameKey IS NOT NULL THEN 'Yes'
                ELSE 'No'
            END AS isMCD,

            a.process,
            a.location,
            a.Mcno,

            CONVERT(char(10),a.Timeofrequest,101)
            + ' '
            + CONVERT(char(8),a.Timeofrequest,108) AS Timeofrequest,

            b.BMCase,

            a.bmid,

            CASE
                WHEN ae.bmID IS NOT NULL THEN 'Yes'
                ELSE 'No'
            END AS HaveAction,

            COALESCE(
                CASE
                    WHEN a.AcredBMContentID <> a.BMContentID THEN
                        CASE
                            WHEN a.AcredBMContentID IS NULL THEN a.BMContentID
                            WHEN a.AcredBMContentID IN ('','0') THEN a.BMContentID
                            ELSE a.AcredBMContentID
                        END
                    ELSE a.BMContentID
                END,
                0
            ) AS BMContentID,

            bc.BMContent,

            la.mcstatus AS MCStatus,

            'None' AS FontColor

        FROM BMData a

        INNER JOIN dbo.BMCase b
            ON a.BMCaseID = b.BMCaseID

        LEFT JOIN ActionExists ae
            ON ae.bmID = a.bmID
           AND ae.machineno = a.Mcno

        LEFT JOIN LatestAction la
            ON la.bmID = a.bmID
           AND la.machineno = a.Mcno
           AND la.rn = 1

        LEFT JOIN dbo.BMContent bc
            ON bc.BMContentID = a.BMContentID
           AND bc.process = @Process

        LEFT JOIN MCDDevices mcd
            ON mcd.DevicenameKey = REPLACE(a.Devicename,' ','')

        ORDER BY
            a.Mcno DESC,
            a.Timeofrequest DESC,
            b.BMCase;";

    // TE query  (teBMMaintenance + BMTEUnit + MachineKarte_ActionCard_TE)
    private const string TeSql = @"
        ;WITH LatestAction AS
        (
            SELECT
                ac.bmID,
                ac.machineno,
                ac.mcstatus,
                ROW_NUMBER() OVER
                (
                    PARTITION BY ac.bmID, ac.machineno
                    ORDER BY ac.Seqno DESC
                ) AS rn
            FROM dbo.MachineKarte_ActionCard_TE ac
        ),
        ActionExists AS
        (
            SELECT DISTINCT
                bmID,
                machineno
            FROM dbo.MachineKarte_ActionCard_TE
        ),
        MCDDevices AS
        (
            SELECT DISTINCT
                REPLACE(Devicename,' ','') AS DevicenameKey
            FROM dbo.MachineKarte_MCD
            WHERE isactive = 1
        ),
        TEData AS
        (
            SELECT
                a.Devicename,
                a.process,
                a.location,
                a.testerNo,
                a.Timeofrequest,
                a.bmunitid,
                a.bmid,
                a.AcredBMContentID,
                a.BMContentID
            FROM dbo.teBMMaintenance a
            WHERE
                a.Timeofrequest >= @Range1
                AND a.Timeofrequest < DATEADD(DAY,1,@Range2)
                AND a.Pmcategoryid IN ('1','4')
                AND a.BMStatusID NOT IN ('30','31','1','2','3','7','17')
                AND a.bmunitid <> 0
                AND a.Location = @Location
                AND a.process = @Process
        )
        SELECT
            a.Devicename,

            CASE
                WHEN mcd.DevicenameKey IS NOT NULL THEN 'Yes'
                ELSE 'No'
            END AS isMCD,

            a.process,
            a.location,
            a.testerNo AS Mcno,

            CONVERT(char(10),a.Timeofrequest,101)
            + ' '
            + CONVERT(char(8),a.Timeofrequest,108) AS Timeofrequest,

            b.bmteunit AS BMCase,

            a.bmid,

            CASE
                WHEN ae.bmID IS NOT NULL THEN 'Yes'
                ELSE 'No'
            END AS HaveAction,

            COALESCE(
                CASE
                    WHEN a.AcredBMContentID <> a.BMContentID THEN
                        CASE
                            WHEN a.AcredBMContentID IS NULL THEN a.BMContentID
                            WHEN a.AcredBMContentID IN ('','0') THEN a.BMContentID
                            ELSE a.AcredBMContentID
                        END
                    ELSE a.BMContentID
                END,
                0
            ) AS BMContentID,

            bc.BMContent,

            la.mcstatus AS MCStatus,

            'None' AS FontColor

        FROM TEData a

        INNER JOIN dbo.BMTEUnit b
            ON a.bmunitid = b.bmteunitid

        LEFT JOIN ActionExists ae
            ON ae.bmID = a.bmID
           AND ae.machineno = a.testerNo

        LEFT JOIN LatestAction la
            ON la.bmID = a.bmID
           AND la.machineno = a.testerNo
           AND la.rn = 1

        LEFT JOIN dbo.BMContent bc
            ON bc.BMContentID = a.BMContentID
           AND bc.process = @Process

        LEFT JOIN MCDDevices mcd
            ON mcd.DevicenameKey = REPLACE(a.Devicename,' ','')

        ORDER BY
            a.testerNo DESC,
            a.Timeofrequest DESC,
            b.bmteunitid;";

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
