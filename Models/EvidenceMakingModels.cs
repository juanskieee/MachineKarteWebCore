// Models/EvidenceMakingModels.cs
// Converted from Form4.vb — E-Karte System
// C# ASP.NET Core

namespace MachineKarte.Web.Models
{
    /// <summary>
    /// Request payload for generating an evidence PDF.
    /// Mirrors EMakingPrint(ProcessCase, bmid) parameters.
    /// </summary>
    public class EvidenceGenerateRequest
    {
        public string Process { get; set; } = string.Empty;   // DC, DB, WB, MP, PL, FL, SING, FT, TP, EDS, MEMS
        public int BmId { get; set; }
    }

    /// <summary>
    /// One evidence image+remark row from gridEvidenceMaking_Ver3.
    /// Maps to: remarksData(n), imagesData(n) arrays in Form4.vb.
    /// </summary>
    public class EvidenceImageRow
    {
        public string Description { get; set; } = string.Empty;  // BMEvidence_Desc
        public string ImageFile { get; set; } = string.Empty;  // BMEvidence_ImgFile
    }

    /// <summary>
    /// All five evidence category groups fetched per BM.
    /// Mirrors mSQLStr1..5 in Form4.vb.
    /// </summary>
    public class EvidenceImageGroups
    {
        // TOP 3 — PROBLEM POINT / PHENOMENON
        public EvidenceImageRow[] ProblemPoint { get; set; } = new EvidenceImageRow[3];
        // TOP 2 — ACTION TAKEN / EVIDENCE AFTER ADJUSTMENT
        public EvidenceImageRow[] ActionTaken { get; set; } = new EvidenceImageRow[2];
        // TOP 2 — PARTS REPLACED BEFORE / AFTER
        public EvidenceImageRow[] PartsReplaced { get; set; } = new EvidenceImageRow[2];
        // TOP 2 — DETAILS OF DIS-ASSEMBLY / RE-ASSEMBLY
        public EvidenceImageRow[] DisAssembly { get; set; } = new EvidenceImageRow[2];
        // TOP 3 — BEFORE AND AFTER QUALITY CONFIRMATION
        public EvidenceImageRow[] QualityConfirm { get; set; } = new EvidenceImageRow[3];
    }

    /// <summary>
    /// TSC confirmation flags from the process-specific view (e.g. viewDCEMaking_Ver3).
    /// Mirrors tsc1..tsc10 variables, BMEvidence_TCConfirm1..9, BMRouteNo, BMVersionNo.
    /// </summary>
    public class TscConfirmation
    {
        public string Tsc1 { get; set; } = string.Empty;
        public string Tsc2 { get; set; } = string.Empty;
        public string Tsc3 { get; set; } = string.Empty;
        public string Tsc4 { get; set; } = string.Empty;
        public string Tsc5 { get; set; } = string.Empty;
        public string Tsc6 { get; set; } = string.Empty;
        public string Tsc7 { get; set; } = string.Empty;
        public string Tsc8 { get; set; } = string.Empty;
        public string Tsc9 { get; set; } = string.Empty;
        public string RouteNo { get; set; } = string.Empty;  // BMRouteNo
        public string VersionNo { get; set; } = string.Empty;  // BMVersionNo

        // SING-only extra fields
        public string SignalTowerKanban { get; set; } = string.Empty;
        public string SafetyTags { get; set; } = string.Empty;
        public string Bypass { get; set; } = string.Empty;
    }

    /// <summary>
    /// Machine header data written to the top of the Excel template.
    /// Mirrors the ProcessTableData SELECT in Form4.vb.
    /// </summary>
    public class EvidenceMachineHeader
    {
        public string McNo { get; set; } = string.Empty;
        public string McType { get; set; } = string.Empty;
        public string PkgName { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string LotNo { get; set; } = string.Empty;
        public string PmConfirmBy { get; set; } = string.Empty;  // PMConfirmGLOPReqBy
        public string OccurrenceDate { get; set; } = string.Empty;
        public string StartOfRepair { get; set; } = string.Empty;
        public string EndOfRepair { get; set; } = string.Empty;
    }

    /// <summary>
    /// All data needed to populate the Excel evidence template.
    /// Aggregates all queries from Form4.vb into one object.
    /// </summary>
    public class EvidenceTemplateData
    {
        public string Process { get; set; } = string.Empty;
        public int BmId { get; set; }
        public EvidenceMachineHeader Header { get; set; } = new();
        public EvidenceImageGroups Images { get; set; } = new();
        public TscConfirmation Tsc { get; set; } = new();
        public bool IsSing { get; set; }  // drives different cell addresses
    }

    /// <summary>
    /// Result returned to the browser after PDF generation.
    /// </summary>
    public class EvidenceGenerateResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;  // e.g. /TempData/GeneratedPdf/DCEvidence_123.pdf
        public string FileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Process → Excel template + DB view mapping table.
    /// Mirrors the Select Case ProcessGroup in Form4.vb.
    /// </summary>
    public static class ProcessTemplateMap
    {
        public record ProcessConfig(
            string TemplateFile,     // e.g. EvidenceMaking_DC.xlsx
            string EvidenceView,     // e.g. viewDCEMaking_Ver3
            string DataView          // e.g. ViewDCData_Ver4
        );

        public static readonly Dictionary<string, ProcessConfig> Map =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["DC"] = new("EvidenceMaking_DC.xlsx", "viewDCEMaking_Ver3", "ViewDCData_Ver4"),
                ["DB"] = new("EvidenceMaking_DB.xlsx", "viewDBEMaking_Ver2", "ViewDBData_Ver4"),
                ["WB"] = new("EvidenceMaking_WB.xlsx", "viewWBEMaking_Ver2", "ViewWBData_Ver4"),
                ["MP"] = new("EvidenceMaking_MP.xlsx", "viewMPEMaking_Ver2", "ViewMPData_Ver4"),
                ["PL"] = new("EvidenceMaking_PL.xlsx", "viewPLEMaking_Ver2", "ViewPLData_Ver2"),
                ["FL"] = new("EvidenceMaking_FL.xlsx", "viewFLEMaking_ver2", "ViewFLData_V2"),
                ["SING"] = new("EvidenceMaking_SING.xlsx", "viewSINGEMaking", "ViewSINGData"),
                ["FT"] = new("EvidenceMaking_FT.xlsx", "viewFTEMaking_Ver2", "ViewFTData"),
                ["TP"] = new("EvidenceMaking_TP.xlsx", "viewTPEMaking_Ver2", "ViewTPData"),
                ["EDS"] = new("EvidenceMaking_EDS.xlsx", "viewEDSEMaking", "viewedsdata_ver2"),
                ["MEMS"] = new("EvidenceMaking_MEMS.xlsx", "viewMEMSEMaking", "ViewMEMSData"),
            };

        public static ProcessConfig? Get(string process) =>
            Map.TryGetValue(process, out var cfg) ? cfg : null;
    }

    /// <summary>
    /// Excel cell placement instruction for one image or text value.
    /// Used by EvidenceMakingService to write to the right cell per process.
    /// </summary>
    public class CellPlacement
    {
        public string Cell { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsText { get; set; }
    }
}