/**
 * Monitoring.js
 * Converted from Form1.vb event handlers — E-Karte System
 * Handles: filter cascades, search, pagination, auto-refresh timer,
 *          machine card rendering, floor summary panel, machine list sidebar.
 */

// ════════════════════════════════════════════════════════
// STATE  (mirrors Form1 private fields)
// ════════════════════════════════════════════════════════
const State = {
    page: 1,       // lblPageNumber
    totalPages: 1,
    searchId: 0,
    timerCount: 600,     // lblTimer
    timerHandle: null,    // Timer1 interval
    autoHandle: null,    // Timer2 interval (auto-refresh)
    lastFilter: null,    // cached last successful MonitoringFilter
    antiForgery: ''
};

// ════════════════════════════════════════════════════════
// INIT
// ════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', () => {
    State.antiForgery = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // Set default dates (mirrors Form1_Load dt1/dt2 init)
    const today = new Date();
    document.getElementById('dt2').value = formatDate(new Date(today.getTime() + 86400000));
    document.getElementById('dt1').value = formatDate(new Date(today.getTime() - 30 * 86400000));

    startRefreshTimer();
    initializeFromQueryString();
    renderFloorSummary([]);
    updatePagination();
});

async function initializeFromQueryString() {
    const query = new URLSearchParams(window.location.search);
    if (query.has('workflow')) return;
    return applyMonitoringSelection(Object.fromEntries(query));
}

async function applyMonitoringSelection(selection) {
    const query = new URLSearchParams(selection);
    const process = query.get('process');
    const floor = query.get('floor') || query.get('location');
    const machine = query.get('machine');
    const userType = query.get('userType') || 'PM';
    const dateFrom = query.get('dateFrom');
    const dateTo = query.get('dateTo');

    if (dateFrom) document.getElementById('dt1').value = dateFrom.slice(0, 10);
    if (dateTo) document.getElementById('dt2').value = dateTo.slice(0, 10);

    if (!process || !floor || !machine) return;

    const user = document.getElementById('cbUser');
    user.value = userType;
    await onUserChanged();

    const processSelect = document.getElementById('cbProcess');
    if (!Array.from(processSelect.options).some(option => option.value === process)) return;
    processSelect.value = process;
    await onProcessChanged();

    const floorSelect = document.getElementById('cbFloor');
    if (!Array.from(floorSelect.options).some(option => option.value === floor)) return;
    floorSelect.value = floor;
    await onFloorChanged();

    const machineSelect = document.getElementById('cbMachine');
    if (!Array.from(machineSelect.options).some(option => option.value === machine)) return;
    machineSelect.value = machine;
    await doSearch(1);
}

// ════════════════════════════════════════════════════════
// DROPDOWN CASCADES
// ════════════════════════════════════════════════════════

/** Mirrors cbUser_SelectedIndexChanged — reload process list */
async function onUserChanged() {
    const userType = val('cbUser');
    const label = document.getElementById('lblProcessLabel');
    label.textContent = (userType === 'TE') ? 'Tester process' : 'Process';

    const resp = await get(`/Monitoring?handler=ProcessList&userType=${enc(userType)}`);
    if (!resp.success) return;

    fillSelect('cbProcess', resp.data, '-Select-');
    fillSelect('cbFloor', ['-Select-'], '-Select-');
    fillSelect('cbMachine', ['-Select-'], '');
}

/** Mirrors cbProcess_SelectedIndexChanged — reload floor list */
async function onProcessChanged() {
    if (val('cbProcess') === '-Select-') return;

    const resp = await get('/Monitoring?handler=FloorList');
    if (!resp.success) return;

    fillSelect('cbFloor', resp.data, '-Select-');
    fillSelect('cbMachine', [], '');
}

/** Mirrors cbFloor_SelectedIndexChanged — reload machine list */
async function onFloorChanged() {
    const process = val('cbProcess');
    const floor = val('cbFloor');

    if (process === '-Select-') {
        showToast('Please select a process.'); return;
    }
    if (floor === '-Select-') return;

    const userType = val('cbUser');
    const resp = await get(
        `/Monitoring?handler=MachineList&userType=${enc(userType)}&process=${enc(process)}&floor=${enc(floor)}`
    );
    if (!resp.success) return;

    fillSelect('cbMachine', resp.data.machines, 'All');
}

// ════════════════════════════════════════════════════════
// SEARCH  (btnSearch_Click)
// ════════════════════════════════════════════════════════

async function doSearch(page = 1) {
    const machine = val('cbMachine');
    if (!machine) { showToast('Please select a machine number.'); return; }
    if (!val('cbUser')) { showToast('Please select a User first.'); return; }

    State.page = page;
    const searchId = ++State.searchId;
    State.timerCount = 600;

    const filter = buildFilter();
    State.lastFilter = filter;
    document.getElementById('monitoringWelcome').classList.add('is-hidden');

    showSpinner(true);
    document.getElementById('noResultMsg').classList.add('is-hidden');
    clearCards();

    const resp = await post('/Monitoring?handler=Search', filter);
    if (searchId !== State.searchId) return;
    showSpinner(false);

    if (!resp.success) { showToast(resp.message); return; }

    State.page = resp.data.currentPage;
    State.totalPages = resp.data.totalPages;
    filter.page = State.page;
    updatePagination();
    renderFloorSummary(resp.data.floorSummaries);
    await renderMachineCards(resp.data.machineSummaries, filter);
}

// ════════════════════════════════════════════════════════
// PAGINATION  (lblPrev / lblNext Click)
// ════════════════════════════════════════════════════════

function changePage(delta) {
    const newPage = State.page + delta;
    if (newPage < 1 || newPage > State.totalPages) return;
    doSearch(newPage);
}

function updatePagination() {
    const counter = document.getElementById('lblPageNumber');
    counter.textContent = `${State.page}/${State.totalPages}`;
    counter.setAttribute('aria-label', `Page ${State.page} of ${State.totalPages}`);
    document.getElementById('btnPrev').disabled = State.page <= 1;
    document.getElementById('btnNext').disabled = State.page >= State.totalPages;
}

// ════════════════════════════════════════════════════════
// AUTO-REFRESH TIMER  (Timer1_Tick in Form1)
// Counts down 600 → 0, then auto-searches if a machine is selected.
// ════════════════════════════════════════════════════════

function startRefreshTimer() {
    if (State.timerHandle) clearInterval(State.timerHandle);

    State.timerHandle = setInterval(() => {
        State.timerCount--;
        document.getElementById('lblTimer').textContent = State.timerCount;

        if (State.timerCount <= 0) {
            State.timerCount = 600;
            window.dispatchEvent(new Event('monitoring:refresh'));

            // Mirrors: if (TimeOfDay.Hour == 8 && minute < 59) SetDate()
            const now = new Date();
            if (now.getHours() === 8 && now.getMinutes() < 59) {
                const dt2 = document.getElementById('dt2');
                const dt1 = document.getElementById('dt1');
                const today = new Date();
                dt2.value = formatDate(today);
                dt1.value = formatDate(new Date(today.getTime() - 30 * 86400000));
            }

            if (val('cbMachine')) {
                doSearch(State.page);
            }
        }
    }, 1000);
}

// ════════════════════════════════════════════════════════
// FLOOR SUMMARY RENDERING  (Panel5 / Label_N / lbl_N in Form1)
// ════════════════════════════════════════════════════════

function renderFloorSummary(summaries) {
    const grid = document.getElementById('floorSummaryGrid');
    const floors = ['LSI1-1F', 'LSI1-2F', 'LSI2-1F', 'LSI2-2F', 'LSI2-3F', 'LSI3-1F', 'LSI3-2F', 'LSI3-3F', 'LSI3-4F'];
    const rows = [
        { id: 'btnRepeatBM', section: 'RepeatBM', name: 'Re-Occuring BM Case', count: 'repeatBMCount', machines: 'repeatMachines', className: 'summary-repeat' },
        { id: 'btnMCD', section: 'MCD', name: 'Important Device (Need Action)', count: 'needActionCount', machines: 'needActionMachines', className: 'summary-need' },
        { id: 'btnWithAction', section: 'WithAction', name: 'Important Device (With Action)', count: 'withActionCount', machines: 'withActionMachines', className: 'summary-with' }
    ];
    grid.innerHTML = `<thead><tr><th scope="col">Action Item</th>${floors.map(floor => `<th scope="col">${floor}</th>`).join('')}</tr></thead><tbody></tbody>`;
    const body = grid.querySelector('tbody');
    rows.forEach(row => {
        const tr = document.createElement('tr');
        tr.className = row.className;
        tr.innerHTML = `<th scope="row"><a id="${row.id}" href="#${row.section}">${row.name}</a></th>`;
        tr.querySelector('a').addEventListener('click', event => { event.preventDefault(); openSection(row.section); });
        floors.forEach(floor => {
            const summary = summaries?.find(item => item.floorCode === floor);
            const count = summary?.[row.count] ?? 0;
            const cell = document.createElement('td');
            cell.textContent = count;
            if (count > 0) {
                cell.className = 'has-count';
                const link = document.createElement('a');
                link.href = '#machineListSidebar';
                link.textContent = count;
                link.setAttribute('aria-label', `${row.name}, ${floor}: ${count}`);
                link.addEventListener('click', event => {
                    event.preventDefault();
                    onFloorCellClick(floor, summary[row.machines] || []);
                });
                cell.replaceChildren(link);
            }
            tr.appendChild(cell);
        });
        body.appendChild(tr);
    });
}

/** Mirrors LabelMain_Click — shows Listbox_main for the clicked floor */
function onFloorCellClick(floorCode, machines) {
    const sidebar = document.getElementById('machineListSidebar');
    const list = document.getElementById('listboxMain');
    document.getElementById('sidebarTitle').textContent = floorCode;
    list.innerHTML = '';
    const go = document.getElementById('machineListGo');
    go.disabled = true;
    let selected = '';
    machines.forEach(machine => {
        const li = document.createElement('li');
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'summary-machine-item';
        button.textContent = machine;
        button.setAttribute('aria-pressed', 'false');
        button.addEventListener('click', () => {
            selected = machine;
            list.querySelectorAll('button').forEach(item => item.setAttribute('aria-pressed', String(item === button)));
            go.disabled = false;
        });
        button.addEventListener('dblclick', () => { button.click(); go.click(); });
        li.appendChild(button);
        list.appendChild(li);
    });
    go.onclick = async () => {
        if (!selected) return;
        closeSidebar();
        await applyMonitoringSelection({
            userType: val('cbUser'), process: val('cbProcess'), floor: floorCode, machine: selected,
            dateFrom: val('dt1'), dateTo: val('dt2')
        });
    };
    sidebar.showModal();
}

function closeSidebar() {
    document.getElementById('machineListSidebar').close();
}

// ════════════════════════════════════════════════════════
// MACHINE CARD RENDERING  (Panel1 / uC_Test in Form1)
// ════════════════════════════════════════════════════════

async function renderMachineCards(summaries, filter) {
    const scroller = document.getElementById('cardScroller');
    const noResult = document.getElementById('noResultMsg');

    if (!summaries || summaries.length === 0) {
        noResult.classList.remove('is-hidden');
        return;
    }
    noResult.classList.add('is-hidden');

    await Promise.all(summaries.map(async (mc, idx) => {
        const col = document.createElement('div');
        col.className = 'machine-col';
        col.dataset.mcno = mc.machineNo;
        col.id = `col_${idx}`;

        col.innerHTML = `
            <div class="mc-col-header">
                <span class="mc-location">${escHtml(filter.floor)}</span>
                <span class="mc-no">${escHtml(mc.machineNo)}</span>
                <span class="mc-type">${escHtml(mc.machineType)}</span>
                <span class="need-action-label" title="Repeat BM groups within seven days without an action card">Need Action</span><span class="need-action-badge">…</span>
            </div>
            <div class="bm-card-list" id="cards_${idx}" tabindex="0" role="region" aria-label="BM history for ${escHtml(mc.machineNo)}">
                <div class="card-loading">Loading...</div>
            </div>`;
        scroller.appendChild(col);

        // Lazy-load BM cards for this column
        await loadMachineCard(mc, filter, col.querySelector('.bm-card-list'));
    }));
}

/** Lazy loads BM card rows for one machine column (mirrors uC_Test.LoadMain) */
async function loadMachineCard(mc, filter, container) {
    container.innerHTML = '<div class="card-loading" role="status">Loading…</div>';
    const url = `/Monitoring?handler=MachineCard` +
        `&userType=${enc(filter.userType)}` +
        `&process=${enc(filter.process)}` +
        `&floor=${enc(filter.floor)}` +
        `&machineNo=${enc(mc.machineNo)}` +
        `&machineType=${enc(mc.machineType)}` +
        `&actionLine=${mc.actionLine}` +
        `&dateFrom=${enc(filter.dateFrom)}` +
        `&dateTo=${enc(filter.dateTo)}`;

    const resp = await get(url);
    if (!container.isConnected) return;

    if (!resp || !resp.success || !resp.data) {
        container.innerHTML = `<div class="card-error" role="alert"><p>${escHtml(resp?.message || 'BM records could not be loaded.')}</p><button type="button">Retry</button></div>`;
        container.querySelector('button').addEventListener('click', () => loadMachineCard(mc, filter, container));
        return;
    }

    container.innerHTML = '';
    const { bmCards, actionLine, needActionCount } = resp.data;
    const column = container.closest('.machine-col');
    const needActionBadge = column?.querySelector('.need-action-badge');
    if (needActionBadge) {
        needActionBadge.textContent = needActionCount ?? 0;
    }
    const location = column.querySelector('.mc-location');
    if (location && bmCards.length) location.textContent = [...new Set(bmCards.map(card => card.location))].join(', ');

    if (bmCards.length === 0) {
        container.innerHTML = '<div class="no-bm">No BM records</div>';
        return;
    }

    bmCards.forEach((card, index) => {
        const element = buildBMCard(card, actionLine, filter.userType);
        // LoadProcessCapa: Salmon at the bottom, then SM/DM/DV tiers overlapping its boundary.
        const tier = Math.floor((bmCards.length - 1 - actionLine - index) / 2);
        if (actionLine < bmCards.length && tier >= 0) element.classList.add(['band-sm', 'band-dm', 'band-dv'][Math.min(tier, 2)]);
        container.appendChild(element);
    });
    container.scrollTop = container.scrollHeight;
}

/** Builds one BM card DOM element (mirrors MachineMonitoringCard.ascx HTML) */
function buildBMCard(card, actionLine, userType) {
    const div = document.createElement('div');

    // Priority star class
    const priorityClass = card.priority === '1'
        ? 'priority-star'
        : '';

    // Legacy right-docked Major Customer Device stars.
    const mcdBadge = card.isMCD === 'Yes'
        ? `<span class="badge-mcd ${card.mcdNeedsAction ? 'needs-mcd-action' : ''}" role="img" aria-label="Major Customer Device${card.mcdNeedsAction ? ', needs action' : ''}" title="Major Customer Device: ${escHtml(card.deviceName)}">&#9733;&#9733;&#9733;</span>`
        : '';

    // Action badge
    const actionColor = (card.actionColor || derivedColor(card.mcStatus)).toLowerCase();
    const actionTitle = actionColor === 'green' && derivedColor(card.mcStatus) !== 'Green'
        ? `Solved by the legacy seven-day check. Stored status: ${card.mcStatus || '—'}`
        : card.mcStatus;
    const actionBadge = card.haveAction === 'Yes'
        ? `<span class="badge-action status-${actionColor || 'red'}" title="${escHtml(actionTitle)}">Action</span>`
        : '';

    const markers = [
        card.actionMarker ? '<span class="bm-marker bm-marker-ac">AC</span>' : '',
        card.sectionMarker ? '<span class="bm-marker bm-marker-sm">SM</span>' : '',
        card.departmentMarker ? '<span class="bm-marker bm-marker-dm">DM</span>' : '',
        card.divisionMarker ? '<span class="bm-marker bm-marker-dv">DV</span>' : ''
    ].join('');

    div.className = `bm-card bm-card-clickable ${priorityClass} ${card.needsAction ? 'needs-action' : ''}`.trim();
    const repeatColor = (card.fontColor || '').toLowerCase();
    if (['red', 'blue', 'black', 'green', 'darkgreen', 'orange'].includes(repeatColor)) {
        div.style.setProperty('--repeat-color', repeatColor);
    }
    if (['red', 'blue'].includes(repeatColor)) div.classList.add(`repeat-${repeatColor}`);
    div.title = card.needsAction ? 'Repeat BM group needs an action card. Open Check And Action Card.' : 'Open Check And Action Card';
    div.tabIndex = 0;
    div.setAttribute('role', 'button');
    div.addEventListener('keydown', event => { if (event.target === div && (event.key === 'Enter' || event.key === ' ')) { event.preventDefault(); div.click(); } });

    div.addEventListener('click', () => {
        openActionCard(card, card.machineNo, userType);
    });

    div.innerHTML = `
        <div class="bm-card-markers">${markers}${actionBadge}</div>
        <div class="bm-card-content"><span class="bm-content-text">${escHtml(userType === 'TE' ? card.priority : card.bmContent)}</span>${mcdBadge}</div>
        <div class="bm-card-case">
            <span class="bm-case-text">${escHtml(card.bmCase)}</span>
        </div>
        <div class="bm-card-time">${escHtml(card.timeOfRequest)}</div>
        <div class="bm-card-footer">
            <span class="bm-card-actions">
                ${card.ocapCount > 0 ? `<button type="button" class="bm-command" data-command="ocap">O→</button>` : ''}
                <button type="button" class="bm-command" data-command="report">R→</button>
                <button type="button" class="bm-command" data-command="evidence">E→</button>
            </span>
        </div>`;

    div.querySelectorAll('.bm-command').forEach(button => {
        button.addEventListener('click', event => {
            event.stopPropagation();
            openBMCommand(button.dataset.command, card, userType);
        });
    });

    return div;
}

function openBMCommand(command, card, userType) {
    const params = {
        process: card.process || val('cbProcess'),
        bmId: String(card.bmId),
        timeOfRequest: card.timeOfRequest || '',
        machineNo: card.machineNo || ''
    };
    if (command === 'report') Workflows.open(userType === 'TE' ? 'BmReport' : 'PmReport', params);
    if (command === 'evidence') Workflows.open(userType === 'TE' ? 'TeEvidenceMaking' : 'EvidenceMaking', params);
    if (command === 'ocap') Workflows.open('OcapViewer', {
        ...params, bmProcess: params.process, bmCaseId: card.bmCaseId,
        machineType: card.machineType || '', location: card.location || '', bmCase: card.bmCase || ''
    });
}
function clearCards() {
    const scroller = document.getElementById('cardScroller');
    // Remove all machine columns but keep the static children (spinner/noResult)
    Array.from(scroller.querySelectorAll('.machine-col')).forEach(el => el.remove());
    scroller.scrollTop = 0;
}

// ════════════════════════════════════════════════════════
// BM CARD CLICK → Action Card modal
// Mirrors: Form_Input.LoadData(bmid, status, mcno, ColorT, HeaderCase)
// ════════════════════════════════════════════════════════

function openActionCard(card, mcNo, userType) {
    const colorName = card.haveAction === 'Yes'
        ? (card.actionColor || derivedColor(card.mcStatus))
        : (card.fontColor && card.fontColor !== 'None' ? card.fontColor : derivedColor(card.mcStatus));
    Workflows.open('ActionCards', {
        bmId: String(card.bmId), mcNo, userType,
        status: card.haveAction === 'Yes' ? 'Existing' : 'New',
        colorName, headerCase: card.bmCase || ''
    });
}

window.refreshMonitoring = () => { if (State.lastFilter) doSearch(State.page); };
window.applyMonitoringSelection = applyMonitoringSelection;
function derivedColor(mcStatus) {
    if (!mcStatus) return '';
    const s = mcStatus.toUpperCase();
    if (s.includes('NO RE-OCC')) return 'Green';
    if (s.includes('LOT EVALUATION')) return 'LightGreen';
    if (s.includes('STOP (ONGOING')) return 'Red';
    if (s.includes('100%')) return 'DarkOrange';
    if (s.includes('ONGOING INVEST')) return 'DarkGray';
    if (s.includes('INSPECTION RESULT')) return 'Violet';
    if (s.includes('QC INSTRUCTION')) return 'Aqua';
    return '';
}

// ════════════════════════════════════════════════════════
// NAVIGATION BUTTONS  (Button5 / btnMCD / btnWithAction)
// ════════════════════════════════════════════════════════

function openSection(section) {
    const userType = val('cbUser');
    if (!userType) { showToast('Please select a User first.'); return; }
    const names = { RepeatBM: 'AlertBoard', MCD: 'MachineSummary', WithAction: 'BmAnalytics', RawData: 'RawData', McdMonitoring: 'McdMonitoring', BmAnalytics: 'BmAnalytics' };
    const params = {
        userType, userMode: userType === 'TE' ? 'TE' : 'EEE',
        dateFrom: val('dt1'), dateTo: val('dt2'),
        process: val('cbProcess'), location: val('cbFloor'), machine: val('cbMachine')
    };
    if (section === 'RawData' && (params.process === '-Select-' || params.location === '-Select-')) {
        showToast('Select a process and location to view Raw Data.'); return;
    }
    if (section === 'WithAction') params.legacyForm = '12';
    if (section === 'BmAnalytics') params.legacyForm = '8';
    Workflows.open(names[section], params);
}
// ════════════════════════════════════════════════════════
// HELPERS
// ════════════════════════════════════════════════════════

function buildFilter() {
    return {
        userType: val('cbUser'),
        process: val('cbProcess'),
        floor: val('cbFloor'),
        machine: val('cbMachine'),
        dateFrom: toApiDate(document.getElementById('dt1').value),
        dateTo: toApiDate(document.getElementById('dt2').value),
        page: State.page
    };
}

function val(id) {
    return document.getElementById(id)?.value ?? '';
}

function fillSelect(id, items, defaultSelect) {
    const sel = document.getElementById(id);
    sel.innerHTML = '';
    items.forEach(item => {
        const opt = document.createElement('option');
        opt.value = item;
        opt.textContent = item;
        if (item === defaultSelect) opt.selected = true;
        sel.appendChild(opt);
    });
}

function showSpinner(show) {
    document.getElementById('loadingSpinner').classList.toggle('is-hidden', !show);
}

function showToast(msg) {
    const t = document.getElementById('toastMsg');
    t.textContent = msg;
    t.classList.remove('is-hidden');
    setTimeout(() => { t.classList.add('is-hidden'); }, 4000);
}

function formatDate(d) {
    return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'), String(d.getDate()).padStart(2, '0')].join('-');
}

function toApiDate(isoDate) {
    if (!isoDate) return '';
    const [y, m, day] = isoDate.split('-');
    return `${m}/${day}/${y} 00:00:00`;
}

function enc(s) { return encodeURIComponent(s ?? ''); }

function escHtml(s) {
    return String(s ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}

async function get(url) {
    try {
        const r = await fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        return await r.json();
    } catch (e) {
        console.error('GET error:', e);
        return { success: false, message: String(e) };
    }
}

async function post(url, body) {
    try {
        const r = await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'X-Requested-With': 'XMLHttpRequest',
                'RequestVerificationToken': State.antiForgery
            },
            body: JSON.stringify(body)
        });
        return await r.json();
    } catch (e) {
        console.error('POST error:', e);
        return { success: false, message: String(e) };
    }
}
