using System.Data;
using Microsoft.Data.SqlClient;
using MachineKarte.Web.Models;

namespace MachineKarte.Web.Repositories
{
    public class BmReportRepository
    {
        private readonly string _connStr;

        public BmReportRepository(IConfiguration cfg)
        {
            _connStr = cfg.GetConnectionString("DBConnection")!;
        }

        // -----------------------------------------------------------------------
        // Main record from ViewTEData_Test
        // -----------------------------------------------------------------------
        public async Task<BmReportData?> GetReportDataAsync(int bmId)
        {
            const string sql = "SELECT TOP 1 * FROM ViewTEData_Test WHERE bmID = @bmID";

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmID", bmId);

            await using var dr = await cmd.ExecuteReaderAsync();
            if (!await dr.ReadAsync())
                return null;

            var d = new BmReportData { BmId = bmId };

            string S(string col) =>
                dr.GetOrdinal(col) >= 0 && !dr.IsDBNull(dr.GetOrdinal(col))
                    ? dr[col].ToString()!
                    : "";

            d.TesterNo = S("TesterNo"); d.MCNo = S("MCNo"); d.CellLine = S("CellLine");
            d.DeviceName = S("DeviceName"); d.PKGName = S("PKGName"); d.LOTNo = S("LOTNo");
            d.DUT = S("DUT"); d.BoxNo = S("BoxNo"); d.SubDUT = S("SubDUT");
            d.Freq = S("Freq"); d.Designation = S("Designation"); d.SocketBoard = S("SocketBoard");
            d.ShipmentDate = S("ShipmentDate");
            d.PMCategoryID = S("PMCategoryID"); d.BMContentID = S("BMContentID");
            d.BMCaseID = S("BMCaseID"); d.BMUnitID = S("BMUnitID");
            d.BMPointID = S("BMPointID"); d.BMWorkContentID = S("BMWorkContentID");
            d.BMCauseID = S("BMCauseID"); d.PrevMeasureID = S("PrevMeasureID");
            d.Setup = S("Setup"); d.OPPosition = S("OPPosition");
            d.MCStatus = S("MCStatus"); d.TestFlow = S("TestFlow"); d.OPRemarks = S("OPRemarks");
            d.TestNoNG = S("TestNoNG"); d.LotNG = S("LotNG");
            d.KeepSample = S("KeepSample"); d.BinCat = S("BinCat");
            d.RequestedBy = S("RequestedBy"); d.GLConfirmOPReqBy = S("GLConfirmOPReqBy");
            d.CurrentTE = S("CurrentTE"); d.PMConfirmGLOPReqBy = S("PMConfirmGLOPReqBy");
            d.RepairedByEnd = S("RepairedByEnd"); d.GLRepairConfirmationBy = S("GLRepairConfirmationBy");
            d.EndBMBy = S("EndBMBy"); d.TESign = S("TESign"); d.PDSign1 = S("PDSign1");
            d.d_TEJudgment = S("d_TEJudgment");
            d.OccurrenceDate = S("OccurrenceDate"); d.TEReceived = S("TEReceived");
            d.StartOfRepair = S("StartOfRepair"); d.EndOfRepair = S("EndOfRepair");
            d.GLConfirmOPReqDate = S("GLConfirmOPReqDate");
            d.PMConfirmGLOPReqDate = S("PMConfirmGLOPReqDate");
            d.SKCTPDOnHoldDate = S("SKCTPDOnHoldDate"); d.SKCTPDReleaseDate = S("SKCTPDReleaseDate");
            d.TotalSKCTPDtime = S("TotalSKCTPDtime");
            d.WaitingTime1 = S("WaitingTime1"); d.RepairTime1 = S("RepairTime1");
            d.MonitoringTime1 = S("MonitoringTime1"); d.TotalStopTime = S("TotalStopTime");
            d.Why1 = S("Why1"); d.Why2 = S("Why2"); d.Why3 = S("Why3"); d.Why4 = S("Why4");
            d.PMUnknown = S("PMUnknown");
            d.Priority = S("Priority"); d.GLConfi = S("GLConfi");
            d.GLConfi_Date = S("GLConfi_Date"); d.RequestTo = S("RequestTo");
            d.Remarks = S("Remarks"); d.ChangingPoint = S("ChangingPoint");
            d.MittomonaiClaim = S("MittomonaiClaim");
            d.AccreditorRemarks = S("AccreditorRemarks");
            d.AccreditorRemarks_Max = S("AccreditorRemarks_Max");
            d.AccreditorPMReply = S("AccreditorPMReply");
            d.BMStatusID = S("BMStatusID"); d.AcredPMCategoryID = S("AcredPMCategoryID");
            d.AcredBMCaseID = S("AcredBMCaseID"); d.AcredBMMCUnitID = S("AcredBMMCUnitID");
            d.AcredBMWorkContentID = S("AcredBMWorkContentID");
            d.AQI = S("AQI"); d.Andon = S("Andon");
            d.GLAndon = S("GLAndon"); d.GLAndon2 = S("GLAndon2");
            d.FourM = S("FourM"); d.FourMData = S("FourMData");
            d.AdjustedRecordData = S("AdjustedRecordData");
            d.CheckItemData = S("CheckItemData"); d.QualityCheckData = S("QualityCheckData");
            d.QcJudgement = S("QcJudgement"); d.CPData = S("CPData");
            d.UntestedSample = S("UntestedSample"); d.ItemTotalQTYData = S("ItemTotalQTYData");
            d.ExternalAccessories = S("ExternalAccessories");
            d.ExternalAccessories_B = S("ExternalAccessories_B");
            d.ExternalAccessoriesData = S("ExternalAccessoriesData");
            d.ChangePartsName1 = S("ChangePartsName1"); d.ChangePartsType1 = S("ChangePartsType1"); d.ChangePartsQty1 = S("ChangePartsQty1");
            d.ChangePartsName2 = S("ChangePartsName2"); d.ChangePartsType2 = S("ChangePartsType2"); d.ChangePartsQty2 = S("ChangePartsQty2");
            d.ChangePartsName3 = S("ChangePartsName3"); d.ChangePartsType3 = S("ChangePartsType3"); d.ChangePartsQty3 = S("ChangePartsQty3");
            d.ChangePartsName4 = S("ChangePartsName4"); d.ChangePartsType4 = S("ChangePartsType4"); d.ChangePartsQty4 = S("ChangePartsQty4");
            d.WD1 = S("WD1"); d.WDate1 = S("WDate1"); d.WDDate1End = S("WDDate1End"); d.WAcc1 = S("WAcc1");
            d.WD2 = S("WD2"); d.WDate2 = S("WDate2"); d.WDDate2End = S("WDDate2End"); d.WAcc2 = S("WAcc2");
            d.WD3 = S("WD3"); d.WDate3 = S("WDate3"); d.WDDate3End = S("WDDate3End"); d.WAcc3 = S("WAcc3");
            d.WD4 = S("WD4"); d.WDate4 = S("WDate4"); d.WDDate4End = S("WDDate4End"); d.WAcc4 = S("WAcc4");
            d.WD5 = S("WD5"); d.WDate5 = S("WDate5"); d.WDDate5End = S("WDDate5End"); d.WAcc5 = S("WAcc5");
            d.WD6 = S("WD6"); d.WDate6 = S("WDate6"); d.WDDate6End = S("WDDate6End"); d.WAcc6 = S("WAcc6");
            d.WD7 = S("WD7"); d.WDate7 = S("WDate7"); d.WDDate7End = S("WDDate7End"); d.WAcc7 = S("WAcc7");
            d.WD8 = S("WD8"); d.WDate8 = S("WDate8"); d.WDDate8End = S("WDDate8End"); d.WAcc8 = S("WAcc8");
            d.WD9 = S("WD9"); d.WDate9 = S("WDate9"); d.WDDate9End = S("WDDate9End"); d.WAcc9 = S("WAcc9");
            d.WD10 = S("WD10"); d.WDate10 = S("WDate10"); d.WDDate10End = S("WDDate10End"); d.WAcc10 = S("WAcc10");
            d.QAction = S("QAction"); d.ReqTestNoNG = S("ReqTestNoNG");
            d.StopAddressX = S("StopAddressX"); d.StopAddressY = S("StopAddressY");
            d.TEX = S("TEX"); d.TEY = S("TEY");
            d.PDGLX = S("PDGLX"); d.PDGLY = S("PDGLY");
            d.WaferMapAnswer = S("WaferMapAnswer"); d.WaferMapNOD = S("WaferMapNOD");
            d.AcredINSPN = S("AcredINSPN");
            d.WDSetupQty = S("WDSetupQty"); d.WDWIPQty = S("WDWIPQty"); d.WDBoxBoardQty = S("WDBoxBoardQty");
            d.DropICCheck = S("DropICCheck"); d.DropICCheckAfterWork = S("DropICCheckAfterWork");
            d.DropICCheckBy = S("DropICCheckBy"); d.DropICTreatedBy = S("DropICTreatedBy");
            d.LotSlipvsOIS = S("LotSlipvsOIS"); d.LoadOIS = S("LoadOIS");
            d.LoadTestFlow = S("LoadTestFlow"); d.FTADisk = S("FTADisk");
            d.LastTestNoCheck = S("LastTestNoCheck");
            d.BoxChangedfrom = S("BoxChangedfrom"); d.BoxChangedTo = S("BoxChangedTo");
            d.TesterChangeFrom = S("TesterChangeFrom"); d.TesterChangeTo = S("TesterChangeTo");
            d.QItemsNoIC = S("QItemsNoIC");
            d.QItemsYN1 = S("QItemsYN1"); d.QItemsYN2 = S("QItemsYN2"); d.QItemsYN3 = S("QItemsYN3");
            d.QItemsYN4 = S("QItemsYN4"); d.QItemsYN5 = S("QItemsYN5");
            d.EDSCS1Pprod = S("EDSCS1Pprod");
            d.OverDriveBefore = S("OverDriveBefore"); d.OverDriveAFter = S("OverDriveAFter");
            d.EDSTotalDutNG = S("EDSTotalDutNG"); d.EDSTotalDutCOUNT = S("EDSTotalDutCOUNT");
            d.ProbeRingBefore = S("ProbeRingBefore"); d.ProbeRingAfter = S("ProbeRingAfter");
            d.IronPlateBefore = S("IronPlateBefore"); d.IronPlateAfter = S("IronPlateAfter");
            d.StopAddAxisX = S("StopAddAxisX"); d.StopAddAxisY = S("StopAddAxisY");
            d.StartAddAxisX = S("StartAddAxisX"); d.StartAddAxisY = S("StartAddAxisY");

            return d;
        }

        // -----------------------------------------------------------------------
        // VR Numbers
        // -----------------------------------------------------------------------
        public async Task<(string vr1, string vr2, string vr3)> GetVrNumbersAsync(int bmId)
        {
            const string sql =
                "SELECT VRNumber1, VRNumber2, VRNumber3 " +
                "FROM [eMaintenanceSystemDB].[dbo].[VRNumber] WHERE bmID = @bmID";

            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@bmID", bmId);
            await using var dr = await cmd.ExecuteReaderAsync();
            if (await dr.ReadAsync())
                return (dr["VRNumber1"]?.ToString() ?? "",
                        dr["VRNumber2"]?.ToString() ?? "",
                        dr["VRNumber3"]?.ToString() ?? "");
            return ("", "", "");
        }

        // -----------------------------------------------------------------------
        // Generic lookup helpers (mirrors getProcCodes / getPMCategory in VB)
        // -----------------------------------------------------------------------
        public async Task<string> GetLookupValueAsync(
            string id, string tableName, string idColumn, string valueColumn)
        {
            if (string.IsNullOrWhiteSpace(id) || id == "0") return "";
            // Only allow known table names to prevent SQL injection via table name interpolation
            var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PMCategory", "BMContent", "TEContent", "TECase", "BMTEUnit",
                "TEPoint", "TEWorkContent", "TECause", "TEPREVMeasure"
            };
            if (!allowed.Contains(tableName)) return "";

            var sql = $"SELECT {valueColumn} FROM {tableName} WHERE {idColumn} = @id";
            await using var conn = new SqlConnection(_connStr);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@id", id);
            var result = await cmd.ExecuteScalarAsync();
            return result?.ToString() ?? "";
        }
    }
}