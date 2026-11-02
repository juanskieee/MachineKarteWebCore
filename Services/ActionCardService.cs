// Services/ActionCardService.cs
// Converted from Form_Input.vb business logic — E-Karte System
// C# ASP.NET Core

using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;

namespace MachineKarte.Web.Services
{
    public class ActionCardService
    {
        private readonly ActionCardRepository _repo;
        private readonly ILogger<ActionCardService> _logger;

        public ActionCardService(
            ActionCardRepository repo,
            ILogger<ActionCardService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        // ----------------------------------------------------------------
        // LOAD  (mirrors LoadData public + private overloads in Form_Input.vb)
        // ----------------------------------------------------------------

        /// <summary>
        /// Prepares the card data for the page:
        /// - "Existing" → fetches from DB, pre-populates all fields.
        /// - "New"      → returns empty card with DateCard = now, status pre-set from color.
        /// </summary>
        public async Task<ActionCardData> PrepareCardAsync(ActionCardLoadRequest req)
        {
            if (req.Status == "Existing")
            {
                var existing = await _repo.LoadAsync(req.BmId, req.McNo, req.UserType);

                if (existing is null)
                    throw new InvalidOperationException(
                        $"No action card found for BM {req.BmId} / Machine {req.McNo}.");

                existing.UserType = req.UserType;
                existing.HeaderCase = req.HeaderCase;
                existing.ColorName = req.ColorName;
                var colorStatus = McStatusOptions.FromColorName(req.ColorName);
                if (colorStatus != "-SELECT-")
                    existing.McStatus = colorStatus;
                return existing;
            }

            // New card — mirrors the Else branch in LoadData()
            return new ActionCardData
            {
                BmId = req.BmId,
                MachineNo = req.McNo,
                UserType = req.UserType,
                Status = "New",
                HeaderCase = req.HeaderCase,
                ColorName = req.ColorName,
                DateCard = DateTime.Now.ToString("MM/dd/yyyy hh:mm:ss"),
                McStatus = McStatusOptions.FromColorName(req.ColorName)
            };
        }

        // ----------------------------------------------------------------
        // SAVE / UPDATE  (mirrors btnSave_Click → SaveData / UpdateData)
        // ----------------------------------------------------------------

        public async Task<AjaxResult<string>> SaveAsync(ActionCardSaveRequest req)
        {
            // Validation: mirrors "If cbStatus.Text = '-SELECT-'" guard
            if (req.Card.McStatus == "-SELECT-" || string.IsNullOrWhiteSpace(req.Card.McStatus))
                return Fail("Please select a MC Status.");

            if (req.StatusId is not ("New" or "Existing"))
                return Fail("Invalid action card status. Please reopen the card.");

            if (!int.TryParse(req.Card.BmId, out var bmId) || bmId <= 0 ||
                string.IsNullOrWhiteSpace(req.Card.MachineNo))
                return Fail("A valid BM ID and machine number are required.");

            if (req.StatusId == "Existing" && req.Card.Seqno <= 0)
                return Fail("Please reopen the existing action card before saving.");

            try
            {
                bool existing = req.StatusId == "Existing";
                bool saved = existing
                    ? await _repo.UpdateAsync(req.Card, req.Card.UserType)
                    : await _repo.InsertAsync(req.Card, req.Card.UserType);

                if (!saved)
                    return Fail(existing
                        ? "This action card no longer matches the loaded record. Refresh Monitoring and reopen it."
                        : "An action card already exists for this BM. Refresh Monitoring and open the existing card.");

                string msg = existing
                    ? "Successfully Updated"
                    : "Successfully Saved";

                return new AjaxResult<string> { Success = true, Message = msg, Data = msg };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ActionCard save failed BM={BmId} MC={McNo}",
                    req.Card.BmId, req.Card.MachineNo);
                return Fail("An error occurred: " + ex.Message);
            }
        }

        private static AjaxResult<string> Fail(string msg) =>
            new() { Success = false, Message = msg };
    }
}
