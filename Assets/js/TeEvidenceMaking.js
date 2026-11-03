/** TE Evidence (Form10): preserve explicit generation, download, and legacy filenames. */
Workflows.register('TeEvidenceMaking', (root, options, context) => {
    const byId = id => root.querySelector(`[data-id="${id}"]`);
    const button = byId('btnGenerate');
    const statusArea = byId('statusArea');
    const statusSpinner = byId('statusSpinner');
    const status = byId('status');
    const previewPanel = byId('previewPanel');
    const previewFileName = byId('previewFileName');
    const previewFrame = byId('previewFrame');
    const download = byId('download');

    byId('recordProcess').textContent = options.process || '—';
    byId('recordBmId').textContent = options.bmId || '—';

    function showStatus(message, type) {
        statusArea.classList.remove('is-hidden');
        status.textContent = message;
        status.className = `status-msg status-${type}`;
    }

    function clearPreview() {
        previewPanel.classList.add('is-hidden');
        previewFrame.removeAttribute('src');
        previewFrame.classList.add('is-hidden');
        download.removeAttribute('href');
    }

    context.signal.addEventListener('abort', clearPreview, { once: true });

    button.addEventListener('click', async () => {
        if (!options.bmId) {
            showStatus('Select a BM record in Monitoring first.', 'error');
            return;
        }
        button.disabled = true;
        clearPreview();
        showStatus('', 'info');
        statusSpinner.classList.remove('is-hidden');

        const format = byId('outputFormat').value;
        try {
            const response = await context.fetch(
                `/TeEvidenceMaking?handler=Generate&bmId=${encodeURIComponent(options.bmId)}&format=${format}`);
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || 'Evidence generation failed.');
            }
            if (context.signal.aborted) return;

            const downloadUrl = result.url ||
                `/TeEvidenceMaking?handler=Download&bmId=${encodeURIComponent(options.bmId)}&format=${format}`;
            // Form10 writes this exact filename in TeEvidenceMakingService.
            const fileName = `TE_Evidence_${options.bmId}.${format}`;

            download.href = downloadUrl;
            download.download = fileName;
            download.textContent = 'Download';
            previewFileName.textContent = fileName;
            if (format === 'pdf') {
                const inlineUrl = new URL(downloadUrl, window.location.href);
                inlineUrl.searchParams.set('inline', 'true');
                previewFrame.src = inlineUrl.href;
                previewFrame.classList.remove('is-hidden');
            }
            previewPanel.classList.remove('is-hidden');
            showStatus(`Evidence generated: ${fileName}`, 'success');
        } catch (error) {
            if (!context.signal.aborted) showStatus(error.message || 'Evidence generation failed.', 'error');
        } finally {
            statusSpinner.classList.add('is-hidden');
            button.disabled = false;
        }
    });
});
