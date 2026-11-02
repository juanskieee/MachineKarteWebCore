using System.Collections.Generic;
using System.Threading.Tasks;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class AlertBoardService
    {
        private readonly AlertBoardRepository _repo;

        public AlertBoardService(AlertBoardRepository repo) => _repo = repo;

        public Task<AlertBoardData> LoadBoardAsync(AlertBoardRequest req)
            => _repo.LoadBoardAsync(req);

        /// <summary>
        /// Returns the machine list for a cell key (e.g. "DC_0") plus a
        /// human-readable title for the drilldown panel.
        /// </summary>
        public (List<string> Machines, string Title) GetDrilldown(
            AlertBoardData board, string cellKey)
        {
            string title = ResolveCellTitle(cellKey);
            var machines = _repo.GetCellMachines(board, cellKey);
            return (machines, title);
        }

        private static string ResolveCellTitle(string key)
            => AlertBoardConfig.Cells.TryGetValue(key, out var cell)
                ? $"{cell.Process} ( {cell.Location} )" : key;
    }
}