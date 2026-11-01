// Form5: both monthly stacked charts and their source tables stay together.
Workflows.register('RawData', (root, options, context) => {
    const find = id => root.querySelector(`[data-id="${id}"]`);
    const colors = ['#4e79a7', '#f28e2b', '#e15759', '#76b7b2', '#59a14f', '#edc948', '#b07aa1', '#ff9da7', '#9c755f'];
    const error = find('rd-error');
    const process = find('hdnProcess').value;
    const location = find('hdnLocation').value;
    const dateFrom = find('hdnDateFrom').value;
    const dateTo = find('hdnDateTo').value;
    if (process && location && dateFrom && dateTo) load();
    else {
        error.textContent = 'Select a process, location, and date range in Monitoring first.';
        error.classList.remove('is-hidden');
    }

    async function load() {
        find('rd-loading').classList.remove('is-hidden');
        const query = new URLSearchParams({ process, location, machine: find('hdnMachine').value, dateFrom, dateTo });
        try {
            const response = await context.fetch(`/RawData?handler=LoadData&${query}`);
            const result = await response.json();
            if (!response.ok || !result.success) throw new Error(result.message || 'Unable to load Raw Data.');
            const data = result.data;
            renderTable('grid-frequency', data.frequencyTable, ['frequency', 'dateLabel', 'total']);
            renderTable('grid-bmcase', data.bmCaseTable, ['bmCase', 'dateMonth', 'dateYear', 'bmCount']);
            renderChart('chart-frequency', data.frequencyChart);
            renderChart('chart-bmcase', data.bmCaseChart);
        } catch (err) {
            error.textContent = err.message;
            error.classList.remove('is-hidden');
        } finally { find('rd-loading').classList.add('is-hidden'); }
    }

    function renderTable(id, rows, columns) {
        const body = find(id).querySelector('tbody');
        body.replaceChildren();
        if (!rows?.length) {
            const cell = body.insertRow().insertCell();
            cell.colSpan = columns.length;
            cell.textContent = 'No data';
            return;
        }
        rows.forEach(row => {
            const tr = body.insertRow();
            columns.forEach((column, index) => {
                const cell = tr.insertCell();
                cell.textContent = row[column] ?? '';
                if (index === columns.length - 1) cell.className = 'rd-num';
            });
        });
    }

    // Native SVG keeps the legacy stacked-column charts available without a CDN.
    function renderChart(id, series) {
        const container = find(id);
        container.replaceChildren();
        if (!series?.length) { container.textContent = 'No data available.'; return; }
        const months = { Jan: 1, Feb: 2, Mar: 3, Apr: 4, May: 5, Jun: 6, Jul: 7, Aug: 8, Sep: 9, Oct: 10, Nov: 11, Dec: 12 };
        const order = label => { const [month, year] = label.split(' '); return Number(year) * 100 + (months[month] || 0); };
        const labels = [...new Set(series.flatMap(item => item.points.map(point => point.label)))].sort((a, b) => order(a) - order(b));
        const stacks = labels.map(label => series.map(item => Math.max(0, Number(item.points.find(point => point.label === label)?.value) || 0)));
        const largestStack = Math.max(1, ...stacks.map(stack => stack.reduce((sum, value) => sum + value, 0)));
        const maximum = Math.max(1, Math.ceil(largestStack / 4)) * 4;
        const width = Math.max(920, labels.length * 65);
        const height = 300;
        const left = 50;
        const plotWidth = width - left - 15;
        const plotHeight = 235;
        const svg = element('svg', { viewBox: `0 0 ${width} ${height}`, class: 'rd-chart', role: 'img', 'aria-label': 'Stacked monthly BM counts; values are listed in the adjacent table.' });
        svg.append(element('title', {}, 'Monthly BM count'));
        for (let step = 0; step <= 4; step++) {
            const y = 15 + plotHeight * (1 - step / 4);
            svg.append(element('line', { x1: left, x2: width - 15, y1: y, y2: y, stroke: '#ccc' }));
            svg.append(element('text', { x: left - 8, y: y + 4, 'text-anchor': 'end' }, String(Math.round(maximum * step / 4))));
        }
        const slot = plotWidth / Math.max(1, labels.length);
        const barWidth = Math.min(64, slot * .6);
        stacks.forEach((stack, index) => {
            let total = 0;
            stack.forEach((value, band) => {
                const barHeight = value / maximum * plotHeight;
                const rect = element('rect', {
                    x: left + index * slot + (slot - barWidth) / 2,
                    y: 15 + plotHeight - total / maximum * plotHeight - barHeight,
                    width: barWidth, height: barHeight, fill: colors[band % colors.length]
                });
                rect.append(element('title', {}, `${series[band].name}: ${value} (${labels[index]})`));
                svg.append(rect);
                total += value;
            });
            svg.append(element('text', { x: left + (index + .5) * slot, y: 274, 'text-anchor': 'middle' }, labels[index]));
        });
        container.append(svg);
        const legend = document.createElement('div');
        legend.className = 'rd-chart-legend';
        series.forEach((item, index) => {
            const entry = document.createElement('span');
            const swatch = element('svg', { viewBox: '0 0 12 12', 'aria-hidden': 'true' });
            swatch.append(element('rect', { width: 12, height: 12, fill: colors[index % colors.length] }));
            entry.append(swatch, document.createTextNode(item.name));
            legend.append(entry);
        });
        container.append(legend);
    }

    function element(name, attributes, text) {
        const node = document.createElementNS('http://www.w3.org/2000/svg', name);
        Object.entries(attributes).forEach(([key, value]) => node.setAttribute(key, value));
        if (text !== undefined) node.textContent = text;
        return node;
    }
});
