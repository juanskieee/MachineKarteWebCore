Workflows.register('BmAnalytics', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;
    const setInterval = context.setInterval;
    const setTimeout = context.setTimeout;
/**
 * BmAnalytics.js
 * Client-side logic for the BM Analytics page (Form8/Form12 conversion).
 *
 * Grid: process × location count table.
 * Active column → counts MCD machines that already have an action card.
 * Clicking a non-zero cell opens a flyout listing machine numbers.
 * "View in Monitoring" navigates to /Monitoring?process=X&floor=Y&machine=Z
 * Auto-refreshes on the legacy Form1 timer cycle.
 */

    'use strict';

    // -----------------------------------------------------------------------
    // Constants
    // -----------------------------------------------------------------------
    const ALL_LOCATIONS = [
        'LSI1-1F', 'LSI1-2F',
        'LSI2-1F', 'LSI2-2F', 'LSI2-3F',
        'LSI3-1F', 'LSI3-2F', 'LSI3-3F', 'LSI3-4F'
    ];

    // Process → which location indices they occupy (1-based).
    const EEE_PROCESS_LOCS = {
        DC: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        DB: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        WB: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        MP: ['LSI1-1F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        PL: ['LSI1-1F', 'LSI2-1F'],
        FL: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        FT: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        TP: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        MEMS: ['LSI3-2F'],
        EDS: ['LSI3-4F'],
        SING: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        ATC: ['LSI1-1F', 'LSI2-1F'],
    };
    const EEE_PROCESSES = ['DC', 'DB', 'WB', 'MP', 'PL', 'ATC', 'FL', 'SING', 'FT', 'TP', 'EDS', 'MEMS'];

    const TE_PROCESS_LOCS = {
        FL: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        FT: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
        TP: ['LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F'],
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

    // -----------------------------------------------------------------------
    // DOM refs
    // -----------------------------------------------------------------------
    const dtFrom = byId('dtFrom');
    const dtTo = byId('dtTo');
    const selMode = byId('selMode');
    const btnSearch = byId('btnSearch');
    const searchSpinner = byId('searchSpinner');
    const lblStatus = byId('lblStatus');
    const gridWrapper = byId('gridWrapper');
    const flyoutPanel = byId('flyoutPanel');
    const flyoutTitle = byId('flyoutTitle');
    const flyoutList = byId('flyoutList');
    const btnCloseFlyout = byId('btnCloseFlyout');
    const btnViewMon = byId('btnViewMonitoring');
    const rvt = root.querySelector('input[name="__RequestVerificationToken"]');
    const legacyForm = byId('legacyForm')?.value === '12' ? '12' : '8';

    const FORM12_EEE_PROCESS_LOCS = {
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
        ATC: ['LSI1-1F', 'LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-1F', 'LSI3-3F']
    };

    // -----------------------------------------------------------------------
    // Search
    // -----------------------------------------------------------------------
    btnSearch.addEventListener('click', () => triggerSearch());

    function triggerSearch() {
        const payload = {
            dateFrom: dtFrom.value.replace('T', ' '),
            dateTo: dtTo.value.replace('T', ' '),
            mode: selMode.value,
            legacyForm
        };
        currentMode = selMode.value;
        loadGrid(payload);
    }

    async function loadGrid(payload) {
        setLoading(true);
        closeFlyout();

        try {
            const res = await fetch(`/BmAnalytics?handler=LoadGrid&legacyForm=${legacyForm}`, {
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
            gridWrapper.innerHTML = `<div class="alert alert-danger">Error: ${escapeHtml(err.message)}</div>`;
        } finally {
            setLoading(false);
        }
    }

    // -----------------------------------------------------------------------
    // Render grid
    // -----------------------------------------------------------------------
    function renderGrid(result, mode) {
        const processes = mode === 'TE' ? TE_PROCESSES : EEE_PROCESSES;
        const processLocs = legacyForm === '12'
            ? FORM12_EEE_PROCESS_LOCS
            : (mode === 'TE' ? TE_PROCESS_LOCS : EEE_PROCESS_LOCS);
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

                html += `<td class="${cls}" role="button" tabindex="${count > 0 ? 0 : -1}" data-key="${key}" data-proc="${proc}" data-loc="${loc}"
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
        const idx = locs.indexOf(location) + 1;   // 1-based
        if (legacyForm === '12' && process === 'ATC' && location === 'LSI3-1F') {
            return 'ATC_65';
        }
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
        btnViewMon.disabled = true;
        btnViewMon.dataset.machine = '';
        flyoutTitle.textContent = `${proc}  ( ${loc} )`;
        flyoutList.innerHTML = '';

        (cell.mcnos || []).forEach(mc => {
            const li = document.createElement('li');
            li.className = 'list-group-item list-group-item-action py-1 px-2 bma-mc-item';
            li.tabIndex = 0;
            li.setAttribute('role', 'button');
            li.textContent = mc;
            li.addEventListener('click', () => {
                flyoutList.querySelectorAll('.active').forEach(el => el.classList.remove('active'));
                li.classList.add('active');
                btnViewMon.disabled = false;
                btnViewMon.dataset.proc = proc;
                btnViewMon.dataset.floor = loc;
                btnViewMon.dataset.machine = mc;
            });
            flyoutList.appendChild(li);
        });

        flyoutPanel.classList.remove('is-hidden');
    }

    function closeFlyout() {
        flyoutPanel.classList.add('is-hidden');
        btnViewMon.disabled = true;
        btnViewMon.dataset.machine = '';
        currentCellKey = null;
    }

    btnCloseFlyout.addEventListener('click', closeFlyout);

    btnViewMon.addEventListener('click', () => {
        const proc = btnViewMon.dataset.proc;
        const floor = btnViewMon.dataset.floor;
        const machine = btnViewMon.dataset.machine;
        if (!machine) return;
        const params = new URLSearchParams({
            process: proc,
            floor,
            machine,
            userType: currentMode === 'TE' ? 'TE' : (options.userType && options.userType !== 'TE' ? options.userType : 'EEE'),
            dateFrom: dtFrom.value,
            dateTo: dtTo.value
        });
        Workflows.selectMachine(params);
    });

    // -----------------------------------------------------------------------
    // Auto-refresh follows the shared Form1 timer-zero signal.
    // -----------------------------------------------------------------------
    context.onRefresh(triggerSearch);

    if (options.dateFrom) dtFrom.value = options.dateFrom.slice(0, 10) + 'T00:00';
    if (options.dateTo) dtTo.value = options.dateTo.slice(0, 10) + 'T00:00';
    selMode.value = options.userType === 'TE' ? 'TE' : 'EEE';
    triggerSearch();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    function setLoading(on) {
        btnSearch.disabled = on;
        searchSpinner.classList.toggle('d-none', !on);
    }

    function escapeHtml(value) { const span = document.createElement('span'); span.textContent = value; return span.innerHTML; }

});
