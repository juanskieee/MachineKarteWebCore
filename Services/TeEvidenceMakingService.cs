using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using ClosedXML.Excel;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
using Microsoft.Extensions.Configuration;

namespace MachineKarte.Web.Services.Reporting
{
    /// <summary>
    /// Web-side equivalent of Form10.EMakingPrint().
    /// Loads CWC.xlsx, fills it with data from TEBMEvidenceMaking + ViewTEData,
    /// saves the completed workbook in TempData/GeneratedPdf/, and returns the
    /// server-relative download URL.
    ///
    /// NOTE: ClosedXML does not support inserting images or saving as PDF natively.
    /// The two workarounds used throughout this project are:
    ///   1. Images → embed as base64 data-URIs in the HTML preview served to the browser.
    ///   2. PDF → use the ClosedXML + ClosedXML.Report pipeline or a headless Chrome
    ///      print, or simply return the filled .xlsx for the user to print.
    /// For consistency with EvidenceMakingService (Form4) the same approach is used:
    /// we fill the workbook cells and return a download URL for the .xlsx; the browser
    /// side triggers a print-to-PDF prompt.  If the project later adds a PDF library
    /// (e.g. Telerik Reporting, SelectPdf), only the save step here changes.
    /// </summary>
    public class TeEvidenceMakingService
    {
        private readonly TeEvidenceRepository _repo;
        private readonly string _templatesRoot;
        private readonly string _imagesRoot;
        private readonly string _outputRoot;

        // Cell mappings for category descriptions (mirrors VB Do While emDRCat.Read loop)
        // Key: (Category, Condition) → Excel cell address on ws (Sheet 1)
        private static readonly Dictionary<(string Cat, string Cond), string> DescCellMap
            = new()
            {
                { ("PHENOMENON",                     "BEFORE"), "H20" },
                { ("PHENOMENON",                     "AFTER"),  "P20" },
                { ("MACHINE COUNTER AND VISUAL BIN", "BEFORE"), "H31" },
                { ("MACHINE COUNTER AND VISUAL BIN", "AFTER"),  "P31" },
                { ("PROGRAM LOADED",                 "BEFORE"), "H36" },
                { ("PROGRAM LOADED",                 "AFTER"),  "P36" },
                { ("TEST BOARD",                     "BEFORE"), "H45" },
                { ("TEST BOARD",                     "AFTER"),  "P45" },
                { ("TEST CABLE",                     "BEFORE"), "H54" },
                { ("TEST CABLE",                     "AFTER"),  "P54" },
                { ("OTHER EVIDENCE",                 "BEFORE"), "H62" },
                { ("OTHER EVIDENCE",                 "AFTER"),  "P62" },
            };

        // Quality-condition cell mappings (ws_3 / Sheet 3) — text descriptions
        private static readonly Dictionary<(string Cat, string Cond), (int Sheet, string Cell)>
            QualityDescCellMap = new()
            {
                { ("BEFORE QUALITY CONDITION", "BEFORE"), (3, "E7") },
                { ("AFTER QUALITY CONDITION",  "AFTER"),  (3, "J7") },
                { ("IDEAL QUALITY CONDITION",  "AFTER"),  (3, "O7") },
            };

        public TeEvidenceMakingService(
            TeEvidenceRepository repo,
            IConfiguration cfg)
        {
            _repo = repo;
            _templatesRoot = cfg["EvidenceMaking:TemplatesRoot"] ?? "Templates/BmReport";
            _imagesRoot = cfg["EvidenceMaking:ImagesRoot"] ?? "Uploads/EvidenceImages";
            _outputRoot = cfg["EvidenceMaking:OutputRoot"] ?? "TempData/GeneratedPdf";
        }

        // ─── Main entry point (mirrors Form10.EMakingPrint) ──────────────────

        /// <summary>
        /// Fills CWC.xlsx with TE evidence data and saves a completed workbook.
        /// Returns the web-relative path of the generated file.
        /// </summary>
        public async Task<string> GenerateAsync(string bmId)
        {
            // 1. Load all data in parallel
            var headerTask = _repo.GetHeaderAsync(bmId);
            var teAssignTask = _repo.GetTEAssignAsync(bmId);
            var catDescsTask = _repo.GetCategoryDescsAsync(bmId);
            var imagesTask = _repo.GetCategoryImagesAsync(bmId);
            var telekoTask = _repo.GetTelekoImagesAsync(bmId);
            var qualityTask = _repo.GetQualityImagesAsync(bmId);
            var miscTask = _repo.GetMiscDataAsync(bmId);

            await Task.WhenAll(headerTask, teAssignTask, catDescsTask,
                               imagesTask, telekoTask, qualityTask, miscTask);

            var header = await headerTask;
            header.TEAssign = await teAssignTask;
            var (ideas, waferNo, remarks, qcData) = await miscTask;
            header.Ideas = ideas;
            header.WaferNo = waferNo;
            header.Remarks = remarks;
            header.TelekoCheckingData = qcData;

            // 2. Open the CWC.xlsx template
            string templatePath = Path.Combine(_templatesRoot, "CWC.xlsx");
            if (!File.Exists(templatePath))
                throw new FileNotFoundException($"TE Evidence template not found: {templatePath}");

            using var wb = new XLWorkbook(templatePath);

            // Mirrors VB: ws = Worksheet(0), ws_3 = Worksheet(2)
            var ws = wb.Worksheet(1);   // ClosedXML is 1-based
            var ws_3 = wb.Worksheets.Count >= 3 ? wb.Worksheet(3) : null;

            // 3. Fill header cells (mirrors VB Do While DR.Read)
            ws.Cell("M4").Value = header.MCNo;
            ws.Cell("P4").Value = header.TesterNo;
            ws.Cell("M6").Value = header.DeviceName;
            ws.Cell("M8").Value = header.LotNo;
            ws.Cell("B5").Value = header.TestNoNG;
            ws.Cell("M7").Value = header.WaferNo;
            ws.Cell("M14").Value = header.Remarks;
            ws.Cell("N12").Value = header.TEAssign;
            ws.Cell("B78").Value = header.Ideas;

            if (DateTime.TryParse(header.StartOfRepair, out var dts))
            {
                ws.Cell("M10").Value = dts.ToString("MM-dd-yyyy");
                ws.Cell("M11").Value = dts.ToString("HH:mm:ss");
            }
            if (DateTime.TryParse(header.EndOfRepair, out var dte))
            {
                ws.Cell("O10").Value = dte.ToString("MM-dd-yyyy");
                ws.Cell("O11").Value = dte.ToString("HH:mm:ss");
            }

            // 4. TelekoCheckingData → QC table rows B72:G76 (mirrors VB arrQCData loop)
            FillQcTable(ws, header.TelekoCheckingData);

            // 5. Category descriptions
            var catDescs = await catDescsTask;
            foreach (var d in catDescs)
            {
                var key = (d.Category, d.Condition.ToUpperInvariant());
                if (DescCellMap.TryGetValue(key, out var cell))
                    ws.Cell(cell).Value = d.Desc;
                else if (QualityDescCellMap.TryGetValue(key, out var qc) && ws_3 != null)
                    wb.Worksheet(qc.Sheet).Cell(qc.Cell).Value = d.Desc;
            }

            // 6. Images — ClosedXML supports AddPicture from file path
            //    We skip images silently if the file does not exist (mirror VB noimage fallback).
            var images = await imagesTask;
            FillCategoryImages(ws, images, _imagesRoot);

            var telekoImages = await telekoTask;
            FillTelekoImages(ws, telekoImages, _imagesRoot);

            if (ws_3 != null)
            {
                var qualityImages = await qualityTask;
                FillQualityImages(ws_3, qualityImages, _imagesRoot);
            }

            // 7. Save output
            Directory.CreateDirectory(_outputRoot);
            string fileName = $"TE_Evidence_{bmId}.xlsx";
            string outputPath = Path.Combine(_outputRoot, fileName);
            wb.SaveAs(outputPath);

            // Return web-relative URL so the Razor Page can stream it as a download
            return $"/TempData/GeneratedPdf/{fileName}";
        }

        // ─── QC table (B72:G76) ───────────────────────────────────────────────

        private static void FillQcTable(IXLWorksheet ws, string qcData)
        {
            if (string.IsNullOrWhiteSpace(qcData)) return;
            var rows = qcData.Split('|');
            string[,] startCells =
            {
                { "B72","C72","E72","G72" },
                { "B73","C73","E73","G73" },
                { "B74","C74","E74","G74" },
                { "B75","C75","E75","G75" },
                { "B76","C76","E76","G76" },
            };
            int rowMax = Math.Min(rows.Length, 5);
            for (int r = 0; r < rowMax; r++)
            {
                var cols = rows[r].Split(',');
                int colMax = Math.Min(cols.Length, 4);
                for (int c = 0; c < colMax; c++)
                    ws.Cell(startCells[r, c]).Value = cols[c];
            }
        }

        // ─── Category image slots → Excel pictures ────────────────────────────

        // Maps (category, condition, imageIndex) → (Excel cell, widthInch, heightInch)
        // Mirrors the VB ws.Pictures.Add(…, "B20", 1.3, 1.61, LengthUnit.Inch) calls.
        private static readonly (string Cat, string Cond, int Idx, string Cell, double W, double H)[] ImageSlots =
        {
            // PHENOMENON BEFORE
            ("PHENOMENON","BEFORE",0,"B20",1.30,1.61),
            ("PHENOMENON","BEFORE",1,"D20",1.46,1.61),
            ("PHENOMENON","BEFORE",2,"F20",1.52,1.61),
            // PHENOMENON AFTER
            ("PHENOMENON","AFTER",0,"J20",1.30,1.61),
            ("PHENOMENON","AFTER",1,"L20",1.46,1.61),
            ("PHENOMENON","AFTER",2,"N20",1.52,1.61),
            // MACHINE COUNTER BEFORE
            ("MACHINE COUNTER AND VISUAL BIN","BEFORE",0,"B31",1.29,1.44),
            ("MACHINE COUNTER AND VISUAL BIN","BEFORE",1,"D31",1.44,1.44),
            ("MACHINE COUNTER AND VISUAL BIN","BEFORE",2,"F31",1.48,1.44),
            // MACHINE COUNTER AFTER
            ("MACHINE COUNTER AND VISUAL BIN","AFTER",0,"J31",1.27,1.44),
            ("MACHINE COUNTER AND VISUAL BIN","AFTER",1,"L31",1.48,1.44),
            ("MACHINE COUNTER AND VISUAL BIN","AFTER",2,"N31",1.52,1.44),
            // PROGRAM LOADED BEFORE
            ("PROGRAM LOADED","BEFORE",0,"B36",1.27,1.66),
            ("PROGRAM LOADED","BEFORE",1,"D36",1.44,1.66),
            ("PROGRAM LOADED","BEFORE",2,"F36",1.48,1.66),
            // PROGRAM LOADED AFTER
            ("PROGRAM LOADED","AFTER",0,"J36",1.27,1.66),
            ("PROGRAM LOADED","AFTER",1,"L36",1.44,1.66),
            ("PROGRAM LOADED","AFTER",2,"N36",1.52,1.66),
            // TEST BOARD BEFORE (cols B,D,F; rows 45+)
            ("TEST BOARD","BEFORE",0,"B45",1.27,1.44),
            ("TEST BOARD","BEFORE",1,"D45",1.44,1.44),
            ("TEST BOARD","BEFORE",2,"F45",1.48,1.44),
            // TEST BOARD AFTER
            ("TEST BOARD","AFTER",0,"J45",1.27,1.44),
            ("TEST BOARD","AFTER",1,"L45",1.44,1.44),
            ("TEST BOARD","AFTER",2,"N45",1.52,1.44),
            // TEST CABLE BEFORE
            ("TEST CABLE","BEFORE",0,"B54",1.27,1.42),
            ("TEST CABLE","BEFORE",1,"D54",1.46,1.42),
            ("TEST CABLE","BEFORE",2,"F54",1.46,1.42),
            // TEST CABLE AFTER
            ("TEST CABLE","AFTER",0,"J54",1.27,1.42),
            ("TEST CABLE","AFTER",1,"L54",1.46,1.42),
            ("TEST CABLE","AFTER",2,"N54",1.53,1.42),
            // OTHER EVIDENCE BEFORE
            ("OTHER EVIDENCE","BEFORE",0,"B62",1.27,1.42),
            ("OTHER EVIDENCE","BEFORE",1,"D62",1.46,1.42),
            ("OTHER EVIDENCE","BEFORE",2,"F62",1.46,1.42),
            // OTHER EVIDENCE AFTER
            ("OTHER EVIDENCE","AFTER",0,"J62",1.26,1.42),
            ("OTHER EVIDENCE","AFTER",1,"L62",1.46,1.42),
            ("OTHER EVIDENCE","AFTER",2,"N62",1.52,1.42),
        };

        private static void FillCategoryImages(
            IXLWorksheet ws,
            Dictionary<string, List<string>> images,
            string imagesRoot)
        {
            foreach (var (cat, cond, idx, cell, w, h) in ImageSlots)
            {
                var key = $"{cat}_{cond}";
                if (!images.TryGetValue(key, out var list) || idx >= list.Count) continue;

                var relativePath = list[idx];
                if (string.IsNullOrWhiteSpace(relativePath)) continue;

                var fullPath = Path.Combine(imagesRoot, relativePath.TrimStart('/', '\\'));
                if (!File.Exists(fullPath)) continue;

                try
                {
                    var pic = ws.AddPicture(fullPath);
                    var anchor = ws.Cell(cell);
                    pic.MoveTo(anchor);
                    pic.Width = (int)(w * 96);  // approx px at 96 dpi
                    pic.Height = (int)(h * 96);
                }
                catch { /* skip unreadable images */ }
            }
        }

        private static void FillTelekoImages(
            IXLWorksheet ws,
            List<TelekoEvidenceImage> images,
            string imagesRoot)
        {
            var cells = new[] { "B70", "D70", "F70", "H70" };
            for (var index = 0; index < Math.Min(images.Count, cells.Length); index++)
                AddPicture(ws, images[index].TelekoEvidence, cells[index], 125, 125, imagesRoot);
        }

        private static void FillQualityImages(
            IXLWorksheet ws,
            List<QualityConditionImage> images,
            string imagesRoot)
        {
            var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BEFORE QUALITY CONDITION"] = "F3",
                ["AFTER QUALITY CONDITION"] = "K3",
                ["IDEAL QUALITY CONDITION"] = "P3"
            };

            foreach (var image in images)
            {
                if (cells.TryGetValue(image.Category, out var cell))
                    AddPicture(ws, image.ImgFile, cell, 150, 150, imagesRoot);
            }
        }

        private static void AddPicture(
            IXLWorksheet ws,
            string relativePath,
            string cell,
            int width,
            int height,
            string imagesRoot)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return;

            var fullPath = Path.Combine(imagesRoot, relativePath.TrimStart('/', '\\'));
            if (!File.Exists(fullPath)) return;

            try
            {
                var picture = ws.AddPicture(fullPath);
                picture.MoveTo(ws.Cell(cell));
                picture.Width = width;
                picture.Height = height;
            }
            catch
            {
                // Preserve workbook generation when an individual image is unavailable.
            }
        }
    }
}