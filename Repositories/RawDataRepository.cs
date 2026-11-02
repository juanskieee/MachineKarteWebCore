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
        public async Task<DataTable> GetFrequencyRawRowsAsync(string tableName, string location,
            string dateFrom, string dateTo, CancellationToken cancellationToken = default)
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

            return await QueryAsync(sql, tableName, location, dateFrom, dateTo, cancellationToken);
        }

        // ── BM-case count by month query (Chart2 + DataGridView1) ───────────
        public async Task<DataTable> GetBmCaseMonthlyRowsAsync(string tableName, string location,
            string dateFrom, string dateTo, CancellationToken cancellationToken = default)
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

            return await QueryAsync(sql, tableName, location, dateFrom, dateTo, cancellationToken);
        }

        private async Task<DataTable> QueryAsync(string sql, string tableName, string location,
            string dateFrom, string dateTo, CancellationToken cancellationToken)
        {
            // Validate at the SQL boundary as well as in the service.
            if (tableName is not ("DCBMMaintenance" or "DBBMMaintenance" or "WBBMMaintenance" or
                "MPBMMaintenance" or "PLBMMaintenance" or "FLBMMaintenance" or "FTBMMaintenance" or
                "TPBMMaintenance" or "EDSBMMaintenance" or "MEMSBMMaintenance"))
                throw new ArgumentException("Unknown raw-data table.", nameof(tableName));

            await using var conn = new SqlConnection(_connectionString);
            await using var cmd = new SqlCommand(sql.Replace("{TABLE}", tableName), conn);
            cmd.CommandType = CommandType.Text;
            cmd.CommandTimeout = 120;
            cmd.Parameters.AddWithValue("@Location", location);
            cmd.Parameters.AddWithValue("@DateFrom", dateFrom);
            cmd.Parameters.AddWithValue("@DateTo", dateTo);

            await conn.OpenAsync(cancellationToken);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            return await ReadTableAsync(reader, cancellationToken);
        }

        private static async Task<DataTable> ReadTableAsync(
            System.Data.Common.DbDataReader reader, CancellationToken cancellationToken)
        {
            var dt = new DataTable();
            // SELECT INTO emits a row count, not a tabular result. Read the
            // current SELECT result before advancing, and consume DROP afterward.
            do
            {
                if (reader.FieldCount == 0) continue;
                for (var i = 0; i < reader.FieldCount; i++)
                    dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
                dt.BeginLoadData();
                try
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var values = new object[reader.FieldCount];
                        reader.GetValues(values);
                        dt.Rows.Add(values);
                    }
                }
                finally { dt.EndLoadData(); }
            } while (await reader.NextResultAsync(cancellationToken));
            return dt;
        }
    }
}
