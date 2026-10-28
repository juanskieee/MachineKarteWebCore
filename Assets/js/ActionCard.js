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
 *   Me.Close()                                       → window.close() / redirect
 *   Form1.LoadMCName(...)                            → postMessage to opener (Monitoring)
 */

let antiForgery = '';

document.addEventListener('DOMContentLoaded', () => {
    antiForgery = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // Read load params from query string
    // Mirrors Form_Input being opened by Form1 with LoadData() args
    const p = new URLSearchParams(location.search);
    const bmId = p.get('bmId') ?? '';
    const mcNo = p.get('mcNo') ?? '';
    const userType = p.get('userType') ?? 'PM';
    const status = p.get('status') ?? 'New';
    const colorName = p.get('colorName') ?? '';
    const headerCase = p.get('headerCase') ?? '';

    // Store in hidden fields (mirrors VB module-level StatusID)
    hf('hfBmId', bmId);
    hf('hfMcNo', mcNo);
    hf('hfUserType', userType);
    hf('hfStatusId', status);
    hf('hfColorName', colorName);

    // Update reply label: "PM Reply" vs "TE Reply"
    // Mirrors: If Form1.cbUser.Text = "TE" Then Label18.Text = "TE Reply"
    const replyText = userType === 'TE' ? 'TE Reply' : 'PM Reply';
    document.getElementById('replyLabel').textContent = replyText;
    for (let i = 1; i <= 3; i++) {
        const cell = document.getElementById(`replyLabelCell${i}`);
        if (cell) cell.textContent = `${replyText} ${i} :`;
    }

    // Show machine banner
    if (mcNo) {
        document.getElementById('bannerMcNo').textContent = mcNo.toUpperCase();
        document.getElementById('bannerHeaderCase').textContent = headerCase
            ? ` — ${headerCase}` : '';
        document.getElementById('machineNoBanner').style.display = 'flex';
        document.title = `${mcNo.toUpperCase()} — Check And Action Card`;
    }

    // Load data if params are present
    if (bmId && mcNo) loadCard(bmId, mcNo, userType, status, colorName, headerCase);
});

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
    const dateField = document.getElementById(`tbdate_${idx}`);
    if (!dateField) return;
    dateField.value = el.value.trim() !== '' ? nowStr() : '';
}

// Mirrors tbReply1_Leave / tbReply2_Leave / tbReply3_Leave
function onReplyBlur(el, idx) {
    const dateField = document.getElementById(`tbReplyDate${idx}`);
    if (!dateField) return;
    dateField.value = el.value.trim() !== '' ? nowStr() : '';
}

// ════════════════════════════════════════════════════════
// SAVE  (mirrors btnSave_Click → SaveData / UpdateData)
// ════════════════════════════════════════════════════════

async function saveCard() {
    const status = document.getElementById('cbStatus').value;
    const statusId = document.getElementById('hfStatusId').value;
    const userType = document.getElementById('hfUserType').value;

    // Mirrors: If cbStatus.Text = "-SELECT-" → MsgBox
    if (!status || status === '-SELECT-') {
        showStatus('Please select a MC Status.', 'error');
        return;
    }

    const card = {
        bmId: document.getElementById('hfBmId').value,
        machineNo: document.getElementById('hfMcNo').value,
        userType: userType,
        status: statusId,
        colorName: document.getElementById('hfColorName').value,
        dateCard: document.getElementById('tbDate').value,
        problemContent: document.getElementById('tbProblemContent').value,
        inCharge: document.getElementById('tbInCharge').value,
        investigationDone: document.getElementById('tbInvestigation').value,
        action: document.getElementById('tbAction').value,
        mcStatus: status,

        comment: document.getElementById('tbComment').value,
        date1: document.getElementById('tbdate_1').value,
        incharge1: document.getElementById('tbIncharge_1').value,
        comment2: document.getElementById('tbComment2').value,
        date2: document.getElementById('tbdate_2').value,
        incharge2: document.getElementById('tbIncharge_2').value,
        comment3: document.getElementById('tbComment3').value,
        date3: document.getElementById('tbdate_3').value,
        incharge3: document.getElementById('tbIncharge_3').value,

        pmReply1: document.getElementById('tbReply1').value,
        pmIncharge1: document.getElementById('tbReplyEmp1').value,
        pmDate1: document.getElementById('tbReplyDate1').value,
        pmReply2: document.getElementById('tbReply2').value,
        pmIncharge2: document.getElementById('tbReplyEmp2').value,
        pmDate2: document.getElementById('tbReplyDate2').value,
        pmReply3: document.getElementById('tbReply3').value,
        pmIncharge3: document.getElementById('tbReplyEmp3').value,
        pmDate3: document.getElementById('tbReplyDate3').value,
    };

    document.getElementById('btnSave').disabled = true;
    const resp = await post('/ActionCards?handler=Save', { card, statusId });
    document.getElementById('btnSave').disabled = false;

    if (!resp.success) {
        showStatus(resp.message, 'error');
        return;
    }

    showStatus(resp.message, 'success');

    // Mirrors: Form1.LoadMCName(...) — notify Monitoring page to refresh
    // Works whether opened in the same tab (via query param) or as a popup
    if (window.opener && !window.opener.closed) {
        window.opener.postMessage({ type: 'actionCardSaved' }, location.origin);
        setTimeout(() => window.close(), 1200);
    } else {
        // Opened in same tab (mobile / single-window mode) → go back after delay
        setTimeout(() => history.back(), 1400);
    }
}

// ════════════════════════════════════════════════════════
// STATUS BAR
// ════════════════════════════════════════════════════════

function showStatus(msg, type = 'info') {
    const el = document.getElementById('saveStatus');
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
    const el = document.getElementById(id);
    if (el) el.value = val;
}

function setVal(id, val) {
    const el = document.getElementById(id);
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