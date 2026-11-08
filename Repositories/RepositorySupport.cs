using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Repositories;

// Shared SQL plumbing only. Each repository owns its legacy filters and row ordering.
internal static class RepositorySql
{
    public static void AddVarChar(SqlCommand command, string name, string value, int size) =>
        command.Parameters.Add(name, SqlDbType.VarChar, Math.Max(size, value.Length)).Value = value;

    public static void AddDateRange(SqlCommand command, string from, string to,
        string fromParameter = "@Range1", string toParameter = "@Range2")
    {
        command.Parameters.Add(fromParameter, SqlDbType.DateTime).Value = DateTime.Parse(from, CultureInfo.InvariantCulture);
        command.Parameters.Add(toParameter, SqlDbType.DateTime).Value = DateTime.Parse(to, CultureInfo.InvariantCulture);
    }

    public static void AddDateRange(SqlCommand command, DateTime from, DateTime to)
    {
        // These callers previously sent HH:mm:ss strings, discarding fractional seconds.
        command.Parameters.Add("@Range1", SqlDbType.DateTime).Value = from.AddTicks(-(from.Ticks % TimeSpan.TicksPerSecond));
        command.Parameters.Add("@Range2", SqlDbType.DateTime).Value = to.AddTicks(-(to.Ticks % TimeSpan.TicksPerSecond));
    }

    public static HashSet<string> GetColumns(SqlDataReader reader) =>
        Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string ReadOptional(SqlDataReader reader, IReadOnlySet<string> columns, string name) =>
        columns.Contains(name) ? reader[name].ToString() ?? string.Empty : string.Empty;
}

// Forms 7 and 11 share eligibility and ordering.
// Form 7 counts distinct machines.
// Form 11 counts BM rows.
internal static class ImportantDeviceQuery
{
    public static async Task<List<string>> GetMachinesAsync(
        DatabaseConnectionResolver connections,
        string process,
        string location,
        DateTime from,
        DateTime to,
        bool te,
        bool distinct,
        CancellationToken cancellationToken)
    {
        var machines = await GetMachinesByLocationAsync(connections, process, [location],
            from, to, te, distinct, cancellationToken);
        return machines[location];
    }

    public static async Task<Dictionary<string, List<string>>> GetMachinesByLocationAsync(
        DatabaseConnectionResolver connections,
        string process,
        string[] locations,
        DateTime from,
        DateTime to,
        bool te,
        bool distinct,
        CancellationToken cancellationToken)
    {
        var machines = await GetMachinesByProcessAsync(connections,
            new Dictionary<string, string[]> { [process] = locations }, from, to, te, distinct, cancellationToken);
        return machines[process];
    }

    public static async Task<Dictionary<string, Dictionary<string, List<string>>>> GetMachinesByProcessAsync(
        DatabaseConnectionResolver connections, IReadOnlyDictionary<string, string[]> processLocations,
        DateTime from, DateTime to, bool te, bool distinct, CancellationToken cancellationToken)
    {
        var machines = processLocations.ToDictionary(entry => entry.Key,
            entry => entry.Value.ToDictionary(location => location, _ => new List<string>(),
                StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var queries = processLocations.Where(entry => entry.Value.Length > 0).ToArray();
        if (queries.Length == 0) return machines;

        string machine = te
            ? "TesterNo"
            : "Mcno";

        string caseId = te
            ? "BMUnitID"
            : "BMCaseID";

        string statusFilter = te
            ? "a.BMStatusID NOT IN (30,31,1,2,3,7,17)"
            : "a.BMStatusID NOT IN (30,31)";

        string categoryFilter = te
            ? "a.PMCategoryID IN (1,4)"
            : "a.PMCategoryID = 1";

        // Keep each table's legacy eligibility; TE shares one table across processes.
        string LocationFilter(int i) => $"a.Location IN ({string.Join(", ", queries[i].Value.Select((_, j) => $"@Location{i}_{j}"))})";
        string MaintenanceSelect(string table, string process, string filter) => $@"
    SELECT
        {process} AS Process,
        a.BMID,
        a.DeviceName,
        a.Location,
        a.{machine} AS MachineNo
    FROM dbo.{table} a
    WHERE
        a.TimeOfRequest >= @Range1
        AND a.TimeOfRequest < DATEADD(DAY,1,@Range2)
        AND ({filter})
        AND {categoryFilter}
        AND {statusFilter}
        AND a.{caseId} <> 0";

        string maintenance = te
            ? MaintenanceSelect("TEBMMaintenance", "a.Process", string.Join(" OR ", queries.Select((_, i) =>
                $"(a.Process = @Process{i} AND {LocationFilter(i)})")))
            : string.Join(" UNION ALL ", queries.Select((entry, i) =>
                MaintenanceSelect($"{entry.Key}BMMaintenance", $"@Process{i}", LocationFilter(i))));

        string sql = $@"
;WITH FilteredMaintenance AS ({maintenance})
SELECT {(distinct ? "DISTINCT" : string.Empty)}
       fm.MachineNo, fm.Location, fm.Process
FROM FilteredMaintenance fm

WHERE EXISTS
(
    SELECT 1
    FROM dbo.MachineKarte_MCD mcd
    WHERE
        mcd.IsActive = 1 AND REPLACE(mcd.DeviceName,' ','')
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

        await using var connection = await connections.OpenConnectionAsync(cancellationToken);

        await using var command = new SqlCommand(sql, connection);

        RepositorySql.AddDateRange(command, from, to);
        for (var i = 0; i < queries.Length; i++)
        {
            RepositorySql.AddVarChar(command, $"@Process{i}", queries[i].Key, 20);
            for (var j = 0; j < queries[i].Value.Length; j++)
                RepositorySql.AddVarChar(command, $"@Location{i}_{j}", queries[i].Value[j], 20);
        }


        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.GetString(0).Trim();

            if (!string.IsNullOrWhiteSpace(value))
            {
                machines[reader.GetString(2).Trim()][reader.GetString(1).Trim()].Add(value);
            }
        }

        return machines;
    }
}

internal static class MonitoringQueryBatch
{
    // Bound dashboard batches; interactive report generation does not use this queue.
    private const int BatchSize = 4;
    private static readonly SemaphoreSlim Slots = new(BatchSize);

    public static async IAsyncEnumerable<(T Item, TResult Result)> ReadAsync<T, TResult>(
        IEnumerable<T> items, Func<T, Task<TResult>> read,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Consume batches in input order to bound raw-row buffering.
        foreach (var batch in items.Chunk(BatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var results = await Task.WhenAll(batch.Select(item => RunAsync(() => read(item), cancellationToken)));
            for (var i = 0; i < batch.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return (batch[i], results[i]);
            }
        }
    }

    public static async Task<TResult> RunAsync<TResult>(
        Func<Task<TResult>> read, CancellationToken cancellationToken = default)
    {
        await Slots.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await read();
        }
        finally { Slots.Release(); }
    }
}
