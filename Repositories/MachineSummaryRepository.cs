using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Repositories;

public class MachineSummaryRepository
{
    private readonly DatabaseConnectionResolver _connections;

    public MachineSummaryRepository(DatabaseConnectionResolver connections) =>
        _connections = connections;

    public Task<Dictionary<string, Dictionary<string, List<string>>>> GetAllMachinesAsync(
        DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default) =>
        ImportantDeviceQuery.GetMachinesByProcessAsync(_connections,
            MachineSummaryConfig.GetProcesses(isTe).ToDictionary(process => process,
                process => MachineSummaryConfig.GetLocations(process, isTe)),
            from, to, isTe, distinct: false, cancellationToken);

    public async Task<List<string>> GetMachinesAsync(
        string process, string location, DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default)
    {
        if (!MachineSummaryConfig.IsValidProcess(process, isTe))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));

        return await ImportantDeviceQuery.GetMachinesAsync(_connections, process, location,
            from, to, isTe, distinct: false, cancellationToken);
    }

    public Task<Dictionary<string, List<string>>> GetMachinesByLocationAsync(
        string process, DateTime from, DateTime to, bool isTe, CancellationToken cancellationToken = default)
    {
        if (!MachineSummaryConfig.IsValidProcess(process, isTe))
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));

        return ImportantDeviceQuery.GetMachinesByLocationAsync(_connections, process,
            MachineSummaryConfig.GetLocations(process, isTe), from, to, isTe, distinct: false, cancellationToken);
    }
}
