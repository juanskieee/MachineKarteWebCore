Workflows.register('BmReport', (root, options, context) => {
    const form = root.querySelector('form');
    const status = root.querySelector('[data-id="reportStatus"]');
    const button = root.querySelector('[data-id="btnDownload"]');
    form.addEventListener('submit', async event => {
        event.preventDefault();
        button.disabled = true;
        status.className = 'report-status';
        status.textContent = 'Generating report…';
        try {
            const response = await context.fetch(form.action, { method: 'POST', body: new FormData(form) });
            const type = response.headers.get('content-type') || '';
            if (!response.ok || !type.includes('application/vnd.openxmlformats-officedocument.spreadsheetml.sheet')) {
                const result = type.includes('application/json') ? await response.json() : null;
                throw new Error(result?.message || 'Report generation failed. Try again.');
            }
            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const disposition = response.headers.get('content-disposition') || '';
            const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
            const plain = /filename="?([^";]+)"?/i.exec(disposition);
            const link = document.createElement('a');
            link.href = url;
            link.download = utf8 ? decodeURIComponent(utf8[1]) : (plain?.[1] || `BM_${options.bmId}.xlsx`);
            link.click();
            window.setTimeout(() => URL.revokeObjectURL(url), 1000);
            status.textContent = 'Report generated. Open the downloaded workbook to view it.';
        } catch (error) {
            status.textContent = error.message;
            status.classList.add('status-error');
        } finally { button.disabled = false; }
    });
});
