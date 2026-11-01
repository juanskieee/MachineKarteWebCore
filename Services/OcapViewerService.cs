// Services/OcapViewerService.cs
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class OcapViewerService
    {
        private readonly OcapViewerRepository _repo;

        public OcapViewerService(OcapViewerRepository repo) => _repo = repo;

        public OcapViewerResult Load(OcapViewerRequest req)
        {
            var tableName = GetTableName(req.BmProcess);
            if (string.IsNullOrEmpty(tableName))
                throw new ArgumentException($"Unknown process code: {req.BmProcess}");

            // 1. Load the OCAP checklist rows
            var dt = _repo.GetOcapRows(req.BmCaseId, req.BmProcess);

            var rows = new List<OcapRow>();
            foreach (System.Data.DataRow r in dt.Rows)
            {
                rows.Add(new OcapRow
                {
                    CheckItem = r["BMOCAPName"].ToString() ?? string.Empty,
                    CheckingMethod = r["BMOCAPCheckingMethod"].ToString() ?? string.Empty,
                    TreatmentAction = r["BMOCAPTreatmentAction"].ToString() ?? string.Empty,
                    Judgement = "test1",
                    Remarks = "test2"
                });
            }

            // 2. Overlay saved Judgement / Remarks from OCAPData (pipe|comma-delimited)
            //    Format: "remark1,remark2,…|result1,result2,…"
            //    VB: arrOCAPData(0) → Remarks, arrOCAPData(1) → Judgement
            var ocapData = _repo.GetOcapData(req.BmId, tableName);
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

        // ── Process → table name (same map as RawDataService) ────────────────
        private static string GetTableName(string process) => process switch
        {
            "DC" => "DCBMMaintenance",
            "DB" => "DBBMMaintenance",
            "WB" => "WBBMMaintenance",
            "MP" => "MPBMMaintenance",
            "PL" => "PLBMMaintenance",
            "FL" => "FLBMMaintenance",
            "FT" => "FTBMMaintenance",
            "TP" => "TPBMMaintenance",
            "EDS" => "EDSBMMaintenance",
            "MEMS" => "MEMSBMaintenance",
            _ => string.Empty
        };
    }
}