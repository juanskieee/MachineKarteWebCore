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

        function showPreview(
            url,
            fileName,
            isPdf = fileName.toLowerCase().endsWith(".pdf"),
        ) {
            clearPreview();
            if (url.startsWith("blob:")) objectUrl = url;
            download.href = url;
            download.download = fileName;
            download.textContent = "Download";
            fileNameLabel.textContent = fileName;
            if (isPdf) {
                const inlineUrl = new URL(url, window.location.href);
                if (!objectUrl) inlineUrl.searchParams.set("inline", "true");
                frame.src = inlineUrl.href;
                frame.classList.remove("is-hidden");
            }
            panel.classList.remove("is-hidden");
            showStatus(`Evidence generated: ${fileName}`, "success");
        }

        clearPreview();
        signal.addEventListener("abort", clearPreview, { once: true });
        return { setBusy, showStatus, showPreview };
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
