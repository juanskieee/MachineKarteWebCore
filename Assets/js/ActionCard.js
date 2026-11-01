Workflows.register('ActionCards', (root, options, context) => {
    const byId = id => root.querySelector(`[data-id="${id}"]`);
    const antiForgery = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const fetch = context.fetch;
/**
 * ActionCards.js
 * Converted from Form_Input.vb event handlers — E-Karte System
 *
 * Replaces:
 *   LoadData(bmid, status, mcno, ColorT, HeaderCase) → loadCard()
 *   SaveData() / UpdateData()                        → saveCard()
 *   tbComment_Leave / tbComment2_Leave / tbComment3_Leave
 *                                                    → onCommentBlur()
 *   tbReply1_Leave / tbReply2_Leave / tbReply3_Leave → onReplyBlur()
 *   MsgBox                                           → showStatus()
 *   Me.Close() / Form1.LoadMCName(...)                → close dialog and refresh Monitoring
 */

function initializeActionCard(params) {

    hf('hfBmId', params.bmId);
    hf('hfMcNo', params.mcNo);
    hf('hfUserType', params.userType);
    hf('hfStatusId', params.status);
    hf('hfColorName', params.colorName);

    // Update reply label: "PM Reply" vs "TE Reply"
    // Mirrors: If Form1.cbUser.Text = "TE" Then Label18.Text = "TE Reply"
    const replyText = params.userType === 'TE' ? 'TE Reply' : 'PM Reply';

    for (let i = 1; i <= 3; i++) {
        const cell = byId(`replyLabelCell${i}`);
        if (cell) cell.textContent = `${['Accreditor', 'Section Manager', 'Department Manager'][i - 1]} / ${replyText}`;
    }

    // Show machine banner
    if (params.mcNo) {
        byId('bannerMcNo').textContent = params.mcNo.toUpperCase();
        byId('bannerHeaderCase').textContent = params.headerCase
            ? ` — ${params.headerCase}` : '';
        byId('machineNoBanner').classList.remove('is-hidden');
    }

    // Load data if params are present
    if (params.bmId && params.mcNo) {
        loadCard(params.bmId, params.mcNo, params.userType, params.status, params.colorName, params.headerCase);
    }


}

// ════════════════════════════════════════════════════════
// LOAD  (mirrors LoadData public + private overloads)
// ════════════════════════════════════════════════════════

async function loadCard(bmId, mcNo, userType, status, colorName, headerCase) {
    const url = `/ActionCards?handler=Load` +
        `&bmId=${enc(bmId)}&mcNo=${enc(mcNo)}&userType=${enc(userType)}` +
        `&status=${enc(status)}&colorName=${enc(colorName)}&headerCase=${enc(headerCase)}`;

    const resp = await get(url);

    if (!resp.success) {
        showStatus(resp.message || 'No data found.', 'error');
        return;
    }

    byId('btnSave').disabled = false;

    const c = resp.data;

    // Populate all form fields
    // Mirrors the row("Field").ToString() assignments in LoadData() private
    setVal('tbProblemContent', c.problemContent);
    setVal('tbInCharge', c.inCharge);
    setVal('tbDate', c.dateCard || nowStr());
    setVal('tbInvestigation', c.investigationDone);
    setVal('tbAction', c.action);
    setVal('cbStatus', c.mcStatus || '-SELECT-');

    // Division comments × 3
    setVal('tbComment', c.comment);
    setVal('tbdate_1', c.date1);
    setVal('tbIncharge_1', c.incharge1);
    setVal('tbComment2', c.comment2);
    setVal('tbdate_2', c.date2);
    setVal('tbIncharge_2', c.incharge2);
    setVal('tbComment3', c.comment3);
    setVal('tbdate_3', c.date3);
    setVal('tbIncharge_3', c.incharge3);

    // PM / TE replies × 3
    setVal('tbReply1', c.pmReply1);
    setVal('tbReplyEmp1', c.pmIncharge1);
    setVal('tbReplyDate1', c.pmDate1);
    setVal('tbReply2', c.pmReply2);
    setVal('tbReplyEmp2', c.pmIncharge2);
    setVal('tbReplyDate2', c.pmDate2);
    setVal('tbReply3', c.pmReply3);
    setVal('tbReplyEmp3', c.pmIncharge3);
    setVal('tbReplyDate3', c.pmDate3);

    // If new card, set DateCard = now
    if (status !== 'Existing') {
        setVal('tbDate', nowStr());
    }
}

// ════════════════════════════════════════════════════════
// AUTO-DATE ON BLUR
// Mirrors tbComment_Leave / tbComment2_Leave / tbComment3_Leave
// ════════════════════════════════════════════════════════

function onCommentBlur(el, idx) {
    // tbdate_1 / tbdate_2 / tbdate_3
    const dateField = byId(`tbdate_${idx}`);
    if (!dateField) return;
    dateField.value = el.value.trim() !== '' ? nowStr() : '';
}

// Mirrors tbReply1_Leave / tbReply2_Leave / tbReply3_Leave
function onReplyBlur(el, idx) {
    const dateField = byId(`tbReplyDate${idx}`);
    if (!dateField) return;
    dateField.value = el.value.trim() !== '' ? nowStr() : '';
}

// ════════════════════════════════════════════════════════
// SAVE  (mirrors btnSave_Click → SaveData / UpdateData)
// ════════════════════════════════════════════════════════

async function saveCard() {
    const status = byId('cbStatus').value;
    const statusId = byId('hfStatusId').value;
    const userType = byId('hfUserType').value;

    // Mirrors: If cbStatus.Text = "-SELECT-" → MsgBox
    if (!status || status === '-SELECT-') {
        showStatus('Please select a MC Status.', 'error');
        return;
    }

    const card = {
        bmId: byId('hfBmId').value,
        machineNo: byId('hfMcNo').value,
        userType: userType,
        status: statusId,
        colorName: byId('hfColorName').value,
        dateCard: byId('tbDate').value,
        problemContent: byId('tbProblemContent').value,
        inCharge: byId('tbInCharge').value,
        investigationDone: byId('tbInvestigation').value,
        action: byId('tbAction').value,
        mcStatus: status,

        comment: byId('tbComment').value,
        date1: byId('tbdate_1').value,
        incharge1: byId('tbIncharge_1').value,
        comment2: byId('tbComment2').value,
        date2: byId('tbdate_2').value,
        incharge2: byId('tbIncharge_2').value,
        comment3: byId('tbComment3').value,
        date3: byId('tbdate_3').value,
        incharge3: byId('tbIncharge_3').value,

        pmReply1: byId('tbReply1').value,
        pmIncharge1: byId('tbReplyEmp1').value,
        pmDate1: byId('tbReplyDate1').value,
        pmReply2: byId('tbReply2').value,
        pmIncharge2: byId('tbReplyEmp2').value,
        pmDate2: byId('tbReplyDate2').value,
        pmReply3: byId('tbReply3').value,
        pmIncharge3: byId('tbReplyEmp3').value,
        pmDate3: byId('tbReplyDate3').value,
    };

    byId('btnSave').disabled = true;
    const resp = await post('/ActionCards?handler=Save', { card, statusId });
    byId('btnSave').disabled = false;

    if (!resp.success) {
        showStatus(resp.message, 'error');
        return;
    }

    showStatus(resp.message, 'success');

    Workflows.close();
    window.refreshMonitoring();
}

// ════════════════════════════════════════════════════════
// STATUS BAR
// ════════════════════════════════════════════════════════

function showStatus(msg, type = 'info') {
    const el = byId('saveStatus');
    el.textContent = msg;
    el.className = `save-status status-${type}`;
}

// ════════════════════════════════════════════════════════
// HELPERS
// ════════════════════════════════════════════════════════

function nowStr() {
    const d = new Date();
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const dd = String(d.getDate()).padStart(2, '0');
    const yyyy = d.getFullYear();
    const hh = String(d.getHours() % 12 || 12).padStart(2, '0');
    const min = String(d.getMinutes()).padStart(2, '0');
    const ss = String(d.getSeconds()).padStart(2, '0');
    return `${mm}/${dd}/${yyyy} ${hh}:${min}:${ss}`;
}

function hf(id, val) {
    const el = byId(id);
    if (el) el.value = val;
}

function setVal(id, val) {
    const el = byId(id);
    if (!el) return;
    if (el.tagName === 'SELECT') {
        el.value = val;
        if (el.value !== val) el.selectedIndex = 0;  // fallback
    } else {
        el.value = val ?? '';
    }
}

function enc(s) { return encodeURIComponent(s ?? ''); }

async function get(url) {
    try {
        const r = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        return await r.json();
    } catch (e) {
        return { success: false, message: String(e) };
    }
}

async function post(url, body) {
    try {
        const r = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest',
                'RequestVerificationToken': antiForgery
            },
            body: JSON.stringify(body)
        });
        return await r.json();
    } catch (e) {
        return { success: false, message: String(e) };
    }
}

    root.querySelector('form').addEventListener('submit', event => { event.preventDefault(); saveCard(); });
    root.querySelectorAll('[data-comment-idx]').forEach(field => field.addEventListener('blur', () => onCommentBlur(field, Number(field.dataset.commentIdx))));
    root.querySelectorAll('[data-reply-idx]').forEach(field => field.addEventListener('blur', () => onReplyBlur(field, Number(field.dataset.replyIdx))));
    initializeActionCard({ userType: 'PM', status: 'New', colorName: '', headerCase: '', ...options });
});
