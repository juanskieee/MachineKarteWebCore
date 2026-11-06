/** OCAP (Form6): load and display read-only checking results for one BM record. */
Workflows.register("OcapViewer", (root, options, context) => {
    "use strict";

    const { byId } = context;
    const { escapeHtml } = Karte;
    const bmId = byId("hdnBmId")?.value ?? "";
    const bmProcess = byId("hdnBmProcess")?.value ?? "";
    const bmCaseId = byId("hdnBmCaseId")?.value ?? "";
    byId("recordBmId").textContent = bmId || "—";
    byId("recordMachine").textContent =
        [options.machineNo, options.machineType].filter(Boolean).join(" / ") ||
        "—";
    byId("recordLocation").textContent = options.location || "—";
    byId("recordRequested").textContent = options.timeOfRequest || "—";
    byId("recordCase").textContent = options.bmCase || bmCaseId || "—";

    if (bmId && bmProcess && bmCaseId) {
        loadOcap(bmId, bmProcess, bmCaseId);
    }

    // Load the current dialog's selected OCAP record.

    async function loadOcap(bmId, bmProcess, bmCaseId) {
        setLoading(true);
        clearError();

        const params = new URLSearchParams({ bmId, bmProcess, bmCaseId });
        const url = `/OcapViewer?handler=LoadOcap&${params}`;

        try {
            const json = await context.getJson(url);
            if (!json.success) throw new Error(json.message ?? "Server error");

            renderTable(json.data.rows);
        } catch (err) {
            if (!context.signal.aborted) showError(err.message);
        } finally {
            setLoading(false);
        }
    }

    function renderTable(rows) {
        const tbody = byId("ocap-tbody");
        tbody.replaceChildren();

        if (!rows?.length) {
            tbody.innerHTML =
                '<tr><td colspan="5" class="ocap-empty">No OCAP data found.</td></tr>';
            return;
        }

        // Build rows off-DOM to avoid repeated table layout during larger OCAP loads.
        const fragment = document.createDocumentFragment();
        rows.forEach((row, index) => {
            const tr = document.createElement("tr");
            tr.className = index % 2 === 0 ? "ocap-row-even" : "ocap-row-odd";
            tr.innerHTML = `
                <td>${escapeHtml(row.checkItem)}</td>
                <td>${escapeHtml(row.checkingMethod)}</td>
                <td>${escapeHtml(row.treatmentAction)}</td>
                <td class="ocap-centre">${escapeHtml(row.judgement)}</td>
                <td>${escapeHtml(row.remarks)}</td>`;
            fragment.append(tr);
        });
        tbody.replaceChildren(fragment);
    }

    function setLoading(on) {
        const el = byId("ocap-loading");
        if (el) el.classList.toggle("is-hidden", !on);
    }

    function showError(msg) {
        const el = byId("ocap-error");
        if (el) {
            el.textContent = msg;
            el.classList.remove("is-hidden");
        }
    }

    function clearError() {
        const el = byId("ocap-error");
        if (el) {
            el.textContent = "";
            el.classList.add("is-hidden");
        }
    }
});
