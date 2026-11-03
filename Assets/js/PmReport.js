/** PM Maintenance Record (Form3): preserve explicit generate and download actions. */
Workflows.register('PmReport', (root, options, context) => {
    const byId = id => root.querySelector('[data-id="' + id + '"], [id="' + id + '"]');
    const fetch = context.fetch;

    const button = byId('pmGenerate');
    const status = byId('pmStatus');
    const download = byId('pmDownload');

    button?.addEventListener('click', generate);

    // Request the report only when the user explicitly selects Generate.
    async function generate() {
        setStatus('Generating report...', 'busy');
        button.disabled = true;
        download.hidden = true;

        const query = new URLSearchParams({
            process: options.process ?? '',
            bmId: String(options.bmId ?? ''),
            timeOfRequest: options.timeOfRequest ?? '',
            machineNo: options.machineNo ?? '',
            format: byId('outputFormat').value
        });

        try {
            const response = await fetch(`/PmReport?handler=Generate&${query.toString()}`);
            const result = await response.json();
            if (!response.ok || !result.success || !result.data?.success) {
                throw new Error(result.message || result.data?.message || 'Report generation failed.');
            }

            download.href = result.data.downloadUrl;
            download.download = result.data.fileName;
            download.hidden = false;
            download.textContent = `Download ${result.data.fileName}`;
            setStatus('Report generated.', 'success');
        } catch (error) {
            setStatus(error.message || 'Report generation failed.', 'error');
        } finally {
            button.disabled = false;
        }
    }

    function setStatus(message, state) {
        status.hidden = false;
        status.textContent = message;
        status.dataset.state = state;
    }
});
