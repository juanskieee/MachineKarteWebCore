// Pages/ActionCards.cshtml.cs
// Converted from Form_Input.vb — E-Karte System
// C# ASP.NET Core Razor Pages

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Pages
{
    public class ActionCardsModel : PageModel
    {
        private readonly ActionCardService _svc;
        private readonly ILogger<ActionCardsModel> _logger;

        public ActionCardsModel(
            ActionCardService svc,
            ILogger<ActionCardsModel> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        // ── GET ──────────────────────────────────────────────────────
        // Called when:
        //   a) User clicks a BM card → "New"
        //      /ActionCards?bmId=123&mcNo=MC01&userType=PM&colorName=Red&headerCase=OPEN+BM
        //   b) User clicks "With Action" on a card → "Existing"
        //      /ActionCards?bmId=123&mcNo=MC01&userType=PM&status=Existing
        // Mirrors LoadData(bmid, status, mcno, ColorT, HeaderCase) in Form_Input.vb

        public void OnGet() { }

        // ── AJAX: Load card data ─────────────────────────────────────
        // GET /ActionCards?handler=Load&bmId=...&mcNo=...&userType=...&status=...&colorName=...&headerCase=...

        public async Task<IActionResult> OnGetLoadAsync(
            string bmId, string mcNo, string userType = "PM",
            string status = "New", string colorName = "", string headerCase = "")
        {
            try
            {
                var req = new ActionCardLoadRequest
                {
                    BmId = bmId,
                    McNo = mcNo,
                    UserType = userType,
                    Status = status,
                    ColorName = colorName,
                    HeaderCase = headerCase
                };

                var card = await _svc.PrepareCardAsync(req);

                return new JsonResult(new AjaxResult<ActionCardData>
                {
                    Success = true,
                    Data = card
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ActionCard Load: BM={BmId} MC={McNo}", bmId, mcNo);
                return new JsonResult(new AjaxResult<ActionCardData>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // ── AJAX: Save / Update ──────────────────────────────────────
        // Mirrors btnSave_Click → SaveData() / UpdateData()
        // POST /ActionCards?handler=Save
        // Body: ActionCardSaveRequest JSON

        public async Task<IActionResult> OnPostSaveAsync(
            [FromBody] ActionCardSaveRequest req)
        {
            if (req?.Card is null)
                return new JsonResult(new AjaxResult<string>
                {
                    Success = false,
                    Message = "Invalid request."
                });

            var result = await _svc.SaveAsync(req);
            return new JsonResult(result);
        }
    }
}