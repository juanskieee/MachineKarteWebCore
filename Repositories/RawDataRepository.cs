// Repositories/RawDataRepository.cs
using System.Data;
using Microsoft.Data.SqlClient;
using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories
{
    public class RawDataRepository
    {
        private readonly string _connectionString;

        public RawDataRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DBConnection")
                ?? throw new InvalidOperationException("DBConnection not configured.");
        }

        // ── Frequency recurrence query (Chart1 + DataGridView2) ─────────────
        /// <summary>
        /// Returns every row from the temp-table query that matches
        /// comparison_result = 'Same as next row', ordered chronologically.
        /// The service layer then groups / counts repeated BM cases per machine.
        /// </summary>
        public DataTable GetFrequencyRawRows(string tableName, string location,
                                             string dateFrom, string dateTo)
        {
            // Identical SQL logic to the original VB, parameterised where possible.
            // The table name is validated by the service before reaching here.
            const string sql = @"
                SELECT   a.mcno,
                         b.BMCase AS bmcase,
                         OccurrenceDate,
                         CASE
                           WHEN mcno = LEAD(mcno) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                             THEN
                               CASE
                                 WHEN bmcase = LEAD(bmcase) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                   THEN 'Same as next row'
                                 ELSE
                                   CASE
                                     WHEN bmcase = LAG(bmcase) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                          AND mcno = LAG(mcno)  OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                       THEN 'Same as next row'
                                     ELSE 'Different from next row'
                                   END
                               END
                           ELSE
                             CASE
                               WHEN bmcase = LAG(bmcase) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                    AND mcno = LAG(mcno)  OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                 THEN 'Same as next row'
                               ELSE 'Different from next row'
                             END
                         END AS comparison_result
                INTO     #freq_raw
                FROM     {TABLE} a
                         INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b
                                 ON a.BMCaseID = b.BMCaseID
                WHERE    OccurrenceDate IS NOT NULL
                         AND (bmcase NOT LIKE '%corrective%' AND bmcase NOT LIKE '%preventive%')
                         AND Location = @Location
                         AND OccurrenceDate BETWEEN @DateFrom AND @DateTo
                ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC;

                SELECT   mcno,
                         bmcase AS BMCase,
                         DATEPART(MM, OccurrenceDate)         AS DateNum,
                         DATENAME(month, OccurrenceDate)      AS DateMonth,
                         DATEPART(year,  OccurrenceDate)      AS DateYear
                FROM     #freq_raw
                WHERE    comparison_result = 'Same as next row'
                ORDER BY DATEPART(year, OccurrenceDate),
                         DATEPART(MM,   OccurrenceDate) ASC,
                         mcno;

                DROP TABLE #freq_raw;";

            var dt = new DataTable();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql.Replace("{TABLE}", tableName), conn);
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@Location", location);
            cmd.Parameters.AddWithValue("@DateFrom", dateFrom);
            cmd.Parameters.AddWithValue("@DateTo", dateTo);

            conn.Open();
            using var da = new SqlDataAdapter(cmd);
            // The batch returns two result sets; we want the second (SELECT after DROP).
            // SqlDataAdapter.Fill picks up the first result set of the first SELECT
            // that returns rows — to get the named SELECT we run a reader manually.
            using var reader = cmd.ExecuteReader();
            // Skip to the second result set (the SELECT from #freq_raw).
            while (reader.NextResult())
            {
                dt.Load(reader);
                break;
            }
            return dt;
        }

        // ── BM-case count by month query (Chart2 + DataGridView1) ───────────
        public DataTable GetBmCaseMonthlyRows(string tableName, string location,
                                              string dateFrom, string dateTo)
        {
            const string sql = @"
                SELECT   a.mcno,
                         b.BMCase AS bmcase,
                         OccurrenceDate,
                         CASE
                           WHEN mcno = LEAD(mcno) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                             THEN
                               CASE
                                 WHEN bmcase = LEAD(bmcase) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                   THEN 'Same as next row'
                                 ELSE
                                   CASE
                                     WHEN bmcase = LAG(bmcase) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                          AND mcno = LAG(mcno)  OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                       THEN 'Same as next row'
                                     ELSE 'Different from next row'
                                   END
                               END
                           ELSE
                             CASE
                               WHEN bmcase = LAG(bmcase) OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                    AND mcno = LAG(mcno)  OVER (ORDER BY mcno DESC, OccurrenceDate ASC, bmcase DESC)
                                 THEN 'Same as next row'
                               ELSE 'Different from next row'
                             END
                         END AS comparison_result
                INTO     #bmcase_raw
                FROM     {TABLE} a
                         INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b
                                 ON a.BMCaseID = b.BMCaseID
                WHERE    OccurrenceDate IS NOT NULL
                         AND (bmcase NOT LIKE '%corrective%' AND bmcase NOT LIKE '%preventive%')
                         AND Location = @Location;

                SELECT   bmcase                               AS BMCase,
                         COUNT(bmcase)                        AS BMCount,
                         DATEPART(MM, OccurrenceDate)         AS DateNum,
                         DATENAME(month, OccurrenceDate)      AS DateMonth,
                         DATEPART(year,  OccurrenceDate)      AS DateYear
                FROM     #bmcase_raw
                WHERE    comparison_result = 'Same as next row'
                         AND OccurrenceDate BETWEEN @DateFrom AND @DateTo
                GROUP BY DATEPART(MM,   OccurrenceDate),
                         bmcase,
                         DATEPART(year,  OccurrenceDate),
                         DATENAME(month, OccurrenceDate)
                ORDER BY DATEPART(year, OccurrenceDate),
                         DATEPART(MM,   OccurrenceDate) ASC,
                         BMCount DESC;

                DROP TABLE #bmcase_raw;";

            var dt = new DataTable();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql.Replace("{TABLE}", tableName), conn);
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@Location", location);
            cmd.Parameters.AddWithValue("@DateFrom", dateFrom);
            cmd.Parameters.AddWithValue("@DateTo", dateTo);

            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.NextResult())
            {
                dt.Load(reader);
                break;
            }
            return dt;
        }
    }
}