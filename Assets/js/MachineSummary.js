Workflows.register('MachineSummary', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;
/**
 * MachineSummary.js
 * Form11 → MachineSummary.cshtml  (E-Karte System)
 *
 * Responsibilities
 *  • Load the process × location count grid from OnGetSummaryAsync
 *  • Load the machine list for a clicked cell from OnGetMachineListAsync
 *  • "View in Monitoring" navigates to Monitoring with pre-filled filters
 *  • Auto-refresh when Monitoring signals the shared legacy timer reaching zero
 */
    'use strict';

    const BASE = { summaryUrl: '/MachineSummary', machineListUrl: '/MachineSummary' };

    const $ = id => byId(id);
    const elSearch = $('msSearch');
    const { dateFrom, dateTo, userMode } = options;
    const elStatus = $('msStatus');
    const loading = $('summaryLoading');
    const layout = JSON.parse($('msGrid').dataset.layout);
    let gridMode = null;
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

    elSearch.addEventListener('click', () => search(false));
    elBody.addEventListener('click', onCellClick);
    elClose.addEventListener('click', closePanel);
    elOverlay.addEventListener('click', closePanel);
    elGo.addEventListener('click', goToMonitoring);
    root.addEventListener('keydown', e => {
        if (e.key === 'Escape' && isPanelOpen()) closePanel();
    });

    search(false);

    // ── Grid load ─────────────────────────────────────────────────────────────
    async function search(background) {
        if (elSearch.disabled) return;

        if (!dateFrom || !dateTo) {
            if (!background) setStatus('Please select a date range.', true);
            return;
        }

        const seq = ++loadSeq;
        lastFilter = { dateFrom, dateTo, userMode };

        closePanel();
        renderLayout(userMode);
        renderGrid(null);
        setStatus('');
        setLoading(true);

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
                setLoading(false);
            }
        }
    }

    // ── Grid render ───────────────────────────────────────────────────────────
    function renderLayout(mode) {
        if (gridMode === mode) return;
        gridMode = mode;
        elHead.innerHTML = '<tr><th class="ms-th-process" scope="col">Process</th>' +
            layout.columns.map(column => `<th class="ms-th-loc" scope="col">${esc(column)}</th>`).join('') +
            '<th class="ms-th-total" scope="col">Total</th></tr>';
        elBody.innerHTML = layout.modes[mode].map(row =>
            `<tr data-process="${row.process}"><td class="ms-td-process">${row.process}</td>` +
            layout.columns.map(column => `<td class="ms-cell" data-location="${column}" data-applicable="${row.locations.includes(column)}"></td>`).join('') +
            '<td class="ms-td-rowtotal"></td></tr>'
        ).join('');
        elFoot.innerHTML = '<tr><td class="ms-tf-label">Total</td>' +
            layout.columns.map(column => `<td class="ms-tf-cell" data-location="${column}"></td>`).join('') +
            '<td class="ms-tf-grand"></td></tr>';
    }

    function renderGrid(data) {
        const rows = new Map((data?.rows || []).map(row => [row.process, row]));
        elBody.querySelectorAll('tr').forEach(tr => {
            const process = tr.dataset.process;
            const row = rows.get(process);
            tr.querySelectorAll('[data-location]').forEach(td => {
                const location = td.dataset.location;
                const cell = row?.cells?.[location];
                const unavailable = td.dataset.applicable === 'false' || cell?.notApplicable;
                const count = data ? (cell?.count || 0) : '—';
                const active = !unavailable && count > 0;
                td.textContent = unavailable ? '' : count;
                td.classList.toggle('ms-na', !!unavailable);
                td.classList.toggle('ms-active', active);
                td.tabIndex = active ? 0 : -1;
                td.setAttribute('role', 'button');
                td.setAttribute('aria-disabled', String(!active));
                td.setAttribute('aria-label', `${process} / ${location}: ${unavailable ? 'Not applicable' : count}`);
            });
            tr.querySelector('.ms-td-rowtotal').textContent = data ? (row?.rowTotal || 0) : '—';
        });
        elFoot.querySelectorAll('[data-location]').forEach(td => {
            td.textContent = data ? (data.locationTotals?.[td.dataset.location] || 0) : '—';
        });
        elFoot.querySelector('.ms-tf-grand').textContent = data ? (data.grandTotal || 0) : '—';
    }

    // ── Cell click → machine list ─────────────────────────────────────────────
    async function onCellClick(e) {
        if (root.getAttribute('aria-busy') === 'true') return;
        const td = e.target.closest('.ms-active');
        if (!td) return;
        const process = td.closest('tr').dataset.process;
        const location = td.dataset.location;
        if (!process || !location) return;

        const seq = ++cellSeq;
        currentCell = {
            process,
            location,
            userMode: lastFilter.userMode
        };

        elTitle.textContent = `${process} ( ${location} ) — loading…`;
        elList.innerHTML = '<li class="summary-panel-message"><span class="spinner" aria-hidden="true"></span>Loading machines…</li>';
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
                elList.innerHTML = `<li class="summary-panel-message">${esc(json.message || 'Unable to load machines.')}</li>`;
                return;
            }

            const data = json.data || {};
            currentCell.process = data.process || process;
            currentCell.location = data.location || location;
            elTitle.textContent = `${currentCell.process} ( ${currentCell.location} )`;

            elList.innerHTML = '';
            const machines = data.machines || [];
            if (machines.length === 0) {
                elList.innerHTML = '<li class="summary-panel-message">No machines.</li>';
                return;
            }

            machines.forEach(machine => {
                const li = document.createElement('li');
                li.className = 'ms-panel-item summary-machine-item';
                li.setAttribute('aria-pressed', 'false');
                li.tabIndex = 0;
                li.setAttribute('role', 'button');
                li.textContent = machine;
                li.dataset.machine = machine;
                li.addEventListener('click', () => selectMachine(li));
                li.addEventListener('dblclick', goToMonitoring);
                elList.appendChild(li);
            });
        } catch (err) {
            if (seq === cellSeq) {
                elTitle.textContent = `${process} ( ${location} )`;
                elList.innerHTML = `<li class="summary-panel-message">${esc(err.message)}</li>`;
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
            item.setAttribute('aria-pressed', 'false');
        });

        li.classList.add('ms-selected');
        li.setAttribute('aria-pressed', 'true');
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
            userType: currentCell.userMode === 'TE' ? 'TE' : (options.userType && options.userType !== 'TE' ? options.userType : 'EEE'),
            dateFrom: lastFilter.dateFrom,
            dateTo: lastFilter.dateTo
        });

        Workflows.selectMachine(params);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    // Form11's timer is driven by Form1.lblTimer, not an independent interval.
    // Monitoring publishes the equivalent zero signal for converted pages.
    context.onRefresh(() => search(true));

    function setStatus(message, isError) {
        elStatus.textContent = message || '';
        elStatus.classList.toggle('status-error', !!isError);
        elStatus.classList.toggle('status-info', !isError);
    }

    function setLoading(on) {
        elSearch.disabled = on;
        loading.hidden = !on;
        root.setAttribute('aria-busy', String(on));
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
});
