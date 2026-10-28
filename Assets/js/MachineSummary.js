/**
 * MachineSummary.js
 * Form11 → MachineSummary.cshtml  (E-Karte System)
 *
 * Responsibilities
 *  • POST search parameters → OnGetSummaryAsync
 *  • Render the process × location count grid
 *  • Open a flyout panel showing the machine list for a clicked cell
 *  • "View in Monitoring" button navigates to Monitoring with pre-filled filters
 *  • Auto-refresh every 60 seconds (mirrors tmrRefresh_Tick on Form1 tick)
 */
(function () {
    'use strict';

    const BASE = window.__ms;
    let _state = null;          // last loaded MachineSummaryResult
    let _refreshTimer = null;
    const REFRESH_MS = 60_000;

    // ── DOM references ────────────────────────────────────────────────────────
    const $ = id => document.getElementById(id);
    const elSearch = $('msSearch');
    const elFrom = $('msFrom');
    const elTo = $('msTo');
    const elMode = $('msMode');
    const elStatus = $('msStatus');
    const elProgress = $('msProgress');
    const elHead = $('msGridHead');
    const elBody = $('msGridBody');
    const elFoot = $('msGridFoot');
    const elPanel = $('msPanel');
    const elOverlay = $('msOverlay');
    const elTitle = $('msPanelTitle');
    const elList = $('msPanelList');
    const elGo = $('msPanelGo');
    const elClose = $('msPanelClose');

    // ── Init ──────────────────────────────────────────────────────────────────
    elSearch.addEventListener('click', () => search());
    elClose.addEventListener('click', closePanel);
    elOverlay.addEventListener('click', closePanel);
    elGo.addEventListener('click', goToMonitoring);

    // Start a periodic refresh (mirrors tmrRefresh_Tick)
    function startRefreshTimer() {
        clearTimeout(_refreshTimer);
        _refreshTimer = setTimeout(() => { search(true); }, REFRESH_MS);
    }

    // Kick off the first load immediately
    search();

    // ── Search ────────────────────────────────────────────────────────────────
    async function search(silent = false) {
        if (!silent) setStatus('Loading…');
        showProgress(true);

        const params = new URLSearchParams({
            dateFrom: elFrom.value,
            dateTo: elTo.value,
            userMode: elMode.value
        });

        try {
            const res = await fetch(`${BASE.summaryUrl}&${params}`, { cache: 'no-store' });
            const json = await res.json();

            if (!json.success) throw new Error(json.message || 'Server error');

            _state = json.data;
            renderGrid(_state);
            setStatus(`Updated: ${new Date().toLocaleTimeString()}`);
        } catch (err) {
            setStatus(`Error: ${err.message}`, true);
        } finally {
            showProgress(false);
            startRefreshTimer();
        }
    }

    // ── Grid render ───────────────────────────────────────────────────────────
    function renderGrid(data) {
        const cols = data.columns;   // e.g. ["LSI1-1F", "LSI1-2F", ...]

        // ─ Header row ─
        let hTr = '<tr>';
        hTr += '<th class="ms-th-process">PROCESS</th>';
        for (const col of cols) {
            hTr += `<th class="ms-th-loc">${col}</th>`;
        }
        hTr += '<th class="ms-th-total">TOTAL</th></tr>';
        elHead.innerHTML = hTr;

        // ─ Data rows ─
        let bodyHtml = '';
        for (const row of data.rows) {
            bodyHtml += `<tr data-process="${esc(row.process)}">`;
            bodyHtml += `<td class="ms-td-process">${esc(row.process)}</td>`;

            for (const col of cols) {
                const cell = row.cells[col];
                if (!cell || cell.notApplicable) {
                    bodyHtml += '<td class="ms-cell ms-na">—</td>';
                } else if (cell.count === 0) {
                    bodyHtml += '<td class="ms-cell ms-zero">0</td>';
                } else {
                    const machinesJson = esc(JSON.stringify(cell.machines));
                    bodyHtml += `<td class="ms-cell ms-active" tabindex="0"
                        data-process="${esc(row.process)}"
                        data-location="${esc(col)}"
                        data-machines="${machinesJson}"
                        role="button"
                        aria-label="${cell.count} machines at ${row.process} ${col}"
                        >${cell.count}</td>`;
                }
            }

            // Row total
            const rowTotalClass = row.rowTotal > 0 ? 'ms-td-rowtotal ms-row-nonzero' : 'ms-td-rowtotal';
            bodyHtml += `<td class="${rowTotalClass}">${row.rowTotal}</td>`;
            bodyHtml += '</tr>';
        }
        elBody.innerHTML = bodyHtml;

        // ─ Footer (column totals) ─
        let fTr = '<tr>';
        fTr += '<td class="ms-tf-label">TOTAL</td>';
        for (const col of cols) {
            const t = data.locationTotals[col] ?? 0;
            const cls = t > 0 ? 'ms-tf-cell ms-tf-nonzero' : 'ms-tf-cell';
            fTr += `<td class="${cls}">${t}</td>`;
        }
        const grandCls = data.grandTotal > 0 ? 'ms-tf-grand ms-tf-nonzero' : 'ms-tf-grand';
        fTr += `<td class="${grandCls}">${data.grandTotal}</td>`;
        fTr += '</tr>';
        elFoot.innerHTML = fTr;

        // Attach cell click listeners
        elBody.querySelectorAll('.ms-active').forEach(td => {
            td.addEventListener('click', onCellClick);
            td.addEventListener('keydown', e => { if (e.key === 'Enter' || e.key === ' ') onCellClick.call(td, e); });
        });
    }

    // ── Cell click ────────────────────────────────────────────────────────────
    function onCellClick(e) {
        const td = e.currentTarget ?? this;
        const process = td.dataset.process;
        const location = td.dataset.location;
        const machines = JSON.parse(td.dataset.machines || '[]');
        openPanel(process, location, machines);
    }

    function openPanel(process, location, machines) {
        elTitle.textContent = `${process}  ( ${location} )`;

        // Build machine list
        elList.innerHTML = '';
        machines.forEach(m => {
            const li = document.createElement('li');
            li.className = 'ms-panel-item';
            li.textContent = m;
            li.dataset.machine = m;
            li.dataset.process = process;
            li.dataset.location = location;
            li.addEventListener('click', () => selectMachine(li));
            elList.appendChild(li);
        });

        elGo.disabled = true;
        elGo.dataset.machine = '';
        elGo.dataset.process = process;
        elGo.dataset.location = location;

        elPanel.classList.add('ms-panel-open');
        elPanel.setAttribute('aria-hidden', 'false');
        elOverlay.classList.add('ms-overlay-show');
        elOverlay.setAttribute('aria-hidden', 'false');
    }

    function closePanel() {
        elPanel.classList.remove('ms-panel-open');
        elPanel.setAttribute('aria-hidden', 'true');
        elOverlay.classList.remove('ms-overlay-show');
        elOverlay.setAttribute('aria-hidden', 'true');
    }

    function selectMachine(li) {
        elList.querySelectorAll('.ms-panel-item').forEach(x => x.classList.remove('ms-selected'));
        li.classList.add('ms-selected');
        elGo.disabled = false;
        elGo.dataset.machine = li.dataset.machine;
    }

    // ── Navigate to Monitoring ────────────────────────────────────────────────
    // Mirrors Button2_Click: sets Form1.cbMachine + cbProcess + cbFloor + search.
    // In the web version we pass them as query-string parameters.
    function goToMonitoring() {
        const machine = elGo.dataset.machine;
        const process = elGo.dataset.process;
        const location = elGo.dataset.location;
        if (!machine) return;

        const url = `${BASE.monitoringUrl}?process=${encodeURIComponent(process)}&floor=${encodeURIComponent(location)}&machine=${encodeURIComponent(machine)}`;
        window.open(url, '_blank');
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    function setStatus(msg, isError = false) {
        elStatus.textContent = msg;
        elStatus.style.color = isError ? '#f44' : '#adf';
    }

    function showProgress(on) {
        elProgress.style.display = on ? 'block' : 'none';
    }

    function esc(str) {
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/"/g, '&quot;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

})();