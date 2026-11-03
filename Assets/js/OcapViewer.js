/** OCAP (Form6): load and display read-only checking results for one BM record. */
Workflows.register('OcapViewer', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;
    const bmId = byId('hdnBmId')?.value ?? '';
    const bmProcess = byId('hdnBmProcess')?.value ?? '';
    const bmCaseId = byId('hdnBmCaseId')?.value ?? '';
    byId('recordBmId').textContent = bmId || '—';
    byId('recordMachine').textContent = [options.machineNo, options.machineType].filter(Boolean).join(' / ') || '—';
    byId('recordLocation').textContent = options.location || '—';
    byId('recordRequested').textContent = options.timeOfRequest || '—';
    byId('recordCase').textContent = options.bmCase || bmCaseId || '—';

    if (bmId && bmProcess && bmCaseId) {
        loadOcap(bmId, bmProcess, bmCaseId);
    }


    // Load the current dialog's selected OCAP record.

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
        const tbody = byId('ocap-tbody');
        tbody.innerHTML = '';

        if (!rows || rows.length === 0) {
            tbody.innerHTML =
                '<tr><td colspan="5" class="ocap-empty">No OCAP data found.</td></tr>';
            return;
        }

        // Build rows off-DOM to avoid repeated table layout during larger OCAP loads.
        const fragment = document.createDocumentFragment();
        rows.forEach((r, idx) => {
            const tr = document.createElement('tr');
            tr.className = idx % 2 === 0 ? 'ocap-row-even' : 'ocap-row-odd';
            tr.innerHTML = `
            <td>${escHtml(r.checkItem)}</td>
            <td>${escHtml(r.checkingMethod)}</td>
            <td>${escHtml(r.treatmentAction)}</td>
            <td class="ocap-centre">${escHtml(r.judgement)}</td>
            <td>${escHtml(r.remarks)}</td>`;
            fragment.appendChild(tr);
        });
        tbody.replaceChildren(fragment);
    }

    // ── UI helpers ────────────────────────────────────────────────────────────────
    function setLoading(on) {
        const el = byId('ocap-loading');
        if (el) el.classList.toggle('is-hidden', !on);
    }

    function showError(msg) {
        const el = byId('ocap-error');
        if (el) { el.textContent = msg; el.classList.remove('is-hidden'); }
    }

    function clearError() {
        const el = byId('ocap-error');
        if (el) { el.textContent = ''; el.classList.add('is-hidden'); }
    }

    function escHtml(str) {
        return String(str ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }
});
