using MachineKarte.Web.Services;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public class TeEvidenceRepository
{
    private readonly DatabaseConnectionResolver _connections;

    public TeEvidenceRepository(DatabaseConnectionResolver connections)
        => _connections = connections;

    // Header from ViewTEData
    public async Task<TeEvidenceHeader?> GetHeaderAsync(string bmId, CancellationToken cancellationToken = default)
    {
        // Project only the header fields from the wide view. Inspecting its columns
        // keeps compatibility with older/sparse views where some fields are absent.
        const string sql = @"
DECLARE @columns nvarchar(max) = STUFF((
    SELECT N',' + QUOTENAME(COLUMN_NAME)
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = 'ViewTEData'
      AND COLUMN_NAME IN ('bmID', 'MCNo', 'TesterNo', 'DeviceName', 'LOTNo', 'TestNoNG',
          'WaferNo', 'StartOfRepair', 'EndOfRepair', 'Remarks', 'PMConfirmGLOPReqBy', 'RepairedByEnd')
    FOR XML PATH(''), TYPE).value('.', 'nvarchar(max)'), 1, 1, N'');
DECLARE @query nvarchar(max) = N'SELECT ' + @columns + N' FROM dbo.ViewTEData WHERE bmID = @id';
EXEC sys.sp_executesql @query, N'@id int', @id = @bmId;";

        var header = new TeEvidenceHeader();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bmId", bmId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await rdr.ReadAsync(cancellationToken))
        {
            var columns = RepositorySql.GetColumns(rdr);
            header.MCNo = RepositorySql.ReadOptional(rdr, columns, "MCNo");
            header.TesterNo = RepositorySql.ReadOptional(rdr, columns, "TesterNo");
            header.DeviceName = RepositorySql.ReadOptional(rdr, columns, "DeviceName");
            header.LotNo = RepositorySql.ReadOptional(rdr, columns, "LOTNo");
            header.TestNoNG = RepositorySql.ReadOptional(rdr, columns, "TestNoNG");
            header.WaferNo = RepositorySql.ReadOptional(rdr, columns, "WaferNo");
            header.StartOfRepair = RepositorySql.ReadOptional(rdr, columns, "StartOfRepair");
            header.EndOfRepair = RepositorySql.ReadOptional(rdr, columns, "EndOfRepair");
            header.Remarks = RepositorySql.ReadOptional(rdr, columns, "Remarks");
            var requestedBy = RepositorySql.ReadOptional(rdr, columns, "PMConfirmGLOPReqBy");
            var repairedBy = RepositorySql.ReadOptional(rdr, columns, "RepairedByEnd");
            header.TEAssign = requestedBy == repairedBy ? requestedBy : $"{requestedBy}, {repairedBy}";
            return header;
        }
        return null;
    }

    // Category descriptions (text cells in the Excel template)
    public async Task<List<TeEvidenceCategoryDesc>> GetCategoryDescsAsync(string bmId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT DISTINCT BMEvidence_Category, Condition, BMEvidence_Desc
FROM   TEBMEvidenceMaking
WHERE  bmID = @bmId
  AND  BMEvidence_Category <> 'TELEKO EVIDENCE'
  AND  BMEvidence_Desc <> ''";

        var list = new List<TeEvidenceCategoryDesc>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bmId", bmId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
            list.Add(new TeEvidenceCategoryDesc
            {
                Category = rdr["BMEvidence_Category"].ToString()!,
                Condition = rdr["Condition"].ToString()!,
                Desc = rdr["BMEvidence_Desc"].ToString()!
            });
        return list;
    }

    public async Task<List<TeEvidenceImage>> GetCategoryImageRowsAsync(string bmId, CancellationToken cancellationToken = default)
    {
        // Categories and their TOP N limits (mirror the VB mSQLStr1_A … mSQLStr6_A)
        var categories = new (string Cat, int TopN)[]
        {
            ("PHENOMENON",                         6),
            ("MACHINE COUNTER AND VISUAL BIN",     6),
            ("PROGRAM LOADED",                     6),
            ("TEST BOARD",                        20),
            ("TEST CABLE",                         6),
            ("OTHER EVIDENCE",                     6)
        };

        var result = new List<TeEvidenceImage>();

        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);

        // One round trip; TOP is still applied across BEFORE/AFTER together.
        var sql = string.Join(";", categories.Select((category, i) => $@"
SELECT TOP {category.TopN} Condition, BMEvidence_ImgFile, CN
FROM   TEBMEvidenceMaking
WHERE  BMEvidence_Category = @cat{i} AND bmID = @bmId
ORDER BY BMEvidenceID"));
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bmId", bmId);
        for (var i = 0; i < categories.Length; i++)
            RepositorySql.AddVarChar(cmd, $"@cat{i}", categories[i].Cat, 100);

        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        for (var i = 0; i < categories.Length; i++)
        {
            var cat = categories[i].Cat;

            while (await rdr.ReadAsync(cancellationToken))
            {
                result.Add(new TeEvidenceImage
                {
                    Category = cat,
                    Condition = rdr["Condition"].ToString()!,
                    ImgFile = rdr["BMEvidence_ImgFile"].ToString()!.Replace("~", ""),
                    CN = rdr["CN"].ToString()!
                });
            }
            if (i + 1 < categories.Length)
                await rdr.NextResultAsync(cancellationToken);
        }
        return result;
    }

    // Teleko evidence images (ws.Pictures at row 70)
    public async Task<List<TelekoEvidenceImage>> GetTelekoImagesAsync(string bmId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT TOP 4 TelekoEvidence
FROM   TEBMEvidenceMaking
WHERE  bmID = @bmId
  AND  BMEvidence_Category = 'TELEKO EVIDENCE'
  AND  TelekoEvidence <> ''
  AND  TelekoEvidence IS NOT NULL";

        var list = new List<TelekoEvidenceImage>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bmId", bmId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await rdr.ReadAsync(cancellationToken))
            list.Add(new TelekoEvidenceImage
            { TelekoEvidence = rdr["TelekoEvidence"].ToString()!.Replace("~", "") });
        return list;
    }

    // Quality-condition images (ws_3, BEFORE/AFTER/IDEAL)
    public async Task<List<QualityConditionImage>> GetQualityImagesAsync(string bmId, CancellationToken cancellationToken = default)
    {
        var categories = new[]
            { "BEFORE QUALITY CONDITION", "AFTER QUALITY CONDITION", "IDEAL QUALITY CONDITION" };

        var list = new List<QualityConditionImage>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);

        var sql = string.Join(";", categories.Select((_, i) => $@"
SELECT TOP 1 BMEvidence_ImgFile
FROM   TEBMEvidenceMaking
WHERE  BMEvidence_Category = @cat{i} AND bmID = @bmId
ORDER BY BMEvidenceID"));
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bmId", bmId);
        for (var i = 0; i < categories.Length; i++)
            RepositorySql.AddVarChar(cmd, $"@cat{i}", categories[i], 100);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        for (var i = 0; i < categories.Length; i++)
        {
            if (await rdr.ReadAsync(cancellationToken))
                list.Add(new QualityConditionImage
                {
                    Category = categories[i],
                    ImgFile = rdr["BMEvidence_ImgFile"].ToString()!.Replace("~", "")
                });
            if (i + 1 < categories.Length)
                await rdr.NextResultAsync(cancellationToken);
        }
        return list;
    }

    public async Task<IList<IDictionary<string, object>>> GetMiscRowsAsync(string bmId, CancellationToken cancellationToken = default)
    {
        var rows = new List<IDictionary<string, object>>();
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);

        var availableColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string schemaSql = @"
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME = 'TEBMEvidenceMaking'
  AND COLUMN_NAME IN ('TelekoCheckingData', 'TelekoEvidence', 'Ideas', 'Remarks', 'WaferNo', 'QualityDefect')";

        await using (var schemaCommand = new SqlCommand(schemaSql, conn))
        await using (var schemaReader = await schemaCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await schemaReader.ReadAsync(cancellationToken))
                availableColumns.Add(schemaReader.GetString(0));
        }

        var selectColumns = new[] { "TelekoCheckingData", "TelekoEvidence", "Ideas", "Remarks", "WaferNo", "QualityDefect" }
            .Where(availableColumns.Contains)
            .ToArray();

        if (selectColumns.Length == 0)
            return rows;

        var sql = $@"
SELECT DISTINCT {string.Join(", ", selectColumns)}
FROM   [dbo].[TEBMEvidenceMaking]
WHERE  bmID = @bmId
GROUP BY {string.Join(", ", selectColumns)}";

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@bmId", bmId);
        await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
        // Form10 iterates every distinct row. A later empty QC value does not
        // erase an earlier QC table, so collapsing this to TOP 1 loses data.
        while (await rdr.ReadAsync(cancellationToken))
            rows.Add(new Dictionary<string, object>
            {
                ["Ideas"] = RepositorySql.ReadOptional(rdr, availableColumns, "Ideas"),
                ["WaferNo"] = RepositorySql.ReadOptional(rdr, availableColumns, "WaferNo"),
                ["Remarks"] = RepositorySql.ReadOptional(rdr, availableColumns, "Remarks"),
                ["TelekoCheckingData"] = RepositorySql.ReadOptional(rdr, availableColumns, "TelekoCheckingData")
            });
        return rows;
    }

}
