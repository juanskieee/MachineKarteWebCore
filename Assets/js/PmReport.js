/** PM Maintenance Record (Form3): keep Generate and Download as separate user actions. */
Workflows.register('PmReport', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"]');
    const button = byId('pmGenerate');
    const statusArea = byId('statusArea');
    const statusSpinner = byId('statusSpinner');
    const status = byId('pmStatus');
    const previewPanel = byId('previewPanel');
    const previewFileName = byId('previewFileName');
    const previewFrame = byId('previewFrame');
    const download = byId('pmDownload');

    context.signal.addEventListener('abort', () => previewFrame.removeAttribute('src'), { once: true });
    button.addEventListener('click', generate);

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

    async function generate() {
        button.disabled = true;
        clearPreview();
        showStatus('', 'info');
        statusSpinner.classList.remove('is-hidden');

        const query = new URLSearchParams({
            process: options.process ?? '',
            bmId: String(options.bmId ?? ''),
            timeOfRequest: options.timeOfRequest ?? '',
            machineNo: options.machineNo ?? '',
            format: byId('outputFormat').value
        });

        try {
            const response = await context.fetch(`/PmReport?handler=Generate&${query}`);
            const result = await response.json();
            if (!response.ok || !result.success || !result.data?.success) {
                throw new Error(result.message || result.data?.message || 'Report generation failed.');
            }
            if (context.signal.aborted) return;

            const { fileName, downloadUrl } = result.data;
            download.href = downloadUrl;
            download.download = fileName;
            download.textContent = 'Download';
            previewFileName.textContent = fileName;

            // Excel stays download-only; the same PDF endpoint also supports inline display.
            if (fileName.toLowerCase().endsWith('.pdf')) {
                const inlineUrl = new URL(downloadUrl, window.location.href);
                inlineUrl.searchParams.set('inline', 'true');
                previewFrame.src = inlineUrl.href;
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
    }
});
