/** Load, edit, and save the selected action card while preserving its record sequence. */

Workflows.register("ActionCards", (root, options, context) => {
    "use strict";

    // Preserve Seqno: existing Action Card edits must target the exact loaded record.
    const { byId } = context;
    let cardSeqno = 0;

    function initializeActionCard(params) {
        setValue("hfBmId", params.bmId);
        setValue("hfMcNo", params.mcNo);
        setValue("hfUserType", params.userType);
        setValue("hfStatusId", params.status);
        setValue("hfColorName", params.colorName);

        // Update reply label: "PM Reply" vs "TE Reply"
        // Mirrors: If Form1.cbUser.Text = "TE" Then Label18.Text = "TE Reply"
        const replyText = params.userType === "TE" ? "TE Reply" : "PM Reply";

        for (let i = 1; i <= 3; i++) {
            const cell = byId(`replyLabelCell${i}`);
            if (cell)
                cell.textContent = `${["Accreditor", "Section Manager", "Department Manager"][i - 1]} / ${replyText}`;
        }

        // Show machine banner
        if (params.mcNo) {
            byId("bannerMcNo").textContent = params.mcNo.toUpperCase();
            byId("bannerHeaderCase").textContent = params.headerCase
                ? ` — ${params.headerCase}`
                : "";
            byId("machineNoBanner").classList.remove("is-hidden");
        }

        // Load data if params are present
        if (params.bmId && params.mcNo) {
            loadCard(params).catch((error) => {
                if (!context.signal.aborted) showStatus(error.message, "error");
            });
        }
    }

    // LOAD  (mirrors LoadData public + private overloads)

    async function loadCard({
        bmId,
        mcNo,
        userType,
        status,
        colorName,
        headerCase,
    }) {
        const query = new URLSearchParams({
            bmId,
            mcNo,
            userType,
            status,
            colorName,
            headerCase,
        });
        const resp = await context.getJson(
            `/ActionCards?handler=Load&${query}`,
        );

        if (!resp.success) {
            showStatus(resp.message || "No data found.", "error");
            return;
        }

        byId("btnSave").disabled = false;

        const card = resp.data;
        cardSeqno = card.seqno || 0;

        // Populate all form fields
        // Mirrors the row("Field").ToString() assignments in LoadData() private
        setValue("tbProblemContent", card.problemContent);
        setValue("tbInCharge", card.inCharge);
        setValue("tbDate", card.dateCard || formatTimestamp());
        setValue("tbInvestigation", card.investigationDone);
        setValue("tbAction", card.action);
        setValue("cbStatus", card.mcStatus || "-SELECT-");

        // Division comments × 3
        setValue("tbComment", card.comment);
        setValue("tbdate_1", card.date1);
        setValue("tbIncharge_1", card.incharge1);
        setValue("tbComment2", card.comment2);
        setValue("tbdate_2", card.date2);
        setValue("tbIncharge_2", card.incharge2);
        setValue("tbComment3", card.comment3);
        setValue("tbdate_3", card.date3);
        setValue("tbIncharge_3", card.incharge3);

        // PM / TE replies × 3
        setValue("tbReply1", card.pmReply1);
        setValue("tbReplyEmp1", card.pmIncharge1);
        setValue("tbReplyDate1", card.pmDate1);
        setValue("tbReply2", card.pmReply2);
        setValue("tbReplyEmp2", card.pmIncharge2);
        setValue("tbReplyDate2", card.pmDate2);
        setValue("tbReply3", card.pmReply3);
        setValue("tbReplyEmp3", card.pmIncharge3);
        setValue("tbReplyDate3", card.pmDate3);

        // If new card, set DateCard = now
        if (status !== "Existing") {
            setValue("tbDate", formatTimestamp());
        }
    }

    // AUTO-DATE ON BLUR
    // Mirrors tbComment_Leave / tbComment2_Leave / tbComment3_Leave

    function updateDate(field, dateFieldId) {
        const dateField = byId(dateFieldId);
        if (!dateField) return;
        dateField.value = field.value.trim() ? formatTimestamp() : "";
    }

    // SAVE  (mirrors btnSave_Click → SaveData / UpdateData)

    async function saveCard() {
        if (byId("btnSave").disabled) return;
        const status = byId("cbStatus").value;
        const statusId = byId("hfStatusId").value;
        const userType = byId("hfUserType").value;

        // Mirrors: If cbStatus.Text = "-SELECT-" → MsgBox
        if (!status || status === "-SELECT-") {
            showStatus("Please select a MC Status.", "error");
            return;
        }

        const card = {
            seqno: cardSeqno,
            bmId: byId("hfBmId").value,
            machineNo: byId("hfMcNo").value,
            userType,
            status: statusId,
            colorName: byId("hfColorName").value,
            dateCard: byId("tbDate").value,
            problemContent: byId("tbProblemContent").value,
            inCharge: byId("tbInCharge").value,
            investigationDone: byId("tbInvestigation").value,
            action: byId("tbAction").value,
            mcStatus: status,

            comment: byId("tbComment").value,
            date1: byId("tbdate_1").value,
            incharge1: byId("tbIncharge_1").value,
            comment2: byId("tbComment2").value,
            date2: byId("tbdate_2").value,
            incharge2: byId("tbIncharge_2").value,
            comment3: byId("tbComment3").value,
            date3: byId("tbdate_3").value,
            incharge3: byId("tbIncharge_3").value,

            pmReply1: byId("tbReply1").value,
            pmIncharge1: byId("tbReplyEmp1").value,
            pmDate1: byId("tbReplyDate1").value,
            pmReply2: byId("tbReply2").value,
            pmIncharge2: byId("tbReplyEmp2").value,
            pmDate2: byId("tbReplyDate2").value,
            pmReply3: byId("tbReply3").value,
            pmIncharge3: byId("tbReplyEmp3").value,
            pmDate3: byId("tbReplyDate3").value,
        };

        byId("btnSave").disabled = true;
        try {
            const resp = await context.postJson("/ActionCards?handler=Save", {
                card,
                statusId,
            });
            if (!resp.success) {
                showStatus(resp.message, "error");
                return;
            }
            showStatus(resp.message, "success");
            Workflows.close();
            window.refreshMonitoring();
        } catch (error) {
            if (!context.signal.aborted) showStatus(error.message, "error");
        } finally {
            byId("btnSave").disabled = false;
        }
    }

    // STATUS BAR

    function showStatus(msg, type = "info") {
        const el = byId("saveStatus");
        el.textContent = msg;
        el.className = `save-status status-${type}`;
    }

    // HELPERS

    function formatTimestamp() {
        // Retain the legacy 12-hour timestamp format used by the Action Card service.
        const d = new Date();
        const mm = String(d.getMonth() + 1).padStart(2, "0");
        const dd = String(d.getDate()).padStart(2, "0");
        const yyyy = d.getFullYear();
        const hh = String(d.getHours() % 12 || 12).padStart(2, "0");
        const min = String(d.getMinutes()).padStart(2, "0");
        const ss = String(d.getSeconds()).padStart(2, "0");
        return `${mm}/${dd}/${yyyy} ${hh}:${min}:${ss}`;
    }

    function setValue(id, val) {
        const el = byId(id);
        if (!el) return;
        if (el.tagName === "SELECT") {
            el.value = val;
            if (el.value !== val) el.selectedIndex = 0; // fallback
        } else {
            el.value = val ?? "";
        }
    }

    root.querySelector("form").addEventListener("submit", (event) => {
        event.preventDefault();
        saveCard();
    });
    root.querySelectorAll("[data-comment-idx]").forEach((field) => {
        field.addEventListener("blur", () =>
            updateDate(field, `tbdate_${Number(field.dataset.commentIdx)}`),
        );
    });
    root.querySelectorAll("[data-reply-idx]").forEach((field) => {
        field.addEventListener("blur", () =>
            updateDate(field, `tbReplyDate${Number(field.dataset.replyIdx)}`),
        );
    });
    initializeActionCard({
        userType: "PM",
        status: "New",
        colorName: "",
        headerCase: "",
        ...options,
    });
});
