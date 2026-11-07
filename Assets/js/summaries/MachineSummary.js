/** Render need-action counts and machine drilldowns using the legacy Form11 mapping. */

Workflows.register("MachineSummary", (root, options, context) => {
    "use strict";

    // Retain the Form11 designer cells and their legacy process/location mapping.
    const { byId } = context;
    const { escapeHtml } = Karte;

    const elSearch = byId("msSearch");
    const { dateFrom, dateTo, userMode } = options;
    const elStatus = byId("summaryError");
    const loading = byId("summaryLoading");
    const layout = JSON.parse(byId("msGrid").dataset.layout);
    let gridMode = null;
    const elHead = byId("msGridHead");
    const elBody = byId("msGridBody");
    const elFoot = byId("msGridFoot");
    const elPanel = byId("msPanel");
    const elOverlay = byId("msOverlay");
    const elTitle = byId("msPanelTitle");
    const elList = byId("msPanelList");
    const elGo = byId("msPanelGo");
    const elClose = byId("msPanelClose");

    let lastFilter = { dateFrom: "", dateTo: "", userMode: "EEE" };
    let currentCell = null;
    let loadSeq = 0;
    let cellSeq = 0;
    let summarySnapshot = null;
    const cellRequests = new Map();

    elSearch.addEventListener("click", () => search(false));
    elBody.addEventListener("click", onCellClick);
    elClose.addEventListener("click", closePanel);
    elOverlay.addEventListener("click", closePanel);
    context.dismissOnOutside(elPanel, closePanel);
    elGo.addEventListener("click", goToMonitoring);
    root.addEventListener("keydown", (e) => {
        if (e.key === "Escape" && isPanelOpen()) closePanel();
    });

    search(false);

    async function search(background) {
        if (elSearch.disabled) return;

        if (!dateFrom || !dateTo) {
            if (!background) setStatus("Please select a date range.", true);
            return;
        }

        const seq = ++loadSeq;
        summarySnapshot = null;
        cellRequests.clear();
        lastFilter = { dateFrom, dateTo, userMode };

        closePanel();
        renderLayout(userMode);
        renderGrid(null);
        setStatus("");
        setLoading(true);

        try {
            const params = new URLSearchParams({
                handler: "Summary",
                dateFrom,
                dateTo,
                userMode,
            });

            const json = await context.getJson(`/MachineSummary?${params}`, {
                cache: "no-store",
            });

            if (seq !== loadSeq) return;
            if (!json.success) {
                setStatus(json.message || "Server error", true);
                return;
            }

            if (context.signal.aborted) return;
            const snapshot = {
                rows: new Map(json.data.rows.map((row) => [row.process, row])),
                expiresAt: Date.now() + 60000,
            };
            summarySnapshot = snapshot;
            context.setTimeout(() => {
                if (summarySnapshot === snapshot) summarySnapshot = null;
            }, 60000);
            renderGrid(json.data);
        } catch (err) {
            if (seq === loadSeq && !context.signal.aborted)
                setStatus(`Error: ${err.message}`, true);
        } finally {
            if (seq === loadSeq) {
                setLoading(false);
            }
        }
    }

    function renderLayout(mode) {
        if (gridMode === mode) return;
        gridMode = mode;
        elHead.innerHTML =
            '<tr><th class="ms-th-process" scope="col">Process</th>' +
            layout.columns
                .map(
                    (column) =>
                        `<th class="ms-th-loc" scope="col">${escapeHtml(column)}</th>`,
                )
                .join("") +
            '<th class="ms-th-total" scope="col">Total</th></tr>';
        elBody.innerHTML = layout.modes[mode]
            .map(
                (row) =>
                    `<tr data-process="${escapeHtml(row.process)}"><td class="ms-td-process">${escapeHtml(row.process)}</td>` +
                    layout.columns
                        .map(
                            (column) =>
                                `<td class="ms-cell" data-location="${escapeHtml(column)}" data-applicable="${row.locations.includes(column)}" data-queried="${row.queryLocations.includes(column)}"></td>`,
                        )
                        .join("") +
                    '<td class="ms-td-rowtotal"></td></tr>',
            )
            .join("");
        elFoot.innerHTML =
            '<tr><td class="ms-tf-label">Total</td>' +
            layout.columns
                .map(
                    (column) =>
                        `<td class="ms-tf-cell" data-location="${escapeHtml(column)}"></td>`,
                )
                .join("") +
            '<td class="ms-tf-grand"></td></tr>';
    }

    function renderGrid(data) {
        const rows = new Map(
            (data?.rows || []).map((row) => [row.process, row]),
        );
        elBody.querySelectorAll("tr").forEach((tr) => {
            const process = tr.dataset.process;
            const row = rows.get(process);
            tr.querySelectorAll("[data-location]").forEach((td) => {
                const location = td.dataset.location;
                const cell = row?.cells?.[location];
                const unavailable = td.dataset.applicable === "false";
                const count = data
                    ? td.dataset.queried === "false"
                        ? 0
                        : cell?.count || 0
                    : "—";
                const active = !unavailable && count > 0;
                td.textContent = unavailable ? "" : count;
                td.classList.toggle("ms-na", unavailable);
                td.classList.toggle("ms-active", active);
                td.tabIndex = active ? 0 : -1;
                td.setAttribute("role", "button");
                td.setAttribute("aria-disabled", String(!active));
                td.setAttribute(
                    "aria-label",
                    `${process} / ${location}: ${unavailable ? "Not applicable" : count}`,
                );
            });
            tr.querySelector(".ms-td-rowtotal").textContent = data
                ? tr.querySelector('[data-queried="true"]')
                    ? row?.rowTotal || 0
                    : 0
                : "—";
        });
        elFoot.querySelectorAll("[data-location]").forEach((td) => {
            td.textContent = data
                ? data.locationTotals?.[td.dataset.location] || 0
                : "—";
        });
        elFoot.querySelector(".ms-tf-grand").textContent = data
            ? data.grandTotal || 0
            : "—";
    }

    async function onCellClick(e) {
        if (root.getAttribute("aria-busy") === "true") return;
        const td = e.target.closest(".ms-active");
        if (!td) return;
        const process = td.closest("tr").dataset.process;
        const location = td.dataset.location;
        if (!process || !location) return;

        const seq = ++cellSeq;
        currentCell = {
            process,
            location,
            userMode: lastFilter.userMode,
        };

        elTitle.textContent = `${process} ( ${location} ) — loading…`;
        elList.innerHTML =
            '<li class="summary-panel-message"><span class="spinner" aria-hidden="true"></span>Loading machines…</li>';
        elGo.disabled = true;
        elGo.dataset.machine = "";
        openPanel();

        try {
            const params = new URLSearchParams({
                handler: "MachineList",
                process,
                location,
                dateFrom: lastFilter.dateFrom,
                dateTo: lastFilter.dateTo,
                userMode: lastFilter.userMode,
            });

            const cachedCell =
                summarySnapshot && Date.now() < summarySnapshot.expiresAt
                    ? summarySnapshot.rows.get(process)?.cells?.[location]
                    : null;
            const url = `/MachineSummary?${params}`;
            const json = Array.isArray(cachedCell?.machines)
                ? {
                      success: true,
                      data: {
                          process,
                          location,
                          machines: cachedCell.machines,
                      },
                  }
                : await requestCell(url);

            if (seq !== cellSeq) return;

            if (!json.success) {
                elTitle.textContent = `${process} ( ${location} )`;
                elList.innerHTML = `<li class="summary-panel-message">${escapeHtml(json.message || "Unable to load machines.")}</li>`;
                return;
            }

            const data = json.data || {};
            currentCell.process = data.process || process;
            currentCell.location = data.location || location;
            elTitle.textContent = `${currentCell.process} ( ${currentCell.location} )`;

            const fragment = document.createDocumentFragment();
            const machines = data.machines || [];
            if (machines.length === 0) {
                elList.innerHTML =
                    '<li class="summary-panel-message">No machines.</li>';
                return;
            }

            machines.forEach((machine) => {
                const li = document.createElement("li");
                li.className = "ms-panel-item summary-machine-item";
                li.setAttribute("aria-pressed", "false");
                li.tabIndex = 0;
                li.setAttribute("role", "button");
                li.textContent = machine;
                li.dataset.machine = machine;
                li.addEventListener("click", () => selectMachine(li));
                li.addEventListener("dblclick", goToMonitoring);
                fragment.append(li);
            });
            elList.replaceChildren(fragment);
        } catch (err) {
            if (seq === cellSeq && !context.signal.aborted) {
                elTitle.textContent = `${process} ( ${location} )`;
                elList.innerHTML = `<li class="summary-panel-message">${escapeHtml(err.message)}</li>`;
            }
        }
    }

    function requestCell(url) {
        if (!cellRequests.has(url)) {
            const request = context
                .getJson(url, { cache: "no-store" })
                .finally(() => {
                    if (cellRequests.get(url) === request)
                        cellRequests.delete(url);
                });
            cellRequests.set(url, request);
        }
        return cellRequests.get(url);
    }

    function openPanel() {
        elPanel.classList.add("ms-panel-open");
        elPanel.setAttribute("aria-hidden", "false");
        elOverlay.classList.add("ms-overlay-show");
        elOverlay.setAttribute("aria-hidden", "false");
    }

    function closePanel() {
        elPanel.classList.remove("ms-panel-open");
        elPanel.setAttribute("aria-hidden", "true");
        elOverlay.classList.remove("ms-overlay-show");
        elOverlay.setAttribute("aria-hidden", "true");
        cellSeq++;
        currentCell = null;
    }

    function isPanelOpen() {
        return elPanel.classList.contains("ms-panel-open");
    }

    function selectMachine(li) {
        const previous = elList.querySelector(".ms-selected");
        previous?.classList.remove("ms-selected");
        previous?.setAttribute("aria-pressed", "false");

        li.classList.add("ms-selected");
        li.setAttribute("aria-pressed", "true");
        elGo.disabled = false;
        elGo.dataset.machine = li.dataset.machine;
    }

    function goToMonitoring() {
        const machine = elGo.dataset.machine;
        if (!machine || !currentCell) return;

        const params = new URLSearchParams({
            machine,
            process: currentCell.process,
            floor: currentCell.location,
            userType:
                currentCell.userMode === "TE"
                    ? "TE"
                    : options.userType && options.userType !== "TE"
                      ? options.userType
                      : "EEE",
            dateFrom: lastFilter.dateFrom,
            dateTo: lastFilter.dateTo,
        });

        Workflows.selectMachine(params);
    }

    // Form11's timer is driven by Form1.lblTimer, not an independent interval.
    // Monitoring publishes the equivalent zero signal for converted pages.
    context.onRefresh(() => search(true));

    function setStatus(message, isError) {
        elStatus.textContent = message || "";
        elStatus.hidden = !message;
        elStatus.classList.toggle("status-error", !!isError);
        elStatus.classList.toggle("status-info", !isError);
    }

    function setLoading(on) {
        elSearch.disabled = on;
        loading.hidden = !on;
        root.setAttribute("aria-busy", String(on));
    }
});
