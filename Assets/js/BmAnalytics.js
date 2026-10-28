/**
 * BmAnalytics.js
 * Client-side logic for the BM Analytics page (Form12 conversion).
 *
 * Grid: process × location count table.
 * Active column → counts MCD machines that already have an action card.
 * Clicking a non-zero cell opens a flyout listing machine numbers.
 * "View in Monitoring" navigates to /Monitoring?process=X&floor=Y&machine=Z
 * Auto-refreshes every 60 s (mirrors tmrRefresh_Tick).
 */

(() => {
    'use strict';

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    const REFRESH_INTERVAL_MS = 60_000;

    const ALL_LOCATIONS = [
        'LSI1-1F', 'LSI1-2F',
        'LSI2-1F', 'LSI2-2F', 'LSI2-3F',
        'LSI3-1F', 'LSI3-2F', 'LSI3-3F', 'LSI3-4F'
    ];

    // Process → which location indices they occupy (1-based).
    // ATC has a special slot "65" for LSI3-1F.
    const EEE_PROCESS_LOCS = {
        DC: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-2F'],
        DB: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-2F'],
        WB: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-2F'],
        MP: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-1F'],
        PL: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-1F'],
        FL: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        FT: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        TP: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        MEMS: ['LSI3-2F'],
        EDS: ['LSI3-4F'],
        SING: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        // ATC index 6 (1-based) = LSI3-1F → key "ATC_65"; index 7 = LSI3-3F → key "ATC_6"
        ATC: ['LSI1-1F', 'LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-1F', 'LSI3-3F'],
    };
    const EEE_PROCESSES = ['DC', 'DB', 'WB', 'MP', 'PL', 'FL', 'FT', 'TP', 'MEMS', 'EDS', 'SING', 'ATC'];

    const TE_PROCESS_LOCS = {
        FL: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        FT: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        TP: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-3F'],
        EDS: ['LSI3-4F'],
        MEMS: ['LSI3-2F'],
    };
    const TE_PROCESSES = ['FL', 'FT', 'TP', 'MEMS', 'EDS'];

    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------
    let currentResult = null;   // last BmAnalyticsResult from server
    let currentMode = 'EEE';
    let currentCellKey = null;   // key of the open flyout cell
    let refreshTimer = null;

    // -----------------------------------------------------------------------
    // DOM refs
    // -----------------------------------------------------------------------
    const dtFrom = document.getElementById('dtFrom');
    const dtTo = document.getElementById('dtTo');
    const selMode = document.getElementById('selMode');
    const btnSearch = document.getElementById('btnSearch');
    const searchSpinner = document.getElementById('searchSpinner');
    const lblStatus = document.getElementById('lblStatus');
    const gridWrapper = document.getElementById('gridWrapper');
    const flyoutPanel = document.getElementById('flyoutPanel');
    const flyoutTitle = document.getElementById('flyoutTitle');
    const flyoutList = document.getElementById('flyoutList');
    const btnCloseFlyout = document.getElementById('btnCloseFlyout');
    const btnViewMon = document.getElementById('btnViewMonitoring');
    const rvt = document.querySelector('input[name="__RequestVerificationToken"]');

    // -----------------------------------------------------------------------
    // Search
    // -----------------------------------------------------------------------
    btnSearch.addEventListener('click', () => triggerSearch());

    function triggerSearch() {
        const payload = {
            dateFrom: dtFrom.value.replace('T', ' '),
            dateTo: dtTo.value.replace('T', ' '),
            mode: selMode.value
        };
        currentMode = selMode.value;
        loadGrid(payload);
    }

    async function loadGrid(payload) {
        setLoading(true);
        closeFlyout();

        try {
            const res = await fetch('/BmAnalytics?handler=LoadGrid', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': rvt ? rvt.value : ''
                },
                body: JSON.stringify(payload)
            });
            const json = await res.json();
            if (!json.success) throw new Error(json.message);

            currentResult = json.data;
            renderGrid(currentResult, currentMode);
            lblStatus.textContent = `Last updated: ${new Date().toLocaleTimeString()}`;
        } catch (err) {
            gridWrapper.innerHTML = `<div class="alert alert-danger">Error: ${err.message}</div>`;
        } finally {
            setLoading(false);
        }
    }

    // -----------------------------------------------------------------------
    // Render grid
    // -----------------------------------------------------------------------
    function renderGrid(result, mode) {
        const processes = mode === 'TE' ? TE_PROCESSES : EEE_PROCESSES;
        const processLocs = mode === 'TE' ? TE_PROCESS_LOCS : EEE_PROCESS_LOCS;
        const cells = result.cells || {};
        const locTotals = result.locationTotals || {};
        const procTotals = result.processTotals || {};
        const grandTotal = result.grandTotal || 0;

        let html = `
        <div class="table-responsive">
        <table class="table table-bordered table-sm bma-table">
            <thead>
                <tr>
                    <th class="bma-hdr-process">Process</th>
                    ${ALL_LOCATIONS.map(l => `<th class="bma-hdr-loc">${l}</th>`).join('')}
                    <th class="bma-hdr-total">Total</th>
                </tr>
            </thead>
            <tbody>`;

        for (const proc of processes) {
            const locs = processLocs[proc] || [];
            html += `<tr><td class="bma-proc-label">${proc}</td>`;

            for (const loc of ALL_LOCATIONS) {
                // Build the cell key matching the server's CellKey logic
                const key = buildCellKey(proc, loc, locs);

                if (!key) {
                    // This process doesn't appear in this location
                    html += `<td class="bma-cell bma-cell-na"></td>`;
                    continue;
                }

                const cell = cells[key] || { count: 0, mcnos: [] };
                const count = cell.count || 0;
                const cls = count > 0 ? 'bma-cell bma-cell-active' : 'bma-cell bma-cell-zero';

                html += `<td class="${cls}" data-key="${key}" data-proc="${proc}" data-loc="${loc}"
                             title="${proc} / ${loc}: ${count}">${count}</td>`;
            }

            const pTotal = procTotals[proc] ?? 0;
            html += `<td class="bma-row-total">${pTotal}</td></tr>`;
        }

        // Location totals row
        html += `<tr class="bma-total-row"><td class="bma-proc-label fw-bold">Total</td>`;
        for (const loc of ALL_LOCATIONS) {
            html += `<td class="bma-col-total">${locTotals[loc] ?? 0}</td>`;
        }
        html += `<td class="bma-grand-total">${grandTotal}</td></tr>`;

        html += `</tbody></table></div>`;

        gridWrapper.innerHTML = html;

        // Attach click handlers to non-zero cells
        gridWrapper.querySelectorAll('.bma-cell-active').forEach(td => {
            td.addEventListener('click', () => {
                const key = td.dataset.key;
                const proc = td.dataset.proc;
                const loc = td.dataset.loc;
                openFlyout(key, proc, loc);
            });
        });
    }

    // Build the cell key that matches BmAnalyticsMappings.CellKey() on the server.
    function buildCellKey(process, location, locs) {
        if (!locs.includes(location)) return null;
        if (process === 'ATC' && location === 'LSI3-1F') return 'ATC_65';
        if (process === 'ATC' && location === 'LSI3-3F') return 'ATC_6';
        const idx = locs.indexOf(location) + 1;   // 1-based
        return `${process}_${idx}`;
    }

    // -----------------------------------------------------------------------
    // Flyout panel
    // -----------------------------------------------------------------------
    function openFlyout(key, proc, loc) {
        if (!currentResult) return;
        const cells = currentResult.cells || {};
        const cell = cells[key];
        if (!cell || cell.count === 0) return;

        currentCellKey = key;
        flyoutTitle.textContent = `${proc}  ( ${loc} )`;
        flyoutList.innerHTML = '';

        (cell.mcnos || []).forEach(mc => {
            const li = document.createElement('li');
            li.className = 'list-group-item list-group-item-action py-1 px-2 bma-mc-item';
            li.textContent = mc;
            li.addEventListener('click', () => {
                flyoutList.querySelectorAll('.active').forEach(el => el.classList.remove('active'));
                li.classList.add('active');
                btnViewMon.dataset.proc = proc;
                btnViewMon.dataset.floor = loc;
                btnViewMon.dataset.machine = mc;
            });
            flyoutList.appendChild(li);
        });

        flyoutPanel.style.display = 'block';
    }

    function closeFlyout() {
        flyoutPanel.style.display = 'none';
        currentCellKey = null;
    }

    btnCloseFlyout.addEventListener('click', closeFlyout);

    btnViewMon.addEventListener('click', () => {
        const proc = btnViewMon.dataset.proc;
        const floor = btnViewMon.dataset.floor;
        const machine = btnViewMon.dataset.machine;
        if (!machine) return;
        const url = `/Monitoring?process=${encodeURIComponent(proc)}&floor=${encodeURIComponent(floor)}&machine=${encodeURIComponent(machine)}`;
        window.open(url, '_blank');
    });

    // -----------------------------------------------------------------------
    // Auto-refresh (mirrors tmrRefresh_Tick — runs every 60 s)
    // -----------------------------------------------------------------------
    function startAutoRefresh() {
        stopAutoRefresh();
        refreshTimer = setInterval(() => {
            if (currentResult) triggerSearch();
        }, REFRESH_INTERVAL_MS);
    }

    function stopAutoRefresh() {
        if (refreshTimer) { clearInterval(refreshTimer); refreshTimer = null; }
    }

    document.addEventListener('DOMContentLoaded', () => {
        triggerSearch();
        startAutoRefresh();
    });

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    function setLoading(on) {
        btnSearch.disabled = on;
        searchSpinner.classList.toggle('d-none', !on);
    }

})();