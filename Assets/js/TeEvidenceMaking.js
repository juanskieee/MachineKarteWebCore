Workflows.register('TeEvidenceMaking', (root, options, context) => {
    const find = id => root.querySelector(`[data-id="${id}"]`);
    const button = find('btnGenerate');
    const status = find('status');
    const download = find('download');
    find('bmLabel').textContent = `BM ID: ${options.bmId || '—'}`;
    button.addEventListener('click', async () => {
        if (!options.bmId) { status.textContent = 'Select a BM record in Monitoring first.'; return; }
        button.disabled = true;
        download.hidden = true;
        status.className = '';
        status.textContent = 'Generating evidence…';
        try {
            const response = await context.fetch(`/TeEvidenceMaking?handler=Generate&bmId=${encodeURIComponent(options.bmId)}`);
            const result = await response.json();
            if (!response.ok || !result.success) throw new Error(result.message || 'Evidence generation failed.');
            download.href = `/TeEvidenceMaking?handler=Download&bmId=${encodeURIComponent(options.bmId)}`;
            download.hidden = false;
            download.click();
            status.textContent = 'Evidence generated. Open the downloaded workbook to view it.';
            status.className = 'status-success';
        } catch (error) {
            status.textContent = error.message;
            status.className = 'status-error';
        } finally { button.disabled = false; }
    });
    if (options.autoGenerate === '1') button.click();
});
