/** Generate PM evidence for the selected record, with separate preview and download. */
Workflows.register("EvidenceMaking", (root, options, context) => {
    "use strict";

    const { byId } = context;
    const view = Karte.createReportView(byId, context.signal, {
        buttonId: "btnGenerate",
        statusId: "statusMsg",
        downloadId: "downloadLink",
    });
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
            const response = await context.fetch('/EvidenceMaking?handler=Generate', {
                method: 'POST',
                cache: 'no-store',
                headers: {
                    'Content-Type': 'application/json',
                    RequestVerificationToken: root.querySelector('input[name="__RequestVerificationToken"]').value,
                },
                body: JSON.stringify({
                    process,
                    bmId,
                    format: byId("outputFormat").value,
                }),
            });
            await view.showResponse(response);
        } catch (error) {
            if (!context.signal.aborted)
                view.showStatus(error.message || "Generation failed.", "error");
        } finally {
            view.setBusy(false);
        }
    }
});
