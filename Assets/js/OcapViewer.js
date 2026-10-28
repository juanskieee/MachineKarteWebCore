// Assets/js/OcapViewer.js
'use strict';

document.addEventListener('DOMContentLoaded', () => {
    const bmId = document.getElementById('hdnBmId')?.value ?? '';
    const bmProcess = document.getElementById('hdnBmProcess')?.value ?? '';
    const bmCaseId = document.getElementById('hdnBmCaseId')?.value ?? '';

    if (bmId && bmProcess && bmCaseId) {
        loadOcap(bmId, bmProcess, bmCaseId);
    }
});

// Public API — parent page (e.g. Monitoring) can call this directly
window.OcapViewer = {
    load: loadOcap
};

async function loadOcap(bmId, bmProcess, bmCaseId) {
    setLoading(true);
    clearError();

    const params = new URLSearchParams({ bmId, bmProcess, bmCaseId });
    const url = `/OcapViewer?handler=LoadOcap&${params}`;

    try {
        const resp = await fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

        const json = await resp.json();
        if (!json.success) throw new Error(json.message ?? 'Server error');

        renderTable(json.data.rows);
    } catch (err) {
        showError(err.message);
    } finally {
        setLoading(false);
    }
}

function renderTable(rows) {
    const tbody = document.getElementById('ocap-tbody');
    tbody.innerHTML = '';

    if (!rows || rows.length === 0) {
        tbody.innerHTML =
            '<tr><td colspan="5" class="ocap-empty">No OCAP data found.</td></tr>';
        return;
    }

    rows.forEach((r, idx) => {
        const tr = document.createElement('tr');
        tr.className = idx % 2 === 0 ? 'ocap-row-even' : 'ocap-row-odd';
        tr.innerHTML = `
            <td>${escHtml(r.checkItem)}</td>
            <td>${escHtml(r.checkingMethod)}</td>
            <td>${escHtml(r.treatmentAction)}</td>
            <td class="ocap-centre">${escHtml(r.judgement)}</td>
            <td>${escHtml(r.remarks)}</td>`;
        tbody.appendChild(tr);
    });
}

// ── UI helpers ────────────────────────────────────────────────────────────────
function setLoading(on) {
    const el = document.getElementById('ocap-loading');
    if (el) el.style.display = on ? 'flex' : 'none';
}

function showError(msg) {
    const el = document.getElementById('ocap-error');
    if (el) { el.textContent = msg; el.style.display = 'block'; }
}

function clearError() {
    const el = document.getElementById('ocap-error');
    if (el) { el.textContent = ''; el.style.display = 'none'; }
}

function escHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}