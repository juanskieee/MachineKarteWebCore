/** PM maintenance record (Form3): generate first, then preview or download. */
Workflows.register("PmReport", (root, options, context) => {
    "use strict";

    const { byId } = context;
    const view = Karte.createReportView(byId, context.signal, {
        buttonId: "pmGenerate",
        statusId: "pmStatus",
        downloadId: "pmDownload",
    });
    byId("pmGenerate").addEventListener("click", generate);

    async function generate() {
        view.setBusy(true);
        const query = new URLSearchParams({
            process: options.process ?? "",
            bmId: options.bmId ?? "",
            timeOfRequest: options.timeOfRequest ?? "",
            machineNo: options.machineNo ?? "",
            format: byId("outputFormat").value,
        });

        try {
            const result = await context.getJson(
                `/PmReport?handler=Generate&${query}`,
            );
            if (!result.success || !result.data?.success) {
                throw new Error(
                    result.message ||
                        result.data?.message ||
                        "Report generation failed.",
                );
            }
            view.showPreview(result.data.downloadUrl, result.data.fileName);
        } catch (error) {
            if (!context.signal.aborted)
                view.showStatus(
                    error.message || "Report generation failed.",
                    "error",
                );
        } finally {
            view.setBusy(false);
        }
    }
});
