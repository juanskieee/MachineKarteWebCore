using System.Collections.Generic;

namespace MachineKarte.Web.Models
{
    // ─── Header / meta data from ViewTEData ──────────────────────────────────
    public class TeEvidenceHeader
    {
        public string MCNo { get; set; } = "";
        public string TesterNo { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string LotNo { get; set; } = "";
        public string TestNoNG { get; set; } = "";
        public string WaferNo { get; set; } = "";
        public string StartOfRepair { get; set; } = "";
        public string EndOfRepair { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string TEAssign { get; set; } = "";   // PMConfirmGLOPReqBy [+RepairedByEnd]

        // From mSQLStr_Data row
        public string Ideas { get; set; } = "";
        public string TelekoCheckingData { get; set; } = "";  // pipe+comma delimited QC table
        public string QualityDefect { get; set; } = "";
    }

    // ─── One evidence image slot ──────────────────────────────────────────────
    public class TeEvidenceImage
    {
        public string Category { get; set; } = "";
        public string Condition { get; set; } = "";   // "BEFORE" | "AFTER"
        public string ImgFile { get; set; } = "";   // server-relative path like ~/folder/file.png
    }

    // ─── Category description (text that goes into the Excel cells) ──────────
    public class TeEvidenceCategoryDesc
    {
        public string Category { get; set; } = "";
        public string Condition { get; set; } = "";
        public string Desc { get; set; } = "";
    }

    // ─── Teleko evidence image (row from mSQLStr7_A) ─────────────────────────
    public class TelekoEvidenceImage
    {
        public string TelekoEvidence { get; set; } = "";   // server-relative image path
    }

    // ─── Quality-condition images (BEFORE / AFTER / IDEAL, ws_3) ────────────
    public class QualityConditionImage
    {
        public string Category { get; set; } = "";
        public string ImgFile { get; set; } = "";
    }

    // ─── Full payload assembled by the service ────────────────────────────────
    public class TeEvidencePackage
    {
        public TeEvidenceHeader Header { get; set; } = new();
        public List<TeEvidenceCategoryDesc> CategoryDescs { get; set; } = new();
        public Dictionary<string, List<string>> Images { get; set; } = new();
        // key = "{CATEGORY}_{CONDITION}", value = ordered list of server-relative img paths (up to 6)
        // e.g.  "PHENOMENON_BEFORE" → ["/images/...", ...]

        public List<TelekoEvidenceImage> TelekoImages { get; set; } = new();
        public List<QualityConditionImage> QualityImages { get; set; } = new();
    }
}