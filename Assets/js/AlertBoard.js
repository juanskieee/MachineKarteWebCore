Workflows.register('AlertBoard', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;
    const setInterval = context.setInterval;
    const setTimeout = context.setTimeout;
/**
 * AlertBoard.js
 * Handles the BM Alert Board (Form2 → AlertBoard.cshtml).
 *
 * Responsibilities:
 *  • Build & POST the LoadBoard request
 *  • Render count cells with alert colour
 *  • Handle cell clicks → drilldown panel
 *  • "View in Monitoring" navigates with querystring params
 *  • Auto-refresh when Monitoring signals timer=0 via sessionStorage
 */

    'use strict';

    // ── Token ────────────────────────────────────────────────────────────────
    const token = () =>
        root.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // ── Element refs ─────────────────────────────────────────────────────────
    const dtFrom = byId('dtFrom');
    const dtTo = byId('dtTo');
    const selUser = byId('selUser');
    const btnSearch = byId('btnSearch');
    const progress = byId('progressBar');
    const progressInner = byId('progressInner');
    const panelDrill = byId('panelDrilldown');
    const drillTitle = byId('drilldownTitle');
    const drillList = byId('drilldownList');
    const btnClose = byId('btnDrillClose');
    const btnGo = byId('btnDrillGo');

    // State
    let _selectedMachine = null;
    let _selectedCell = null;   // e.g. "DC_0"

    // ── Defaults ─────────────────────────────────────────────────────────────
    (function initDefaults() {
        const now = new Date();
        const from = new Date(now);
        from.setDate(from.getDate() - 30);
        const to = new Date(now);
        to.setDate(to.getDate() + 1);
        dtFrom.value = options.dateFrom ? options.dateFrom.slice(0, 10) + 'T00:00' : toLocalInput(from);
        dtTo.value = options.dateTo ? options.dateTo.slice(0, 10) + 'T00:00' : toLocalInput(to);
        selUser.value = options.userType === 'PM' ? '' : (options.userType || '');
    })();

    function toLocalInput(d) {
        const pad = n => String(n).padStart(2, '0');
        return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
    }

    // ── Search ───────────────────────────────────────────────────────────────
    btnSearch.addEventListener('click', loadBoard);

    async function loadBoard() {
        setProgress(true);
        resetBoard();

        const body = {
            dateFrom: fmtForServer(dtFrom.value),
            dateTo: fmtForServer(dtTo.value),
            user: selUser.value
        };

        try {
            const res = await fetch('/AlertBoard?handler=LoadBoard', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token()
                },
                body: JSON.stringify(body)
            });
            const json = await res.json();
            if (!json.success) throw new Error(json.message);
            renderBoard(json.data);
        } catch (err) {
            byId('boardStatus').textContent = 'Error loading board: ' + err.message;
        } finally {
            setProgress(false);
        }
    }

    function fmtForServer(dtLocal) {
        // Convert "YYYY-MM-DDTHH:mm" → "MM/dd/yyyy HH:mm:ss"
        if (!dtLocal) return '';
        const d = new Date(dtLocal);
        const pad = n => String(n).padStart(2, '0');
        return `${pad(d.getMonth() + 1)}/${pad(d.getDate())}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
    }

    // ── Board rendering ───────────────────────────────────────────────────────

    function resetBoard() {
        root.querySelectorAll('.ab-cell-active').forEach(el => {
            el.textContent = '0';
            el.classList.remove('ab-alert');
        });
        root.querySelectorAll('[data-id^="ptotal_"], [data-id^="ltotal_"], [data-id="grandTotal"]').forEach(el => {
            el.textContent = '0';
        });
        closeDrilldown();
    }

    function renderBoard(data) {
        // data.cells: { "DC_0": { count, machines }, … }
        // data.locationTotals: { "LSI1-1F": n, … }
        // data.grandTotal: n

        const cells = data.cells || {};

        for (const [key, cell] of Object.entries(cells)) {
            const el = byId('cell_' + key);
            if (!el) continue;
            el.textContent = cell.count;
            el.tabIndex = cell.count > 0 ? 0 : -1;
            el.setAttribute('role', 'button');
            if (cell.count > 0) el.classList.add('ab-alert');
        }

        // Location totals
        const locTotals = data.locationTotals || {};
        for (const [loc, n] of Object.entries(locTotals)) {
            const el = byId('ltotal_' + loc);
            if (el) el.textContent = n;
        }

        // Process row totals (sum across all cells for that process)
        const processes = ['DC', 'DB', 'WB', 'MP', 'PL', 'ATC', 'SING', 'FL', 'FT', 'TP', 'EDS', 'MEMS'];
        for (const proc of processes) {
            let total = 0;
            for (const [key, cell] of Object.entries(cells)) {
                if (key.startsWith(proc + '_')) total += cell.count;
            }
            const el = byId('ptotal_' + proc);
            if (el) el.textContent = total;
        }

        const grand = byId('grandTotal');
        if (grand) grand.textContent = data.grandTotal ?? 0;
    }

    // ── Cell click → drilldown ─────────────────────────────────────────────

    root.addEventListener('click', async function (e) {
        const cell = e.target.closest('.ab-cell-active');
        if (!cell) return;
        const count = parseInt(cell.textContent, 10);
        if (!count || count === 0) return;

        _selectedCell = cell.dataset.cell;
        await openDrilldown(_selectedCell);
    });

    async function openDrilldown(cellKey) {
        try {
            const query = new URLSearchParams({
                cellKey,
                dateFrom: fmtForServer(dtFrom.value),
                dateTo: fmtForServer(dtTo.value),
                user: selUser.value
            });
            const res = await fetch('/AlertBoard?handler=Drilldown&' + query.toString());
            const json = await res.json();
            if (!json.success) throw new Error(json.message);

            const { machines, title } = json.data;
            drillTitle.textContent = title;
            drillList.innerHTML = '';
            _selectedMachine = null;

            machines.forEach(m => {
                const li = document.createElement('li');
                li.textContent = m;
                li.classList.add('ab-machine-item');
                li.tabIndex = 0;
                li.setAttribute('role', 'button');
                li.addEventListener('click', () => {
                    drillList.querySelectorAll('li').forEach(x => x.classList.remove('selected'));
                    li.classList.add('selected');
                    _selectedMachine = m;
                });
                drillList.appendChild(li);
            });

            panelDrill.classList.remove('is-hidden');
        } catch (err) {
            byId('boardStatus').textContent = 'Drilldown error: ' + err.message;
        }
    }

    // ── Drilldown panel controls ───────────────────────────────────────────

    btnClose.addEventListener('click', closeDrilldown);

    btnGo.addEventListener('click', () => {
        if (!_selectedMachine) return;
        // Navigate to Monitoring with the machine pre-selected
        // Monitoring.js reads ?machine=<value> on load (mirrors VB Form1 navigation)
        const [proc, locKey] = resolveProcessLocation(_selectedCell);
        const params = new URLSearchParams({
            machine: _selectedMachine,
            process: proc,
            location: locKey,
            userType: selUser.value,
            dateFrom: dtFrom.value,
            dateTo: dtTo.value
        });
        params.set('userType', selUser.value || 'PM'); Workflows.selectMachine(params);
        closeDrilldown();
    });

    function closeDrilldown() {
        panelDrill.classList.add('is-hidden');
        drillList.innerHTML = '';
        _selectedMachine = null;
    }

    // Maps cell key → { process, location } for Monitoring navigation
    function resolveProcessLocation(key) {
        const map = {
            DC_0: 'DC|LSI1-1F', DC_2: 'DC|LSI2-1F', DC_3: 'DC|LSI2-2F', DC_4: 'DC|LSI2-3F', DC_6: 'DC|LSI3-2F',
            DB_0: 'DB|LSI1-1F', DB_2: 'DB|LSI2-1F', DB_3: 'DB|LSI2-2F', DB_4: 'DB|LSI2-3F', DB_6: 'DB|LSI3-2F',
            WB_0: 'WB|LSI1-1F', WB_2: 'WB|LSI2-1F', WB_3: 'WB|LSI2-2F', WB_4: 'WB|LSI2-3F', WB_6: 'WB|LSI3-2F',
            MP_0: 'MP|LSI1-1F', MP_2: 'MP|LSI2-1F', MP_3: 'MP|LSI2-2F', MP_4: 'MP|LSI2-3F', MP_6: 'MP|LSI3-2F',
            PL_0: 'PL|LSI1-1F', PL_2: 'PL|LSI2-1F', PL_3: 'PL|LSI2-2F', PL_4: 'PL|LSI2-3F', PL_5: 'PL|LSI3-1F',
            ATC_0: 'ATC|LSI1-1F', ATC_1: 'ATC|LSI1-2F', ATC_2: 'ATC|LSI2-1F', ATC_3: 'ATC|LSI2-2F',
            ATC_4: 'ATC|LSI2-3F', ATC_65: 'ATC|LSI3-1F', ATC_6: 'ATC|LSI3-3F',
            FL_1: 'FL|LSI1-2F', FL_2: 'FL|LSI2-1F', FL_3: 'FL|LSI2-2F', FL_4: 'FL|LSI2-3F', FL_5: 'FL|LSI3-3F',
            SING_1: 'SING|LSI1-2F', SING_2: 'SING|LSI2-1F', SING_3: 'SING|LSI2-2F',
            SING_4: 'SING|LSI2-3F', SING_5: 'SING|LSI3-3F',
            FT_1: 'FT|LSI1-2F', FT_2: 'FT|LSI2-1F', FT_3: 'FT|LSI2-2F', FT_4: 'FT|LSI2-3F', FT_5: 'FT|LSI3-3F',
            TP_1: 'TP|LSI1-2F', TP_2: 'TP|LSI2-1F', TP_3: 'TP|LSI2-2F', TP_4: 'TP|LSI2-3F', TP_5: 'TP|LSI3-3F',
            EDS_1: 'EDS|LSI3-4F', MEMS_1: 'MEMS|LSI3-2F'
        };
        const val = map[key] ?? '|';
        const [p, l] = val.split('|');
        return [p, l];
    }

    // ── Progress bar ─────────────────────────────────────────────────────────

    let _progressInterval = null;

    function setProgress(active) {
        if (active) {
            progress.classList.remove('is-hidden');
            let pct = 0;
            _progressInterval = setInterval(() => {
                pct = Math.min(pct + 2, 90);
                progressInner.style.width = pct + '%';
            }, 100);
        } else {
            clearInterval(_progressInterval);
            progressInner.style.width = '100%';
            setTimeout(() => {
                progress.classList.add('is-hidden');
                progressInner.style.width = '0';
            }, 300);
        }
    }

    // ── Auto-refresh (mirrors VB tmrRefresh_Tick) ─────────────────────────
    // Monitoring page writes sessionStorage.monitoringTimer = "0" when its
    // countdown hits zero.  AlertBoard polls that and auto-reloads.

    context.onRefresh(loadBoard);

    // Initial load on page show
    loadBoard();

});
