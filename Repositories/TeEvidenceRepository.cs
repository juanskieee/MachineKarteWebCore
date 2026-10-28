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

        public async Task<string> GetTEAssignAsync(string bmId)
        {
            const string sql = "SELECT PMConfirmGLOPReqBy, RepairedByEnd FROM ViewTEData WHERE bmID = @bmId";
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                var req = rdr["PMConfirmGLOPReqBy"].ToString()!;
                var rep = rdr["RepairedByEnd"].ToString()!;
                return req == rep ? req : $"{req}, {rep}";
            }
            return "";
        }

        // ─── Header from ViewTEData ───────────────────────────────────────────

        public async Task<TeEvidenceHeader> GetHeaderAsync(string bmId)
        {
            const string sql = @"
SELECT MCNo, TesterNo, DeviceName, LOTNo, TestNoNG, WaferNo,
       StartOfRepair, EndOfRepair, Remarks
FROM   ViewTEData
WHERE  bmID = @bmId";

            var header = new TeEvidenceHeader();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
            {
                header.MCNo = rdr["MCNo"].ToString()!;
                header.TesterNo = rdr["TesterNo"].ToString()!;
                header.DeviceName = rdr["DeviceName"].ToString()!;
                header.LotNo = rdr["LOTNo"].ToString()!;
                header.TestNoNG = rdr["TestNoNG"].ToString()!;
                header.WaferNo = rdr["WaferNo"].ToString()!;
                header.StartOfRepair = rdr["StartOfRepair"].ToString()!;
                header.EndOfRepair = rdr["EndOfRepair"].ToString()!;
                header.Remarks = rdr["Remarks"].ToString()!;
            }
            return header;
        }

        // ─── Category descriptions (text cells in the Excel template) ─────────

        public async Task<List<TeEvidenceCategoryDesc>> GetCategoryDescsAsync(string bmId)
        {
            const string sql = @"
SELECT DISTINCT BMEvidence_Category, Condition, BMEvidence_Desc
FROM   TEBMEvidenceMaking
WHERE  bmID = @bmId
  AND  BMEvidence_Category <> 'TELEKO EVIDENCE'
  AND  BMEvidence_Desc <> ''";

            var list = new List<TeEvidenceCategoryDesc>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                list.Add(new TeEvidenceCategoryDesc
                {
                    Category = rdr["BMEvidence_Category"].ToString()!,
                    Condition = rdr["Condition"].ToString()!,
                    Desc = rdr["BMEvidence_Desc"].ToString()!
                });
            return list;
        }

        // ─── Evidence images (BEFORE/AFTER) grouped by category ──────────────

        public async Task<Dictionary<string, List<string>>> GetCategoryImagesAsync(string bmId)
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

            var result = new Dictionary<string, List<string>>();

            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            foreach (var (cat, topN) in categories)
            {
                string sql = $@"
SELECT TOP {topN} Condition, BMEvidence_ImgFile
FROM   TEBMEvidenceMaking
WHERE  BMEvidence_Category = @cat AND bmID = @bmId
ORDER BY BMEvidenceID";

                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@cat", cat);
                cmd.Parameters.AddWithValue("@bmId", bmId);
                await using var rdr = await cmd.ExecuteReaderAsync();

                var beforeKey = $"{cat}_BEFORE";
                var afterKey = $"{cat}_AFTER";
                result[beforeKey] = new List<string>();
                result[afterKey] = new List<string>();

                while (await rdr.ReadAsync())
                {
                    var condition = rdr["Condition"].ToString()!.ToUpperInvariant();
                    var imgFile = rdr["BMEvidence_ImgFile"].ToString()!.Replace("~", "");
                    var key = condition == "BEFORE" ? beforeKey : afterKey;
                    result[key].Add(imgFile);
                }
            }
            return result;
        }

        // ─── Teleko evidence images (ws.Pictures at row 70) ──────────────────

        public async Task<List<TelekoEvidenceImage>> GetTelekoImagesAsync(string bmId)
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
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                list.Add(new TelekoEvidenceImage
                { TelekoEvidence = rdr["TelekoEvidence"].ToString()!.Replace("~", "") });
            return list;
        }

        // ─── Quality-condition images (ws_3, BEFORE/AFTER/IDEAL) ─────────────

        public async Task<List<QualityConditionImage>> GetQualityImagesAsync(string bmId)
        {
            var categories = new[]
                { "BEFORE QUALITY CONDITION", "AFTER QUALITY CONDITION", "IDEAL QUALITY CONDITION" };

            var list = new List<QualityConditionImage>();
            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();

            foreach (var cat in categories)
            {
                const string sql = @"
SELECT TOP 1 BMEvidence_ImgFile
FROM   TEBMEvidenceMaking
WHERE  BMEvidence_Category = @cat AND bmID = @bmId
ORDER BY BMEvidenceID";
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@cat", cat);
                cmd.Parameters.AddWithValue("@bmId", bmId);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (await rdr.ReadAsync())
                    list.Add(new QualityConditionImage
                    {
                        Category = cat,
                        ImgFile = rdr["BMEvidence_ImgFile"].ToString()!.Replace("~", "")
                    });
            }
            return list;
        }

        // ─── Misc data row (Ideas, WaferNo, Remarks, TelekoCheckingData) ──────

        public async Task<(string Ideas, string WaferNo, string Remarks, string TelekoCheckingData)>
            GetMiscDataAsync(string bmId)
        {
            const string sql = @"
SELECT DISTINCT TelekoCheckingData, Ideas, Remarks, WaferNo
FROM   TEBMEvidenceMaking
WHERE  bmID = @bmId
GROUP BY TelekoCheckingData, TelekoEvidence, Ideas, Remarks, WaferNo, QualityDefect";

            await using var conn = new SqlConnection(_conn);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmId", bmId);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (await rdr.ReadAsync())
                return (
                    rdr["Ideas"].ToString()!,
                    rdr["WaferNo"].ToString()!,
                    rdr["Remarks"].ToString()!,
                    rdr["TelekoCheckingData"].ToString()!
                );
            return ("", "", "", "");
        }
    }
}