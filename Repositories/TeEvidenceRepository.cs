using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Repositories
{
    public class TeEvidenceRepository
    {
        private readonly string _conn;

        public TeEvidenceRepository(IConfiguration cfg)
            => _conn = cfg.GetConnectionString("DBConnection")!;

        // ─── TEAssign (mirrors VB GetTEAssign) ───────────────────────────────

        public async Task<string> GetTEAssignAsync(string bmId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT PMConfirmGLOPReqBy, RepairedByEnd FROM ViewTEData WHERE bmID = @bmId";
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rdr.ReadAsync(cancellationToken))
            {
                var req = rdr["PMConfirmGLOPReqBy"].ToString()!;
                var rep = rdr["RepairedByEnd"].ToString()!;
                return req == rep ? req : $"{req}, {rep}";
            }
            return "";
        }

        // ─── Header from ViewTEData ───────────────────────────────────────────

        public async Task<TeEvidenceHeader?> GetHeaderAsync(string bmId, CancellationToken cancellationToken = default)
        {
            const string sql = "SELECT * FROM ViewTEData WHERE bmID = @bmId";

            var header = new TeEvidenceHeader();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await rdr.ReadAsync(cancellationToken))
            {
                header.MCNo = ReadOptional(rdr, "MCNo");
                header.TesterNo = ReadOptional(rdr, "TesterNo");
                header.DeviceName = ReadOptional(rdr, "DeviceName");
                header.LotNo = ReadOptional(rdr, "LOTNo");
                header.TestNoNG = ReadOptional(rdr, "TestNoNG");
                header.WaferNo = ReadOptional(rdr, "WaferNo");
                header.StartOfRepair = ReadOptional(rdr, "StartOfRepair");
                header.EndOfRepair = ReadOptional(rdr, "EndOfRepair");
                header.Remarks = ReadOptional(rdr, "Remarks");
                return header;
            }
            return null;
        }

        // ─── Category descriptions (text cells in the Excel template) ─────────

        public async Task<List<TeEvidenceCategoryDesc>> GetCategoryDescsAsync(string bmId, CancellationToken cancellationToken = default)
        {
            const string sql = @"
SELECT DISTINCT BMEvidence_Category, Condition, BMEvidence_Desc
FROM   TEBMEvidenceMaking
WHERE  bmID = @bmId
  AND  BMEvidence_Category <> 'TELEKO EVIDENCE'
  AND  BMEvidence_Desc <> ''";

            var list = new List<TeEvidenceCategoryDesc>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);
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

        // ─── Evidence images (BEFORE/AFTER) grouped by category ──────────────

        public async Task<Dictionary<string, List<string>>> GetCategoryImagesAsync(string bmId, CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var category in new[] { "PHENOMENON", "MACHINE COUNTER AND VISUAL BIN", "PROGRAM LOADED", "TEST BOARD", "TEST CABLE", "OTHER EVIDENCE" })
            {
                result[category + "_BEFORE"] = new();
                result[category + "_AFTER"] = new();
            }
            foreach (var row in await GetCategoryImageRowsAsync(bmId, cancellationToken))
                result[row.Category + (row.Condition.ToUpperInvariant() == "BEFORE" ? "_BEFORE" : "_AFTER")].Add(row.ImgFile);
            return result;
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

            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);

            // One round trip; TOP is still applied across BEFORE/AFTER together.
            var sql = string.Join(";", categories.Select((category, i) => $@"
SELECT TOP {category.TopN} Condition, BMEvidence_ImgFile, CN
FROM   TEBMEvidenceMaking
WHERE  BMEvidence_Category = @cat{i} AND bmID = @bmId
ORDER BY BMEvidenceID"));
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            for (var i = 0; i < categories.Length; i++)
                cmd.Parameters.AddWithValue($"@cat{i}", categories[i].Cat);

            await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
            for (var i = 0; i < categories.Length; i++)
            {
                var cat = categories[i].Cat;

                while (await rdr.ReadAsync(cancellationToken))
                {
                    result.Add(new TeEvidenceImage { Category = cat, Condition = rdr["Condition"].ToString()!,
                        ImgFile = rdr["BMEvidence_ImgFile"].ToString()!.Replace("~", ""), CN = rdr["CN"].ToString()! });
                }
                if (i + 1 < categories.Length)
                    await rdr.NextResultAsync(cancellationToken);
            }
            return result;
        }

        // ─── Teleko evidence images (ws.Pictures at row 70) ──────────────────

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
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rdr.ReadAsync(cancellationToken))
                list.Add(new TelekoEvidenceImage
                { TelekoEvidence = rdr["TelekoEvidence"].ToString()!.Replace("~", "") });
            return list;
        }

        // ─── Quality-condition images (ws_3, BEFORE/AFTER/IDEAL) ─────────────

        public async Task<List<QualityConditionImage>> GetQualityImagesAsync(string bmId, CancellationToken cancellationToken = default)
        {
            var categories = new[]
                { "BEFORE QUALITY CONDITION", "AFTER QUALITY CONDITION", "IDEAL QUALITY CONDITION" };

            var list = new List<QualityConditionImage>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);

            var sql = string.Join(";", categories.Select((_, i) => $@"
SELECT TOP 1 BMEvidence_ImgFile
FROM   TEBMEvidenceMaking
WHERE  BMEvidence_Category = @cat{i} AND bmID = @bmId
ORDER BY BMEvidenceID"));
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            for (var i = 0; i < categories.Length; i++)
                cmd.Parameters.AddWithValue($"@cat{i}", categories[i]);
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

        // ─── Misc data row (Ideas, WaferNo, Remarks, TelekoCheckingData) ──────

        public async Task<(string Ideas, string WaferNo, string Remarks, string TelekoCheckingData)>
            GetMiscDataAsync(string bmId, CancellationToken cancellationToken = default)
        {
            var row = (await GetMiscRowsAsync(bmId, cancellationToken)).LastOrDefault();
            return row is null ? ("", "", "", "") :
                (row["Ideas"].ToString()!, row["WaferNo"].ToString()!, row["Remarks"].ToString()!, row["TelekoCheckingData"].ToString()!);
        }

        public async Task<IList<IDictionary<string, object>>> GetMiscRowsAsync(string bmId, CancellationToken cancellationToken = default)
        {
            var rows = new List<IDictionary<string, object>>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);

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
                rows.Add(new Dictionary<string, object> {
                    ["Ideas"] = ReadOptional(rdr, "Ideas"), ["WaferNo"] = ReadOptional(rdr, "WaferNo"),
                    ["Remarks"] = ReadOptional(rdr, "Remarks"), ["TelekoCheckingData"] = ReadOptional(rdr, "TelekoCheckingData")
                });
            return rows;
        }

        private static string ReadOptional(SqlDataReader reader, string columnName)
        {
            try
            {
                var ordinal = reader.GetOrdinal(columnName);
                return reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal).ToString() ?? string.Empty;
            }
            catch (IndexOutOfRangeException)
            {
                return string.Empty;
            }
        }
    }
}
