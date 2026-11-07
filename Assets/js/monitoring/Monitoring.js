/** Monitoring filters, pagination, shared refresh clock, machine histories, and floor summaries. */

"use strict";

const { escapeHtml, formatDate } = Karte;

// STATE  (mirrors Form1 private fields)

const State = {
    page: 1, // lblPageNumber
    totalPages: 1,
    searchId: 0,
    filterId: 0,
    searchController: null,
    filterController: null,
    timerCount: 600, // lblTimer
    timerHandle: null, // Timer1 interval
    toastTimer: null,
    lastFilter: null, // cached last successful MonitoringFilter
    dashboard: null, // matching columns for every page of the current search
    antiForgery: "",
};

// INIT

document.addEventListener("DOMContentLoaded", () => {
    State.antiForgery =
        document.querySelector('input[name="__RequestVerificationToken"]')
            ?.value ?? "";
    document
        .getElementById("btnClearFilters")
        .addEventListener("click", clearFilters);
    const legend = document.querySelector('.monitoring-legend');
    document.getElementById('machineBoard').addEventListener('contentvisibilityautostatechange', event => {
        if (!event.skipped) synchronizeBlinkAnimations(event.target);
    }, { capture: true });
    document.addEventListener('click', (event) => {
        if (!legend.contains(event.target)) legend.open = false;
    }, { capture: true });

    // Set default dates (mirrors Form1_Load dt1/dt2 init)
    resetDates();

    startRefreshTimer();
    initializeFromQueryString();
    renderFloorSummary([]);
    updatePagination();
});

// Keep optional session tracking out of the initial HTML and asset load.
window.addEventListener(
    "load",
    () => {
        fetch("/Monitoring?handler=TrackSession", {
            method: "POST",
            headers: { RequestVerificationToken: State.antiForgery },
        }).catch(() => {});
    },
    { once: true },
);

async function initializeFromQueryString() {
    const query = new URLSearchParams(window.location.search);
    if (query.has("workflow")) return;
    return applyMonitoringSelection(Object.fromEntries(query));
}

async function applyMonitoringSelection(selection) {
    const query = new URLSearchParams(selection);
    const process = query.get("process");
    const floor = query.get("floor") || query.get("location");
    const machine = query.get("machine");
    const userType = query.get("userType") || "PM";
    const dateFrom = query.get("dateFrom");
    const dateTo = query.get("dateTo");

    if (dateFrom) document.getElementById("dt1").value = dateFrom.slice(0, 10);
    if (dateTo) document.getElementById("dt2").value = dateTo.slice(0, 10);

    if (!process || !floor || !machine) return;

    const user = document.getElementById("cbUser");
    user.value = userType;
    await onUserChanged();

    const processSelect = document.getElementById("cbProcess");
    if (
        !Array.from(processSelect.options).some(
            (option) => option.value === process,
        )
    )
        return;
    processSelect.value = process;
    await onProcessChanged();

    const floorSelect = document.getElementById("cbFloor");
    if (
        !Array.from(floorSelect.options).some(
            (option) => option.value === floor,
        )
    )
        return;
    floorSelect.value = floor;
    await onFloorChanged();

    const machineSelect = document.getElementById("cbMachine");
    if (
        !Array.from(machineSelect.options).some(
            (option) => option.value === machine,
        )
    )
        return;
    machineSelect.value = machine;
    await doSearch(1);
}

// DROPDOWN CASCADES

/** Mirrors cbUser_SelectedIndexChanged — reload process list */
async function onUserChanged() {
    const filterId = ++State.filterId;
    State.filterController?.abort();
    State.filterController = new AbortController();
    const userType = val("cbUser");
    document.getElementById("lblMachineLabel").textContent =
        userType === "TE" ? "Tester" : "Machine";
    fillSelect("cbProcess", ["-Select-"], "-Select-");
    fillSelect("cbFloor", ["-Select-"], "-Select-");
    fillSelect("cbMachine", [], "");
    if (!userType) return;

    const query = new URLSearchParams({ userType });
    const resp = await request(`/Monitoring?handler=ProcessList&${query}`, {
        signal: State.filterController.signal,
    });
    if (filterId !== State.filterId || !resp.success) return;

    fillSelect("cbProcess", resp.data, "-Select-");
    fillSelect("cbFloor", ["-Select-"], "-Select-");
}

/** Mirrors cbProcess_SelectedIndexChanged — reload floor list */
async function onProcessChanged() {
    const filterId = ++State.filterId;
    State.filterController?.abort();
    State.filterController = new AbortController();
    fillSelect("cbFloor", ["-Select-"], "-Select-");
    fillSelect("cbMachine", [], "");
    if (val("cbProcess") === "-Select-") return;

    const resp = await request("/Monitoring?handler=FloorList", {
        signal: State.filterController.signal,
    });
    if (filterId !== State.filterId || !resp.success) return;

    fillSelect("cbFloor", resp.data, "-Select-");
    fillSelect("cbMachine", [], "");
}

/** Mirrors cbFloor_SelectedIndexChanged — reload machine list */
async function onFloorChanged() {
    const filterId = ++State.filterId;
    State.filterController?.abort();
    State.filterController = new AbortController();
    fillSelect("cbMachine", [], "");
    const process = val("cbProcess");
    const floor = val("cbFloor");

    if (process === "-Select-") {
        showToast("Please select a process.");
        return;
    }
    if (!floor || floor === "-Select-" || floor.toLowerCase() === "all") return;

    const userType = val("cbUser");
    const query = new URLSearchParams({ userType, process, floor });
    const resp = await request(`/Monitoring?handler=MachineList&${query}`, {
        signal: State.filterController.signal,
    });
    if (filterId !== State.filterId || !resp.success) return;

    fillSelect("cbMachine", resp.data.machines, "All");
}

function resetDates() {
    const today = new Date();
    document.getElementById("dt1").value = formatDate(
        new Date(today.getTime() - 30 * 86400000),
    );
    document.getElementById("dt2").value = formatDate(
        new Date(today.getTime() + 86400000),
    );
}

function clearFilters() {
    State.searchController?.abort();
    State.filterController?.abort();
    clearTimeout(State.toastTimer);
    ++State.searchId;
    ++State.filterId;
    State.lastFilter = null;
    State.dashboard = null;
    State.page = State.totalPages = 1;
    State.timerCount = 600;
    document.getElementById("lblTimer").textContent = "600";
    document.getElementById("cbUser").value = "";
    document.getElementById("lblMachineLabel").textContent = "Machine";
    fillSelect("cbProcess", ["-Select-"], "-Select-");
    fillSelect("cbFloor", ["-Select-"], "-Select-");
    fillSelect("cbMachine", [], "");
    resetDates();
    closeSidebar();
    document.getElementById("listboxMain").replaceChildren();
    document.getElementById("machineListGo").onclick = null;
    document.getElementById("machineListGo").disabled = true;
    clearCards();
    showSpinner(false);
    document.getElementById("noResultMsg").classList.add("is-hidden");
    document.getElementById("toastMsg").classList.add("is-hidden");
    document.getElementById("monitoringWelcome").classList.remove("is-hidden");
    renderFloorSummary([]);
    updatePagination();
    history.replaceState(null, "", "/Monitoring");
}

// SEARCH  (btnSearch_Click)

async function doSearch(page = 1) {
    const machine = val("cbMachine");
    if (!machine) {
        showToast("Please select a machine number.");
        return;
    }
    if (!val("cbUser")) {
        showToast("Please select a User first.");
        return;
    }
    const floor = val("cbFloor");
    if (!floor || floor === "-Select-" || floor.toLowerCase() === "all") {
        showToast("Please select a specific location.");
        return;
    }

    State.page = page;
    const searchId = ++State.searchId;
    State.searchController?.abort();
    const controller = (State.searchController = new AbortController());
    State.timerCount = 600;

    const filter = buildFilter();
    State.lastFilter = filter;
    State.dashboard = null;
    document.getElementById("monitoringWelcome").classList.add("is-hidden");

    showSpinner(true);
    document.getElementById("noResultMsg").classList.add("is-hidden");
    clearCards();

    const resp = await request("/Monitoring?handler=Search", {
        body: filter,
        signal: controller.signal,
    });
    if (searchId !== State.searchId) return;

    if (!resp.success) {
        showSpinner(false);
        showToast(resp.message);
        return;
    }

    State.page = resp.data.currentPage;
    State.totalPages = resp.data.totalPages;
    filter.page = State.page;
    State.dashboard = resp.data;
    updatePagination();
    renderFloorSummary(resp.data.floorSummaries);
    await renderCurrentPage(controller.signal);
    if (searchId === State.searchId) showSpinner(false);
}

// PAGINATION  (lblPrev / lblNext Click)

async function changePage(delta) {
    const newPage = State.page + delta;
    if (newPage < 1 || newPage > State.totalPages) return;
    const filter = buildFilter();
    if (!State.dashboard || !['userType', 'process', 'floor', 'machine', 'dateFrom', 'dateTo']
        .every(key => filter[key] === State.lastFilter[key]))
        return doSearch(1);

    State.page = newPage;
    updatePagination();
    await renderCurrentPage(State.searchController?.signal);
}

function renderCurrentPage(signal) {
    const offset = (State.page - 1) * 10;
    return renderMachineCards(State.dashboard.machineCards.slice(offset, offset + 10), State.lastFilter, signal);
}

function updatePagination() {
    const counter = document.getElementById("lblPageNumber");
    counter.textContent = `${State.page}/${State.totalPages}`;
    counter.setAttribute(
        "aria-label",
        `Page ${State.page} of ${State.totalPages}`,
    );
    document.getElementById("btnPrev").disabled = State.page <= 1;
    document.getElementById("btnNext").disabled =
        State.page >= State.totalPages;
}

// AUTO-REFRESH TIMER  (Timer1_Tick in Form1)
// Counts down 600 → 0, then auto-searches if a machine is selected.

function startRefreshTimer() {
    if (State.timerHandle) clearInterval(State.timerHandle);

    State.timerHandle = setInterval(() => {
        State.timerCount--;
        document.getElementById("lblTimer").textContent = State.timerCount;

        if (State.timerCount <= 0) {
            State.timerCount = 600;
            window.dispatchEvent(new Event("monitoring:refresh"));

            // Mirrors: if (TimeOfDay.Hour == 8 && minute < 59) SetDate()
            const now = new Date();
            if (now.getHours() === 8 && now.getMinutes() < 59) {
                const dt2 = document.getElementById("dt2");
                const dt1 = document.getElementById("dt1");
                const today = new Date();
                dt2.value = formatDate(today);
                dt1.value = formatDate(
                    new Date(today.getTime() - 30 * 86400000),
                );
            }

            if (val("cbMachine")) {
                doSearch(State.page);
            }
        }
    }, 1000);
}

// FLOOR SUMMARY RENDERING  (Panel5 / Label_N / lbl_N in Form1)

function renderFloorSummary(summaries) {
    const grid = document.getElementById("floorSummaryGrid");
    const floors = [
        "LSI1-1F",
        "LSI1-2F",
        "LSI2-1F",
        "LSI2-2F",
        "LSI2-3F",
        "LSI3-1F",
        "LSI3-2F",
        "LSI3-3F",
        "LSI3-4F",
    ];
    const rows = [
        {
            id: "btnRepeatBM",
            section: "RepeatBM",
            name: "Re-Occuring BM Case",
            count: "repeatBMCount",
            machines: "repeatMachines",
            className: "summary-repeat",
        },
        {
            id: "btnMCD",
            section: "MCD",
            name: "Important Device (Need Action)",
            count: "needActionCount",
            machines: "needActionMachines",
            className: "summary-need",
        },
        {
            id: "btnWithAction",
            section: "WithAction",
            name: "Important Device (With Action)",
            count: "withActionCount",
            machines: "withActionMachines",
            className: "summary-with",
        },
    ];
    // Index summaries once per render. Keep the first matching floor, as Array.find did.
    const summariesByFloor = new Map();
    for (const summary of summaries || []) {
        if (!summariesByFloor.has(summary.floorCode)) {
            summariesByFloor.set(summary.floorCode, summary);
        }
    }
    grid.innerHTML = `<thead><tr><th scope="col">Action Item</th>${floors.map((floor) => `<th scope="col">${floor}</th>`).join("")}</tr></thead><tbody></tbody>`;
    const body = grid.querySelector("tbody");
    rows.forEach((row) => {
        const tr = document.createElement("tr");
        tr.className = row.className;
        tr.innerHTML = `<th scope="row"><a id="${row.id}" href="#${row.section}">${row.name}</a></th>`;
        tr.querySelector("a").addEventListener("click", (event) => {
            event.preventDefault();
            openSection(row.section);
        });
        floors.forEach((floor) => {
            const summary = summariesByFloor.get(floor);
            const count = summary?.[row.count] ?? 0;
            const cell = document.createElement("td");
            cell.textContent = count;
            if (count > 0) {
                cell.className = "has-count";
                const link = document.createElement("a");
                link.href = "#machineListSidebar";
                link.textContent = count;
                link.setAttribute(
                    "aria-label",
                    `${row.name}, ${floor}: ${count}`,
                );
                link.addEventListener("click", (event) => {
                    event.preventDefault();
                    onFloorCellClick(floor, summary[row.machines] || []);
                });
                cell.replaceChildren(link);
            }
            tr.append(cell);
        });
        body.append(tr);
    });
    synchronizeBlinkAnimations(grid);
}

function synchronizeBlinkAnimations(root) {
    // Form1 enables every uc_Test timer after loading. Use one document clock
    // so cards arriving in separate AJAX responses still share the same phase.
    root.getAnimations({ subtree: true }).forEach((animation) => {
        animation.startTime = 0;
    });
}

/** Mirrors LabelMain_Click — shows Listbox_main for the clicked floor */
function onFloorCellClick(floorCode, machines) {
    const sidebar = document.getElementById("machineListSidebar");
    const list = document.getElementById("listboxMain");
    document.getElementById("sidebarTitle").textContent = floorCode;
    list.replaceChildren();
    const go = document.getElementById("machineListGo");
    go.disabled = true;
    let selected = "";
    let selectedButton = null;
    // Construct the sidebar list off-DOM; large machine lists then repaint once.
    const fragment = document.createDocumentFragment();
    machines.forEach((machine) => {
        const li = document.createElement("li");
        const button = document.createElement("button");
        button.type = "button";
        button.className = "summary-machine-item";
        button.textContent = machine;
        button.setAttribute("aria-pressed", "false");
        button.addEventListener("click", () => {
            selected = machine;
            selectedButton?.setAttribute("aria-pressed", "false");
            selectedButton = button;
            button.setAttribute("aria-pressed", "true");
            go.disabled = false;
        });
        button.addEventListener("dblclick", () => {
            button.click();
            go.click();
        });
        li.append(button);
        fragment.append(li);
    });
    list.append(fragment);
    go.onclick = async () => {
        if (!selected) return;
        closeSidebar();
        await applyMonitoringSelection({
            userType: val("cbUser"),
            process: val("cbProcess"),
            floor: floorCode,
            machine: selected,
            dateFrom: val("dt1"),
            dateTo: val("dt2"),
        });
    };
    sidebar.showModal();
}

function closeSidebar() {
    document.getElementById("machineListSidebar").close();
}

// MACHINE CARD RENDERING  (Panel1 / uC_Test in Form1)

async function renderMachineCards(columns, filter, signal) {
    const scroller = document.getElementById("cardScroller");
    const board = document.getElementById("machineBoard");
    const searchId = State.searchId;
    const page = State.page;
    const noResult = document.getElementById("noResultMsg");

    if (signal?.aborted) return;
    if (!columns || columns.length === 0) {
        board.replaceChildren();
        noResult.classList.remove("is-hidden");
        return;
    }
    noResult.classList.add("is-hidden");

    const fragment = document.createDocumentFragment();
    columns.forEach((mc, idx) => {
        const col = document.createElement("div");
        col.className = "machine-col";
        col.dataset.mcno = mc.machineNo;
        col.id = `col_${idx}`;

        col.innerHTML = `
            <div class="mc-col-header">
                <span class="mc-location">${escapeHtml(filter.floor)}</span>
                <span class="mc-no">${escapeHtml(mc.machineNo)}</span>
                <span class="mc-type">${escapeHtml(mc.machineType)}</span>
                <span class="need-action-label" title="Repeat BM groups within seven days without an action card">Need Action</span><span class="need-action-badge"></span>
            </div>
            <div class="bm-card-list" id="cards_${idx}">
            </div>`;
        fragment.append(col);
        renderMachineCard(mc, filter, col.querySelector('.bm-card-list'));
    });
    // Insert completed columns together; request completion order cannot affect the board.
    board.replaceChildren(fragment);
    synchronizeBlinkAnimations(board);
    // Resolve the shared content height (including horizontal overflow) before
    // starting at the legacy bottom position. Ignore superseded searches.
    await new Promise(requestAnimationFrame);
    if (searchId === State.searchId && page === State.page && !signal?.aborted) {
        scroller.scrollTop = scroller.scrollHeight;
        scroller.scrollLeft = 0;
    }
}

function renderMachineCard(data, filter, container) {
    const { bmCards, actionLine, needActionCount } = data;
    const column = container.closest(".machine-col");
    const needActionBadge = column?.querySelector(".need-action-badge");
    if (needActionBadge) {
        needActionBadge.textContent = needActionCount ?? 0;
    }
    const location = column.querySelector(".mc-location");
    if (location && bmCards.length)
        location.textContent = [
            ...new Set(bmCards.map((card) => card.location)),
        ].join(", ");

    if (bmCards.length === 0) {
        container.innerHTML = '<div class="no-bm">No BM records</div>';
        return;
    }

    const fragment = document.createDocumentFragment();
    bmCards.forEach((card, index) => {
        const element = buildBMCard(card, actionLine, filter.userType);
        // LoadProcessCapa: Salmon at the bottom, then SM/DM/DV tiers overlapping its boundary.
        const tier = Math.floor((bmCards.length - 1 - actionLine - index) / 2);
        if (actionLine < bmCards.length && tier >= 0)
            element.classList.add(
                ["band-sm", "band-dm", "band-dv"][Math.min(tier, 2)],
            );
        fragment.append(element);
    });
    container.replaceChildren(fragment);
}

/** Builds one BM card DOM element (mirrors MachineMonitoringCard.ascx HTML) */
function buildBMCard(card, actionLine, userType) {
    const div = document.createElement("div");

    // Priority star class
    const priorityClass = card.priority === "1" ? "priority-star" : "";

    // Legacy right-docked Major Customer Device stars.
    const mcdBadge =
        card.isMCD === "Yes"
            ? `<span class="badge-mcd ${card.mcdNeedsAction ? "needs-mcd-action" : ""}" role="img" aria-label="Major Customer Device${card.mcdNeedsAction ? ", needs action" : ""}" title="Major Customer Device: ${escapeHtml(card.deviceName)}">&#9733;&#9733;&#9733;</span>`
            : "";

    // Action badge
    const statusColor = derivedColor(card.mcStatus);
    const actionColor = (card.actionColor || statusColor).toLowerCase();
    const actionTitle =
        actionColor === "green" && statusColor !== "Green"
            ? `Solved by the legacy seven-day check. Stored status: ${card.mcStatus || "—"}`
            : card.mcStatus;
    const actionBadge =
        card.haveAction === "Yes"
            ? `<span class="badge-action status-${actionColor || "red"}" title="${escapeHtml(actionTitle)}">Action</span>`
            : "";

    const markers = [
        card.actionMarker
            ? '<span class="bm-marker bm-marker-ac">AC</span>'
            : "",
        card.sectionMarker
            ? '<span class="bm-marker bm-marker-sm">SM</span>'
            : "",
        card.departmentMarker
            ? '<span class="bm-marker bm-marker-dm">DM</span>'
            : "",
        card.divisionMarker
            ? '<span class="bm-marker bm-marker-dv">DV</span>'
            : "",
    ].join("");

    div.className =
        `bm-card bm-card-clickable ${priorityClass} ${card.needsAction ? "needs-action" : ""}`.trim();
    const repeatColor = (card.fontColor || "").toLowerCase();
    if (
        ["red", "blue", "black", "green", "darkgreen", "orange"].includes(
            repeatColor,
        )
    ) {
        div.style.setProperty("--repeat-color", repeatColor);
    }
    if (["red", "blue"].includes(repeatColor))
        div.classList.add(`repeat-${repeatColor}`);
    div.title = card.needsAction
        ? "Repeat BM group needs an action card. Open Check And Action Card."
        : "Open Check And Action Card";
    div.tabIndex = 0;
    div.setAttribute("role", "button");
    div.addEventListener("keydown", (event) => {
        if (
            event.target === div &&
            (event.key === "Enter" || event.key === " ")
        ) {
            event.preventDefault();
            div.click();
        }
    });

    div.addEventListener("click", (event) => {
        const command = event.target.closest(".bm-command");
        if (command) {
            event.stopPropagation();
            openBMCommand(command.dataset.command, card, userType);
        } else {
            openActionCard(card, card.machineNo, userType);
        }
    });

    div.innerHTML = `
        <div class="bm-card-markers">${markers}${actionBadge}</div>
        <div class="bm-card-content"><span class="bm-content-text">${escapeHtml(userType === "TE" ? card.priority : card.bmContent)}</span>${mcdBadge}</div>
        <div class="bm-card-case">
            <span class="bm-case-text">${escapeHtml(card.bmCase)}</span>
        </div>
        <div class="bm-card-time">${escapeHtml(card.timeOfRequest)}</div>
        <div class="bm-card-footer">
            <span class="bm-card-actions">
                ${card.ocapCount > 0 ? `<button type="button" class="bm-command" data-command="ocap">O→</button>` : ""}
                <button type="button" class="bm-command" data-command="report">R→</button>
                <button type="button" class="bm-command" data-command="evidence">E→</button>
            </span>
        </div>`;

    return div;
}

function openBMCommand(command, card, userType) {
    const params = {
        process: card.process || val("cbProcess"),
        bmId: String(card.bmId),
        timeOfRequest: card.timeOfRequest || "",
        machineNo: card.machineNo || "",
    };
    if (command === "report")
        Workflows.open(userType === "TE" ? "BmReport" : "PmReport", params);
    if (command === "evidence")
        Workflows.open(
            userType === "TE" ? "TeEvidenceMaking" : "EvidenceMaking",
            params,
        );
    if (command === "ocap")
        Workflows.open("OcapViewer", {
            ...params,
            bmProcess: params.process,
            bmCaseId: card.bmCaseId,
            machineType: card.machineType || "",
            location: card.location || "",
            bmCase: card.bmCase || "",
        });
}
function clearCards() {
    const scroller = document.getElementById("cardScroller");
    document.getElementById("machineBoard").replaceChildren();
    scroller.scrollTop = 0;
    scroller.scrollLeft = 0;
}

// BM CARD CLICK → Action Card modal
// Mirrors: Form_Input.LoadData(bmid, status, mcno, ColorT, HeaderCase)

function openActionCard(card, mcNo, userType) {
    const colorName =
        card.haveAction === "Yes"
            ? card.actionColor || derivedColor(card.mcStatus)
            : card.fontColor && card.fontColor !== "None"
              ? card.fontColor
              : derivedColor(card.mcStatus);
    Workflows.open("ActionCards", {
        bmId: String(card.bmId),
        mcNo,
        userType,
        status: card.haveAction === "Yes" ? "Existing" : "New",
        colorName,
        headerCase: card.bmCase || "",
    });
}

window.refreshMonitoring = () => {
    if (State.lastFilter) doSearch(State.page);
};
window.applyMonitoringSelection = applyMonitoringSelection;
function derivedColor(mcStatus) {
    if (!mcStatus) return "";
    const s = mcStatus.toUpperCase();
    if (s.includes("NO RE-OCC")) return "Green";
    if (s.includes("LOT EVALUATION")) return "LightGreen";
    if (s.includes("STOP (ONGOING")) return "Red";
    if (s.includes("100%")) return "DarkOrange";
    if (s.includes("ONGOING INVEST")) return "DarkGray";
    if (s.includes("INSPECTION RESULT")) return "Violet";
    if (s.includes("QC INSTRUCTION")) return "Aqua";
    return "";
}

// NAVIGATION BUTTONS  (Button5 / btnMCD / btnWithAction)

function openSection(section) {
    const userType = val("cbUser");
    if (!userType) {
        showToast("Please select a User first.");
        return;
    }
    const names = {
        RepeatBM: "AlertBoard",
        MCD: "MachineSummary",
        WithAction: "BmAnalytics",
        RawData: "RawData",
        McdMonitoring: "McdMonitoring",
        BmAnalytics: "BmAnalytics",
    };
    const params = {
        userType,
        userMode: userType === "TE" ? "TE" : "EEE",
        dateFrom: val("dt1"),
        dateTo: val("dt2"),
        process: val("cbProcess"),
        location: val("cbFloor"),
        machine: val("cbMachine"),
    };
    if (
        section === "RawData" &&
        (params.process === "-Select-" || params.location === "-Select-")
    ) {
        showToast("Select a process and location to view Raw Data.");
        return;
    }
    if (section === "WithAction") params.legacyForm = "12";
    if (section === "BmAnalytics") params.legacyForm = "8";
    Workflows.open(names[section], params);
}

// HELPERS

function buildFilter() {
    return {
        userType: val("cbUser"),
        process: val("cbProcess"),
        floor: val("cbFloor"),
        machine: val("cbMachine"),
        dateFrom: toApiDate(document.getElementById("dt1").value),
        dateTo: toApiDate(document.getElementById("dt2").value),
        page: State.page,
    };
}

function val(id) {
    return document.getElementById(id)?.value ?? "";
}

function fillSelect(id, items, defaultSelect) {
    const sel = document.getElementById(id);
    const fragment = document.createDocumentFragment();
    if (!items.length) fragment.append(new Option("-Select-", ""));
    items.forEach((item) => {
        const opt = document.createElement("option");
        opt.value = item;
        opt.textContent = item;
        if (item === defaultSelect) opt.selected = true;
        fragment.append(opt);
    });
    sel.replaceChildren(fragment);
}

function showSpinner(show) {
    document
        .getElementById("loadingSpinner")
        .classList.toggle("is-hidden", !show);
}

function showToast(msg) {
    const t = document.getElementById("toastMsg");
    t.textContent = msg;
    t.classList.remove("is-hidden");
    clearTimeout(State.toastTimer);
    State.toastTimer = setTimeout(() => {
        t.classList.add("is-hidden");
    }, 4000);
}

function toApiDate(isoDate) {
    if (!isoDate) return "";
    const [y, m, day] = isoDate.split("-");
    return `${m}/${day}/${y} 00:00:00`;
}

async function request(url, { signal, body } = {}) {
    try {
        return body === undefined
            ? await Karte.requestJson(url, { signal })
            : await Karte.postJson(url, body, State.antiForgery, { signal });
    } catch (e) {
        if (e.name === "AbortError") return { success: false, message: "" };
        console.error("Monitoring request failed:", e);
        return { success: false, message: String(e) };
    }
}
