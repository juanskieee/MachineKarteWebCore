// Assets/js/RawData.js
// Handles: data fetch, Chart.js stacked-column charts, table rendering
// Mirrors: Form5.vb LoadData(), RandomRGBColor()

'use strict';

// ── Colour palette ────────────────────────────────────────────────────────────
// Deterministic palette for up to 9 frequency bands (2x–10x).
// For BM cases (open-ended count) we fall back to a seeded random generator.
const BAND_COLORS = [
    '#4e79a7', '#f28e2b', '#e15759', '#76b7b2',
    '#59a14f', '#edc948', '#b07aa1', '#ff9da7', '#9c755f'
];

function seededColor(seed) {
    // Simple deterministic hue from string hash
    let h = 0;
    for (let i = 0; i < seed.length; i++) h = (h * 31 + seed.charCodeAt(i)) & 0xffff;
    return `hsl(${h % 360}, 65%, 55%)`;
}

// ── Chart instances (kept for destroy-on-reload) ──────────────────────────────
let chartFreq = null;
let chartBmCase = null;

// ── Entry point ───────────────────────────────────────────────────────────────
document.addEventListener('DOMContentLoaded', () => {
    const process = document.getElementById('hdnProcess')?.value ?? '';
    const location = document.getElementById('hdnLocation')?.value ?? '';
    const machine = document.getElementById('hdnMachine')?.value ?? '';
    const dateFrom = document.getElementById('hdnDateFrom')?.value ?? '';
    const dateTo = document.getElementById('hdnDateTo')?.value ?? '';

    if (process && location && dateFrom && dateTo) {
        loadData(process, location, machine, dateFrom, dateTo);
    }
});

// ── Public API (called by parent page if opened in an iframe / modal) ─────────
window.RawData = {
    load: loadData
};

// ── Data fetch ────────────────────────────────────────────────────────────────
async function loadData(process, location, machine, dateFrom, dateTo) {
    setLoading(true);
    clearError();

    const params = new URLSearchParams({ process, location, machine, dateFrom, dateTo });
    const url = `/RawData?handler=LoadData&${params}`;

    try {
        const resp = await fetch(url, {
            headers: { 'X-Requested-With': 'XMLHttpRequest' }
        });

        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);

        const json = await resp.json();
        if (!json.success) throw new Error(json.message ?? 'Server error');

        renderAll(json.data);
    } catch (err) {
        showError(err.message);
    } finally {
        setLoading(false);
    }
}

// ── Render everything ─────────────────────────────────────────────────────────
function renderAll(data) {
    renderFrequencyGrid(data.frequencyTable);
    renderFrequencyChart(data.frequencyChart);
    renderBmCaseGrid(data.bmCaseTable);
    renderBmCaseChart(data.bmCaseChart);
}

// ── Frequency grid ────────────────────────────────────────────────────────────
function renderFrequencyGrid(rows) {
    const tbody = document.querySelector('#grid-frequency tbody');
    tbody.innerHTML = '';

    if (!rows || rows.length === 0) {
        tbody.innerHTML = '<tr><td colspan="3" class="rd-empty">No data</td></tr>';
        return;
    }

    rows.forEach(r => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${escHtml(r.frequency)}</td>
            <td>${escHtml(r.dateLabel)}</td>
            <td class="rd-num">${r.total}</td>`;
        tbody.appendChild(tr);
    });
}

// ── Frequency chart (Chart1 equivalent) ──────────────────────────────────────
function renderFrequencyChart(series) {
    const ctx = document.getElementById('chart-frequency');
    if (!ctx) return;

    if (chartFreq) { chartFreq.destroy(); chartFreq = null; }

    if (!series || series.length === 0) {
        ctx.parentElement.innerHTML =
            '<p class="rd-empty">No frequency data available.</p>';
        return;
    }

    // Collect ordered x-labels from all series
    const labels = collectLabels(series);

    const datasets = series.map((s, idx) => ({
        label: s.name,
        data: labels.map(lbl => {
            const pt = s.points.find(p => p.label === lbl);
            return pt ? pt.value : 0;
        }),
        backgroundColor: BAND_COLORS[idx % BAND_COLORS.length],
        stack: 'freq'
    }));

    chartFreq = new Chart(ctx, {
        type: 'bar',
        data: { labels, datasets },
        options: chartOptions('BM Count')
    });
}

// ── BM-case grid ─────────────────────────────────────────────────────────────
function renderBmCaseGrid(rows) {
    const tbody = document.querySelector('#grid-bmcase tbody');
    tbody.innerHTML = '';

    if (!rows || rows.length === 0) {
        tbody.innerHTML = '<tr><td colspan="4" class="rd-empty">No data</td></tr>';
        return;
    }

    rows.forEach(r => {
        const tr = document.createElement('tr');
        tr.innerHTML = `
            <td>${escHtml(r.bmCase)}</td>
            <td>${escHtml(r.dateMonth)}</td>
            <td>${r.dateYear}</td>
            <td class="rd-num">${r.bmCount}</td>`;
        tbody.appendChild(tr);
    });
}

// ── BM-case chart (Chart2 equivalent) ────────────────────────────────────────
function renderBmCaseChart(series) {
    const ctx = document.getElementById('chart-bmcase');
    if (!ctx) return;

    if (chartBmCase) { chartBmCase.destroy(); chartBmCase = null; }

    if (!series || series.length === 0) {
        ctx.parentElement.innerHTML =
            '<p class="rd-empty">No BM case data available.</p>';
        return;
    }

    const labels = collectLabels(series);

    const datasets = series.map(s => ({
        label: s.name,
        data: labels.map(lbl => {
            const pt = s.points.find(p => p.label === lbl);
            return pt ? pt.value : 0;
        }),
        backgroundColor: seededColor(s.name),
        stack: 'bmcase'
    }));

    chartBmCase = new Chart(ctx, {
        type: 'bar',
        data: { labels, datasets },
        options: chartOptions('BM Count')
    });
}

// ── Shared Chart.js options ───────────────────────────────────────────────────
function chartOptions(yTitle) {
    return {
        responsive: true,
        maintainAspectRatio: true,
        plugins: {
            legend: { position: 'bottom', labels: { font: { size: 12 } } },
            tooltip: { mode: 'index', intersect: false }
        },
        scales: {
            x: {
                stacked: true,
                ticks: {
                    maxRotation: 45,
                    font: { size: 11, weight: 'bold' }
                }
            },
            y: {
                stacked: true,
                beginAtZero: true,
                title: { display: true, text: yTitle, font: { size: 13, weight: 'bold' } },
                ticks: { font: { size: 12 } }
            }
        }
    };
}

// ── Collect chronologically ordered labels from all series ────────────────────
function collectLabels(series) {
    const set = new Set();
    series.forEach(s => s.points.forEach(p => set.add(p.label)));
    // Labels are already "Mmm YYYY" — sort by parsing
    return [...set].sort((a, b) => parseMonthYear(a) - parseMonthYear(b));
}

function parseMonthYear(label) {
    const months = {
        Jan: 1, Feb: 2, Mar: 3, Apr: 4, May: 5, Jun: 6,
        Jul: 7, Aug: 8, Sep: 9, Oct: 10, Nov: 11, Dec: 12
    };
    const [mon, yr] = label.split(' ');
    return parseInt(yr) * 100 + (months[mon] ?? 0);
}

// ── UI helpers ────────────────────────────────────────────────────────────────
function setLoading(on) {
    const el = document.getElementById('rd-loading');
    if (el) el.style.display = on ? 'flex' : 'none';
}

function showError(msg) {
    const el = document.getElementById('rd-error');
    if (el) { el.textContent = msg; el.style.display = 'block'; }
}

function clearError() {
    const el = document.getElementById('rd-error');
    if (el) { el.textContent = ''; el.style.display = 'none'; }
}

function escHtml(str) {
    return String(str ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;');
}