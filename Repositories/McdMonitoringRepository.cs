using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Repositories;

/// <summary>
/// Data access for Form7 (MCD Monitoring).
///
/// Form7's active path (NewLoadColor → InputToLabel) counts a row when:
///     isMCD = "Yes" AND HaveAction = "No"
/// Both conditions are pushed into SQL, and the returned machine numbers
/// are distinct – the same effect as Form7's RemoveDuplicates().
/// </summary>
public class McdMonitoringRepository
{
    private readonly DatabaseConnectionResolver _connections;

    public McdMonitoringRepository(DatabaseConnectionResolver connections) =>
        _connections = connections;

    public async Task<List<string>> GetMachinesAsync(
        string process, string location, DateTime dateFrom, DateTime dateTo, bool isTe, CancellationToken cancellationToken = default)
    {
        ValidateProcess(process);

        return await ImportantDeviceQuery.GetMachinesAsync(_connections, process, location,
            dateFrom, dateTo, isTe, distinct: true, cancellationToken);
    }

    private static void ValidateProcess(string process)
    {
        if (!McdMonitoringConfig.EeeProcesses.Contains(process) &&
            !McdMonitoringConfig.TeProcesses.Contains(process))
        {
            throw new ArgumentException($"Unknown process '{process}'.", nameof(process));
        }
    }
}
