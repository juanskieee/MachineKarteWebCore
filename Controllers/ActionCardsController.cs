// Converted from Form_Input.vb — E-Karte System
// ASP.NET Core workflow controller

using Microsoft.AspNetCore.Mvc;
using MachineKarte.Web.Models;
using MachineKarte.Web.Services;

namespace MachineKarte.Web.Controllers
{
    public class ActionCardsController : WorkflowController
    {
        private readonly ActionCardService _svc;
        private readonly ILogger<ActionCardsController> _logger;

        public ActionCardsController(
            ActionCardService svc,
            ILogger<ActionCardsController> logger)
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

        [HttpGet, WorkflowHandler]
        public IActionResult Index() => WorkflowView();

        // ── AJAX: Load card data ─────────────────────────────────────
        // GET /ActionCards?handler=Load&bmId=...&mcNo=...&userType=...&status=...&colorName=...&headerCase=...

        [HttpGet, WorkflowHandler("Load")]
        public async Task<IActionResult> LoadAsync(
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

                var card = await _svc.PrepareCardAsync(req, HttpContext.RequestAborted);

                return new JsonResult(new AjaxResult<ActionCardData>
                {
                    Success = true,
                    Data = card
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !HttpContext.RequestAborted.IsCancellationRequested)
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

        [HttpPost, WorkflowHandler("Save")]
        public async Task<IActionResult> SaveAsync(
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