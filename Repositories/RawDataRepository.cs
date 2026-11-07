using MachineKarte.Web.Services;
using System.Data;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public class RawDataRepository
{
    private readonly DatabaseConnectionResolver _connections;

    public RawDataRepository(DatabaseConnectionResolver connections) =>
        _connections = connections;

    // Form5 compares adjacent rows in this exact order, across case runs and machines.
    // Frequency restricts dates before the window; monthly totals restrict dates afterward.
    private const string RepeatedRowsSql = @"
        ;WITH Neighbors AS
        (
            SELECT a.mcno, b.BMCase AS bmcase, a.OccurrenceDate,
                LEAD(a.mcno) OVER (ORDER BY a.mcno DESC, a.OccurrenceDate ASC, b.BMCase DESC) AS NextMachine,
                LEAD(b.BMCase) OVER (ORDER BY a.mcno DESC, a.OccurrenceDate ASC, b.BMCase DESC) AS NextCase,
                LAG(a.mcno) OVER (ORDER BY a.mcno DESC, a.OccurrenceDate ASC, b.BMCase DESC) AS PreviousMachine,
                LAG(b.BMCase) OVER (ORDER BY a.mcno DESC, a.OccurrenceDate ASC, b.BMCase DESC) AS PreviousCase
            FROM {TABLE} a
            INNER JOIN [eMaintenanceSystemDB].[dbo].[BMCase] b ON a.BMCaseID = b.BMCaseID
            WHERE a.OccurrenceDate IS NOT NULL
                AND b.BMCase NOT LIKE '%corrective%' AND b.BMCase NOT LIKE '%preventive%'
                AND a.Location = @Location
                {DATE_FILTER}
        ),
        RepeatedRows AS
        (
            SELECT mcno, bmcase, OccurrenceDate FROM Neighbors
            WHERE (mcno = NextMachine AND bmcase = NextCase)
               OR (mcno = PreviousMachine AND bmcase = PreviousCase)
        )";

    public Task<DataTable> GetFrequencyRawRowsAsync(string tableName, string location,
        string dateFrom, string dateTo, CancellationToken cancellationToken = default)
    {
        var sql = RepeatedRowsSql.Replace("{DATE_FILTER}", "AND a.OccurrenceDate BETWEEN @DateFrom AND @DateTo") + @"
            SELECT mcno, bmcase AS BMCase, DATEPART(MM,OccurrenceDate) AS DateNum,
                DATENAME(month,OccurrenceDate) AS DateMonth, DATEPART(year,OccurrenceDate) AS DateYear
            FROM RepeatedRows
            ORDER BY DATEPART(year,OccurrenceDate), DATEPART(MM,OccurrenceDate), mcno;";
        return QueryAsync(sql, tableName, location, dateFrom, dateTo, cancellationToken);
    }

    public Task<DataTable> GetBmCaseMonthlyRowsAsync(string tableName, string location,
        string dateFrom, string dateTo, CancellationToken cancellationToken = default)
    {
        var sql = RepeatedRowsSql.Replace("{DATE_FILTER}", "") + @"
            SELECT bmcase AS BMCase, COUNT(bmcase) AS BMCount, DATEPART(MM,OccurrenceDate) AS DateNum,
                DATENAME(month,OccurrenceDate) AS DateMonth, DATEPART(year,OccurrenceDate) AS DateYear
            FROM RepeatedRows WHERE OccurrenceDate BETWEEN @DateFrom AND @DateTo
            GROUP BY DATEPART(MM,OccurrenceDate), bmcase, DATEPART(year,OccurrenceDate), DATENAME(month,OccurrenceDate)
            ORDER BY DATEPART(year,OccurrenceDate), DATEPART(MM,OccurrenceDate), BMCount DESC;";
        return QueryAsync(sql, tableName, location, dateFrom, dateTo, cancellationToken);
    }
    private async Task<DataTable> QueryAsync(string sql, string tableName, string location,
        string dateFrom, string dateTo, CancellationToken cancellationToken)
    {
        // Validate at the SQL boundary as well as in the service.
        if (tableName is not ("DCBMMaintenance" or "DBBMMaintenance" or "WBBMMaintenance" or
            "MPBMMaintenance" or "PLBMMaintenance" or "FLBMMaintenance" or "FTBMMaintenance" or
            "TPBMMaintenance" or "EDSBMMaintenance" or "MEMSBMMaintenance"))
            throw new ArgumentException("Unknown raw-data table.", nameof(tableName));

        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql.Replace("{TABLE}", tableName), conn);
        cmd.CommandType = CommandType.Text;
        cmd.CommandTimeout = 120;
        RepositorySql.AddVarChar(cmd, "@Location", location, 20);
        RepositorySql.AddDateRange(cmd, dateFrom, dateTo, "@DateFrom", "@DateTo");

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await ReadTableAsync(reader, cancellationToken);
    }

    private static async Task<DataTable> ReadTableAsync(
        System.Data.Common.DbDataReader reader, CancellationToken cancellationToken)
    {
        var dt = new DataTable();
        // Preserve SQL column types and DBNull while materializing asynchronously.
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
