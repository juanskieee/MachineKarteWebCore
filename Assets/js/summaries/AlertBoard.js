/** Render repeat BM counts and machine drilldowns using the legacy Form2 mapping. */

Workflows.register("AlertBoard", (root, options, context) => {
    "use strict";

    // A modal-local snapshot keeps drilldowns consistent for 1 minute.
    const { byId } = context;

    const dateFrom = options.dateFrom;
    const dateTo = options.dateTo;
    const user = options.userType === "PM" ? "" : options.userType;
    const btnSearch = byId("btnSearch");
    const loading = byId("summaryLoading");
    const error = byId("summaryError");
    const panelDrill = byId("panelDrilldown");
    const drillTitle = byId("drilldownTitle");
    const drillList = byId("drilldownList");
    const btnClose = byId("btnDrillClose");
    const btnGo = byId("btnDrillGo");
    context.dismissOnOutside(panelDrill, () => btnClose.click());

    // State
    let selectedMachine = null;
    let selectedCell = null; // e.g. "DC_0"
    let drillSeq = 0;
    let boardSnapshot = null;
    const drillRequests = new Map();

    btnSearch.addEventListener("click", loadBoard);

    async function loadBoard() {
        if (btnSearch.disabled) return;
        boardSnapshot = null;
        drillRequests.clear();
        setLoading(true);
        error.textContent = "";
        error.hidden = true;
        resetBoard();

        const body = {
            dateFrom: fmtForServer(dateFrom),
            dateTo: fmtForServer(dateTo),
            user,
        };

        try {
            const json = await context.postJson(
                "/AlertBoard?handler=LoadBoard",
                body,
            );
            if (!json.success) throw new Error(json.message);
            if (context.signal.aborted) return;
            const snapshot = { data: json.data, expiresAt: Date.now() + 60000 };
            boardSnapshot = snapshot;
            context.setTimeout(() => {
                if (boardSnapshot === snapshot) boardSnapshot = null;
            }, 60000);
            renderBoard(json.data);
        } catch (err) {
            if (context.signal.aborted) return;
            error.textContent = "Error loading board: " + err.message;
            error.hidden = false;
        } finally {
            setLoading(false);
        }
    }

    function fmtForServer(dtLocal) {
        // Convert "YYYY-MM-DDTHH:mm" → "MM/dd/yyyy HH:mm:ss"
        if (!dtLocal) return "";
        const d = new Date(
            dtLocal.length === 10 ? dtLocal + "T00:00" : dtLocal,
        );
        const pad = (n) => String(n).padStart(2, "0");
        return `${pad(d.getMonth() + 1)}/${pad(d.getDate())}/${d.getFullYear()} ${pad(d.getHours())}:${pad(d.getMinutes())}:00`;
    }

    function resetBoard() {
        root.querySelectorAll(".ab-cell-active").forEach((el) => {
            el.textContent = "—";
            el.tabIndex = -1;
            el.setAttribute("aria-disabled", "true");
            el.classList.remove("ab-alert");
        });
        root.querySelectorAll(
            '[data-id^="ptotal_"], [data-id^="ltotal_"], [data-id="grandTotal"]',
        ).forEach((el) => {
            el.textContent = "—";
        });
        closeDrilldown();
    }

    function renderBoard(data) {
        // data.cells: { "DC_0": { count, machines }, … }
        // data.locationTotals: { "LSI1-1F": n, … }
        // data.grandTotal: n

        const cells = data.cells || {};
        const processTotals = new Map();
        root.querySelectorAll(
            '.ab-cell-active, [data-id^="ptotal_"], [data-id^="ltotal_"], [data-id="grandTotal"]',
        ).forEach((el) => {
            el.textContent = "0";
        });

        for (const [key, cell] of Object.entries(cells)) {
            const process = key.slice(0, key.lastIndexOf("_"));
            processTotals.set(
                process,
                (processTotals.get(process) || 0) + cell.count,
            );
            const el = byId("cell_" + key);
            if (!el) continue;
            el.textContent = cell.count;
            el.tabIndex = cell.count > 0 ? 0 : -1;
            el.setAttribute("aria-disabled", String(cell.count <= 0));
            el.setAttribute("role", "button");
            if (cell.count > 0) el.classList.add("ab-alert");
        }

        // Location totals
        const locTotals = data.locationTotals || {};
        for (const [loc, n] of Object.entries(locTotals)) {
            const el = byId("ltotal_" + loc);
            if (el) el.textContent = n;
        }

        // Process row totals (sum across all cells for that process)
        for (const row of root.querySelectorAll("tbody tr[data-process]")) {
            const proc = row.dataset.process;
            const el = byId("ptotal_" + proc);
            if (el) el.textContent = processTotals.get(proc) || 0;
        }

        const grand = byId("grandTotal");
        if (grand) grand.textContent = data.grandTotal ?? 0;
    }

    root.addEventListener("click", async function (e) {
        if (btnSearch.disabled) return;
        const cell = e.target.closest(".ab-cell-active");
        if (!cell) return;
        const count = parseInt(cell.textContent, 10);
        if (!count || count === 0) return;

        selectedCell = cell.dataset.cell;
        await openDrilldown(selectedCell);
    });

    async function openDrilldown(cellKey) {
        const seq = ++drillSeq;
        selectedMachine = null;
        btnGo.disabled = true;
        const [process, location] = resolveProcessLocation(cellKey);
        drillTitle.textContent = `${process} ( ${location} )`;
        drillList.innerHTML =
            '<li class="summary-panel-message"><span class="spinner" aria-hidden="true"></span>Loading machines…</li>';
        panelDrill.classList.remove("is-hidden");
        try {
            const cachedCell =
                boardSnapshot && Date.now() < boardSnapshot.expiresAt
                    ? boardSnapshot.data.cells?.[cellKey]
                    : null;
            const json = Array.isArray(cachedCell?.machines)
                ? {
                      success: true,
                      data: {
                          machines: cachedCell.machines,
                          title: `${process} ( ${location} )`,
                      },
                  }
                : await requestDrilldown(cellKey);
            if (!json.success) throw new Error(json.message);

            if (seq !== drillSeq) return;
            const { machines, title } = json.data;
            drillTitle.textContent = title;
            const fragment = document.createDocumentFragment();
            selectedMachine = null;

            machines.forEach((m) => {
                const li = document.createElement("li");
                li.textContent = m;
                li.classList.add("ab-machine-item", "summary-machine-item");
                li.setAttribute("aria-pressed", "false");
                li.tabIndex = 0;
                li.setAttribute("role", "button");
                li.addEventListener("click", () => {
                    const previous = drillList.querySelector(".selected");
                    previous?.classList.remove("selected");
                    previous?.setAttribute("aria-pressed", "false");
                    li.classList.add("selected");
                    li.setAttribute("aria-pressed", "true");
                    selectedMachine = m;
                    btnGo.disabled = false;
                });
                fragment.append(li);
            });
            drillList.replaceChildren(fragment);

            if (!machines.length)
                drillList.innerHTML =
                    '<li class="summary-panel-message">No machines.</li>';
        } catch (err) {
            if (seq !== drillSeq || context.signal.aborted) return;
            drillList.textContent = "Unable to load machines: " + err.message;
        }
    }

    function requestDrilldown(cellKey) {
        const query = new URLSearchParams({
            cellKey,
            dateFrom: fmtForServer(dateFrom),
            dateTo: fmtForServer(dateTo),
            user,
        });
        const url = "/AlertBoard?handler=Drilldown&" + query;
        if (!drillRequests.has(url)) {
            const request = context
                .getJson(url, { cache: "no-store" })
                .finally(() => {
                    if (drillRequests.get(url) === request)
                        drillRequests.delete(url);
                });
            drillRequests.set(url, request);
        }
        return drillRequests.get(url);
    }

    btnClose.addEventListener("click", closeDrilldown);

    btnGo.addEventListener("click", () => {
        if (!selectedMachine) return;
        // Navigate to Monitoring with the machine pre-selected
        // Monitoring.js reads ?machine=<value> on load (mirrors VB Form1 navigation)
        const [proc, locKey] = resolveProcessLocation(selectedCell);
        const params = new URLSearchParams({
            machine: selectedMachine,
            process: proc,
            location: locKey,
            userType: user || "PM",
            dateFrom,
            dateTo,
        });
        Workflows.selectMachine(params);
        closeDrilldown();
    });

    function closeDrilldown() {
        drillSeq++;
        btnGo.disabled = true;
        panelDrill.classList.add("is-hidden");
        drillList.replaceChildren();
        selectedMachine = null;
    }

    function resolveProcessLocation(key) {
        const cell = byId("cell_" + key);
        return [cell.dataset.process, cell.dataset.location];
    }

    function setLoading(active) {
        btnSearch.disabled = active;
        loading.hidden = !active;
        root.setAttribute("aria-busy", String(active));
    }

    // Monitoring raises monitoring:refresh on the legacy 600-second cycle;
    // the workflow context removes this listener when the dialog closes.

    context.onRefresh(loadBoard);

    // Initial load on page show
    loadBoard();
});
