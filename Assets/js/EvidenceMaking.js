Workflows.register('EvidenceMaking', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;
    const setInterval = context.setInterval;
    const setTimeout = context.setTimeout;
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


    antiForgery = root.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // Pre-fill from query string (when opened from Monitoring card click)
    const params = new URLSearchParams(options);
    const proc = params.get('process');
    const bmId = params.get('bmId');

    byId('recordProcess').textContent = proc || '—';
    byId('recordBmId').textContent = bmId || '—';


// ── Generate Evidence (mirrors EMakingPrint) ──────────────────────
async function generateEvidence() {
    const process = proc || '';
    const bmId = Number(options.bmId);

    if (!process || !Number.isSafeInteger(bmId) || bmId <= 0) {
        showStatus('Select a BM record in Monitoring first.', 'error'); return;
    }

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
    byId('previewFileName').textContent = fileName;
    byId('downloadLink').href = url;
    byId('downloadLink').setAttribute('download', fileName);

    const frame = byId('previewFrame');
    // PDF files render natively in modern browsers via iframe
    // xlsx files: prompt download instead of iframe preview
    if (fileName.endsWith('.pdf')) {
        frame.src = url;
        frame.classList.remove('is-hidden');
    } else {
        // For xlsx, just hide the iframe and let the download link do the work
        frame.src = '';
        frame.classList.add('is-hidden');
    }

    byId('previewPanel').classList.remove('is-hidden');
}

function closePreview() {
    byId('previewPanel').classList.add('is-hidden');
    byId('previewFrame').src = '';
    byId('previewFrame').classList.remove('is-hidden');
}

// ── Status / spinner ──────────────────────────────────────────────
function showSpinner(show) {
    byId('btnGenerate').disabled = show;
    const area = byId('statusArea');
    const spinner = byId('statusSpinner');
    area.classList.remove('is-hidden');
    spinner.classList.toggle('is-hidden', !show);
    if (show) byId('statusMsg').textContent = '';
}

function showStatus(msg, type = 'info') {
    const area = byId('statusArea');
    const el = byId('statusMsg');
    area.classList.remove('is-hidden');
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
root.querySelector('[data-action="generate"]')?.addEventListener('click', generateEvidence);
root.querySelector('[data-action="close-preview"]')?.addEventListener('click', closePreview);

});
