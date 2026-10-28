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

        // Maps cell key → "PROCESS ( LOCATION )" title
        private static string ResolveCellTitle(string key)
        {
            var map = new Dictionary<string, string>
            {
                ["DC_0"] = "DC ( LSI1-1F )",
                ["DC_2"] = "DC ( LSI2-1F )",
                ["DC_3"] = "DC ( LSI2-2F )",
                ["DC_4"] = "DC ( LSI2-3F )",
                ["DC_6"] = "DC ( LSI3-2F )",
                ["DB_0"] = "DB ( LSI1-1F )",
                ["DB_2"] = "DB ( LSI2-1F )",
                ["DB_3"] = "DB ( LSI2-2F )",
                ["DB_4"] = "DB ( LSI2-3F )",
                ["DB_6"] = "DB ( LSI3-2F )",
                ["WB_0"] = "WB ( LSI1-1F )",
                ["WB_2"] = "WB ( LSI2-1F )",
                ["WB_3"] = "WB ( LSI2-2F )",
                ["WB_4"] = "WB ( LSI2-3F )",
                ["WB_6"] = "WB ( LSI3-2F )",
                ["MP_0"] = "MP ( LSI1-1F )",
                ["MP_2"] = "MP ( LSI2-1F )",
                ["MP_3"] = "MP ( LSI2-2F )",
                ["MP_4"] = "MP ( LSI2-3F )",
                ["MP_6"] = "MP ( LSI3-2F )",
                ["PL_0"] = "PL ( LSI1-1F )",
                ["PL_2"] = "PL ( LSI2-1F )",
                ["PL_3"] = "PL ( LSI2-2F )",
                ["PL_4"] = "PL ( LSI2-3F )",
                ["PL_5"] = "PL ( LSI3-1F )",
                ["ATC_0"] = "ATC ( LSI1-1F )",
                ["ATC_1"] = "ATC ( LSI1-2F )",
                ["ATC_2"] = "ATC ( LSI2-1F )",
                ["ATC_3"] = "ATC ( LSI2-2F )",
                ["ATC_4"] = "ATC ( LSI2-3F )",
                ["ATC_65"] = "ATC ( LSI3-1F )",
                ["ATC_6"] = "ATC ( LSI3-3F )",
                ["FL_1"] = "FL ( LSI1-2F )",
                ["FL_2"] = "FL ( LSI2-1F )",
                ["FL_3"] = "FL ( LSI2-2F )",
                ["FL_4"] = "FL ( LSI2-3F )",
                ["FL_5"] = "FL ( LSI3-3F )",
                ["SING_1"] = "SING ( LSI1-2F )",
                ["SING_2"] = "SING ( LSI2-1F )",
                ["SING_3"] = "SING ( LSI2-2F )",
                ["SING_4"] = "SING ( LSI2-3F )",
                ["SING_5"] = "SING ( LSI3-3F )",
                ["FT_1"] = "FT ( LSI1-2F )",
                ["FT_2"] = "FT ( LSI2-1F )",
                ["FT_3"] = "FT ( LSI2-2F )",
                ["FT_4"] = "FT ( LSI2-3F )",
                ["FT_5"] = "FT ( LSI3-3F )",
                ["TP_1"] = "TP ( LSI1-2F )",
                ["TP_2"] = "TP ( LSI2-1F )",
                ["TP_3"] = "TP ( LSI2-2F )",
                ["TP_4"] = "TP ( LSI2-3F )",
                ["TP_5"] = "TP ( LSI3-3F )",
                ["EDS_1"] = "EDS ( LSI3-4F )",
                ["MEMS_1"] = "MEMS ( LSI3-2F )",
            };
            return map.TryGetValue(key, out var title) ? title : key;
        }
    }
}