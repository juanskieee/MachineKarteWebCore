/** Shared DOM, JSON request, and report preview helpers for the E-Karte workflows. */
window.Karte = (() => {
    "use strict";

    function createLookup(root) {
        const elements = new Map();
        return (id) => {
            let element = elements.get(id);
            if (!element || !root.contains(element)) {
                const escaped = CSS.escape(id);
                element = root.querySelector(
                    `[data-id="${escaped}"], [id="${escaped}"]`,
                );
                if (element) elements.set(id, element);
            }
            return element;
        };
    }

    function escapeHtml(value) {
        return String(value ?? "")
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;")
            .replace(/"/g, "&quot;")
            .replace(/'/g, "&#39;");
    }

    function formatDate(date) {
        return [
            date.getFullYear(),
            String(date.getMonth() + 1).padStart(2, "0"),
            String(date.getDate()).padStart(2, "0"),
        ].join("-");
    }

    async function requestJson(url, options = {}) {
        const response = await window.fetch(url, {
            ...options,
            headers: {
                "X-Requested-With": "XMLHttpRequest",
                ...options.headers,
            },
        });
        const result = await response.json();
        // Also guard responses already being decoded when a workflow is disposed.
        if (options.signal?.aborted)
            throw new DOMException("The request was aborted.", "AbortError");
        if (!response.ok)
            throw new Error(result?.message || `HTTP ${response.status}`);
        return result;
    }

    function postJson(url, body, token, options = {}) {
        return requestJson(url, {
            ...options,
            method: "POST",
            headers: {
                ...options.headers,
                "Content-Type": "application/json",
                RequestVerificationToken: token,
            },
            body: JSON.stringify(body),
        });
    }

    function createReportView(
        byId,
        signal,
        { buttonId, statusId, downloadId },
    ) {
        const button = byId(buttonId);
        const statusArea = byId("statusArea");
        const spinner = byId("statusSpinner");
        const status = byId(statusId);
        const panel = byId("previewPanel");
        const fileNameLabel = byId("previewFileName");
        const frame = byId("previewFrame");
        const download = byId(downloadId);
        let objectUrl = null;

        function clearPreview() {
            panel.classList.add("is-hidden");
            frame.removeAttribute("src");
            frame.classList.add("is-hidden");
            download.removeAttribute("href");
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }

        function showStatus(message, type = "info") {
            statusArea.classList.remove("is-hidden");
            status.textContent = message;
            status.className = `status-msg status-${type}`;
        }

        function setBusy(busy) {
            button.disabled = busy;
            spinner.classList.toggle("is-hidden", !busy);
            if (busy) {
                clearPreview();
                showStatus("");
            }
        }

        function showPreview(url, fileName, isPdf) {
            clearPreview();
            objectUrl = url;
            download.href = url;
            download.download = fileName;
            download.textContent = "Download";
            fileNameLabel.textContent = fileName;
            if (isPdf) {
                frame.src = url;
                frame.classList.remove("is-hidden");
            }
            panel.classList.remove("is-hidden");
            showStatus(`File ready: ${fileName}`, "success");
        }

        clearPreview();
        signal.addEventListener("abort", clearPreview, { once: true });
        async function showResponse(response) {
            const type = response.headers.get('content-type') || '';
            const isPdf = type.includes('application/pdf');
            const isExcel = type.includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
            if (!response.ok || !(isPdf || isExcel)) {
                const result = type.includes('application/json') ? await response.json() : null;
                throw new Error(result?.message || 'Generation failed. Try again.');
            }
            // Chromium's streamed Blob can fail after HTTP 200 when temporary disk space is low.
            // ponytail: hold report bytes in memory; use native streaming if reports outgrow browser memory.
            const bytes = await response.arrayBuffer();
            if (signal.aborted) return;
            const blob = new Blob([bytes], { type });
            const disposition = response.headers.get('content-disposition') || '';
            const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
            const plain = /filename="?([^";]+)"?/i.exec(disposition);
            const fileName = utf8 ? decodeURIComponent(utf8[1]) : plain?.[1] || `report.${isPdf ? 'pdf' : 'xlsx'}`;
            showPreview(URL.createObjectURL(blob), fileName, isPdf);
        }
        return { setBusy, showStatus, showResponse };
    }

    return {
        createLookup,
        escapeHtml,
        formatDate,
        requestJson,
        postJson,
        createReportView,
    };
})();
