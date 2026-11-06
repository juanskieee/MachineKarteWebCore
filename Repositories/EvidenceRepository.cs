// Converted from Form4.vb SQL queries - E-Karte System
using System.Data;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public class EvidenceRepository
{
    private readonly string _connectionString;

    public EvidenceRepository(IConfiguration configuration) =>
        _connectionString = RepositorySql.GetConnectionString(configuration);

    private SqlConnection Open() => new(_connectionString);
    // Evidence image groups
    // Mirrors mSQLStr1..5 in Form4.vb (gridEvidenceMaking_Ver3 queries)
    public async Task<EvidenceImageGroups> GetEvidenceImagesAsync(int bmId, string process, CancellationToken cancellationToken = default)
    {
        var groups = new EvidenceImageGroups
        {
            ProblemPoint = new EvidenceImageRow[3],
            ActionTaken = new EvidenceImageRow[2],
            PartsReplaced = new EvidenceImageRow[2],
            DisAssembly = new EvidenceImageRow[2],
            QualityConfirm = new EvidenceImageRow[3]
        };

        // Initialise all slots with empty rows so null checks aren't needed later
        InitGroup(groups.ProblemPoint);
        InitGroup(groups.ActionTaken);
        InitGroup(groups.PartsReplaced);
        InitGroup(groups.DisAssembly);
        InitGroup(groups.QualityConfirm);

        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);

        // Keep each category's TOP and ordering, but send all five SELECTs together.
        var categories = new (string Category, EvidenceImageRow[] Rows)[]
        {
            ("PROBLEM POINT / PHENOMENON", groups.ProblemPoint),
            ("ACTION TAKEN / EVIDENCE AFTER ADJUSTMENT", groups.ActionTaken),
            ("PARTS REPLACED BEFORE / PARTS REPLACED AFTER", groups.PartsReplaced),
            ("DETAILS OF DIS-ASSEMBLY  DETAILS OF RE-ASSEMBLY", groups.DisAssembly),
            ("BEFORE AND AFTER QUALITY CONFIRMATION", groups.QualityConfirm)
        };
        await using var cmd = new SqlCommand("", conn);
        cmd.Parameters.Add("@BmId", SqlDbType.Int).Value = bmId;
        RepositorySql.AddVarChar(cmd, "@Process", process, 20);
        for (var i = 0; i < categories.Length; i++)
        {
            cmd.CommandText += $@"SELECT TOP {categories[i].Rows.Length} BMEvidence_Desc, BMEvidence_ImgFile
                FROM gridEvidenceMaking_Ver3
                WHERE BMEvidence_Category = @Cat{i} AND bmID = @BmId AND Process = @Process
                ORDER BY BMEvidenceID;";
            RepositorySql.AddVarChar(cmd, $"@Cat{i}", categories[i].Category, 100);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        for (var category = 0; category < categories.Length; category++)
        {
            var group = categories[category].Rows;
            var i = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                group[i].Description = reader["BMEvidence_Desc"].ToString()!;
                group[i].ImageFile = reader["BMEvidence_ImgFile"].ToString()!;
                i++;
            }
            if (category + 1 < categories.Length)
                await reader.NextResultAsync(cancellationToken);
        }
        return groups;
    }
    // TSC Confirmation data
    // Mirrors Sqlss1 / DCss1 query in Form4.vb
    public async Task<TscConfirmation> GetTscConfirmationAsync(
        int bmId, string process, string evidenceView, CancellationToken cancellationToken = default)
    {
        if (!ProcessTemplateMap.Map.Values.Any(config => config.EvidenceView.Equals(evidenceView, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unknown evidence view.", nameof(evidenceView));

        bool isSing = process.Equals("SING", StringComparison.OrdinalIgnoreCase);

        string extraCols = isSing
            ? ",SignalTowerKanban, SafetyTags, Bypass"
            : string.Empty;

        string sql = $@"SELECT BMRouteNo, BMVersionNo,
                               BMEvidence_TCConfirm1, BMEvidence_TCConfirm2,
                               BMEvidence_TCConfirm3, BMEvidence_TCConfirm4,
                               BMEvidence_TCConfirm5, BMEvidence_TCConfirm6,
                               BMEvidence_TCConfirm7, BMEvidence_TCConfirm8,
                               BMEvidence_TCConfirm9
                               {extraCols}
                        FROM {evidenceView}
                        WHERE bmID = @BmId";

        var tsc = new TscConfirmation();

        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@BmId", SqlDbType.Int).Value = bmId;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            tsc.Tsc1 = reader["BMEvidence_TCConfirm1"].ToString()!;
            tsc.Tsc2 = reader["BMEvidence_TCConfirm2"].ToString()!;
            tsc.Tsc3 = reader["BMEvidence_TCConfirm3"].ToString()!;
            tsc.Tsc4 = reader["BMEvidence_TCConfirm4"].ToString()!;
            tsc.Tsc5 = reader["BMEvidence_TCConfirm5"].ToString()!;
            tsc.Tsc6 = reader["BMEvidence_TCConfirm6"].ToString()!;
            tsc.Tsc7 = reader["BMEvidence_TCConfirm7"].ToString()!;
            tsc.Tsc8 = reader["BMEvidence_TCConfirm8"].ToString()!;
            tsc.Tsc9 = reader["BMEvidence_TCConfirm9"].ToString()!;
            tsc.RouteNo = reader["BMRouteNo"].ToString()!;
            tsc.VersionNo = reader["BMVersionNo"].ToString()!;

            if (isSing)
            {
                tsc.SignalTowerKanban = reader["SignalTowerKanban"].ToString()!;
                tsc.SafetyTags = reader["SafetyTags"].ToString()!;
                tsc.Bypass = reader["Bypass"].ToString()!;
            }
        }

        return tsc;
    }
    // Machine header data
    // Mirrors the ProcessTableData SELECT in Form4.vb
    public async Task<EvidenceMachineHeader?> GetMachineHeaderAsync(
        int bmId, string dataView, CancellationToken cancellationToken = default)
    {
        if (!ProcessTemplateMap.Map.Values.Any(config => config.DataView.Equals(dataView, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Unknown evidence data view.", nameof(dataView));

        string sql = $@"SELECT MCNo, MCType, PkgName, DeviceName, LOTNo, Andon,
                               PMConfirmGLOPReqBy, OccurrenceDate,
                               StartOfRepair, EndOfRepair
                        FROM {dataView}
                        WHERE bmID = @BmId";

        await using var conn = Open();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@BmId", SqlDbType.Int).Value = bmId;

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new EvidenceMachineHeader
        {
            McNo = reader["MCNo"].ToString()!,
            Andon = reader["Andon"].ToString()!,
            McType = reader["MCType"].ToString()!,
            PkgName = reader["PkgName"].ToString()!,
            DeviceName = reader["DeviceName"].ToString()!,
            LotNo = reader["LOTNo"].ToString()!,
            PmConfirmBy = reader["PMConfirmGLOPReqBy"].ToString()!,
            OccurrenceDate = reader["OccurrenceDate"] is DBNull
                             ? string.Empty
                             : Convert.ToDateTime(reader["OccurrenceDate"]).ToShortDateString(),
            StartOfRepair = reader["StartOfRepair"].ToString()!,
            EndOfRepair = reader["EndOfRepair"].ToString()!,
        };
    }
    // Helper
    private static void InitGroup(EvidenceImageRow[] group)
    {
        for (int i = 0; i < group.Length; i++)
            group[i] = new EvidenceImageRow();
    }
}
