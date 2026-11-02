// Services/OcapViewerService.cs
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class OcapViewerService
    {
        private readonly OcapViewerRepository _repo;

        public OcapViewerService(OcapViewerRepository repo) => _repo = repo;

        public async Task<OcapViewerResult> LoadAsync(
            OcapViewerRequest req, CancellationToken cancellationToken = default)
        {
            var tableName = GetTableName(req.BmProcess);
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException($"Unknown process code: {req.BmProcess}");

            // 1. Load the OCAP checklist rows
            var rows = await _repo.GetOcapRowsAsync(req.BmCaseId, req.BmProcess, cancellationToken);

            // 2. Overlay saved Judgement / Remarks from OCAPData (pipe|comma-delimited)
            //    Format: "remark1,remark2,…|result1,result2,…"
            //    VB: arrOCAPData(0) → Remarks, arrOCAPData(1) → Judgement
            var ocapData = await _repo.GetOcapDataAsync(req.BmId, tableName, cancellationToken);
            if (!string.IsNullOrEmpty(ocapData))
            {
                var parts = ocapData.Split('|');
                if (parts.Length == 2)
                {
                    var remarks = parts[0].Split(',');
                    var judgements = parts[1].Split(',');

                    for (int i = 0; i < rows.Count; i++)
                    {
                        if (i < remarks.Length)
                            rows[i].Remarks = remarks[i];
                        if (i < judgements.Length)
                            rows[i].Judgement = judgements[i];
                    }
                }
            }

            return new OcapViewerResult { Rows = rows };
        }

        // Form6 reads the selected process's maintenance table; keep identifiers allowlisted.
        private static string GetTableName(string process) => process switch
        {
            "DC" => "DCBMMaintenance",
            "DB" => "DBBMMaintenance",
            "WB" => "WBBMMaintenance",
            "MP" => "MPBMMaintenance",
            "PL" => "PLBMMaintenance",
            "FL" => "FLBMMaintenance",
            "SING" => "SINGBMMaintenance",
            "ATC" => "ATCBMMaintenance",
            "FT" => "FTBMMaintenance",
            "TP" => "TPBMMaintenance",
            "EDS" => "EDSBMMaintenance",
            "MEMS" => "MEMSBMMaintenance",
            _ => string.Empty
        };
    }
}
