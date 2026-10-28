/**
 * McdMonitoring.js
 * Handles the MCD Monitoring dashboard (Form7 → McdMonitoring.cshtml).
 *
 * Conventions:
 *   - fetch() AJAX only
 *   - AjaxResult<T> envelope: { success, message, data }
 *   - Anti-forgery token in RequestVerificationToken header for POSTs (none here – read-only page)
 */

(function () {
    'use strict';

    // ── Process / location layout (mirrors VB grid design) ──────────────────
    const GRID_ROWS = [
        // { process, cells: [ { cellKey, loc } ] }
        { process: 'DC', cells: [{ k: 'DC_1', loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: 'DC_2', loc: 'LSI2-1F' }, { k: 'DC_3', loc: 'LSI2-2F' }, { k: 'DC_4', loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'DB', cells: [{ k: 'DB_1', loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: 'DB_2', loc: 'LSI2-1F' }, { k: 'DB_3', loc: 'LSI2-2F' }, { k: 'DB_4', loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'WB', cells: [{ k: 'WB_1', loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: 'WB_2', loc: 'LSI2-1F' }, { k: 'WB_3', loc: 'LSI2-2F' }, { k: 'WB_4', loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'MP', cells: [{ k: 'MP_1', loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: 'MP_2', loc: 'LSI2-1F' }, { k: 'MP_3', loc: 'LSI2-2F' }, { k: 'MP_4', loc: 'LSI2-3F' }, { k: 'MP_5', loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'PL', cells: [{ k: 'PL_1', loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: 'PL_2', loc: 'LSI2-1F' }, { k: null, loc: 'LSI2-2F' }, { k: null, loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'FL', cells: [{ k: null, loc: 'LSI1-1F' }, { k: 'FL_1', loc: 'LSI1-2F' }, { k: 'FL_2', loc: 'LSI2-1F' }, { k: 'FL_3', loc: 'LSI2-2F' }, { k: 'FL_4', loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'FT', cells: [{ k: null, loc: 'LSI1-1F' }, { k: 'FT_1', loc: 'LSI1-2F' }, { k: 'FT_2', loc: 'LSI2-1F' }, { k: 'FT_3', loc: 'LSI2-2F' }, { k: 'FT_4', loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'TP', cells: [{ k: null, loc: 'LSI1-1F' }, { k: 'TP_1', loc: 'LSI1-2F' }, { k: 'TP_2', loc: 'LSI2-1F' }, { k: 'TP_3', loc: 'LSI2-2F' }, { k: 'TP_4', loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'MEMS', cells: [{ k: null, loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: null, loc: 'LSI2-1F' }, { k: null, loc: 'LSI2-2F' }, { k: null, loc: 'LSI2-3F' }, { k: 'MEMS_1', loc: 'LSI3-2F' }, { k: null, loc: 'LSI3-4F' }] },
        { process: 'EDS', cells: [{ k: null, loc: 'LSI1-1F' }, { k: null, loc: 'LSI1-2F' }, { k: null, loc: 'LSI2-1F' }, { k: null, loc: 'LSI2-2F' }, { k: null, loc: 'LSI2-3F' }, { k: null, loc: 'LSI3-2F' }, { k: 'EDS_1', loc: 'LSI3-4F' }] },
    ];

    // ── State ────────────────────────────────────────────────────────────────
    let lastFilter = { dateFrom: '', dateTo: '', userRole: '' };
    let currentCellKey = null;

    // ── DOM refs ─────────────────────────────────────────────────────────────
    const dtFrom = () => document.getElementById('dtFrom');
    const dtTo = () => document.getElementById('dtTo');
    const selUser = () => document.getElementById('selUser');
    const btnSearch = () => document.getElementById('btnSearch');
    const gridBody = () => document.getElementById('gridBody');
    const totalRow = () => document.getElementById('totalRow');
    const progressBar = () => document.getElementById('progressBar');
    const progressInner = () => document.getElementById('progressInner');
    const machinePanel = () => document.getElementById('machinePanel');
    const panelOverlay = () => document.getElementById('panelOverlay');
    const machineList = () => document.getElementById('machineList');
    const panelTitle = () => document.getElementById('panelTitle');

    // ── Init ─────────────────────────────────────────────────────────────────
    document.addEventListener('DOMContentLoaded', () => {
        // Default dates: last 30 days
        const now = new Date();
        const past = new Date(now);
        past.setDate(past.getDate() - 30);
        dtFrom().value = toLocalIso(past);
        dtTo().value = toLocalIso(now);

        buildGridShell();

        btnSearch().addEventListener('click', loadDashboard);
        document.getElementById('btnPanelClose').addEventListener('click', closePanel);
        document.getElementById('btnPanelCancel').addEventListener('click', closePanel);
        document.getElementById('btnGoToMonitoring').addEventListener('click', goToMonitoring);
        panelOverlay().addEventListener('click', closePanel);

        loadDashboard();
    });

    // ── Build static grid shell (process rows with empty cells) ─────────────
    function buildGridShell() {
        const body = gridBody();
        body.innerHTML = '';
        GRID_ROWS.forEach(row => {
            const div = document.createElement('div');
            div.className = 'mcd-grid-row';
            div.dataset.process = row.process;

            let html = `<div class="mcd-process-col mcd-proc-label">${row.process}</div>`;
            row.cells.forEach(cell => {
                if (cell.k) {
                    html += `<div class="mcd-loc-col mcd-cell" 
                                  id="cell_${cell.k}" 
                                  data-key="${cell.k}"
                                  data-loc="${cell.loc}"
                                  title="${row.process} @ ${cell.loc}">0</div>`;
                } else {
                    html += `<div class="mcd-loc-col mcd-cell mcd-cell-na">–</div>`;
                }
            });

            // Process row total
            html += `<div class="mcd-total-col mcd-proc-total" id="ptot_${row.process}">0</div>`;
            div.innerHTML = html;
            body.appendChild(div);
        });

        // Wire cell clicks after DOM insertion
        document.querySelectorAll('.mcd-cell[data-key]').forEach(el => {
            el.addEventListener('click', onCellClick);
        });
    }

    // ── Load dashboard data ──────────────────────────────────────────────────
    async function loadDashboard() {
        const from = dtFrom().value;
        const to = dtTo().value;
        const userRole = selUser().value;

        if (!from || !to) { alert('Please select a date range.'); return; }

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

            if (!json.success) { showError(json.message); return; }

            renderDashboard(json.data);
        } catch (err) {
            showError(err.message);
        } finally {
            showProgress(false);
            btnSearch().disabled = false;
        }
    }

    // ── Render dashboard response ────────────────────────────────────────────
    function renderDashboard(data) {
        // Reset all cells to 0 / default style
        document.querySelectorAll('.mcd-cell[data-key]').forEach(el => {
            el.textContent = '0';
            el.classList.remove('mcd-cell-hot', 'mcd-cell-clickable');
        });
        GRID_ROWS.forEach(r => {
            const el = document.getElementById(`ptot_${r.process}`);
            if (el) el.textContent = '0';
        });

        // Fill cells from server data
        const processTotals = {};
        (data.cells || []).forEach(cell => {
            const key = cellKey(cell.process, cell.location);
            const el = document.getElementById(`cell_${key}`);
            if (!el) return;

            el.textContent = cell.count;
            if (cell.count > 0) {
                el.classList.add('mcd-cell-hot', 'mcd-cell-clickable');
            }

            processTotals[cell.process] = (processTotals[cell.process] || 0) + cell.count;
        });

        // Process totals
        Object.entries(processTotals).forEach(([proc, cnt]) => {
            const el = document.getElementById(`ptot_${proc}`);
            if (el) el.textContent = cnt;
        });

        // Floor totals
        setTotCell('tot_LSI1_1F', data.totalLsi1_1F);
        setTotCell('tot_LSI1_2F', data.totalLsi1_2F);
        setTotCell('tot_LSI2_1F', data.totalLsi2_1F);
        setTotCell('tot_LSI2_2F', data.totalLsi2_2F);
        setTotCell('tot_LSI2_3F', data.totalLsi2_3F);
        setTotCell('tot_LSI3_2F', data.totalLsi3);
        setTotCell('tot_LSI3_4F', data.totalLsi3_4F);
        setTotCell('tot_Grand', data.grandTotal);

        totalRow().style.display = '';
    }

    function setTotCell(id, val) {
        const el = document.getElementById(id);
        if (!el) return;
        el.textContent = val || 0;
        el.classList.toggle('mcd-total-hot', (val || 0) > 0);
    }

    // ── Map (process, location) → cellKey used in GRID_ROWS ─────────────────
    function cellKey(process, location) {
        const map = {
            'DC|LSI1-1F': 'DC_1', 'DC|LSI2-1F': 'DC_2', 'DC|LSI2-2F': 'DC_3', 'DC|LSI2-3F': 'DC_4',
            'DB|LSI1-1F': 'DB_1', 'DB|LSI2-1F': 'DB_2', 'DB|LSI2-2F': 'DB_3', 'DB|LSI2-3F': 'DB_4',
            'WB|LSI1-1F': 'WB_1', 'WB|LSI2-1F': 'WB_2', 'WB|LSI2-2F': 'WB_3', 'WB|LSI2-3F': 'WB_4',
            'MP|LSI1-1F': 'MP_1', 'MP|LSI2-1F': 'MP_2', 'MP|LSI2-2F': 'MP_3', 'MP|LSI2-3F': 'MP_4', 'MP|LSI3-2F': 'MP_5',
            'PL|LSI1-1F': 'PL_1', 'PL|LSI2-1F': 'PL_2',
            'FL|LSI1-2F': 'FL_1', 'FL|LSI2-1F': 'FL_2', 'FL|LSI2-2F': 'FL_3', 'FL|LSI2-3F': 'FL_4',
            'FT|LSI1-2F': 'FT_1', 'FT|LSI2-1F': 'FT_2', 'FT|LSI2-2F': 'FT_3', 'FT|LSI2-3F': 'FT_4',
            'TP|LSI1-2F': 'TP_1', 'TP|LSI2-1F': 'TP_2', 'TP|LSI2-2F': 'TP_3', 'TP|LSI2-3F': 'TP_4',
            'MEMS|LSI3-2F': 'MEMS_1',
            'EDS|LSI3-4F': 'EDS_1',
        };
        return map[`${process}|${location}`] || null;
    }

    // ── Cell click → open machine list panel ─────────────────────────────────
    async function onCellClick(e) {
        const el = e.currentTarget;
        const key = el.dataset.key;
        const cnt = parseInt(el.textContent, 10);
        if (!cnt || cnt === 0) return;

        currentCellKey = key;
        panelTitle().textContent = `Loading ${key.replace('_', ' @ ')}…`;
        machineList().innerHTML = '';
        openPanel();

        try {
            const params = new URLSearchParams({
                handler: 'MachineList',
                cellKey: key,
                dateFrom: lastFilter.dateFrom,
                dateTo: lastFilter.dateTo,
                userRole: lastFilter.userRole
            });

            const res = await fetch(`/McdMonitoring?${params}`);
            const json = await res.json();

            if (!json.success) {
                machineList().innerHTML = `<li class="mcd-list-error">${json.message}</li>`;
                return;
            }

            const d = json.data;
            panelTitle().textContent = `${d.process} ( ${d.location} )`;

            if (!d.machines || d.machines.length === 0) {
                machineList().innerHTML = '<li class="mcd-list-empty">No machines.</li>';
                return;
            }

            machineList().innerHTML = d.machines
                .map(m => `<li class="mcd-list-item" data-mcno="${m}">${m}</li>`)
                .join('');

            // Allow selecting a machine
            machineList().querySelectorAll('.mcd-list-item').forEach(li => {
                li.addEventListener('click', () => {
                    machineList().querySelectorAll('.mcd-list-item').forEach(x => x.classList.remove('selected'));
                    li.classList.add('selected');
                });
            });

        } catch (err) {
            machineList().innerHTML = `<li class="mcd-list-error">${err.message}</li>`;
        }
    }

    // ── Go to monitoring (mirrors Button2_Click) ──────────────────────────────
    function goToMonitoring() {
        const selected = machineList().querySelector('.mcd-list-item.selected');
        if (!selected) { alert('Please select a machine first.'); return; }

        const mcno = selected.dataset.mcno;
        const process = panelTitle().textContent.split('(')[0].trim();
        const loc = (panelTitle().textContent.match(/\((.+?)\)/) || [])[1]?.trim() || '';

        // Navigate to Monitoring page pre-filtered
        const params = new URLSearchParams({ machine: mcno, process, floor: loc });
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
        currentCellKey = null;
    }

    // ── Progress ─────────────────────────────────────────────────────────────
    function showProgress(on) {
        progressBar().style.display = on ? '' : 'none';
        if (on) {
            let w = 0;
            const iv = setInterval(() => {
                w = Math.min(w + 5, 90);
                progressInner().style.width = w + '%';
                if (w >= 90) clearInterval(iv);
            }, 200);
        } else {
            progressInner().style.width = '100%';
            setTimeout(() => { progressInner().style.width = '0'; }, 400);
        }
    }

    function showError(msg) {
        alert('Error: ' + msg);
    }

    // ── Utilities ────────────────────────────────────────────────────────────
    function toLocalIso(d) {
        const pad = n => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }

}());