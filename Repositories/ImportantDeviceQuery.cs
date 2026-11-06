using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

// Forms 7 and 11 share eligibility and ordering. Form 7 counts distinct machines;
// Form 11 counts BM rows, so deduplication must remain an explicit caller choice.
internal static class ImportantDeviceQuery
{
    public static async Task<List<string>> GetMachinesAsync(string connectionString, string process,
        string location, DateTime from, DateTime to, bool te, bool distinct, CancellationToken cancellationToken)
    {
        string table = te ? "TEBMMaintenance" : $"{process}BMMaintenance";
        string machine = te ? "TesterNo" : "Mcno";
        string cases = te ? "BMTEUnit" : "BMCase";
        string caseId = te ? "BMUnitID" : "BMCaseID";
        string joinId = te ? "BMTEUnitID" : "BMCaseID";
        string actions = te ? "MachineKarte_ActionCard_TE" : "MachineKarte_ActionCard";
        string status = te ? "a.BMStatusID NOT IN (30,31,1,2,3,7,17)" : "a.BMStatusID NOT IN (30,31)";
        string category = te ? "a.PMCategoryID IN (1,4)" : "a.PMCategoryID = 1";
        string processFilter = te ? "AND a.Process = @Process" : "";
        string sql = $@"
            SELECT {(distinct ? "DISTINCT" : "")} a.{machine}
            FROM [eMaintenanceSystemDB].[dbo].[{table}] a
            INNER JOIN [eMaintenanceSystemDB].[dbo].[{cases}] b ON a.{caseId} = b.{joinId}
            WHERE a.TimeOfRequest BETWEEN @Range1 AND @Range2
                AND a.Location = @Location AND {category} AND {status} AND a.{caseId} <> 0
                {processFilter}
                AND EXISTS (SELECT 1 FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_MCD] mcd
                    WHERE REPLACE(mcd.DeviceName,' ','') = REPLACE(a.DeviceName,' ','') AND mcd.IsActive = 1)
                AND NOT EXISTS (SELECT 1 FROM [eMaintenanceSystemDB].[dbo].[{actions}] ac
                    WHERE ac.BMID = a.BMID AND ac.MachineNo = a.{machine})
            ORDER BY a.{machine} DESC";

        await using var connection = new SqlConnection(connectionString);
        await using var command = new SqlCommand(sql, connection);
        RepositorySql.AddDateRange(command, from, to);
        RepositorySql.AddVarChar(command, "@Location", location, 20);
        if (te) RepositorySql.AddVarChar(command, "@Process", process, 20);
        await connection.OpenAsync(cancellationToken);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var machines = new List<string>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var value = reader.GetValue(0).ToString()?.Trim();
            if (!string.IsNullOrEmpty(value)) machines.Add(value);
        }
        return machines;
    }
}
