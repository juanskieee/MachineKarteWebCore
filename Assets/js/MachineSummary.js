/**
 * MachineSummary.js
 * Form11 → MachineSummary.cshtml  (E-Karte System)
 *
 * Responsibilities
 *  • Load the process × location count grid from OnGetSummaryAsync
 *  • Load the machine list for a clicked cell from OnGetMachineListAsync
 *  • "View in Monitoring" navigates to Monitoring with pre-filled filters
 *  • Auto-refresh every 60 seconds (mirrors the Form11 refresh timer)
 */
(function () {
    'use strict';

    const BASE = window.__ms || {};
    const REFRESH_MS = 60_000;

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

    let lastFilter = { dateFrom: '', dateTo: '', userMode: 'EEE' };
    let currentCell = null;
    let loadSeq = 0;
    let cellSeq = 0;
    let refreshTimer = null;

    elSearch.addEventListener('click', () => search(false));
    elMode.addEventListener('change', () => search(false));
    elFrom.addEventListener('keydown', e => { if (e.key === 'Enter') search(false); });
    elTo.addEventListener('keydown', e => { if (e.key === 'Enter') search(false); });
    elClose.addEventListener('click', closePanel);
    elOverlay.addEventListener('click', closePanel);
    elGo.addEventListener('click', goToMonitoring);
    document.addEventListener('keydown', e => {
        if (e.key === 'Escape' && isPanelOpen()) closePanel();
    });

    search(false);

    // ── Grid load ─────────────────────────────────────────────────────────────
    async function search(background) {
        const dateFrom = elFrom.value;
        const dateTo = elTo.value;
        const userMode = elMode.value;

        if (!dateFrom || !dateTo) {
            if (!background) setStatus('Please select a date range.', true);
            return;
        }

        const seq = ++loadSeq;
        lastFilter = { dateFrom, dateTo, userMode };

        if (!background) setStatus('Loading…');
        showProgress(true);
        elSearch.disabled = true;

        try {
            const params = new URLSearchParams({
                handler: 'Summary',
                dateFrom,
                dateTo,
                userMode
            });

            const res = await fetch(withParams(BASE.summaryUrl, params), { cache: 'no-store' });
            const json = await res.json();

            if (seq !== loadSeq) return;
            if (!json.success) {
                setStatus(json.message || 'Server error', true);
                return;
            }

            renderGrid(json.data);
            setStatus(`Updated: ${new Date().toLocaleTimeString()}`);
        } catch (err) {
            if (seq === loadSeq) setStatus(`Error: ${err.message}`, true);
        } finally {
            if (seq === loadSeq) {
                showProgress(false);
                elSearch.disabled = false;
                scheduleRefresh();
            }
        }
    }

    // ── Grid render ───────────────────────────────────────────────────────────
    function renderGrid(data) {
        const columns = data.columns || [];
        const rows = data.rows || [];
        const locationTotals = data.locationTotals || {};

        let header = '<tr><th class="ms-th-process">PROCESS</th>';
        columns.forEach(column => {
            header += `<th class="ms-th-loc">${esc(column)}</th>`;
        });
        header += '<th class="ms-th-total">TOTAL</th></tr>';
        elHead.innerHTML = header;

        let body = '';
        rows.forEach(row => {
            const cells = row.cells || {};
            body += `<tr data-process="${esc(row.process)}">`;
            body += `<td class="ms-td-process">${esc(row.process)}</td>`;

            columns.forEach(column => {
                const cell = cells[column];

                if (!cell || cell.notApplicable) {
                    body += '<td class="ms-cell ms-na">—</td>';
                    return;
                }

                const count = cell.count || 0;
                if (count === 0) {
                    body += '<td class="ms-cell ms-zero">0</td>';
                    return;
                }

                body += `<td class="ms-cell ms-active" tabindex="0"
                    data-process="${esc(row.process)}"
                    data-location="${esc(column)}"
                    role="button"
                    aria-label="${count} machines at ${esc(row.process)} ${esc(column)}"
                    >${count}</td>`;
            });

            const rowTotal = row.rowTotal || 0;
            const totalClass = rowTotal > 0
                ? 'ms-td-rowtotal ms-row-nonzero'
                : 'ms-td-rowtotal';
            body += `<td class="${totalClass}">${rowTotal}</td>`;
            body += '</tr>';
        });
        elBody.innerHTML = body;

        let footer = '<tr><td class="ms-tf-label">TOTAL</td>';
        columns.forEach(column => {
            const total = locationTotals[column] || 0;
            const totalClass = total > 0 ? 'ms-tf-cell ms-tf-nonzero' : 'ms-tf-cell';
            footer += `<td class="${totalClass}">${total}</td>`;
        });

        const grandTotal = data.grandTotal || 0;
        const grandClass = grandTotal > 0 ? 'ms-tf-grand ms-tf-nonzero' : 'ms-tf-grand';
        footer += `<td class="${grandClass}">${grandTotal}</td></tr>`;
        elFoot.innerHTML = footer;

        elBody.querySelectorAll('.ms-active').forEach(td => {
            td.addEventListener('click', onCellClick);
            td.addEventListener('keydown', e => {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    onCellClick.call(td, e);
                }
            });
        });
    }

    // ── Cell click → machine list ─────────────────────────────────────────────
    async function onCellClick(e) {
        const td = e.currentTarget ?? this;
        const process = td.dataset.process;
        const location = td.dataset.location;
        if (!process || !location) return;

        const seq = ++cellSeq;
        currentCell = {
            process,
            location,
            userMode: lastFilter.userMode
        };

        elTitle.textContent = `${process} ( ${location} ) — loading…`;
        elList.innerHTML = '';
        elGo.disabled = true;
        elGo.dataset.machine = '';
        openPanel();

        try {
            const params = new URLSearchParams({
                handler: 'MachineList',
                process,
                location,
                dateFrom: lastFilter.dateFrom,
                dateTo: lastFilter.dateTo,
                userMode: lastFilter.userMode
            });

            const res = await fetch(withParams(BASE.machineListUrl, params), { cache: 'no-store' });
            const json = await res.json();

            if (seq !== cellSeq) return;

            if (!json.success) {
                elTitle.textContent = `${process} ( ${location} )`;
                elList.innerHTML = `<li class="ms-panel-item ms-panel-message">${esc(json.message || 'Unable to load machines.')}</li>`;
                return;
            }

            const data = json.data || {};
            currentCell.process = data.process || process;
            currentCell.location = data.location || location;
            elTitle.textContent = `${currentCell.process} ( ${currentCell.location} )`;

            const machines = data.machines || [];
            if (machines.length === 0) {
                elList.innerHTML = '<li class="ms-panel-item ms-panel-message">No machines.</li>';
                return;
            }

            machines.forEach(machine => {
                const li = document.createElement('li');
                li.className = 'ms-panel-item';
                li.textContent = machine;
                li.dataset.machine = machine;
                li.addEventListener('click', () => selectMachine(li));
                li.addEventListener('dblclick', goToMonitoring);
                elList.appendChild(li);
            });
        } catch (err) {
            if (seq === cellSeq) {
                elTitle.textContent = `${process} ( ${location} )`;
                elList.innerHTML = `<li class="ms-panel-item ms-panel-message">${esc(err.message)}</li>`;
            }
        }
    }

    function openPanel() {
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
        cellSeq++;
        currentCell = null;
    }

    function isPanelOpen() {
        return elPanel.classList.contains('ms-panel-open');
    }

    function selectMachine(li) {
        elList.querySelectorAll('.ms-panel-item').forEach(item => {
            item.classList.remove('ms-selected');
        });

        li.classList.add('ms-selected');
        elGo.disabled = false;
        elGo.dataset.machine = li.dataset.machine;
    }

    // ── Navigate to Monitoring ────────────────────────────────────────────────
    function goToMonitoring() {
        const machine = elGo.dataset.machine;
        if (!machine || !currentCell) return;

        const params = new URLSearchParams({
            machine,
            process: currentCell.process,
            floor: currentCell.location,
            userType: currentCell.userMode === 'TE' ? 'TE' : 'EEE',
            dateFrom: lastFilter.dateFrom,
            dateTo: lastFilter.dateTo
        });

        window.location.href = withParams(BASE.monitoringUrl, params);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    function scheduleRefresh() {
        clearTimeout(refreshTimer);
        refreshTimer = setTimeout(() => search(true), REFRESH_MS);
    }

    function setStatus(message, isError) {
        elStatus.textContent = message || '';
        elStatus.style.color = isError ? '#f44' : '#adf';
    }

    function showProgress(on) {
        elProgress.style.display = on ? 'block' : 'none';
    }

    function withParams(url, params) {
        const value = url || window.location.pathname;
        return value + (value.includes('?') ? '&' : '?') + params.toString();
    }

    function esc(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }
}());
