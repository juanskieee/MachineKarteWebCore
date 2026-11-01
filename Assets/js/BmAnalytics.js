Workflows.register('BmAnalytics', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;
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
    // State
    // -----------------------------------------------------------------------
    let currentResult = null;   // last BmAnalyticsResult from server
    let currentMode = 'EEE';

    // -----------------------------------------------------------------------
    // DOM refs
    // -----------------------------------------------------------------------
    const { dateFrom, dateTo } = options;
    const mode = options.userMode;
    const btnSearch = byId('btnSearch');
    const loading = byId('summaryLoading');
    const lblStatus = byId('lblStatus');
    const gridWrapper = byId('gridWrapper');
    const layout = JSON.parse(gridWrapper.dataset.layout);
    let gridMode = null;
    const flyoutPanel = byId('flyoutPanel');
    const flyoutTitle = byId('flyoutTitle');
    const flyoutList = byId('flyoutList');
    const btnCloseFlyout = byId('btnCloseFlyout');
    const btnViewMon = byId('btnViewMonitoring');
    const rvt = root.querySelector('input[name="__RequestVerificationToken"]');
    const legacyForm = byId('legacyForm')?.value === '12' ? '12' : '8';

    // -----------------------------------------------------------------------
    // Search
    // -----------------------------------------------------------------------
    btnSearch.addEventListener('click', () => triggerSearch());

    function triggerSearch() {
        if (btnSearch.disabled) return;
        const payload = {
            dateFrom: dateFrom.replace('T', ' '),
            dateTo: dateTo.replace('T', ' '),
            mode,
            legacyForm
        };
        currentMode = mode;
        loadGrid(payload);
    }

    async function loadGrid(payload) {
        setLoading(true);
        closeFlyout();
        currentResult = null;
        lblStatus.textContent = '';
        lblStatus.classList.remove('status-error');
        renderLayout(payload.mode);
        renderGrid(null);

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
            renderGrid(currentResult);
            lblStatus.textContent = `Last updated: ${new Date().toLocaleTimeString()}`;
        } catch (err) {
            lblStatus.textContent = 'Unable to load summary: ' + err.message;
            lblStatus.classList.add('status-error');
        } finally {
            setLoading(false);
        }
    }

    // -----------------------------------------------------------------------
    // Render grid
    // -----------------------------------------------------------------------
    function renderLayout(mode) {
        if (gridMode === mode) return;
        gridMode = mode;
        gridWrapper.innerHTML = '<table class="bma-table" aria-label="Action with MCD summary"><thead><tr>' +
            '<th class="bma-hdr-process" scope="col">Process</th>' +
            layout.columns.map(location => `<th class="bma-hdr-loc" scope="col">${location}</th>`).join('') +
            '<th class="bma-hdr-total" scope="col">Total</th></tr></thead><tbody>' +
            layout.modes[mode].map(row =>
                `<tr><td class="bma-proc-label">${row.process}</td>` +
                layout.columns.map(location => row.cells[location]
                    ? `<td class="bma-cell" data-key="${row.cells[location]}" data-proc="${row.process}" data-loc="${location}"></td>`
                    : '<td class="bma-cell bma-cell-na"></td>').join('') +
                `<td class="bma-row-total" data-process="${row.process}"></td></tr>`
            ).join('') +
            '<tr class="bma-total-row"><td class="bma-proc-label">Total</td>' +
            layout.columns.map(location => `<td class="bma-col-total" data-location="${location}"></td>`).join('') +
            '<td class="bma-grand-total"></td></tr></tbody></table>';
    }

    function renderGrid(result) {
        gridWrapper.querySelectorAll('[data-key]').forEach(td => {
            const count = result ? (result.cells?.[td.dataset.key]?.count || 0) : '—';
            const active = count > 0;
            td.textContent = count;
            td.classList.toggle('bma-cell-active', active);
            td.tabIndex = active ? 0 : -1;
            td.setAttribute('role', 'button');
            td.setAttribute('aria-disabled', String(!active));
            td.setAttribute('aria-label', `${td.dataset.proc} / ${td.dataset.loc}: ${count}`);
        });
        gridWrapper.querySelectorAll('.bma-row-total').forEach(td => {
            td.textContent = result ? (result.processTotals?.[td.dataset.process] || 0) : '—';
        });
        gridWrapper.querySelectorAll('.bma-col-total').forEach(td => {
            td.textContent = result ? (result.locationTotals?.[td.dataset.location] || 0) : '—';
        });
        gridWrapper.querySelector('.bma-grand-total').textContent = result ? (result.grandTotal || 0) : '—';
    }

    gridWrapper.addEventListener('click', event => {
        if (btnSearch.disabled) return;
        const td = event.target.closest('.bma-cell-active');
        if (td) openFlyout(td.dataset.key, td.dataset.proc, td.dataset.loc);
    });

    // -----------------------------------------------------------------------
    // Flyout panel
    // -----------------------------------------------------------------------
    function openFlyout(key, proc, loc) {
        if (!currentResult) return;
        const cells = currentResult.cells || {};
        const cell = cells[key];
        if (!cell || cell.count === 0) return;

        btnViewMon.disabled = true;
        btnViewMon.dataset.machine = '';
        flyoutTitle.textContent = `${proc}  ( ${loc} )`;
        flyoutList.innerHTML = '';

        (cell.mcnos || []).forEach(mc => {
            const li = document.createElement('li');
            li.className = 'bma-mc-item summary-machine-item';
            li.setAttribute('aria-pressed', 'false');
            li.tabIndex = 0;
            li.setAttribute('role', 'button');
            li.textContent = mc;
            li.addEventListener('click', () => {
                flyoutList.querySelectorAll('.active').forEach(el => { el.classList.remove('active'); el.setAttribute('aria-pressed', 'false'); });
                li.classList.add('active');
                li.setAttribute('aria-pressed', 'true');
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
            dateFrom,
            dateTo
        });
        Workflows.selectMachine(params);
    });

    // -----------------------------------------------------------------------
    // Auto-refresh follows the shared Form1 timer-zero signal.
    // -----------------------------------------------------------------------
    context.onRefresh(triggerSearch);

    triggerSearch();

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    function setLoading(on) {
        btnSearch.disabled = on;
        loading.hidden = !on;
        root.setAttribute('aria-busy', String(on));
    }


});
