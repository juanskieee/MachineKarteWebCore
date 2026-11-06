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
            const result = await context.getJson(
                `/TeEvidenceMaking?handler=Generate&${query}`,
            );
            if (!result.success)
                throw new Error(
                    result.message || "Evidence generation failed.",
                );
            const downloadUrl =
                result.url || `/TeEvidenceMaking?handler=Download&${query}`;
            // Form10 writes this exact filename in TeEvidenceMakingService.
            view.showPreview(
                downloadUrl,
                `TE_Evidence_${options.bmId}.${format}`,
            );
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
