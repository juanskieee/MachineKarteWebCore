/**
 * McdMonitoring.js
 * Handles the MCD Monitoring dashboard (Form7 → McdMonitoring.cshtml).
 *
 * The grid is fully server-driven: the Dashboard handler returns the columns,
 * the process rows, each cell's count/key and all row/column/grand totals.
 * This file only renders that response and wires the machine-list panel.
 *
 * Conventions:
 *   - fetch() AJAX only
 *   - AjaxResult<T> envelope: { success, message, data }
 *   - Read-only page, so no anti-forgery POST is needed
 */

(function () {
    'use strict';

    // ── State ────────────────────────────────────────────────────────────────
    let lastFilter = { dateFrom: '', dateTo: '', userRole: '' };
    let currentCell = null;
    let loadSeq = 0;
    let cellSeq = 0;

    // ── DOM refs ─────────────────────────────────────────────────────────────
    const dtFrom = () => document.getElementById('dtFrom');
    const dtTo = () => document.getElementById('dtTo');
    const selUser = () => document.getElementById('selUser');
    const btnSearch = () => document.getElementById('btnSearch');
    const gridContainer = () => document.getElementById('gridContainer');
    const lblStatus = () => document.getElementById('lblStatus');
    const progressBar = () => document.getElementById('progressBar');
    const progressInner = () => document.getElementById('progressInner');
    const machinePanel = () => document.getElementById('machinePanel');
    const panelOverlay = () => document.getElementById('panelOverlay');
    const machineList = () => document.getElementById('machineList');
    const panelTitle = () => document.getElementById('panelTitle');

    // ── Init ─────────────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', () => {
        const now = new Date();
        const past = new Date(now);
        past.setDate(past.getDate() - 30);
        dtFrom().value = toLocalIso(past);
        dtTo().value = toLocalIso(now);

        btnSearch().addEventListener('click', () => loadDashboard(false));
        selUser().addEventListener('change', () => loadDashboard(false));

        document.getElementById('btnPanelClose').addEventListener('click', closePanel);
        document.getElementById('btnPanelCancel').addEventListener('click', closePanel);
        document.getElementById('btnGoToMonitoring').addEventListener('click', goToMonitoring);
        panelOverlay().addEventListener('click', closePanel);

        document.addEventListener('keydown', e => {
            if (e.key === 'Escape' && machinePanel().style.display !== 'none') closePanel();
            if (e.key === 'Enter' && document.activeElement === dtFrom()) loadDashboard();
            if (e.key === 'Enter' && document.activeElement === dtTo()) loadDashboard();
        });

        loadDashboard();

        setInterval(() => {
            if (lastFilter.dateFrom && lastFilter.dateTo) loadDashboard(true);
        }, 60000);
    });

    // ── Load dashboard data ──────────────────────────────────────────────────
    async function loadDashboard(background) {
        const from = dtFrom().value;
        const to = dtTo().value;
        const userRole = selUser().value;

        if (!from || !to) {
            if (!background) setStatus('Please select a date range.', true);
            return;
        }

        const seq = ++loadSeq;
        lastFilter = { dateFrom: from, dateTo: to, userRole };

        showProgress(true);
        btnSearch().disabled = true;

        try {
            const params = new URLSearchParams({
                handler: 'Dashboard',
                dateFrom: from,
                dateTo: to,
                userRole
            });

            const res = await fetch(`/McdMonitoring?${params}`);
            const json = await res.json();

            if (seq !== loadSeq) return;
            if (!json.success) { setStatus(json.message, true); return; }

            renderGrid(json.data);
        } catch (err) {
            if (seq === loadSeq) setStatus(err.message, true);
        } finally {
            if (seq === loadSeq) {
                showProgress(false);
                btnSearch().disabled = false;
            }
        }
    }

    // ── Render the complete grid from the server result ──────────────────────
    function renderGrid(data) {
        const columns = data.columns || [];
        const rows = data.rows || [];
        const locationTotals = data.locationTotals || {};

        let html = '<div class="mcd-header-row">'
            + '<div class="mcd-process-col mcd-header">Process</div>';

        columns.forEach(col => {
            html += `<div class="mcd-loc-col mcd-header">${esc(col)}</div>`;
        });
        html += '<div class="mcd-total-col mcd-header">Total</div></div>';

        if (rows.length === 0) {
            html += '<div class="mcd-grid-empty">No processes found for this mode.</div>';
            gridContainer().innerHTML = html;
            setStatus('No data.', false);
            return;
        }

        rows.forEach(row => {
            const cells = row.cells || {};
            html += `<div class="mcd-grid-row" data-process="${esc(row.process)}">`;
            html += `<div class="mcd-process-col mcd-proc-label">${esc(row.process)}</div>`;

            columns.forEach(loc => {
                const cell = cells[loc];

                if (!cell || cell.notApplicable) {
                    html += '<div class="mcd-loc-col mcd-cell mcd-cell-na">–</div>';
                    return;
                }

                const count = cell.count || 0;
                const cls = count > 0
                    ? 'mcd-cell mcd-cell-hot mcd-cell-clickable'
                    : 'mcd-cell mcd-cell-zero';
                const title = `${esc(row.process)} @ ${esc(loc)}: ${count} machine(s)`;

                html += `<div class="${cls}" data-key="${esc(cell.key)}" data-loc="${esc(loc)}" title="${title}">${count}</div>`;
            });

            const rowTotal = row.rowTotal || 0;
            const totalCls = rowTotal > 0 ? 'mcd-proc-total mcd-total-hot' : 'mcd-proc-total';
            html += `<div class="mcd-total-col ${totalCls}">${rowTotal}</div>`;
            html += '</div>';
        });

        // Column totals row
        html += '<div class="mcd-total-row">'
            + '<div class="mcd-process-col mcd-total-label">TOTAL</div>';

        columns.forEach(col => {
            const value = locationTotals[col] || 0;
            const cls = value > 0 ? 'mcd-total-cell mcd-total-hot' : 'mcd-total-cell';
            html += `<div class="mcd-loc-col ${cls}">${value}</div>`;
        });

        const grand = data.grandTotal || 0;
        const grandCls = grand > 0 ? 'mcd-total-cell mcd-total-hot' : 'mcd-total-cell';
        html += `<div class="mcd-total-col ${grandCls}">${grand}</div></div>`;

        gridContainer().innerHTML = html;
        wireCellClicks();

        const mode = data.mode === 'TE' ? 'TE' : 'Standard';
        setStatus(`Updated ${new Date().toLocaleTimeString()} · ${mode}`, false);
    }

    function wireCellClicks() {
        gridContainer()
            .querySelectorAll('.mcd-cell[data-key]')
            .forEach(el => el.addEventListener('click', onCellClick));
    }

    // ── Cell click → open machine list panel ─────────────────────────────────
    async function onCellClick(e) {
        const el = e.currentTarget;
        const count = parseInt(el.textContent, 10) || 0;
        if (count === 0) return;

        const row = el.closest('.mcd-grid-row');
        const cell = {
            key: el.dataset.key,
            location: el.dataset.loc,
            process: row ? row.dataset.process : '',
            userRole: lastFilter.userRole
        };
        currentCell = cell;
        const seq = ++cellSeq;

        panelTitle().textContent = `${cell.process} ( ${cell.location} ) — loading…`;
        machineList().innerHTML = '';
        openPanel();

        try {
            const params = new URLSearchParams({
                handler: 'MachineList',
                cellKey: cell.key,
                dateFrom: lastFilter.dateFrom,
                dateTo: lastFilter.dateTo,
                userRole: cell.userRole
            });

            const res = await fetch(`/McdMonitoring?${params}`);
            const json = await res.json();

            if (seq !== cellSeq) return;

            if (!json.success) {
                machineList().innerHTML = `<li class="mcd-list-error">${esc(json.message)}</li>`;
                return;
            }

            const d = json.data;
            cell.process = d.process || cell.process;
            cell.location = d.location || cell.location;
            panelTitle().textContent = `${cell.process} ( ${cell.location} )`;

            if (!d.machines || d.machines.length === 0) {
                machineList().innerHTML = '<li class="mcd-list-empty">No machines.</li>';
                return;
            }

            machineList().innerHTML = d.machines
                .map(m => `<li class="mcd-list-item" data-mcno="${esc(m)}">${esc(m)}</li>`)
                .join('');

            machineList().querySelectorAll('.mcd-list-item').forEach(li => {
                li.addEventListener('click', () => {
                    machineList().querySelectorAll('.mcd-list-item')
                        .forEach(x => x.classList.remove('selected'));
                    li.classList.add('selected');
                });
                li.addEventListener('dblclick', goToMonitoring);
            });
        } catch (err) {
            if (seq === cellSeq) {
                machineList().innerHTML = `<li class="mcd-list-error">${esc(err.message)}</li>`;
            }
        }
    }

    // ── Go to monitoring (mirrors Button2_Click) ─────────────────────────────
    function goToMonitoring() {
        const selected = machineList().querySelector('.mcd-list-item.selected');
        if (!selected) { setStatus('Please select a machine first.', true); return; }
        if (!currentCell) return;

        const params = new URLSearchParams({
            machine: selected.dataset.mcno,
            process: currentCell.process,
            floor: currentCell.location,
            userType: currentCell.userRole === 'TE' ? 'TE' : 'PM'
        });

        window.location.href = `/Monitoring?${params}`;
    }

    // ── Panel helpers ────────────────────────────────────────────────────────
    function openPanel() {
        machinePanel().style.display = '';
        panelOverlay().style.display = '';
    }

    function closePanel() {
        machinePanel().style.display = 'none';
        panelOverlay().style.display = 'none';
        cellSeq++;
        currentCell = null;
    }

    // ── Progress / status ────────────────────────────────────────────────────
    let progressTimer = null;

    function showProgress(on) {
        progressBar().style.display = on ? '' : 'none';

        if (on) {
            clearInterval(progressTimer);
            let w = 0;
            progressInner().style.width = '0';
            progressTimer = setInterval(() => {
                w = Math.min(w + 5, 90);
                progressInner().style.width = w + '%';
            }, 200);
        } else {
            clearInterval(progressTimer);
            progressInner().style.width = '100%';
            setTimeout(() => { progressInner().style.width = '0'; }, 400);
        }
    }

    function setStatus(message, isError) {
        const el = lblStatus();
        if (!el) return;
        el.textContent = message || '';
        el.classList.toggle('mcd-status-error', !!isError);
    }

    // ── Utilities ────────────────────────────────────────────────────────────
    function toLocalIso(d) {
        const pad = n => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
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
