// Repositories/OcapViewerRepository.cs
using System.Data;
using Microsoft.Data.SqlClient;
namespace MachineKarte.Web.Repositories
{
    public class OcapViewerRepository
    {
        private readonly string _connectionString;

        public OcapViewerRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DBConnection")
                ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        // ── OCAP checklist rows ───────────────────────────────────────────────
        public DataTable GetOcapRows(string bmCaseId, string bmProcess)
        {
            const string sql = @"
                SELECT  [BMOCAPName],
                        [BMOCAPCheckingMethod],
                        [BMOCAPTreatmentAction]
                FROM    gridBMOCAP
                WHERE   BMCaseID = @BMCaseID
                AND     Process  = @Process
                ORDER BY BMOCAPID";

            var dt = new DataTable();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@BMCaseID", bmCaseId);
            cmd.Parameters.AddWithValue("@Process", bmProcess);
            conn.Open();
            using var da = new SqlDataAdapter(cmd);
            da.Fill(dt);
            return dt;
        }

        // ── Saved OCAPData string for a specific BM record ───────────────────
        // The table name is built from the process code (validated before reaching here).
        public string? GetOcapData(int bmId, string tableName)
        {
            var sql = $@"
                SELECT OCAPData
                FROM   [eMaintenanceSystemDB].[dbo].[{tableName}]
                WHERE  BMID = @BMID";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@BMID", bmId);
            conn.Open();
            var result = cmd.ExecuteScalar();
            return result == DBNull.Value ? null : result?.ToString();
        }
    }
}