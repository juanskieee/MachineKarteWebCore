/** TE maintenance record (Form9): generate Excel/PDF, then offer a separate download. */
Workflows.register("BmReport", (root, options, context) => {
    "use strict";

    const { byId } = context;
    const form = byId("frmDownload");
    const view = Karte.createReportView(byId, context.signal, {
        buttonId: "btnDownload",
        statusId: "reportStatus",
        downloadId: "reportDownload",
    });
    form.addEventListener("submit", generate);

    async function generate(event) {
        event.preventDefault();
        view.setBusy(true);

        try {
            const response = await context.fetch(form.action, {
                method: "POST",
                body: new FormData(form),
                cache: 'no-store',
            });
            await view.showResponse(response);
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
