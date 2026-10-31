using ClosedXML.Excel;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    /// <summary>
    /// Generates the BM Maintenance Record report (TE or EDS variant).
    /// Reads the correct .xlsx template from Templates/BmReport/, fills it in
    /// with data from ViewTEData_Test and returns the workbook as a byte array.
    ///
    /// Template conventions (mirrors GemBox cell assignments in Form9.vb):
    ///   Worksheet 0  = main report page
    ///   Worksheet 1  = WD diary (work-description log)
    ///   Worksheet 3  = External accessories / QC sub-sheet
    /// </summary>
    public class BmReportService
    {
        private readonly BmReportRepository _repo;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BmReportService> _log;

        public BmReportService(BmReportRepository repo,
                               IWebHostEnvironment env,
                               ILogger<BmReportService> log)
        {
            _repo = repo;
            _env = env;
            _log = log;
        }

        // -----------------------------------------------------------------------
        // Entry point — returns (bytes, filename) ready to stream to browser
        // -----------------------------------------------------------------------
        public async Task<(byte[] bytes, string fileName)> GenerateAsync(int bmId, string process)
        {
            var data = await _repo.GetReportDataAsync(bmId);
            if (data == null)
                throw new InvalidOperationException($"No record found for bmID={bmId}");

            data.Process = process;

            // Resolve lookups in parallel
            var (vr1, vr2, vr3) = await _repo.GetVrNumbersAsync(bmId);
            data.VRNumber1 = vr1; data.VRNumber2 = vr2; data.VRNumber3 = vr3;

            data.PMCategory = await _repo.GetLookupValueAsync(data.PMCategoryID, "PMCategory", "PMCategoryID", "PMCategory");
            data.BMContent = await _repo.GetLookupValueAsync(data.BMContentID, "BMContent", "BMContentID", "BMContent");
            data.TEContent = await _repo.GetLookupValueAsync(data.BMContentID, "TEContent", "BMContentID", "BMContent");
            data.BMCase = await _repo.GetLookupValueAsync(data.BMCaseID, "TECase", "BMCaseID", "BMCase");
            data.BMTEUnit = await _repo.GetLookupValueAsync(data.BMUnitID, "BMTEUnit", "BMTEUnitID", "BMTEUnit");
            data.BMPoint = await _repo.GetLookupValueAsync(data.BMPointID, "TEPoint", "BMPointID", "BMPoint");
            data.BMWorkContent = await _repo.GetLookupValueAsync(data.BMWorkContentID, "TEWorkContent", "BMWorkContentID", "BMWorkContent");
            data.BMCause = await _repo.GetLookupValueAsync(data.BMCauseID, "TECause", "BMCauseID", "BMCause");
            data.PREVMeasure = await _repo.GetLookupValueAsync(data.PrevMeasureID, "TEPREVMeasure", "PREVMeasureID", "PREVMeasure");

            // Load template
            var templateName = process == "EDS" ? "EDS-CSPTemplate.xlsx" : "TETemplate.xlsx";
            var templatePath = Path.Combine(_env.ContentRootPath, "Templates", "BmReport", templateName);

            using var wb = new XLWorkbook(templatePath);
            FillMainSheet(wb, data, process);
            FillWdSheet(wb, data);
            FillAccessoriesSheet(wb, data);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            var fileName = $"TE_{bmId}.xlsx";
            return (ms.ToArray(), fileName);
        }

        // -----------------------------------------------------------------------
        // Worksheet 0 — main maintenance record
        // -----------------------------------------------------------------------
        private static void FillMainSheet(XLWorkbook wb, BmReportData d, string process)
        {
            var ws = wb.Worksheets.Worksheet(1); // 1-based in ClosedXML

            // VR Numbers
            if (process == "EDS")
            {
                ws.Cell("K39").Value = string.IsNullOrEmpty(d.VRNumber1) ? "VR(1)" : $"VR({d.VRNumber1})";
                ws.Cell("K40").Value = string.IsNullOrEmpty(d.VRNumber2) ? "VR(2)" : $"VR({d.VRNumber2})";
                ws.Cell("K41").Value = string.IsNullOrEmpty(d.VRNumber3) ? "VR(3)" : $"VR({d.VRNumber3})";
            }
            else
            {
                ws.Cell("L38").Value = string.IsNullOrEmpty(d.VRNumber1) ? "VR( 1 )" : $"VR( {d.VRNumber1} )";
                ws.Cell("L39").Value = string.IsNullOrEmpty(d.VRNumber2) ? "VR( 2 )" : $"VR( {d.VRNumber2} )";
                ws.Cell("L40").Value = string.IsNullOrEmpty(d.VRNumber3) ? "VR( 3 )" : $"VR( {d.VRNumber3} )";
            }

            // Signals
            ws.Cell("AD2").Value = string.IsNullOrEmpty(d.AQI) ? "NO" : d.AQI;
            ws.Cell("AJ2").Value = string.IsNullOrEmpty(d.Andon) ? "NO" : d.Andon;
            ws.Cell("AP2").Value = string.IsNullOrEmpty(d.GLAndon) ? "NO" : d.GLAndon;
            ws.Cell("AV2").Value = d.GLAndon2;
            ws.Cell("AJ4").Value = d.GLConfirmOPReqBy;

            // Quality BM priority section
            if (d.Priority == "QUALITY BM")
            {
                try
                {
                    var glDate = DateTime.Parse(d.GLConfi_Date).ToString("yy.MM.dd");
                    if (process == "EDS")
                    {
                        ws.Cell("AM35").Value = d.GLConfi;
                        ws.Cell("AW34").Value = d.Remarks;
                        ws.Cell("AT37").Value = d.ChangingPoint;
                    }
                    else
                    {
                        ws.Cell("AM34").Value = d.GLConfi;
                        ws.Cell("AW34").Value = d.Remarks;
                        ws.Cell("AT36").Value = d.ChangingPoint;
                    }
                    ws.Cell("AZ33").Value = d.GLConfi;
                    ws.Cell("AZ32").Value = d.RequestTo;
                    ws.Cell("AM33").Value = glDate;
                }
                catch { /* date parse may fail for empty */ }
            }

            // PM Category
            ws.Cell("G10").Value = d.PMCategoryID switch
            {
                "1" => "1. BM",
                "2" => "2. PM",
                "3" => "3. CM",
                "4" => "4. SET-UP",
                "5" => "5. Pre-Process",
                "6" or "7" => "6. Others",
                _ => ""
            };

            // BM Content underline (text markers for the template)
            var contentCell = d.BMContent.ToUpperInvariant() switch
            {
                "MEKA" => "G11",
                "QUALITY" => "N11",
                "PERIODIC INSPECTION" => "G12",
                "PM DETECTION" => "N12",
                "OP DETECTION" => "AA11",
                "OTHERS" => "U11",
                "PRE/POST-PROCESS FEEDBACK INVESTIGATION" => "U12",
                _ => null
            };
            if (contentCell != null)
                ws.Cell(contentCell).Style.Font.Underline = XLFontUnderlineValues.Single;

            FillSetupMarkers(ws, d);
            FillOptionMarkers(ws, d, process);
            FillMachineStateMarkers(ws, d);
            FillTestFlowMarker(ws, d);

            // Counts
            ws.Cell("K13").Value = d.TestNoNG;
            ws.Cell("K14").Value = d.LotNG;
            ws.Cell("K15").Value = d.KeepSample;
            ws.Cell("K16").Value = d.BinCat;

            // Machine identification
            ws.Cell("BD12").Value = d.TesterNo;
            ws.Cell("AW12").Value = d.MCNo;
            ws.Cell("AQ12").Value = d.CellLine;
            ws.Cell("AS16").Value = d.DeviceName;
            ws.Cell(process == "EDS" ? "Z20" : "W20").Value = d.PKGName;
            ws.Cell("AS18").Value = d.LOTNo;
            ws.Cell("AA18").Value = d.DUT;
            ws.Cell("V19").Value = d.BoxNo;
            ws.Cell(process == "EDS" ? "AG19" : "AF19").Value = d.SubDUT;
            if (process == "EDS") ws.Cell("Z21").Value = d.Freq;

            // Personnel signature cells
            ws.Cell("AD4").Value = d.RequestedBy;
            ws.Cell(process == "EDS" ? "AE78" : "AE77").Value = d.GLConfirmOPReqBy;
            ws.Cell(process == "EDS" ? "AI78" : "AI77").Value = d.CurrentTE;
            ws.Cell(process == "EDS" ? "AF79" : "AF78").Value =
                string.IsNullOrEmpty(d.d_TEJudgment) ? "N/A" : d.d_TEJudgment;
            ws.Cell("AP4").Value = d.PMConfirmGLOPReqBy == d.RepairedByEnd
                ? d.PMConfirmGLOPReqBy
                : $"{d.PMConfirmGLOPReqBy}, {d.RepairedByEnd}";
            ws.Cell("AV4").Value = d.GLRepairConfirmationBy;
            ws.Cell("BC4").Value = d.EndBMBy;
            ws.Cell("AB55").Value = d.MittomonaiClaim;
            if (process != "EDS")
            {
                ws.Cell("BE52").Value = d.TESign;
                ws.Cell("BE53").Value = d.PDSign1;
                ws.Cell("BE54").Value = d.GLRepairConfirmationBy;
            }

            // MC Status underline
            if (d.MCStatus == "Stop")
                ws.Cell("AQ16").Style.Font.Underline = XLFontUnderlineValues.Single;
            else if (d.MCStatus == "Running")
                ws.Cell("AZ16").Style.Font.Underline = XLFontUnderlineValues.Single;

            // Occurrence dates
            ws.Cell("AS20").Value = d.OccurrenceDate;
            ws.Cell("AS22").Value = d.TEReceived;
            if (process == "EDS")
            {
                ws.Cell("AS24").Value = d.StartOfRepair;
                ws.Cell("AS26").Value = d.EndOfRepair;
                ws.Cell("AS28").Value = d.MonitoringTime1;
                ws.Cell("AS30").Value = d.WaitingTime1;
                ws.Cell("AS32").Value = d.RepairTime1;
                // Total = waiting + repair
                if (decimal.TryParse(d.WaitingTime1, out var wt1) &&
                    decimal.TryParse(d.RepairTime1, out var rt1))
                    ws.Cell("AS34").Value = wt1 + rt1;
            }
            else
            {
                ws.Cell("AS24").Value = d.EndOfRepair;
                ws.Cell("AS26").Value = d.WaitingTime1;
                ws.Cell("AS28").Value = d.RepairTime1;
                if (decimal.TryParse(d.WaitingTime1, out var wt1) &&
                    decimal.TryParse(d.RepairTime1, out var rt1))
                    ws.Cell("AS30").Value = wt1 + rt1;
            }

            // 5 Why
            ws.Cell(process == "EDS" ? "AO38" : "AO37").Value = d.Why1;
            ws.Cell(process == "EDS" ? "AO42" : "AO41").Value = d.Why2;
            ws.Cell(process == "EDS" ? "AO47" : "AO46").Value = d.Why3;
            ws.Cell(process == "EDS" ? "AM77" : "AM76").Value = d.PMUnknown;

            // 4M data
            if (!string.IsNullOrEmpty(d.FourMData))
            {
                var parts = d.FourMData.Split('|');
                if (parts.Length > 0)
                {
                    var vals = parts[0].Split(',');
                    if (vals.Length >= 2 && int.TryParse(d.FourM, out int fm))
                    {
                        var (r1, r2) = fm switch
                        {
                            1 => process == "EDS" ? ("AQ60", "AY60") : ("AQ59", "AY59"),
                            2 => process == "EDS" ? ("AQ62", "AY62") : ("AQ61", "AY61"),
                            3 => process == "EDS" ? ("AQ64", "AY64") : ("AQ63", "AY63"),
                            4 => process == "EDS" ? ("AQ66", "AY66") : ("AQ65", "AY65"),
                            _ => (null, null)
                        };
                        if (r1 != null) { ws.Cell(r1).Value = vals[0]; ws.Cell(r2!).Value = vals[1]; }
                    }
                }
            }

            // Adjusted record data (3 rows × 3 cols)
            if (!string.IsNullOrEmpty(d.AdjustedRecordData))
            {
                var rows = d.AdjustedRecordData.Split('|');
                if (rows.Length == 3)
                {
                    var baseRow = process == "EDS" ? 39 : 38;
                    for (int i = 0; i < 3; i++)
                    {
                        var cols = rows[i].Split(',');
                        if (cols.Length >= 3)
                        {
                            ws.Cell($"U{baseRow + i}").Value = cols[0];
                            ws.Cell($"AA{baseRow + i}").Value = cols[1];
                            ws.Cell($"AG{baseRow + i}").Value = cols[2];
                        }
                    }
                }
            }

            // Drop IC / Q-action
            if (process != "EDS")
            {
                ws.Cell("M23").Value = d.DropICCheck;
                ws.Cell("AB23").Value = d.DropICTreatedBy;
                ws.Cell("D51").Value = d.QAction;
            }
            else
            {
                ws.Cell("D52").Value = d.QAction;
            }
            ws.Cell("D25").Value = d.ReqTestNoNG;

            // Check item data (8 segments)
            FillCheckItemData(ws, d, process);

            // Accreditor remarks
            var accRemark = !string.IsNullOrEmpty(d.AccreditorRemarks)
                ? d.AccreditorRemarks : d.AccreditorRemarks_Max;
            ws.Cell(process == "EDS" ? "AT92" : "AO91").Value = accRemark;

            // Changed parts (4 rows)
            var partsBaseRow = process == "EDS" ? 83 : 82;
            var parts4 = new[]
            {
                (d.ChangePartsName1, d.ChangePartsType1, d.ChangePartsQty1),
                (d.ChangePartsName2, d.ChangePartsType2, d.ChangePartsQty2),
                (d.ChangePartsName3, d.ChangePartsType3, d.ChangePartsQty3),
                (d.ChangePartsName4, d.ChangePartsType4, d.ChangePartsQty4),
            };
            for (int i = 0; i < 4; i++)
            {
                ws.Cell($"AB{partsBaseRow + i}").Value = parts4[i].Item1;
                ws.Cell($"AQ{partsBaseRow + i}").Value = parts4[i].Item2;
                ws.Cell($"BE{partsBaseRow + i}").Value = parts4[i].Item3;
            }

            // Taxonomy footer
            var footerRow = process == "EDS" ? 92 : 91;
            ws.Cell($"A{footerRow}").Value = d.RepairedByEnd;
            if (process == "EDS")
            {
                ws.Cell($"F{footerRow}").Value = d.PMCategory;
                ws.Cell($"K{footerRow}").Value = d.TEContent;
                ws.Cell($"P{footerRow}").Value = d.BMCase;
                ws.Cell($"U{footerRow}").Value = d.BMTEUnit;
                ws.Cell($"Z{footerRow}").Value = d.BMPoint;
                ws.Cell($"AE{footerRow}").Value = d.BMWorkContent;
                ws.Cell($"AJ{footerRow}").Value = d.BMCause;
                ws.Cell($"AO{footerRow}").Value = d.PREVMeasure;
            }
            else
            {
                ws.Cell($"K{footerRow}").Value = d.BMCase;
                ws.Cell($"P{footerRow}").Value = d.BMTEUnit;
                ws.Cell($"U{footerRow}").Value = d.BMPoint;
                ws.Cell($"Z{footerRow}").Value = d.BMWorkContent;
                ws.Cell($"AE{footerRow}").Value = d.BMCause;
                ws.Cell($"AJ{footerRow}").Value = d.PREVMeasure;
            }

            // Inspection / accreditor
            ws.Cell("AL65").Value = d.AcredINSPN;
            ws.Cell($"AY{(process == "EDS" ? 88 : 87)}").Value = d.WDSetupQty;
            ws.Cell($"AY{(process == "EDS" ? 89 : 88)}").Value = d.WDWIPQty;
            ws.Cell($"AY{(process == "EDS" ? 90 : 89)}").Value = d.WDBoxBoardQty;
            ws.Cell("G17").Value = d.OPRemarks;
            ws.Cell("I22").Value = d.Designation;
            ws.Cell("K23").Value = d.SocketBoard;
            ws.Cell("BC17").Value = d.ShipmentDate;

            // EDS-specific cells
            if (process == "EDS")
            {
                ws.Cell("AC15").Value = d.EDSTotalDutNG;
                ws.Cell("AI15").Value = d.EDSTotalDutCOUNT;
                ws.Cell("M73").Value = d.OverDriveBefore;
                ws.Cell("V73").Value = d.OverDriveAFter;
                ws.Cell("AC73").Value = d.EDSCS1Pprod;
                ws.Cell("M76").Value = d.OverDriveBefore;
                ws.Cell("V76").Value = d.OverDriveAFter;
            }

            // TE-specific EDS cells
            if (process != "EDS")
            {
                ws.Cell("I87").Value = d.ProbeRingBefore;
                ws.Cell("T87").Value = d.ProbeRingAfter;
                ws.Cell("I89").Value = d.IronPlateBefore;
                ws.Cell("T89").Value = d.IronPlateAfter;
                ws.Cell("Z84").Value = d.EDSTotalDutNG;
                ws.Cell("AI84").Value = d.EDSTotalDutCOUNT;
                ws.Cell("AA87").Value = d.StopAddAxisX;
                ws.Cell("AJ87").Value = d.StopAddAxisY;
                ws.Cell("AA89").Value = d.StartAddAxisX;
                ws.Cell("AJ89").Value = d.StartAddAxisY;
                ws.Cell("AT87").Value = d.OverDriveBefore;
                ws.Cell("BC87").Value = d.OverDriveAFter;
            }

            // Wafer map / stop address (TE only)
            if (process == "EDS")
            {
                ws.Cell("AS53").Value = d.StopAddressX;
                ws.Cell("BA53").Value = d.StopAddressY;
                ws.Cell("AS54").Value = d.TEX;
                ws.Cell("BA54").Value = d.TEY;
                ws.Cell("AS55").Value = d.PDGLX;
                ws.Cell("BA55").Value = d.PDGLY;
                ws.Cell("AR74").Value = d.WaferMapAnswer;
                ws.Cell("BC74").Value = d.WaferMapNOD;
            }

            // QC / quality-check data
            FillQcData(ws, d, process);

            // Misc
            ws.Cell(process == "EDS" ? "N61" : "N60").Value = d.LotSlipvsOIS;
            ws.Cell(process == "EDS" ? "N62" : "N61").Value = d.LoadOIS;
            ws.Cell(process == "EDS" ? "Y62" : "Y61").Value = d.LoadTestFlow;
            ws.Cell(process == "EDS" ? "O63" : "O62").Value = d.FTADisk;

            // External accessories
            var extBEmpty = string.IsNullOrEmpty(d.ExternalAccessories_B)
                            || d.ExternalAccessories_B.Contains("N/A");
            if (extBEmpty && !string.IsNullOrEmpty(d.ExternalAccessories))
                ws.Cell(process == "EDS" ? "S65" : "V64").Value = d.ExternalAccessories;
            else
                ws.Cell(process == "EDS" ? "X65" : "Z64").Value = d.ExternalAccessories_B;

            ws.Cell(process == "EDS" ? "L66" : "L65").Value = d.LastTestNoCheck;

            // Drop IC
            if (process != "EDS")
            {
                ws.Cell("K71").Value = d.DropICCheckAfterWork;
                ws.Cell("W71").Value = d.DropICCheckBy;
                if (!string.IsNullOrEmpty(d.QualityCheckData))
                {
                    ws.Cell("K72").Value = d.DropICCheckAfterWork;
                    ws.Cell("W72").Value = d.DropICCheckBy;
                }
            }

            ws.Cell(process == "EDS" ? "K78" : "K77").Value = d.BoxChangedfrom;
            ws.Cell(process == "EDS" ? "S78" : "S77").Value = d.BoxChangedTo;
            ws.Cell(process == "EDS" ? "L79" : "L78").Value = d.TesterChangeFrom;
            ws.Cell(process == "EDS" ? "U79" : "U78").Value = d.TesterChangeTo;
            ws.Cell(process == "EDS" ? "P84" : "P83").Value = d.QItemsNoIC;

            var qItemsBase = process == "EDS" ? 81 : 80;
            var qItems = new[] { d.QItemsYN1, d.QItemsYN2, d.QItemsYN3, d.QItemsYN4, d.QItemsYN5 };
            for (int i = 0; i < 5; i++)
                ws.Cell($"V{qItemsBase + i}").Value = qItems[i] == "Yes" ? "Yes" : "N/A";

            // AccreditorPMReply (TE only)
            if (process != "EDS")
                ws.Cell("Z87").Value = d.AccreditorPMReply;

            // Untested sample (TE only, 5 rows)
            if (process != "EDS")
                FillUntestedSample(ws, d);

            // Accreditor colour flagging when BMStatusID = 14
            if (process != "EDS" && d.BMStatusID == "14")
                ApplyAccreditorColours(ws, d);

            // CP Data (TE only)
            if (process != "EDS" && !string.IsNullOrEmpty(d.CPData))
                FillCpData(ws, d);

            CenterComparisonCells(ws);
        }

        private static void SetMarker(IXLWorksheet ws, string cell, string text, double fontSize = 9)
        {
            var c = ws.Cell(cell);
            c.Value = text;
            c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            c.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            c.Style.Font.FontSize = fontSize;
            c.Style.Font.FontColor = XLColor.Black;
        }

        private static void FillSetupMarkers(IXLWorksheet ws, BmReportData d)
        {
            switch ((d.Setup ?? "").Trim())
            {
                case "Newly Set-up":
                    SetMarker(ws, "S13", "✓");
                    break;
                case "Lot Change only":
                    SetMarker(ws, "S14", "✓");
                    break;
                case "Device changed only":
                    SetMarker(ws, "AA13", "✓");
                    break;
                case "Others":
                    SetMarker(ws, "AA14", "✓");
                    break;
            }
        }

        private static void FillOptionMarkers(IXLWorksheet ws, BmReportData d, string process)
        {
            var value = (d.OPPosition ?? "").Trim();
            if (value != "Start of Lot" && value != "Middle of Lot" && value != "End of Lot")
                return;

            var cells = process == "EDS"
                ? new[] { "Y16", "AD16", "AI16" }
                : new[] { "X16", "AD16", "AI16" };

            foreach (var cell in cells)
                SetMarker(ws, cell, "□");

            var index = value switch
            {
                "Start of Lot" => 0,
                "Middle of Lot" => 1,
                _ => 2
            };
            SetMarker(ws, cells[index], "✓");
        }

        private static void FillMachineStateMarkers(IXLWorksheet ws, BmReportData d)
        {
            var value = (d.MCStatus ?? "").Trim();
            if (value != "On-Stop" && value != "Operational")
                return;

            SetMarker(ws, "AK17", "□");
            SetMarker(ws, "AK22", "□");
            SetMarker(ws, value == "On-Stop" ? "AK17" : "AK22", "✓");
        }

        private static void FillTestFlowMarker(IXLWorksheet ws, BmReportData d)
        {
            var value = (d.TestFlow ?? "").Trim();
            if (value != "OS" && value != "A1" && value != "A2" &&
                value != "A3" && value != "A4" && value != "A8")
                return;

            var x17 = ws.Cell("X17");
            var merged = ws.MergedRanges.FirstOrDefault(r => r.Contains(x17));
            if (merged != null && merged.FirstCell().Address.ToString() != x17.Address.ToString())
            {
                var label = ws.Cell("S17");
                var prefix = label.GetString().TrimEnd();
                label.Value = string.IsNullOrEmpty(prefix) ? value : $"{prefix} {value}";
            }
            else
            {
                SetMarker(ws, "X17", value, 10);
            }
        }

        private static void CenterComparisonCells(IXLWorksheet ws)
        {
            foreach (var column in new[] { "H", "K", "N", "Q", "T", "W", "Y", "AB" })
            {
                for (var row = 60; row <= 62; row++)
                    ws.Cell($"{column}{row}").Style.Alignment.Horizontal =
                        XLAlignmentHorizontalValues.Center;
            }
        }

        // -----------------------------------------------------------------------
        // Check item data helper
        // -----------------------------------------------------------------------
        private static void FillCheckItemData(IXLWorksheet ws, BmReportData d, string process)
        {
            if (string.IsNullOrEmpty(d.CheckItemData)) return;
            var seg = d.CheckItemData.Split('|');
            if (seg.Length != 8) return;

            if (process == "EDS")
            {
                var v = seg.Select(s => s.Split(',')).ToArray();
                SetIfLen(ws, "S27", v[0], 1); SetIfLen(ws, "Y27", v[0], 2); SetIfLen(ws, "AH27", v[0], 3);
                SetIfLen(ws, "S28", v[1], 1); SetIfLen(ws, "Y28", v[1], 2); SetIfLen(ws, "AH28", v[1], 3);
                SetIfLen(ws, "O30", v[2], 0); SetIfLen(ws, "T30", v[2], 1); SetIfLen(ws, "Y29", v[2], 2); SetIfLen(ws, "AH30", v[2], 3);
                SetIfLen(ws, "O31", v[3], 0); SetIfLen(ws, "T31", v[3], 1); SetIfLen(ws, "Y30", v[3], 2); SetIfLen(ws, "AH31", v[3], 3);
                SetIfLen(ws, "O32", v[4], 0); SetIfLen(ws, "T32", v[4], 1); SetIfLen(ws, "Y31", v[4], 2); SetIfLen(ws, "AH32", v[4], 3);
                SetIfLen(ws, "O33", v[5], 0); SetIfLen(ws, "T33", v[5], 1); SetIfLen(ws, "Y32", v[5], 2); SetIfLen(ws, "AH33", v[5], 3);
                SetIfLen(ws, "O34", v[6], 0); SetIfLen(ws, "T34", v[6], 1); SetIfLen(ws, "Y33", v[6], 2); SetIfLen(ws, "AH34", v[6], 3);
                SetIfLen(ws, "Y35", v[7], 2); SetIfLen(ws, "AH35", v[7], 3);
            }
            else
            {
                var v = seg.Select(s => s.Split(',')).ToArray();
                SetIfLen(ws, "O29", v[2], 0); SetIfLen(ws, "O30", v[3], 0);
                SetIfLen(ws, "O31", v[4], 0); SetIfLen(ws, "O32", v[5], 0); SetIfLen(ws, "O33", v[6], 0);
                SetIfLen(ws, "S27", v[0], 1); SetIfLen(ws, "S28", v[1], 1);
                SetIfLen(ws, "T29", v[2], 1); SetIfLen(ws, "T30", v[3], 1);
                SetIfLen(ws, "T31", v[4], 1); SetIfLen(ws, "T32", v[5], 1); SetIfLen(ws, "T33", v[6], 1);
                for (int i = 0; i < 8; i++)
                {
                    SetIfLen(ws, $"Y{27 + i}", v[i], 2);
                    SetIfLen(ws, $"AH{27 + i}", v[i], 3);
                }
            }
        }

        private static void SetIfLen(IXLWorksheet ws, string cell, string[] arr, int idx)
        {
            if (arr.Length > idx) ws.Cell(cell).Value = arr[idx];
        }

        // -----------------------------------------------------------------------
        // QC data helper (quality-check items)
        // -----------------------------------------------------------------------
        private static void FillQcData(IXLWorksheet ws, BmReportData d, string process)
        {
            // QC Judgement (single segment)
            if (!string.IsNullOrEmpty(d.QcJudgement))
            {
                var seg = d.QcJudgement.Split('|');
                if (seg.Length > 0)
                {
                    var v = seg[0].Split(',');
                    string pass1 = v.Length > 1 ? v[1] : "";
                    string pass0 = v.Length > 0 ? v[0] : "";
                    ws.Cell(process == "EDS" ? "AE72" : "AE73").Value = IsYes(pass1) ? "Yes" : "N/A";
                    ws.Cell(process == "EDS" ? "AC72" : "AC73").Value = IsYes(pass0) ? "Yes" : "N/A";
                }
            }

            if (string.IsNullOrEmpty(d.QualityCheckData)) return;
            var arr = d.QualityCheckData.Split('|');
            // Delegate to the appropriate mapping depending on array length and process
            // (mirrors the VB if/else branch on arr.Length = 14/15 vs other)
            if (process == "EDS")
                FillQcEds(ws, arr);
            else
                FillQcTe(ws, arr);
        }

        private static bool IsYes(string v) =>
            (v == "Pass" || v == "Yes") && !string.IsNullOrEmpty(v) && v != "0";

        private static void QcCell(IXLWorksheet ws, string cell, string[] arr, int idx)
        {
            ws.Cell(cell).Value = arr.Length > idx && IsYes(arr[idx]) ? "Yes" : "N/A";
        }

        private static void QcRawCell(IXLWorksheet ws, string cell, string[] arr, int idx)
        {
            var value = arr.Length > idx ? arr[idx] : "";
            ws.Cell(cell).Value = IsYes(value) ? value : "N/A";
        }

        private static void FillQcEds(IXLWorksheet ws, string[] arr)
        {
            if (arr.Length == 14 || arr.Length == 15)
            {
                QcCell(ws, "AE60", arr[0].Split(','), 1);
                QcCell(ws, "AE61", arr[1].Split(','), 1); QcCell(ws, "AC61", arr[1].Split(','), 0);
                QcCell(ws, "AE62", arr[2].Split(','), 1); QcCell(ws, "AC62", arr[2].Split(','), 0);
                QcCell(ws, "AE63", arr[3].Split(','), 1);
                QcCell(ws, "AE74", arr[4].Split(','), 1);
                QcCell(ws, "AE65", arr[6].Split(','), 1); QcCell(ws, "AC65", arr[6].Split(','), 0);
                QcCell(ws, "AE66", arr[8].Split(','), 1);
                QcCell(ws, "AE67", arr[9].Split(','), 1); QcCell(ws, "AC67", arr[9].Split(','), 0);
                QcCell(ws, "AE68", arr[10].Split(','), 1);
                QcCell(ws, "AE69", arr[11].Split(','), 1); QcCell(ws, "AC69", arr[11].Split(','), 0);
                QcCell(ws, "AE70", arr[12].Split(','), 1); QcCell(ws, "AC70", arr[12].Split(','), 0);
                QcCell(ws, "AE71", arr[13].Split(','), 1);
            }
            else
            {
                QcCell(ws, "AE60", arr[0].Split(','), 1);
                QcCell(ws, "AE61", arr[1].Split(','), 1); QcCell(ws, "AC61", arr[1].Split(','), 0);
                QcCell(ws, "AE62", arr[2].Split(','), 1); QcCell(ws, "AC62", arr[2].Split(','), 0);
                QcCell(ws, "AE63", arr[3].Split(','), 1);
                QcCell(ws, "AE74", arr[4].Split(','), 1);
                QcCell(ws, "AE65", arr[6].Split(','), 1); QcCell(ws, "AC65", arr[6].Split(','), 0);
                QcCell(ws, "AE66", arr[8].Split(','), 1);
                QcCell(ws, "AE67", arr[9].Split(','), 1); QcCell(ws, "AC67", arr[9].Split(','), 0);
                QcCell(ws, "AE68", arr[10].Split(','), 1);
                QcCell(ws, "AE69", arr[11].Split(','), 1); QcCell(ws, "AC69", arr[11].Split(','), 0);
                QcCell(ws, "AE70", arr[12].Split(','), 1); QcCell(ws, "AC70", arr[12].Split(','), 0);
                QcCell(ws, "AE71", arr[13].Split(','), 1);
            }
        }

        private static void FillQcTe(IXLWorksheet ws, string[] arr)
        {
            if (arr.Length == 14 || arr.Length == 15)
            {
                QcCell(ws, "AE59", arr[0].Split(','), 1);
                QcCell(ws, "AE60", arr[1].Split(','), 1); QcCell(ws, "AC60", arr[1].Split(','), 0);
                QcCell(ws, "AE61", arr[2].Split(','), 1); QcCell(ws, "AC61", arr[2].Split(','), 0);
                QcCell(ws, "AE62", arr[3].Split(','), 1);
                QcCell(ws, "AE63", arr[4].Split(','), 1); QcRawCell(ws, "AC63", arr[4].Split(','), 0);
                QcCell(ws, "AE72", arr[5].Split(','), 1); QcCell(ws, "AC72", arr[5].Split(','), 0);
                QcCell(ws, "AE64", arr[6].Split(','), 1); QcCell(ws, "AC64", arr[6].Split(','), 0);
                QcCell(ws, "AE65", arr[7].Split(','), 1);
                QcCell(ws, "AE66", arr[8].Split(','), 1); QcCell(ws, "AC66", arr[8].Split(','), 0);
                QcCell(ws, "AE67", arr[9].Split(','), 1);
                QcCell(ws, "AE68", arr[10].Split(','), 1); QcCell(ws, "AC68", arr[10].Split(','), 0);
                QcCell(ws, "AE69", arr[11].Split(','), 1); QcCell(ws, "AC69", arr[11].Split(','), 0);
                QcCell(ws, "AE70", arr[12].Split(','), 1);
                QcCell(ws, "AE74", arr[13].Split(','), 1);
                if (arr.Length == 15)
                {
                    var v14 = arr[14].Split(',');
                    ws.Cell("AE74").Value = v14.Length > 1 && IsYes(v14[1]) ? "Yes" : "N/A";
                    ws.Cell("O75").Value = v14.Length > 0 ? v14[0] : "";
                }
                else
                {
                    ws.Cell("AE74").Value = "N/A";
                    ws.Cell("O75").Value = "";
                }
            }
            else
            {
                QcCell(ws, "AE59", arr[0].Split(','), 1);
                QcCell(ws, "AC60", arr[1].Split(','), 0); QcCell(ws, "AE60", arr[1].Split(','), 1);
                QcCell(ws, "AC61", arr[2].Split(','), 0); QcCell(ws, "AE61", arr[2].Split(','), 1);
                QcCell(ws, "AE62", arr[3].Split(','), 1);
                QcRawCell(ws, "AC63", arr[4].Split(','), 0); QcCell(ws, "AE63", arr[4].Split(','), 1);
                QcCell(ws, "AC72", arr[5].Split(','), 0); QcCell(ws, "AE72", arr[5].Split(','), 1);
                QcCell(ws, "AC64", arr[6].Split(','), 0); QcCell(ws, "AE64", arr[6].Split(','), 1);
                if (arr.Length > 8) { QcCell(ws, "AE65", arr[8].Split(','), 1); }
                if (arr.Length > 9) { QcCell(ws, "AC66", arr[9].Split(','), 0); QcCell(ws, "AE66", arr[9].Split(','), 1); }
                if (arr.Length > 10) { QcCell(ws, "AE67", arr[10].Split(','), 1); }
                if (arr.Length > 11) { QcCell(ws, "AC68", arr[11].Split(','), 0); QcCell(ws, "AE68", arr[11].Split(','), 1); }
                if (arr.Length > 12) { QcCell(ws, "AC69", arr[12].Split(','), 0); QcCell(ws, "AE69", arr[12].Split(','), 1); }
                if (arr.Length > 13) { QcCell(ws, "AE70", arr[13].Split(','), 1); }
                ws.Cell("AE74").Value = "N/A";
                ws.Cell("O75").Value = "";
                if (arr.Length > 15)
                {
                    var v14 = arr[15].Split(',');
                    ws.Cell("AE74").Value = v14.Length > 1 && IsYes(v14[1]) ? "Yes" : "N/A";
                    ws.Cell("O75").Value = v14.Length > 0 ? v14[0] : "";
                }
            }
        }

        // -----------------------------------------------------------------------
        // Untested sample (TE only, 5 rows)
        // -----------------------------------------------------------------------
        private static void FillUntestedSample(IXLWorksheet ws, BmReportData d)
        {
            if (string.IsNullOrEmpty(d.UntestedSample)) return;
            var rows = d.UntestedSample.Split('|');
            if (rows.Length != 5) return;
            var baseRow = 70;
            for (int i = 0; i < 5; i++)
            {
                var cols = rows[i].Split(',');
                if (cols.Length > 0) ws.Cell($"AQ{baseRow + i}").Value = cols[0];
                if (cols.Length > 1) ws.Cell($"AT{baseRow + i}").Value = cols[1];
                if (cols.Length > 2) ws.Cell($"AX{baseRow + i}").Value = cols[2];
                if (cols.Length > 3) ws.Cell($"BB{baseRow + i}").Value = cols[3];
                if (cols.Length > 4) ws.Cell($"BE{baseRow + i}").Value = cols[4];
            }
        }

        // -----------------------------------------------------------------------
        // CP Data (TE only) — A/B/C grading pictures replaced by cell text
        // -----------------------------------------------------------------------
        private static void FillCpData(IXLWorksheet ws, BmReportData d)
        {
            var seg = d.CPData.Split('|');
            if (seg.Length != 3) return;
            for (int i = 0; i < 3; i++)
            {
                var cols = seg[i].Split(',');
                if (cols.Length >= 2)
                {
                    ws.Cell($"AQ{54 + i}").Value = cols[0]; // ABC grade 1
                    ws.Cell($"AV{54 + i}").Value = cols[1]; // ABC grade 2
                }
            }
        }

        // -----------------------------------------------------------------------
        // Accreditor colour flagging (BMStatusID = 14)
        // -----------------------------------------------------------------------
        private static void ApplyAccreditorColours(IXLWorksheet ws, BmReportData d)
        {
            if (d.AcredPMCategoryID != d.PMCategoryID)
                ws.Cell(72, 4).Style.Font.FontColor = XLColor.Red;
            if (d.AcredBMCaseID != d.BMCaseID)
                ws.Cell(72, 10).Style.Font.FontColor = XLColor.Red;
            if (d.AcredBMMCUnitID != d.BMUnitID)
                ws.Cell(72, 13).Style.Font.FontColor = XLColor.Red;
            if (d.AcredBMWorkContentID != d.BMWorkContentID)
                ws.Cell(72, 19).Style.Font.FontColor = XLColor.Red;
            if (d.Why4 != d.AcredBMCauseID)
                ws.Cell(72, 22).Style.Font.FontColor = XLColor.Red;
        }

        // -----------------------------------------------------------------------
        // Worksheet 1 — WD diary
        // -----------------------------------------------------------------------
        private static void FillWdSheet(XLWorkbook wb, BmReportData d)
        {
            // Worksheet index 2 in ClosedXML = VB wb.Worksheets(1) (0-based VB → 1-based CXL)
            IXLWorksheet ws;
            try { ws = wb.Worksheets.Worksheet(2); }
            catch { return; }

            var entries = new[]
            {
                (d.WD1,  d.WDate1,  d.WDDate1End,  d.WAcc1,  2,  3,  7,  2),
                (d.WD2,  d.WDate2,  d.WDDate2End,  d.WAcc2,  11, 12, 16, 11),
                (d.WD3,  d.WDate3,  d.WDDate3End,  d.WAcc3,  20, 21, 25, 20),
                (d.WD4,  d.WDate4,  d.WDDate4End,  d.WAcc4,  29, 30, 34, 29),
                (d.WD5,  d.WDate5,  d.WDDate5End,  d.WAcc5,  38, 39, 43, 38),
                (d.WD6,  d.WDate6,  d.WDDate6End,  d.WAcc6,  47, 48, 52, 47),
                (d.WD7,  d.WDate7,  d.WDDate7End,  d.WAcc7,  56, 57, 61, 56),
                (d.WD8,  d.WDate8,  d.WDDate8End,  d.WAcc8,  65, 66, 70, 65),
                (d.WD9,  d.WDate9,  d.WDDate9End,  d.WAcc9,  74, 75, 79, 74),
                (d.WD10, d.WDate10, d.WDDate10End, d.WAcc10, 83, 84, 88, 83),
            };
            foreach (var e in entries)
            {
                ws.Cell($"B{e.Item5}").Value = e.Item1;
                ws.Cell($"Q{e.Item6}").Value = e.Item2;
                ws.Cell($"Q{e.Item7}").Value = e.Item3;
                ws.Cell($"P{e.Item8}").Value = e.Item4;
            }
        }

        // -----------------------------------------------------------------------
        // Worksheet 3 — External accessories
        // -----------------------------------------------------------------------
        private static void FillAccessoriesSheet(XLWorkbook wb, BmReportData d)
        {
            IXLWorksheet ws;
            try { ws = wb.Worksheets.Worksheet(4); } // VB(3) → ClosedXML(4)
            catch { return; }

            ws.Cell("A2").Value = d.ExternalAccessories;
            ws.Cell("B2").Value = d.ExternalAccessories_B;

            if (!string.IsNullOrEmpty(d.ExternalAccessoriesData))
            {
                var parts = d.ExternalAccessoriesData.Split('|');
                for (int i = 0; i < Math.Min(parts.Length, 4); i++)
                {
                    var cols = parts[i].Split(',');
                    if (cols.Length >= 2)
                    {
                        ws.Cell($"A{3 + i}").Value = cols[0];
                        ws.Cell($"B{3 + i}").Value = cols[1];
                    }
                }
            }

            // External accessory QC flag from QualityCheckData[6]
            if (!string.IsNullOrEmpty(d.QualityCheckData))
            {
                var arr = d.QualityCheckData.Split('|');
                if (arr.Length > 6)
                {
                    var cols = arr[6].Split(',');
                    if (cols.Length >= 2)
                    {
                        ws.Cell("C2").Value = cols[0];
                        ws.Cell("D2").Value = cols[1];
                    }
                }
            }
        }
    }
}