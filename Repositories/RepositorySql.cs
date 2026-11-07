    using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

// Shared SQL plumbing only. Each repository owns its legacy filters and row ordering.
internal static class RepositorySql
{
    public static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString("DBConnection")
        ?? throw new InvalidOperationException("DBConnection not configured.");

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
