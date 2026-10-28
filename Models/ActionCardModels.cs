// Models/ActionCardModels.cs
// Converted from Form_Input.vb — E-Karte System
// C# ASP.NET Core

namespace MachineKarte.Web.Models
{
    /// <summary>
    /// Identifies which table to read/write based on the current user type.
    /// Mirrors the Select Case / If-ElseIf TableTemp logic in Form_Input.vb.
    /// </summary>
    public static class ActionCardTableMap
    {
        public static string GetTable(string userType) => userType.ToUpper() switch
        {
            "TE" => "MachineKarte_ActionCard_TE",
            "CC" => "MachineKarte_ActionCard_CC",
            "VIS" => "MachineKarte_ActionCard_VIS",
            "EEE" => "MachineKarte_ActionCard_EEE",
            _ => "MachineKarte_ActionCard"          // PM default
        };

        // Label18/21/24 text: "PM Reply" vs "TE Reply"
        public static string ReplyLabel(string userType) =>
            userType.Equals("TE", StringComparison.OrdinalIgnoreCase)
                ? "TE Reply" : "PM Reply";
    }

    /// <summary>
    /// Machine status options for cbStatus dropdown.
    /// Mirrors the WinForms ComboBox items and the Color → status mapping
    /// in LoadData(bmid, status, mcno, ColorT, HeaderCase).
    /// </summary>
    public static class McStatusOptions
    {
        public static readonly string[] All =
        {
            "-SELECT-",
            "NO RE-OCCURRENCE AFTER 7 DAYS",
            "RUNNING (LOT EVALUATION)",
            "STOP (ONGOING INVESTIGATION)",
            "RUNNING (UNDER 100% INSPECTION)",
            "RUNNING (ONGOING INVESTIGATION/MONITORING)",
            "STOP (WAITING For INSPECTION RESULTS)",
            "STOP (WAITING FOR QC INSTRUCTION)"
        };

        /// <summary>
        /// Maps the WinForms Color.Name used on a BM card to a status string.
        /// Mirrors the ColorT.Name If-ElseIf block in LoadData().
        /// </summary>
        public static string FromColorName(string colorName) => colorName switch
        {
            "Green" => "NO RE-OCCURRENCE AFTER 7 DAYS",
            "LightGreen" => "RUNNING (LOT EVALUATION)",
            "Red" => "STOP (ONGOING INVESTIGATION)",
            "DarkOrange" => "RUNNING (UNDER 100% INSPECTION)",
            "DarkGray" => "RUNNING (ONGOING INVESTIGATION/MONITORING)",
            "Violet" => "STOP (WAITING For INSPECTION RESULTS)",
            "Aqua" => "STOP (WAITING FOR QC INSTRUCTION)",
            _ => "-SELECT-"
        };
    }

    /// <summary>
    /// Request to open/load an action card.
    /// Mirrors LoadData(bmid, status, mcno, ColorT, HeaderCase) parameters.
    /// </summary>
    public class ActionCardLoadRequest
    {
        public string BmId { get; set; } = string.Empty;
        public string McNo { get; set; } = string.Empty;
        public string UserType { get; set; } = "PM";
        /// <summary>"New" or "Existing"</summary>
        public string Status { get; set; } = "New";
        /// <summary>Color name from the BM card (e.g. "Green", "Red").</summary>
        public string ColorName { get; set; } = string.Empty;
        public string HeaderCase { get; set; } = string.Empty;
    }

    /// <summary>
    /// Full action card data — all fields from MachineKarte_ActionCard* tables.
    /// Mirrors every tbXxx / cbStatus control in Form_Input.Designer.vb.
    /// </summary>
    public class ActionCardData
    {
        // ── Header (read-only display) ──────────────────────────────
        public string BmId { get; set; } = string.Empty;   // lblBMID
        public string MachineNo { get; set; } = string.Empty;   // tbMachineNo
        public string DateCard { get; set; } = string.Empty;   // tbDate
        public string UserType { get; set; } = "PM";
        public string Status { get; set; } = "New";           // "New" | "Existing"
        public string ColorName { get; set; } = string.Empty;
        public string HeaderCase { get; set; } = string.Empty;

        // ── Section 1: Problem / Action (Salmon labels) ─────────────
        public string ProblemContent { get; set; } = string.Empty;  // tbProblemContent
        public string InCharge { get; set; } = string.Empty;  // tbInCharge
        public string InvestigationDone { get; set; } = string.Empty;  // tbInvestigation
        public string Action { get; set; } = string.Empty;  // tbAction

        // ── Machine status ───────────────────────────────────────────
        public string McStatus { get; set; } = "-SELECT-";  // cbStatus

        // ── Section 2: Division comments × 3 (LimeGreen labels) ────
        // Comment 1
        public string Comment { get; set; } = string.Empty;   // tbComment
        public string Date1 { get; set; } = string.Empty;   // tbdate_1  (auto-set on blur)
        public string Incharge1 { get; set; } = string.Empty;   // tbIncharge_1

        // Comment 2
        public string Comment2 { get; set; } = string.Empty;   // tbComment2
        public string Date2 { get; set; } = string.Empty;   // tbdate_2
        public string Incharge2 { get; set; } = string.Empty;   // tbIncharge_2

        // Comment 3
        public string Comment3 { get; set; } = string.Empty;   // tbComment3
        public string Date3 { get; set; } = string.Empty;   // tbdate_3
        public string Incharge3 { get; set; } = string.Empty;   // tbIncharge_3

        // ── Section 3: PM / TE Replies × 3 ─────────────────────────
        // Reply 1
        public string PMReply1 { get; set; } = string.Empty;  // tbReply1
        public string PMIncharge1 { get; set; } = string.Empty;  // tbReplyEmp1
        public string PMDate1 { get; set; } = string.Empty;  // tbReplyDate1  (auto-set on blur)

        // Reply 2
        public string PMReply2 { get; set; } = string.Empty;  // tbReply2
        public string PMIncharge2 { get; set; } = string.Empty;  // tbReplyEmp2
        public string PMDate2 { get; set; } = string.Empty;  // tbReplyDate2

        // Reply 3
        public string PMReply3 { get; set; } = string.Empty;  // tbReply3
        public string PMIncharge3 { get; set; } = string.Empty;  // tbReplyEmp3
        public string PMDate3 { get; set; } = string.Empty;  // tbReplyDate3
    }

    /// <summary>
    /// Save/update request payload sent from the browser via AJAX POST.
    /// Mirrors btnSave_Click → SaveData() / UpdateData().
    /// </summary>
    public class ActionCardSaveRequest
    {
        public ActionCardData Card { get; set; } = new();
        /// <summary>
        /// "New" triggers INSERT; "Existing" triggers UPDATE.
        /// Mirrors StatusID field in Form_Input.vb.
        /// </summary>
        public string StatusId { get; set; } = "New";
    }
}