using System.Data;
using System.Globalization;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public sealed class PmReportRepository
{
    private static readonly IReadOnlyDictionary<string, PmReportTemplateDefinition> Definitions =
        new Dictionary<string, PmReportTemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["DC"] = new("DC", "DCTemplate2.xlsx", "DC", "ViewDCData_Ver4", true),
            ["DB"] = new("DB", "DBTemplate.xlsx", " Proposed Template", "ViewDBData_Ver4", true),
            ["WB"] = new("WB", "WBTemplate.xlsx", "wbnew 2", "ViewWBData_Ver4", true),
            ["MP"] = new("MP", "MPTemplate.xlsx", "MP", "ViewMPData_Ver4", true),
            ["PL"] = new("PL", "PLTemplate.xlsx", "PLForm_REV1.3.2", "ViewPLData_Ver2", true),
            ["FT"] = new("FT", "FTTemplate.xlsx", "FT TEMPLATE", "ViewFTData", true),
            ["FL"] = new("FL", "FLTemplate2.xlsx", "Proposed Template", "ViewFLData_V2", true),
            ["TP"] = new("TP", "TPTemplate.xlsx", "TP", "ViewTPData", true),
            ["SING"] = new("SING", "SINGTemplate.xlsx", "SING", "ViewSINGData", true),
            ["MEMS"] = new("MEMS", "MEMS FT-TPTemplate.xlsx", "MEMS", "ViewMEMSData", true),
            ["EDS"] = new("EDS", "EDSTemplate.xlsx", "EDS", "ViewEDSData", false)
        };

    private readonly string _connectionString;

    public PmReportRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DBConnection")
            ?? throw new InvalidOperationException("DBConnection is not configured.");
    }

    public static bool TryGetDefinition(string process, out PmReportTemplateDefinition definition)
        => Definitions.TryGetValue(process.Trim(), out definition!);

    public async Task<PmReportData?> GetReportDataAsync(
        string process,
        int bmId,
        string timeOfRequest,
        string machineNo,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetDefinition(process, out var definition))
            throw new ArgumentException($"Unsupported PM report process: {process}", nameof(process));

        var current = await QuerySingleAsync(
            definition.ViewName,
            "WHERE bmID = @bmId",
            command => command.Parameters.Add("@bmId", SqlDbType.Int).Value = bmId,
            cancellationToken);

        if (current is null)
            return null;

        var history = new List<PmReportRecord>();
        if (definition.HasHistory && !string.IsNullOrWhiteSpace(machineNo) && !string.IsNullOrWhiteSpace(timeOfRequest))
        {
            history = await QueryManyAsync(
                definition.ViewName,
                "WHERE mcno = @machineNo AND bmID <> @bmId AND TimeOfRequest < @timeOfRequest ORDER BY bmID DESC",
                command =>
                {
                    command.Parameters.Add("@machineNo", SqlDbType.NVarChar, 100).Value = machineNo;
                    command.Parameters.Add("@bmId", SqlDbType.Int).Value = bmId;
                    command.Parameters.Add("@timeOfRequest", SqlDbType.NVarChar, 50).Value = timeOfRequest;
                },
                2,
                cancellationToken);
        }

        return new PmReportData
        {
            Process = definition.Process,
            BmId = bmId,
            Current = current,
            History = history
        };
    }

    private async Task<PmReportRecord?> QuerySingleAsync(
        string viewName,
        string predicate,
        Action<SqlCommand> addParameters,
        CancellationToken cancellationToken)
    {
        var rows = await QueryManyAsync(viewName, predicate, addParameters, 1, cancellationToken);
        return rows.FirstOrDefault();
    }

    private async Task<List<PmReportRecord>> QueryManyAsync(
        string viewName,
        string predicate,
        Action<SqlCommand> addParameters,
        int limit,
        CancellationToken cancellationToken)
    {
        var records = new List<PmReportRecord>();
        var sql = $"SELECT TOP ({limit}) * FROM [{viewName}] {predicate}";

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        addParameters(command);

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new PmReportRecord();
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var value = reader.IsDBNull(ordinal)
                    ? string.Empty
                    : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
                record.Fields[reader.GetName(ordinal)] = value;
            }
            records.Add(record);
        }

        return records;
    }
}
