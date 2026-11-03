/** TE Maintenance Record (Form9): generate Excel/PDF, then offer a separate download. */
Workflows.register('BmReport', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"]');
    const form = byId('frmDownload');
    const button = byId('btnDownload');
    const statusArea = byId('statusArea');
    const statusSpinner = byId('statusSpinner');
    const status = byId('reportStatus');
    const previewPanel = byId('previewPanel');
    const previewFileName = byId('previewFileName');
    const previewFrame = byId('previewFrame');
    const download = byId('reportDownload');
    let downloadUrl = null;

    function clearPreview() {
        previewPanel.classList.add('is-hidden');
        previewFrame.removeAttribute('src');
        previewFrame.classList.add('is-hidden');
        download.removeAttribute('href');
        if (downloadUrl) URL.revokeObjectURL(downloadUrl);
        downloadUrl = null;
    }

    function showStatus(message, type) {
        statusArea.classList.remove('is-hidden');
        status.textContent = message;
        status.className = `status-msg status-${type}`;
    }

    // Revoke object URLs whenever the modal closes or the report is regenerated.
    context.signal.addEventListener('abort', clearPreview, { once: true });

    form.addEventListener('submit', async event => {
        event.preventDefault();
        button.disabled = true;
        clearPreview();
        showStatus('', 'info');
        statusSpinner.classList.remove('is-hidden');

        try {
            const response = await context.fetch(form.action, { method: 'POST', body: new FormData(form) });
            const type = response.headers.get('content-type') || '';
            const isPdf = type.includes('application/pdf');
            const isExcel = type.includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
            if (!response.ok || !(isPdf || isExcel)) {
                const result = type.includes('application/json') ? await response.json() : null;
                throw new Error(result?.message || 'Report generation failed. Try again.');
            }

            const blob = await response.blob();
            if (context.signal.aborted) return;
            downloadUrl = URL.createObjectURL(blob);

            const disposition = response.headers.get('content-disposition') || '';
            const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
            const plain = /filename="?([^";]+)"?/i.exec(disposition);
            const fileName = utf8
                ? decodeURIComponent(utf8[1])
                : (plain?.[1] || `TE_${options.bmId}.${isPdf ? 'pdf' : 'xlsx'}`);

            download.href = downloadUrl;
            download.download = fileName;
            download.textContent = 'Download';
            previewFileName.textContent = fileName;
            if (isPdf) {
                previewFrame.src = downloadUrl;
                previewFrame.classList.remove('is-hidden');
            }
            previewPanel.classList.remove('is-hidden');
            showStatus(`Evidence generated: ${fileName}`, 'success');
        } catch (error) {
            if (!context.signal.aborted) showStatus(error.message || 'Report generation failed.', 'error');
        } finally {
            statusSpinner.classList.add('is-hidden');
            button.disabled = false;
        }
    });
});
