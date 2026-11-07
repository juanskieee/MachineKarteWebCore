using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories;

public class MachineSummaryRepository
{
    private readonly string _connectionString;

    public MachineSummaryRepository(IConfiguration configuration) =>
        _connectionString = RepositorySql.GetConnectionString(configuration);

    public Task<Dictionary<string, Dictionary<string, List<string>>>> GetAllMachinesAsync(
        DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default) =>
        ImportantDeviceQuery.GetMachinesByProcessAsync(_connectionString,
            MachineSummaryConfig.GetProcesses(isTe).ToDictionary(process => process,
                process => MachineSummaryConfig.GetLocations(process, isTe)),
            from, to, isTe, distinct: false, cancellationToken);

    public async Task<List<string>> GetMachinesAsync(
        string process, string location, DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default)
    {
        if (!MachineSummaryConfig.IsValidProcess(process, isTe))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));

        return await ImportantDeviceQuery.GetMachinesAsync(_connectionString, process, location,
            from, to, isTe, distinct: false, cancellationToken);
    }

    public Task<Dictionary<string, List<string>>> GetMachinesByLocationAsync(
        string process, DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default)
    {
        if (!MachineSummaryConfig.IsValidProcess(process, isTe))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));

        return ImportantDeviceQuery.GetMachinesByLocationAsync(_connectionString, process,
            MachineSummaryConfig.GetLocations(process, isTe), from, to, isTe, distinct: false, cancellationToken);
    }
}
