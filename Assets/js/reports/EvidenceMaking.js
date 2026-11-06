/** Generate PM evidence for the selected record, with separate preview and download. */
Workflows.register("EvidenceMaking", (root, options, context) => {
    "use strict";

    const { byId } = context;
    const view = Karte.createReportView(byId, context.signal, {
        buttonId: "btnGenerate",
        statusId: "statusMsg",
        downloadId: "downloadLink",
    });
    const history = Karte.createReportHistory(context, 'EvidenceMaking', options, view);
    const process = options.process || "";
    const bmId = Number(options.bmId);
    byId("recordProcess").textContent = process || "—";
    byId("recordBmId").textContent = options.bmId || "—";
    byId("btnGenerate").addEventListener("click", generate);

    async function generate() {
        if (!process || !Number.isSafeInteger(bmId) || bmId <= 0) {
            view.showStatus("Select a BM record in Monitoring first.", "error");
            return;
        }
        view.setBusy(true);

        try {
            const result = await context.postJson(
                "/EvidenceMaking?handler=Generate",
                {
                    process,
                    bmId,
                    format: byId("outputFormat").value,
                },
            );
            if (!result.success)
                throw new Error(result.message || "Generation failed.");
            view.showPreview(result.data.downloadUrl, result.data.fileName);
            history.refresh();
        } catch (error) {
            if (!context.signal.aborted)
                view.showStatus(error.message || "Generation failed.", "error");
        } finally {
            view.setBusy(false);
        }
    }
});
