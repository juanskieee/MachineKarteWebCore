using System.Data;
using System.Globalization;
using MachineKarte.Reporting;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public sealed class PmReportRepository
{
    private static readonly IReadOnlyDictionary<string, PmReportTemplateDefinition> Definitions =
        new Dictionary<string, PmReportTemplateDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["DC"] = new("DC", "DCTemplate2.xlsx", "DC", "ViewDCData_Ver4", true),
            ["DB"] = new("DB", "DBTemplate.xlsx", "DB", "ViewDBData_Ver4", true, "EvidenceMaking"),
            ["WB"] = new("WB", "WBTemplate.xlsx", "Front Blank (2)", "ViewWBData_Ver4", true, "EvidenceMaking"),
            ["MP"] = new("MP", "MPTemplate.xlsx", "MP", "ViewMPData_Ver4", true, "EvidenceMaking"),
            ["PL"] = new("PL", "PLTemplate.xlsx", "PL", "ViewPLData_Ver2", true, "EvidenceMaking"),
            ["FT"] = new("FT", "FTTemplate.xlsx", "FT", "ViewFTData", true, "EvidenceMaking"),
            ["FL"] = new("FL", "FLTemplate2.xlsx", "FL", "ViewFLData_V2", true, "EvidenceMaking"),
            ["TP"] = new("TP", "TPTemplate.xlsx", "TP", "ViewTPData", true),
            ["SING"] = new("SING", "SINGTemplate.xlsx", "SING", "ViewSINGData", true),
            ["MEMS"] = new("MEMS", "MEMS FT-TPTemplate.xlsx", "MEMS", "ViewMEMSData", true),
            ["EDS"] = new("EDS", "EDSTemplate.xlsx", "EDS", "ViewEDSData", false)
        };

    private readonly string _connectionString;

    public PmReportRepository(IConfiguration configuration) =>
        _connectionString = RepositorySql.GetConnectionString(configuration);

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
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray();
        while (await reader.ReadAsync(cancellationToken))
        {
            var record = new PmReportRecord();
            for (var ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                var raw = reader.GetValue(ordinal);
                record.Values[columns[ordinal]] = raw;
                var value = raw is DBNull
                    ? string.Empty
                    : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                record.Fields[columns[ordinal]] = value;
            }
            records.Add(record);
        }

        return records;
    }

    public async Task<Dictionary<string, string>> GetLookupValuesAsync(
        PmReportData data, CancellationToken cancellationToken = default)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var requests = LegacyReportWriter.LookupSpecs(data.Process)
            .Select(spec => spec.Split('|'))
            .SelectMany(spec => (spec[0] == "OtherUnitCheck"
                ? data.Current.Fields.GetValueOrDefault(spec[0], "").Split('|').Select(row => row.Split(',')[0])
                : new[] { data.Current.Fields.GetValueOrDefault(spec[0], "") })
                .Select(id => (Id: id, Table: spec[1], Key: spec[2], Value: spec[3])))
            .Where(spec => !string.IsNullOrWhiteSpace(spec.Id) && spec.Id != "Select").Distinct().ToArray();
        if (requests.Length == 0) return values;

        await using var connection = new SqlConnection(_connectionString);
        await using var command = new SqlCommand("", connection);
        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            // Identifiers come from compiled source, and are also allowlisted here.
            if ((request.Table, request.Key, request.Value) is not (
                ("BMContent", "BMContentID", "BMContent") or ("BMCase", "BMCaseID", "BMCase") or
                ("BMMCUnit", "BMMCUnitID", "BMMCUnit") or ("BMMCUnitWithType", "BMMCUnitID", "BMMCUnit") or
                ("BMPoint", "BMPointID", "BMPoint") or ("BMWorkContent", "BMWorkContentID", "BMWorkContent") or
                ("PMCategory", "PMCategoryID", "PMCategory") or ("BMWhy", "BMWhyID", "BM4M") or ("BMWhy", "BMWhyID", "BMWhy")))
                throw new InvalidOperationException("Unknown report lookup mapping.");
            command.CommandText += request.Table == "BMMCUnitWithType"
                ? $"SELECT TOP (1) a.BMMCUnit FROM BMMCUnit a INNER JOIN MCType b ON a.MCTypeID = b.MCTypeID WHERE a.BMMCUnitID = @id{i};"
                : $"SELECT [{request.Value}] FROM [{request.Table}] WHERE [{request.Key}] = @id{i};";
            command.Parameters.AddWithValue($"@id{i}", request.Id);
        }
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        for (var i = 0; i < requests.Length; i++)
        {
            var request = requests[i];
            var key = $"{request.Table}|{request.Id}|{request.Value}";
            // Original helpers return the last row (normally a primary-key lookup).
            while (await reader.ReadAsync(cancellationToken))
                values[key] = reader.GetValue(0).ToString() ?? "";
            if (i + 1 < requests.Length) await reader.NextResultAsync(cancellationToken);
        }
        return values;
    }
}
