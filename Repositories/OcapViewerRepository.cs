using MachineKarte.Web.Services;
using System.Data;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public class OcapViewerRepository
{
    private readonly DatabaseConnectionResolver _connections;

    public OcapViewerRepository(DatabaseConnectionResolver connections) =>
        _connections = connections;

    // OCAP checklist rows
    public async Task<List<OcapRow>> GetOcapRowsAsync(
        string bmCaseId, string bmProcess, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                BMOCAPName,
                BMOCAPCheckingMethod,
                BMOCAPTreatmentAction
            FROM dbo.gridBMOCAP
            WHERE
                BMCaseID = @BMCaseID
                AND Process = @Process
            ORDER BY
                BMOCAPID;";

        var rows = new List<OcapRow>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@BMCaseID", bmCaseId);
        RepositorySql.AddVarChar(cmd, "@Process", bmProcess, 20);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new OcapRow
            {
                CheckItem = reader["BMOCAPName"].ToString() ?? string.Empty,
                CheckingMethod = reader["BMOCAPCheckingMethod"].ToString() ?? string.Empty,
                TreatmentAction = reader["BMOCAPTreatmentAction"].ToString() ?? string.Empty,
                Judgement = "test1",
                Remarks = "test2"
            });
        return rows;
    }

    // Saved OCAPData string for a specific BM record
    // Validate again at the SQL boundary, including direct repository callers.
    public async Task<string?> GetOcapDataAsync(
        int bmId, string tableName, CancellationToken cancellationToken = default)
    {
        const string suffix = "BMMaintenance";
        if (!tableName.EndsWith(suffix, StringComparison.Ordinal) ||
            !MachineSummaryConfig.IsValidProcess(tableName[..^suffix.Length], false))
            throw new ArgumentException("Unknown OCAP table.", nameof(tableName));

        var sql = $@"
            SELECT OCAPData
            FROM   [eMaintenanceSystemDB].[dbo].[{tableName}]
            WHERE  BMID = @BMID";

        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@BMID", SqlDbType.Int).Value = bmId;
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result == DBNull.Value ? null : result?.ToString();
    }
}
