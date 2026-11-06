using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories;

public class MachineSummaryRepository
{
    private readonly string _connectionString;

    public MachineSummaryRepository(IConfiguration configuration) =>
        _connectionString = RepositorySql.GetConnectionString(configuration);

    public async Task<List<string>> GetMachinesAsync(
        string process, string location, DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default)
    {
        if (!MachineSummaryConfig.IsValidProcess(process, isTe))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));

        return await ImportantDeviceQuery.GetMachinesAsync(_connectionString, process, location,
            from, to, isTe, distinct: false, cancellationToken);
    }
}