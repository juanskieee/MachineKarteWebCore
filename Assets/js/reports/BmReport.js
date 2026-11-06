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
            });
            const type = response.headers.get("content-type") || "";
            const isPdf = type.includes("application/pdf");
            const isExcel = type.includes(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            );
            if (!response.ok || !(isPdf || isExcel)) {
                const result = type.includes("application/json")
                    ? await response.json()
                    : null;
                throw new Error(
                    result?.message || "Report generation failed. Try again.",
                );
            }

            const blob = await response.blob();
            if (context.signal.aborted) return;
            const disposition =
                response.headers.get("content-disposition") || "";
            const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
            const plain = /filename="?([^";]+)"?/i.exec(disposition);
            const fileName = utf8
                ? decodeURIComponent(utf8[1])
                : plain?.[1] || `TE_${options.bmId}.${isPdf ? "pdf" : "xlsx"}`;
            view.showPreview(URL.createObjectURL(blob), fileName, isPdf);
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
