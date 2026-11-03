/** TE Evidence (Form10): keep generate and download separate from dialog opening. */
Workflows.register('TeEvidenceMaking', (root, options, context) => {
    const find = id => root.querySelector(`[data-id="${id}"]`);
    const button = find('btnGenerate');
    const status = find('status');
    const download = find('download');
    find('recordProcess').textContent = options.process || '—';
    find('recordBmId').textContent = options.bmId || '—';
    button.addEventListener('click', async () => {
        status.hidden = false;
        if (!options.bmId) {
            status.textContent = 'Select a BM record in Monitoring first.';
            return;
        }
        button.disabled = true;
        download.hidden = true;
        status.className = '';
        status.textContent = 'Generating evidence…';
        try {
            const format = find('outputFormat').value;
            const response = await context.fetch(`/TeEvidenceMaking?handler=Generate&bmId=${encodeURIComponent(options.bmId)}&format=${format}`);
            const result = await response.json();
            if (!response.ok || !result.success) {
                throw new Error(result.message || 'Evidence generation failed.');
            }
            download.href = result.url || `/TeEvidenceMaking?handler=Download&bmId=${encodeURIComponent(options.bmId)}&format=${format}`;
            download.textContent = `Download evidence (.${format})`;
            download.hidden = false;
            status.textContent = 'Evidence ready. Select Download to save it.';
            status.className = 'status-success';
        } catch (error) {
            status.textContent = error.message;
            status.className = 'status-error';
        } finally {
            button.disabled = false;
        }
    });
});
