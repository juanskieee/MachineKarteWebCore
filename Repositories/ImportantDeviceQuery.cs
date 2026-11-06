using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

// Forms 7 and 11 share eligibility and ordering.
// Form 7 counts distinct machines.
// Form 11 counts BM rows.
internal static class ImportantDeviceQuery
{
    public static async Task<List<string>> GetMachinesAsync(
        string connectionString,
        string process,
        string location,
        DateTime from,
        DateTime to,
        bool te,
        bool distinct,
        CancellationToken cancellationToken)
    {
        var machines = await GetMachinesByLocationAsync(connectionString, process, [location],
            from, to, te, distinct, cancellationToken);
        return machines[location];
    }

    public static async Task<Dictionary<string, List<string>>> GetMachinesByLocationAsync(
        string connectionString,
        string process,
        string[] locations,
        DateTime from,
        DateTime to,
        bool te,
        bool distinct,
        CancellationToken cancellationToken)
    {
        var machines = locations.ToDictionary(location => location, _ => new List<string>(),
            StringComparer.OrdinalIgnoreCase);
        if (locations.Length == 0) return machines;

        string table = te
            ? "TEBMMaintenance"
            : $"{process}BMMaintenance";

        string machine = te
            ? "TesterNo"
            : "Mcno";

        string caseId = te
            ? "BMUnitID"
            : "BMCaseID";

        string processFilter = te
            ? "AND a.Process = @Process"
            : string.Empty;

        string statusFilter = te
            ? "a.BMStatusID NOT IN (30,31,1,2,3,7,17)"
            : "a.BMStatusID NOT IN (30,31)";

        string categoryFilter = te
            ? "a.PMCategoryID IN (1,4)"
            : "a.PMCategoryID = 1";

        string sql = $@"
;WITH FilteredMaintenance AS
(
    SELECT
        a.BMID,
        a.DeviceName,
        a.Location,
        a.{machine} AS MachineNo
    FROM dbo.{table} a
    WHERE
        a.TimeOfRequest >= @Range1
        AND a.TimeOfRequest < DATEADD(DAY,1,@Range2)
        AND a.Location IN ({string.Join(", ", locations.Select((_, i) => $"@Location{i}"))})
        AND {categoryFilter}
        AND {statusFilter}
        AND a.{caseId} <> 0
        {processFilter}
)
SELECT {(distinct ? "DISTINCT" : string.Empty)}
       fm.MachineNo, fm.Location
FROM FilteredMaintenance fm

WHERE EXISTS
(
    SELECT 1
    FROM dbo.MachineKarte_MCD mcd
    WHERE
        mcd.IsActive = 1
        AND REPLACE(mcd.DeviceName,' ','')
            = REPLACE(fm.DeviceName,' ','')
)

AND NOT EXISTS
(
    SELECT 1
    FROM dbo.{(te ? "MachineKarte_ActionCard_TE" : "MachineKarte_ActionCard")} ac
    WHERE
        ac.BMID = fm.BMID
        AND ac.MachineNo = fm.MachineNo
)

ORDER BY
    fm.MachineNo DESC;";

        await using var connection = new SqlConnection(connectionString);

        await using var command = new SqlCommand(sql, connection);

        RepositorySql.AddDateRange(command, from, to);
        for (var i = 0; i < locations.Length; i++)
            RepositorySql.AddVarChar(command, $"@Location{i}", locations[i], 20);

        if (te)
        {
            RepositorySql.AddVarChar(command, "@Process", process, 20);
        }

        await connection.OpenAsync(cancellationToken);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.GetString(0).Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                machines[reader.GetString(1).Trim()].Add(value);
            }
        }

        return machines;
    }
}
