// Models/OcapViewerModels.cs
namespace MachineKarte.Web.Models
{
    // ── Request ───────────────────────────────────────────────────────────────
    public class OcapViewerRequest
    {
        public int BmId { get; set; }
        public string BmProcess { get; set; } = string.Empty;
        public string BmCaseId { get; set; } = string.Empty;
    }

    // ── One OCAP row (5 columns) ──────────────────────────────────────────────
    public class OcapRow
    {
        public string CheckItem { get; set; } = string.Empty;   // BMOCAPName
        public string CheckingMethod { get; set; } = string.Empty;   // BMOCAPCheckingMethod
        public string TreatmentAction { get; set; } = string.Empty;   // BMOCAPTreatmentAction
        public string Judgement { get; set; } = string.Empty;   // from OCAPData
        public string Remarks { get; set; } = string.Empty;   // from OCAPData
    }

    // ── Response ──────────────────────────────────────────────────────────────
    public class OcapViewerResult
    {
        public List<OcapRow> Rows { get; set; } = new();
    }
}