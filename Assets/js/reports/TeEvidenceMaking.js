/** TE evidence (Form10): preserve explicit generation, download, and legacy filenames. */
Workflows.register("TeEvidenceMaking", (root, options, context) => {
    "use strict";

    const { byId } = context;
    const view = Karte.createReportView(byId, context.signal, {
        buttonId: "btnGenerate",
        statusId: "status",
        downloadId: "download",
    });
    byId("recordProcess").textContent = options.process || "—";
    byId("recordBmId").textContent = options.bmId || "—";
    byId("btnGenerate").addEventListener("click", generate);

    async function generate() {
        if (!options.bmId) {
            view.showStatus("Select a BM record in Monitoring first.", "error");
            return;
        }
        view.setBusy(true);
        const format = byId("outputFormat").value;
        const query = new URLSearchParams({ bmId: options.bmId, process: options.process || "", format });

        try {
            const response = await context.fetch(
                `/TeEvidenceMaking?handler=Generate&${query}`,
                { cache: 'no-store' },
            );
            await view.showResponse(response);
        } catch (error) {
            if (!context.signal.aborted)
                view.showStatus(
                    error.message || "Evidence generation failed.",
                    "error",
                );
        } finally {
            view.setBusy(false);
        }
    }
});
