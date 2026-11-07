using MachineKarte.Web.Services;
using System.Data;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Repositories;

public class BmReportRepository
{
    private readonly DatabaseConnectionResolver _connections;

    public BmReportRepository(DatabaseConnectionResolver connections) =>
        _connections = connections;
    // Main record from ViewTEData_Test
    public async Task<BmReportData?> GetReportDataAsync(int bmId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT TOP 1 * FROM ViewTEData_Test WHERE bmID = @bmID";

        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@bmID", SqlDbType.Int).Value = bmId;

        await using var dr = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await dr.ReadAsync(cancellationToken))
            return null;

        var d = new BmReportData { BmId = bmId };
        for (var ordinal = 0; ordinal < dr.FieldCount; ordinal++)
            d.Values[dr.GetName(ordinal)] = dr.GetValue(ordinal);

        string S(string col) => d.Values.TryGetValue(col, out var raw) && raw is not DBNull
            ? raw.ToString() ?? "" : "";

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
        d.AcredBMCauseID = S("AcredBMCauseID");
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

    // Resolve VR numbers and the nine fixed lookups in one command. Each
    // result retains the original first-row/null/empty-ID behavior.
    public async Task LoadSupplementalDataAsync(
        BmReportData data, CancellationToken cancellationToken = default)
    {
        var lookups = new (string Id, string Table, string Key, string Value, Action<string> Assign)[]
        {
            (data.PMCategoryID, "PMCategory", "PMCategoryID", "PMCategory", value => data.PMCategory = value),
            (data.BMContentID, "BMContent", "BMContentID", "BMContent", value => data.BMContent = value),
            (data.BMContentID, "TEContent", "BMContentID", "BMContent", value => data.TEContent = value),
            (data.BMCaseID, "TECase", "BMCaseID", "BMCase", value => data.BMCase = value),
            (data.BMUnitID, "BMTEUnit", "BMTEUnitID", "BMTEUnit", value => data.BMTEUnit = value),
            (data.BMPointID, "TEPoint", "BMPointID", "BMPoint", value => data.BMPoint = value),
            (data.BMWorkContentID, "TEWorkContent", "BMWorkContentID", "BMWorkContent", value => data.BMWorkContent = value),
            (data.BMCauseID, "TECause", "BMCauseID", "BMCause", value => data.BMCause = value),
            (data.PrevMeasureID, "TEPREVMeasure", "PREVMeasureID", "PREVMeasure", value => data.PREVMeasure = value)
        };
        await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
        await using var cmd = new SqlCommand(@"SELECT VRNumber1, VRNumber2, VRNumber3
            FROM [eMaintenanceSystemDB].[dbo].[VRNumber] WHERE bmID = @bmID;", conn);
        cmd.Parameters.Add("@bmID", SqlDbType.Int).Value = data.BmId;
        for (var i = 0; i < lookups.Length; i++)
        {
            var lookup = lookups[i];
            if (string.IsNullOrWhiteSpace(lookup.Id))
                cmd.CommandText += "SELECT N'';";
            else
            {
                cmd.CommandText += $"SELECT [{lookup.Value}] FROM [{lookup.Table}] WHERE [{lookup.Key}] = @id{i};";
                cmd.Parameters.AddWithValue($"@id{i}", lookup.Id);
            }
        }
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            data.VRNumber1 = reader["VRNumber1"].ToString() ?? "";
            data.VRNumber2 = reader["VRNumber2"].ToString() ?? "";
            data.VRNumber3 = reader["VRNumber3"].ToString() ?? "";
        }
        foreach (var lookup in lookups)
        {
            await reader.NextResultAsync(cancellationToken);
            var found = await reader.ReadAsync(cancellationToken);
            var value = found ? reader.GetValue(0).ToString() ?? "" : "";
            lookup.Assign(value);
            data.LookupValues[$"{lookup.Table}|{lookup.Id}|{lookup.Value}"] =
                found && !string.IsNullOrWhiteSpace(lookup.Id) ? value : "";
        }
    }
}
