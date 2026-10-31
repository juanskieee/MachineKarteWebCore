/**
 * BmReport.js
 * Client-side logic for the BM Maintenance Record report page.
 *
 * Usage (injected by BmReport.cshtml):
 *   BmReport.init({ bmId: 123, process: 'TE' });
 */
'use strict';

const BmReport = (() => {
    let _cfg = {};
    let _initialized = false;

    function init(cfg) {
        _cfg = cfg || {};
        if (_initialized) return;

        const form = document.getElementById('frmDownload');
        if (!form) return;

        _initialized = true;
        form.addEventListener('submit', _onSubmit);
    }

    function _setStatus(msg, isError) {
        const el = document.getElementById('reportStatus');
        if (!el) return;
        el.textContent = msg;
        el.className = 'report-status' + (isError ? ' report-status--error' : ' report-status--info');
    }

    function _setBusy(isBusy) {
        const btn = document.getElementById('btnDownload');
        if (!btn) return;
        btn.disabled = isBusy;
        const icon = btn.querySelector('.btn-icon');
        if (icon) icon.textContent = isBusy ? '⏳' : '⬇';
    }

    function _fileName(disposition) {
        if (!disposition) return 'BM_Report.xlsx';

        const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(disposition);
        if (utf8) {
            try { return decodeURIComponent(utf8[1]); } catch { }
        }

        const plain = /filename="?([^";]+)"?/i.exec(disposition);
        return plain ? plain[1] : 'BM_Report.xlsx';
    }

    async function _onSubmit(e) {
        e.preventDefault();
        const form = e.currentTarget;

        _setBusy(true);
        _setStatus('Generating report, please wait…', false);

        try {
            const response = await fetch(form.action, {
                method: 'POST',
                body: new FormData(form),
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });

            const contentType = response.headers.get('content-type') || '';
            if (!response.ok || contentType.includes('application/json')) {
                let message = 'Report generation failed.';
                try {
                    const payload = await response.json();
                    if (payload && payload.message) message = payload.message;
                } catch { }
                throw new Error(message);
            }

            const blob = await response.blob();
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = _fileName(response.headers.get('content-disposition'));
            document.body.appendChild(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);

            _setStatus('Report downloaded successfully.', false);
        } catch (err) {
            _setStatus(err && err.message ? err.message : 'Report generation failed.', true);
        } finally {
            _setBusy(false);
        }
    }

    function _autoInit() {
        const cfg = document.getElementById('bmReportConfig');
        if (!cfg) return;
        init({
            bmId: cfg.dataset.bmid,
            process: cfg.dataset.process
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', _autoInit);
    } else {
        _autoInit();
    }

    return { init };
})();
