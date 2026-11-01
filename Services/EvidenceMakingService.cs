// Services/Reporting/EvidenceMakingService.cs
// Converted from Form4.vb EMakingPrint() — E-Karte System
// C# ASP.NET Core
//
// Replaces: GemBox.Spreadsheet → ClosedXML (open-source, MIT license)
//           PDF export         → ClosedXML + PdfSharp or save Excel then convert
//           Windows impersonation → not needed; server has direct file access
//           WebBrowser control → browser streams the file via DownloadUrl
//
// NuGet packages required:
//   <PackageReference Include="ClosedXML" Version="0.102.*" />
//   (Optional PDF step: use GemBox.Spreadsheet server-side if licensed,
//    or convert via Microsoft.Office.Interop / LibreOffice headless)

using ClosedXML.Excel;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services.Reporting
{
    public class EvidenceMakingService
    {
        private readonly EvidenceRepository _repo;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<EvidenceMakingService> _logger;

        public EvidenceMakingService(
            EvidenceRepository repo,
            IConfiguration config,
            IWebHostEnvironment env,
            ILogger<EvidenceMakingService> logger)
        {
            _repo = repo;
            _config = config;
            _env = env;
            _logger = logger;
        }

        // ── Paths (replace My.Settings.PathServer UNC shares) ────────────
        private string TemplatesRoot =>
            Path.Combine(_env.ContentRootPath, "Templates", "EvidenceMaking");

        private string ImagesRoot =>
            _config["EvidenceMaking:ImagesRoot"]
            ?? Path.Combine(_env.ContentRootPath, "Uploads", "EvidenceImages");

        private string NoImagePath =>
            Path.Combine(ImagesRoot, "noimage.png");

        private string OutputRoot =>
            Path.Combine(_env.ContentRootPath, "TempData", "GeneratedPdf");

        // ----------------------------------------------------------------
        // Main entry point
        // Mirrors: EMakingPrint(ProcessCase As String, bmid As Integer)
        // ----------------------------------------------------------------

        public async Task<EvidenceGenerateResult> GenerateEvidenceAsync(
            EvidenceGenerateRequest request)
        {
            var cfg = ProcessTemplateMap.Get(request.Process);
            if (cfg is null)
                return Fail($"Unknown process: {request.Process}");

            // 1. Fetch all data (mirrors the 5 SQL queries + TSC + header queries)
            var images = await _repo.GetEvidenceImagesAsync(request.BmId, request.Process);
            var tsc = await _repo.GetTscConfirmationAsync(
                             request.BmId, request.Process, cfg.EvidenceView);
            var header = await _repo.GetMachineHeaderAsync(request.BmId, cfg.DataView);

            if (header is null)
                return Fail($"No machine data found for BM ID {request.BmId}");

            var data = new EvidenceTemplateData
            {
                Process = request.Process,
                BmId = request.BmId,
                Header = header,
                Images = images,
                Tsc = tsc,
                IsSing = request.Process.Equals("SING", StringComparison.OrdinalIgnoreCase)
            };

            // 2. Load Excel template and populate it
            string templatePath = Path.Combine(TemplatesRoot, cfg.TemplateFile);
            if (!File.Exists(templatePath))
                return Fail($"Template not found: {cfg.TemplateFile}");

            string outputFileName = $"{request.Process}Evidence_{request.BmId}.xlsx";
            string outputPath = Path.Combine(OutputRoot, outputFileName);
            Directory.CreateDirectory(OutputRoot);

            try
            {
                using var wb = new XLWorkbook(templatePath);
                var ws = wb.Worksheets.First();   // Active worksheet

                PopulateHeader(ws, data.Header);
                PopulateImages(ws, data, request.Process);
                PopulateTscConfirmation(ws, data.Tsc, data.IsSing);

                wb.SaveAs(outputPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evidence generation failed for BM {BmId}", request.BmId);
                return Fail(ex.Message);
            }

            // 3. Return download URL (browser streams the xlsx;
            //    swap to PDF output path if a PDF converter is wired in)
            string downloadUrl =
                $"/EvidenceMaking?handler=Download&fileName={Uri.EscapeDataString(outputFileName)}";
            return new EvidenceGenerateResult
            {
                Success = true,
                DownloadUrl = downloadUrl,
                FileName = outputFileName
            };
        }

        // ----------------------------------------------------------------
        // Populate machine header cells
        // Mirrors DR loop writing E3, E4, E5, E6, O3, O4, AA3, AA4, AA5
        // ----------------------------------------------------------------

        private static void PopulateHeader(IXLWorksheet ws, EvidenceMachineHeader h)
        {
            ws.Cell("E3").Value = h.McNo;
            ws.Cell("E4").Value = h.McType;
            ws.Cell("E5").Value = h.PkgName;
            ws.Cell("E6").Value = h.DeviceName;
            ws.Cell("O3").Value = h.LotNo;
            ws.Cell("O4").Value = h.PmConfirmBy;
            ws.Cell("AA3").Value = h.OccurrenceDate;
            ws.Cell("AA4").Value = h.StartOfRepair;
            ws.Cell("AA5").Value = h.EndOfRepair;
        }

        // ----------------------------------------------------------------
        // Populate evidence images and remark text cells per process
        // Mirrors the large If ProcessGroup = "SING" / Else block in Form4.vb
        //
        // NOTE: ClosedXML does not directly embed images from a file path.
        // Use AddPicture(stream) — the helper below handles existence check
        // (mirrors: If File.Exists(strFolder & imagesData(n)) ... noImage)
        // ----------------------------------------------------------------

        private void PopulateImages(
            IXLWorksheet ws, EvidenceTemplateData data, string process)
        {
            var img = data.Images;
            bool sing = data.IsSing;

            if (sing)
            {
                // ── SING layout ─────────────────────────────────────────
                // Problem Point (3 images)
                AddPicture(ws, img.ProblemPoint[0].ImageFile, "C11", 350, 200);
                ws.Cell("B20").Value = img.ProblemPoint[0].Description;
                AddPicture(ws, img.ProblemPoint[1].ImageFile, "O11", 270, 200);
                ws.Cell("N20").Value = img.ProblemPoint[1].Description;
                AddPicture(ws, img.ProblemPoint[2].ImageFile, "Z11", 310, 200);

                // Action Taken (2 images)
                AddPicture(ws, img.ActionTaken[0].ImageFile, "C27", 280, 200);
                AddPicture(ws, img.ActionTaken[1].ImageFile, "L27", 285, 200);
                ws.Cell("B37").Value = img.ActionTaken[0].Description;
                ws.Cell("K37").Value = img.ActionTaken[1].Description;

                // Parts Replaced (2 images)
                AddPicture(ws, img.PartsReplaced[0].ImageFile, "V27", 215, 200);
                AddPicture(ws, img.PartsReplaced[1].ImageFile, "AD27", 225, 200);
                ws.Cell("U37").Value = img.PartsReplaced[0].Description;
                ws.Cell("AC37").Value = img.PartsReplaced[1].Description;

                // Dis/Re-assembly (2 images)
                AddPicture(ws, img.DisAssembly[0].ImageFile, "C47", 420, 210);
                AddPicture(ws, img.DisAssembly[1].ImageFile, "T47", 350, 210);
                ws.Cell("P46").Value = img.DisAssembly[0].Description;
                ws.Cell("AH46").Value = img.DisAssembly[1].Description;

                // Quality Confirmation (3 images)
                AddPicture(ws, img.QualityConfirm[0].ImageFile, "C69", 365, 220);
                AddPicture(ws, img.QualityConfirm[1].ImageFile, "O69", 310, 220);
                AddPicture(ws, img.QualityConfirm[2].ImageFile, "AA69", 275, 220);
                ws.Cell("D82").Value = img.QualityConfirm[0].Description;
                ws.Cell("P82").Value = img.QualityConfirm[1].Description;
                ws.Cell("AB82").Value = img.QualityConfirm[2].Description;
            }
            else
            {
                // ── All other processes (DC, DB, WB, MP, PL, FL, FT, TP, EDS, MEMS) ──
                // Problem Point (3 images)
                AddPicture(ws, img.ProblemPoint[0].ImageFile, "C11", 280, 240);
                ws.Cell("K10").Value = img.ProblemPoint[0].Description;
                AddPicture(ws, img.ProblemPoint[1].ImageFile, "P11", 270, 240);
                ws.Cell("Y10").Value = img.ProblemPoint[1].Description;
                AddPicture(ws, img.ProblemPoint[2].ImageFile, "AC11", 240, 240);

                // Action Taken (2 images)
                AddPicture(ws, img.ActionTaken[0].ImageFile, "C26", 280, 220);
                AddPicture(ws, img.ActionTaken[1].ImageFile, "L26", 285, 220);
                ws.Cell("B24").Value = img.ActionTaken[0].Description;
                ws.Cell("K24").Value = img.ActionTaken[1].Description;

                // Parts Replaced (2 images)
                AddPicture(ws, img.PartsReplaced[0].ImageFile, "V26", 215, 220);
                AddPicture(ws, img.PartsReplaced[1].ImageFile, "AD26", 225, 220);
                ws.Cell("U24").Value = img.PartsReplaced[0].Description;
                ws.Cell("AC24").Value = img.PartsReplaced[1].Description;

                // Dis/Re-assembly (2 images)
                AddPicture(ws, img.DisAssembly[0].ImageFile, "C46", 420, 220);
                AddPicture(ws, img.DisAssembly[1].ImageFile, "T46", 350, 215);
                ws.Cell("P45").Value = img.DisAssembly[0].Description;
                ws.Cell("AH45").Value = img.DisAssembly[1].Description;

                // Quality Confirmation (3 images)
                AddPicture(ws, img.QualityConfirm[0].ImageFile, "C68", 365, 250);
                AddPicture(ws, img.QualityConfirm[1].ImageFile, "O68", 310, 250);
                AddPicture(ws, img.QualityConfirm[2].ImageFile, "AA68", 275, 250);
                ws.Cell("B81").Value = img.QualityConfirm[0].Description;
                ws.Cell("N81").Value = img.QualityConfirm[1].Description;
                ws.Cell("Z81").Value = img.QualityConfirm[2].Description;
            }
        }

        // ----------------------------------------------------------------
        // Populate TSC confirmation checkmarks
        // Mirrors DRss1 loop in Form4.vb — writes "✔" to specific cells
        // Two sets of cell addresses: SING vs all others
        // ----------------------------------------------------------------

        private static void PopulateTscConfirmation(
            IXLWorksheet ws, TscConfirmation t, bool isSing)
        {
            if (isSing)
            {
                // tsc1 — Route confirmation (row 61)
                SetCheck(ws, t.Tsc1, yes: "S61", no: "W61");
                ws.Cell("AF63").Value = t.RouteNo;

                // tsc6 — Version confirmation (row 40)
                SetCheck3(ws, t.Tsc6, yes: "Q40", no: "U40", na: "Y40");
                ws.Cell("AF42").Value = t.VersionNo;

                // tsc2..5 (rows 62–65)
                SetCheck(ws, t.Tsc2, yes: "S62", no: "W62");
                SetCheck(ws, t.Tsc3, yes: "S63", no: "W63");
                SetCheck(ws, t.Tsc4, yes: "S64", no: "W64");
                SetCheck(ws, t.Tsc5, yes: "S65", no: "W65");

                // tsc7..9 (rows 41–43)
                SetCheck3(ws, t.Tsc7, yes: "Q41", no: "U41", na: "Y41");
                SetCheck3(ws, t.Tsc8, yes: "Q42", no: "U42", na: "Y42");
                SetCheck3(ws, t.Tsc9, yes: "Q43", no: "U43", na: "Y43");

                // SING-only safety dropdowns
                ws.Cell("AF20").Value = NotZeroOrEmpty(t.SignalTowerKanban) ? t.SignalTowerKanban : "";
                ws.Cell("AF21").Value = NotZeroOrEmpty(t.SafetyTags) ? t.SafetyTags : "";
                ws.Cell("AF22").Value = NotZeroOrEmpty(t.Bypass) ? t.Bypass : "";
            }
            else
            {
                // tsc1 — Route confirmation (row 60)
                SetCheck(ws, t.Tsc1, yes: "S60", no: "W60");
                ws.Cell("AF62").Value = t.RouteNo;

                // tsc6 — Version confirmation (row 39)
                SetCheck3(ws, t.Tsc6, yes: "Q39", no: "U39", na: "Y39");
                ws.Cell("AF41").Value = t.VersionNo;

                // tsc2..5 (rows 61–64)
                SetCheck(ws, t.Tsc2, yes: "S61", no: "W61");
                SetCheck(ws, t.Tsc3, yes: "S62", no: "W62");
                SetCheck(ws, t.Tsc4, yes: "S63", no: "W63");
                SetCheck(ws, t.Tsc5, yes: "S64", no: "W64");

                // tsc7..9 (rows 40–42)
                SetCheck3(ws, t.Tsc7, yes: "Q40", no: "U40", na: "Y40");
                SetCheck3(ws, t.Tsc8, yes: "Q41", no: "U41", na: "Y41");
                SetCheck3(ws, t.Tsc9, yes: "Q42", no: "U42", na: "Y42");
            }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Adds a picture from ImagesRoot; falls back to noimage.png.
        /// Mirrors: ws.Pictures.Add(If(File.Exists(...), path, noImage), cell, w, h)
        /// </summary>
        private void AddPicture(IXLWorksheet ws, string relPath,
                                 string cell, int width, int height)
        {
            string fullPath = string.IsNullOrWhiteSpace(relPath)
                ? NoImagePath
                : Path.Combine(ImagesRoot, relPath.TrimStart('/', '\\'));

            if (!File.Exists(fullPath))
                fullPath = NoImagePath;

            if (!File.Exists(fullPath)) return;  // no fallback either — skip

            try
            {
                ws.AddPicture(fullPath)
                  .MoveTo(ws.Cell(cell))
                  .WithSize(width, height);
            }
            catch (Exception ex)
            {
                // Non-fatal — log and continue
                _logger.LogWarning(ex, "Could not embed image: {Path}", fullPath);
            }
        }

        private static void SetCheck(IXLWorksheet ws, string val, string yes, string no)
        {
            if (val == "Yes") ws.Cell(yes).Value = "✔";
            else if (val == "No") ws.Cell(no).Value = "✔";
        }

        private static void SetCheck3(
            IXLWorksheet ws, string val, string yes, string no, string na)
        {
            if (val == "Yes") ws.Cell(yes).Value = "✔";
            else if (val == "No") ws.Cell(no).Value = "✔";
            else if (val == "N/A") ws.Cell(na).Value = "✔";
        }

        private static bool NotZeroOrEmpty(string v) =>
            !string.IsNullOrWhiteSpace(v) && v != "0";

        private static EvidenceGenerateResult Fail(string msg) =>
            new() { Success = false, Message = msg };
    }
}