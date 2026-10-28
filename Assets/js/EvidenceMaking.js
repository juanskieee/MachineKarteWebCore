/**
 * EvidenceMaking.js
 * Converted from Form4.vb event handlers — E-Karte System
 *
 * Replaces:
 *   EMakingPrint()    → generateEvidence() + POST /EvidenceMaking?handler=Generate
 *   WebBrowser ctrl   → inline <iframe> preview
 *   Process.Start     → <a download> link + iframe src
 *   MsgBox            → showStatus()
 */

let antiForgery = '';

document.addEventListener('DOMContentLoaded', () => {
    antiForgery = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // Pre-fill from query string (when opened from Monitoring card click)
    const params = new URLSearchParams(location.search);
    const proc = params.get('process');
    const bmId = params.get('bmId');

    if (proc) document.getElementById('selProcess').value = proc;
    if (bmId) document.getElementById('txtBmId').value = bmId;

    // Auto-generate if both params are supplied (mirrors Form4 being called directly)
    if (proc && bmId) generateEvidence();
});

// ── Generate Evidence (mirrors EMakingPrint) ──────────────────────
async function generateEvidence() {
    const process = document.getElementById('selProcess').value;
    const bmId = parseInt(document.getElementById('txtBmId').value || '0');

    if (!process) { showStatus('Please select a process.', 'error'); return; }
    if (!bmId) { showStatus('Please enter a valid BM ID.', 'error'); return; }

    showSpinner(true);
    closePreview();

    const resp = await post('/EvidenceMaking?handler=Generate', { process, bmId });

    showSpinner(false);

    if (!resp || !resp.success) {
        showStatus(resp?.message || 'Generation failed.', 'error');
        return;
    }

    const result = resp.data;
    showStatus(`Evidence generated: ${result.fileName}`, 'success');

    // Show preview panel (mirrors WebBrowser control / Adobe Reader launch)
    openPreview(result.downloadUrl, result.fileName);
}

// ── Preview panel ─────────────────────────────────────────────────
function openPreview(url, fileName) {
    document.getElementById('previewFileName').textContent = fileName;
    document.getElementById('downloadLink').href = url;
    document.getElementById('downloadLink').setAttribute('download', fileName);

    const frame = document.getElementById('previewFrame');
    // PDF files render natively in modern browsers via iframe
    // xlsx files: prompt download instead of iframe preview
    if (fileName.endsWith('.pdf')) {
        frame.src = url;
    } else {
        // For xlsx, just hide the iframe and let the download link do the work
        frame.src = '';
        frame.style.display = 'none';
    }

    document.getElementById('previewPanel').style.display = 'block';
}

function closePreview() {
    document.getElementById('previewPanel').style.display = 'none';
    document.getElementById('previewFrame').src = '';
    document.getElementById('previewFrame').style.display = '';
}

// ── Status / spinner ──────────────────────────────────────────────
function showSpinner(show) {
    const area = document.getElementById('statusArea');
    const spinner = document.getElementById('statusSpinner');
    area.style.display = 'block';
    spinner.style.display = show ? 'flex' : 'none';
    if (show) document.getElementById('statusMsg').textContent = '';
}

function showStatus(msg, type = 'info') {
    const area = document.getElementById('statusArea');
    const el = document.getElementById('statusMsg');
    area.style.display = 'block';
    el.textContent = msg;
    el.className = `status-msg status-${type}`;
}

// ── HTTP helpers ──────────────────────────────────────────────────
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
        console.error('POST error:', e);
        return { success: false, message: String(e) };
    }
}