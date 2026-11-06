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
            showStatus(`File ready: ${fileName}`, "success");
        }

        clearPreview();
        signal.addEventListener("abort", clearPreview, { once: true });
        return { setBusy, showStatus, showPreview };
    }

    function createReportHistory(context, workflow, options, view) {
        const status = context.byId('historyStatus');
        const table = context.byId('historyTable');
        const body = context.byId('historyFiles');
        const button = context.byId('historyRefresh');
        const dateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' });
        let files = [];
        let requestId = 0;

        button.addEventListener('click', refresh);
        body.addEventListener('click', (event) => {
            const preview = event.target.closest('[data-preview]');
            if (!preview) return;
            const file = files[Number(preview.dataset.preview)];
            if (file) view.showPreview(file.downloadUrl, file.fileName);
        });

        async function refresh() {
            const current = ++requestId;
            button.disabled = true;
            status.textContent = 'Loading saved files…';
            status.className = 'status-msg';
            try {
                const query = new URLSearchParams({ handler: 'History', bmId: options.bmId || '', process: options.process || '' });
                const result = await context.getJson(`/${workflow}?${query}`, { cache: 'no-store' });
                if (context.signal.aborted || current !== requestId) return;
                if (!result.success) throw new Error(result.message || 'Saved files could not be loaded. Try Refresh.');
                files = result.data || [];
                body.innerHTML = files.map((file, index) => {
                    const date = new Date(file.generatedAtUtc);
                    return `<tr><td>${escapeHtml(file.fileName)}</td><td><time datetime="${date.toISOString()}">${escapeHtml(dateFormat.format(date))}</time></td>` +
                        `<td><div class="report-history-actions"><a href="${escapeHtml(file.downloadUrl)}" download="${escapeHtml(file.fileName)}" aria-label="Download ${escapeHtml(file.fileName)} from ${escapeHtml(dateFormat.format(date))}">Download</a>` +
                        (file.fileName.toLowerCase().endsWith('.pdf') ? `<button type="button" data-preview="${index}" aria-label="Preview ${escapeHtml(file.fileName)} from ${escapeHtml(dateFormat.format(date))}">Preview</button>` : '') +
                        '</div></td></tr>';
                }).join('');
                table.classList.toggle('is-hidden', files.length === 0);
                status.textContent = files.length ? `${files.length} saved file${files.length === 1 ? '' : 's'}, newest first.` : 'No saved files for this BM card yet. Generate Excel or PDF to add one.';
            } catch (error) {
                if (context.signal.aborted || current !== requestId) return;
                status.textContent = error.message || 'Saved files could not be loaded. Try Refresh.';
                status.className = 'status-msg status-error';
            } finally {
                if (!context.signal.aborted && current === requestId) button.disabled = false;
            }
        }

        refresh();
        return { refresh };
    }

    return {
        createLookup,
        escapeHtml,
        formatDate,
        requestJson,
        postJson,
        createReportView,
        createReportHistory,
    };
})();
