// Repositories/EvidenceRepository.cs
// Converted from Form4.vb SQL queries — E-Karte System
// C# ASP.NET Core + plain SqlClient

using Microsoft.Data.SqlClient;
using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories
{
    public class EvidenceRepository
    {
        private readonly string _connStr;

        public EvidenceRepository(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DBConnection")
                       ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        private SqlConnection Open() => new(_connStr);

        // ----------------------------------------------------------------
        // Evidence image groups
        // Mirrors mSQLStr1..5 in Form4.vb (gridEvidenceMaking_Ver3 queries)
        // ----------------------------------------------------------------

        public async Task<EvidenceImageGroups> GetEvidenceImagesAsync(int bmId, string process)
        {
            const string tbl = "gridEvidenceMaking_Ver3";

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
            await conn.OpenAsync();

            // Category 1 — PROBLEM POINT / PHENOMENON  (TOP 3)
            await FillGroupAsync(conn, groups.ProblemPoint, tbl, bmId, process,
                "PROBLEM POINT / PHENOMENON", 3);

            // Category 2 — ACTION TAKEN / EVIDENCE AFTER ADJUSTMENT  (TOP 2)
            await FillGroupAsync(conn, groups.ActionTaken, tbl, bmId, process,
                "ACTION TAKEN / EVIDENCE AFTER ADJUSTMENT", 2);

            // Category 3 — PARTS REPLACED BEFORE / PARTS REPLACED AFTER  (TOP 2)
            await FillGroupAsync(conn, groups.PartsReplaced, tbl, bmId, process,
                "PARTS REPLACED BEFORE / PARTS REPLACED AFTER", 2);

            // Category 4 — DETAILS OF DIS-ASSEMBLY  DETAILS OF RE-ASSEMBLY  (TOP 2)
            await FillGroupAsync(conn, groups.DisAssembly, tbl, bmId, process,
                "DETAILS OF DIS-ASSEMBLY  DETAILS OF RE-ASSEMBLY", 2);

            // Category 5 — BEFORE AND AFTER QUALITY CONFIRMATION  (TOP 3)
            await FillGroupAsync(conn, groups.QualityConfirm, tbl, bmId, process,
                "BEFORE AND AFTER QUALITY CONFIRMATION", 3);

            return groups;
        }

        private static async Task FillGroupAsync(
            SqlConnection conn, EvidenceImageRow[] group,
            string table, int bmId, string process, string category, int top)
        {
            string sql = $@"SELECT TOP {top} BMEvidence_Desc, BMEvidence_ImgFile
                            FROM {table}
                            WHERE BMEvidence_Category = @Cat
                              AND bmID = @BmId
                              AND Process = @Process
                            ORDER BY BMEvidenceID";

            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Cat", category);
            cmd.Parameters.AddWithValue("@BmId", bmId);
            cmd.Parameters.AddWithValue("@Process", process);

            await using var reader = await cmd.ExecuteReaderAsync();
            int i = 0;
            while (await reader.ReadAsync() && i < group.Length)
            {
                group[i].Description = reader["BMEvidence_Desc"].ToString()!;
                group[i].ImageFile = reader["BMEvidence_ImgFile"].ToString()!;
                i++;
            }
        }

        // ----------------------------------------------------------------
        // TSC Confirmation data
        // Mirrors Sqlss1 / DCss1 query in Form4.vb
        // ----------------------------------------------------------------

        public async Task<TscConfirmation> GetTscConfirmationAsync(
            int bmId, string process, string evidenceView)
        {
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
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@BmId", bmId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (await reader.ReadAsync())
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

        // ----------------------------------------------------------------
        // Machine header data
        // Mirrors the ProcessTableData SELECT in Form4.vb
        // ----------------------------------------------------------------

        public async Task<EvidenceMachineHeader?> GetMachineHeaderAsync(
            int bmId, string dataView)
        {
            string sql = $@"SELECT MCNo, MCType, PkgName, DeviceName, LOTNo,
                                   PMConfirmGLOPReqBy, OccurrenceDate,
                                   StartOfRepair, EndOfRepair
                            FROM {dataView}
                            WHERE bmID = @BmId";

            await using var conn = Open();
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@BmId", bmId);

            await using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new EvidenceMachineHeader
            {
                McNo = reader["MCNo"].ToString()!,
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

        // ----------------------------------------------------------------
        // Helper
        // ----------------------------------------------------------------

        private static void InitGroup(EvidenceImageRow[] group)
        {
            for (int i = 0; i < group.Length; i++)
                group[i] = new EvidenceImageRow();
        }
    }
}