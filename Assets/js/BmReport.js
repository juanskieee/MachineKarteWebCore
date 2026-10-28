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

    function init(cfg) {
        _cfg = cfg || {};
        const form = document.getElementById('frmDownload');
        if (form) {
            form.addEventListener('submit', _onSubmit);
        }
    }

    function _setStatus(msg, isError) {
        const el = document.getElementById('reportStatus');
        if (!el) return;
        el.textContent = msg;
        el.className = 'report-status' + (isError ? ' report-status--error' : ' report-status--info');
    }

    function _onSubmit(e) {
        const btn = document.getElementById('btnDownload');
        if (btn) {
            btn.disabled = true;
            btn.querySelector('.btn-icon').textContent = '⏳';
        }
        _setStatus('Generating report, please wait…', false);

        // Let the normal form POST proceed — the server streams back the file.
        // Re-enable button after a short delay so users can download again.
        setTimeout(() => {
            if (btn) {
                btn.disabled = false;
                btn.querySelector('.btn-icon').textContent = '⬇';
            }
            _setStatus('Report downloaded successfully.', false);
        }, 4000);
    }

    return { init };
})();