namespace MachineKarte.Web.Models
{
    // Holds every column returned by ViewTEData_Test that Form9 writes into the report.
    public class BmReportData
    {
        public int BmId { get; set; }
        public string Process { get; set; } = "";          // "EDS" or anything else → TETemplate

        // --- Header / identification ---
        public string TesterNo { get; set; } = "";
        public string MCNo { get; set; } = "";
        public string CellLine { get; set; } = "";
        public string DeviceName { get; set; } = "";
        public string PKGName { get; set; } = "";
        public string LOTNo { get; set; } = "";
        public string DUT { get; set; } = "";
        public string BoxNo { get; set; } = "";
        public string SubDUT { get; set; } = "";
        public string Freq { get; set; } = "";
        public string Designation { get; set; } = "";
        public string SocketBoard { get; set; } = "";
        public string ShipmentDate { get; set; } = "";

        // --- PM Category / BM content ---
        public string PMCategoryID { get; set; } = "";
        public string PMCategory { get; set; } = "";       // looked up separately
        public string BMContentID { get; set; } = "";
        public string BMContent { get; set; } = "";        // looked up: BMContent table
        public string TEContent { get; set; } = "";        // looked up: TEContent table
        public string BMCaseID { get; set; } = "";
        public string BMCase { get; set; } = "";
        public string BMUnitID { get; set; } = "";
        public string BMTEUnit { get; set; } = "";
        public string BMPointID { get; set; } = "";
        public string BMPoint { get; set; } = "";
        public string BMWorkContentID { get; set; } = "";
        public string BMWorkContent { get; set; } = "";
        public string BMCauseID { get; set; } = "";
        public string BMCause { get; set; } = "";
        public string PrevMeasureID { get; set; } = "";
        public string PREVMeasure { get; set; } = "";

        // --- Setup / OPPosition / MCStatus / TestFlow ---
        public string Setup { get; set; } = "";
        public string OPPosition { get; set; } = "";
        public string MCStatus { get; set; } = "";
        public string TestFlow { get; set; } = "";
        public string OPRemarks { get; set; } = "";

        // --- Counts ---
        public string TestNoNG { get; set; } = "";
        public string LotNG { get; set; } = "";
        public string KeepSample { get; set; } = "";
        public string BinCat { get; set; } = "";

        // --- Personnel ---
        public string RequestedBy { get; set; } = "";
        public string GLConfirmOPReqBy { get; set; } = "";
        public string CurrentTE { get; set; } = "";
        public string PMConfirmGLOPReqBy { get; set; } = "";
        public string RepairedByEnd { get; set; } = "";
        public string GLRepairConfirmationBy { get; set; } = "";
        public string EndBMBy { get; set; } = "";
        public string TESign { get; set; } = "";
        public string PDSign1 { get; set; } = "";
        public string d_TEJudgment { get; set; } = "";

        // --- Dates / times ---
        public string OccurrenceDate { get; set; } = "";
        public string TEReceived { get; set; } = "";
        public string StartOfRepair { get; set; } = "";
        public string EndOfRepair { get; set; } = "";
        public string GLConfirmOPReqDate { get; set; } = "";
        public string PMConfirmGLOPReqDate { get; set; } = "";
        public string SKCTPDOnHoldDate { get; set; } = "";
        public string SKCTPDReleaseDate { get; set; } = "";
        public string TotalSKCTPDtime { get; set; } = "";
        public string WaitingTime1 { get; set; } = "";
        public string RepairTime1 { get; set; } = "";
        public string MonitoringTime1 { get; set; } = "";
        public string TotalStopTime { get; set; } = "";

        // --- 5-why ---
        public string Why1 { get; set; } = "";
        public string Why2 { get; set; } = "";
        public string Why3 { get; set; } = "";
        public string Why4 { get; set; } = "";
        public string PMUnknown { get; set; } = "";

        // --- Quality / Accreditor ---
        public string Priority { get; set; } = "";
        public string GLConfi { get; set; } = "";
        public string GLConfi_Date { get; set; } = "";
        public string RequestTo { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string ChangingPoint { get; set; } = "";
        public string MittomonaiClaim { get; set; } = "";
        public string AccreditorRemarks { get; set; } = "";
        public string AccreditorRemarks_Max { get; set; } = "";
        public string AccreditorPMReply { get; set; } = "";
        public string BMStatusID { get; set; } = "";
        public string AcredPMCategoryID { get; set; } = "";
        public string AcredBMCaseID { get; set; } = "";
        public string AcredBMMCUnitID { get; set; } = "";
        public string AcredBMWorkContentID { get; set; } = "";

        // --- Signals ---
        public string AQI { get; set; } = "";
        public string Andon { get; set; } = "";
        public string GLAndon { get; set; } = "";
        public string GLAndon2 { get; set; } = "";

        // --- Serialised composite fields ---
        public string FourM { get; set; } = "";
        public string FourMData { get; set; } = "";
        public string AdjustedRecordData { get; set; } = "";
        public string CheckItemData { get; set; } = "";
        public string QualityCheckData { get; set; } = "";
        public string QcJudgement { get; set; } = "";
        public string CPData { get; set; } = "";
        public string UntestedSample { get; set; } = "";
        public string ItemTotalQTYData { get; set; } = "";
        public string ExternalAccessories { get; set; } = "";
        public string ExternalAccessories_B { get; set; } = "";
        public string ExternalAccessoriesData { get; set; } = "";

        // --- Changed parts (up to 4) ---
        public string ChangePartsName1 { get; set; } = "";
        public string ChangePartsType1 { get; set; } = "";
        public string ChangePartsQty1 { get; set; } = "";
        public string ChangePartsName2 { get; set; } = "";
        public string ChangePartsType2 { get; set; } = "";
        public string ChangePartsQty2 { get; set; } = "";
        public string ChangePartsName3 { get; set; } = "";
        public string ChangePartsType3 { get; set; } = "";
        public string ChangePartsQty3 { get; set; } = "";
        public string ChangePartsName4 { get; set; } = "";
        public string ChangePartsType4 { get; set; } = "";
        public string ChangePartsQty4 { get; set; } = "";

        // --- WD diary entries (worksheets 1, up to 10 rows) ---
        public string WD1 { get; set; } = ""; public string WDate1 { get; set; } = ""; public string WDDate1End { get; set; } = ""; public string WAcc1 { get; set; } = "";
        public string WD2 { get; set; } = ""; public string WDate2 { get; set; } = ""; public string WDDate2End { get; set; } = ""; public string WAcc2 { get; set; } = "";
        public string WD3 { get; set; } = ""; public string WDate3 { get; set; } = ""; public string WDDate3End { get; set; } = ""; public string WAcc3 { get; set; } = "";
        public string WD4 { get; set; } = ""; public string WDate4 { get; set; } = ""; public string WDDate4End { get; set; } = ""; public string WAcc4 { get; set; } = "";
        public string WD5 { get; set; } = ""; public string WDate5 { get; set; } = ""; public string WDDate5End { get; set; } = ""; public string WAcc5 { get; set; } = "";
        public string WD6 { get; set; } = ""; public string WDate6 { get; set; } = ""; public string WDDate6End { get; set; } = ""; public string WAcc6 { get; set; } = "";
        public string WD7 { get; set; } = ""; public string WDate7 { get; set; } = ""; public string WDDate7End { get; set; } = ""; public string WAcc7 { get; set; } = "";
        public string WD8 { get; set; } = ""; public string WDate8 { get; set; } = ""; public string WDDate8End { get; set; } = ""; public string WAcc8 { get; set; } = "";
        public string WD9 { get; set; } = ""; public string WDate9 { get; set; } = ""; public string WDDate9End { get; set; } = ""; public string WAcc9 { get; set; } = "";
        public string WD10 { get; set; } = ""; public string WDate10 { get; set; } = ""; public string WDDate10End { get; set; } = ""; public string WAcc10 { get; set; } = "";

        // --- Various QC / inspection misc ---
        public string QAction { get; set; } = "";
        public string ReqTestNoNG { get; set; } = "";
        public string StopAddressX { get; set; } = "";
        public string StopAddressY { get; set; } = "";
        public string TEX { get; set; } = "";
        public string TEY { get; set; } = "";
        public string PDGLX { get; set; } = "";
        public string PDGLY { get; set; } = "";
        public string WaferMapAnswer { get; set; } = "";
        public string WaferMapNOD { get; set; } = "";
        public string AcredINSPN { get; set; } = "";
        public string WDSetupQty { get; set; } = "";
        public string WDWIPQty { get; set; } = "";
        public string WDBoxBoardQty { get; set; } = "";
        public string DropICCheck { get; set; } = "";
        public string DropICCheckAfterWork { get; set; } = "";
        public string DropICCheckBy { get; set; } = "";
        public string DropICTreatedBy { get; set; } = "";
        public string LotSlipvsOIS { get; set; } = "";
        public string LoadOIS { get; set; } = "";
        public string LoadTestFlow { get; set; } = "";
        public string FTADisk { get; set; } = "";
        public string LastTestNoCheck { get; set; } = "";
        public string BoxChangedfrom { get; set; } = "";
        public string BoxChangedTo { get; set; } = "";
        public string TesterChangeFrom { get; set; } = "";
        public string TesterChangeTo { get; set; } = "";
        public string QItemsNoIC { get; set; } = "";
        public string QItemsYN1 { get; set; } = "";
        public string QItemsYN2 { get; set; } = "";
        public string QItemsYN3 { get; set; } = "";
        public string QItemsYN4 { get; set; } = "";
        public string QItemsYN5 { get; set; } = "";
        public string EDSCS1Pprod { get; set; } = "";
        public string OverDriveBefore { get; set; } = "";
        public string OverDriveAFter { get; set; } = "";
        public string EDSTotalDutNG { get; set; } = "";
        public string EDSTotalDutCOUNT { get; set; } = "";
        public string ProbeRingBefore { get; set; } = "";
        public string ProbeRingAfter { get; set; } = "";
        public string IronPlateBefore { get; set; } = "";
        public string IronPlateAfter { get; set; } = "";
        public string StopAddAxisX { get; set; } = "";
        public string StopAddAxisY { get; set; } = "";
        public string StartAddAxisX { get; set; } = "";
        public string StartAddAxisY { get; set; } = "";

        // --- VR Numbers (from VRNumber table) ---
        public string VRNumber1 { get; set; } = "";
        public string VRNumber2 { get; set; } = "";
        public string VRNumber3 { get; set; } = "";
    }
}