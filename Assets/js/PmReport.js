(() => {
    'use strict';

    const config = window.__pmReport ?? {};
    const button = document.getElementById('pmGenerate');
    const status = document.getElementById('pmStatus');
    const download = document.getElementById('pmDownload');

    button?.addEventListener('click', generate);

    if (config.process && Number(config.bmId) > 0) {
        generate();
    }

    async function generate() {
        setStatus('Generating report...', 'busy');
        button.disabled = true;
        download.hidden = true;

        const query = new URLSearchParams({
            process: config.process ?? '',
            bmId: String(config.bmId ?? ''),
            timeOfRequest: config.timeOfRequest ?? '',
            machineNo: config.machineNo ?? ''
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
            download.click();
        } catch (error) {
            setStatus(error.message || 'Report generation failed.', 'error');
        } finally {
            button.disabled = false;
        }
    }

    function setStatus(message, state) {
        status.textContent = message;
        status.dataset.state = state;
    }
})();
