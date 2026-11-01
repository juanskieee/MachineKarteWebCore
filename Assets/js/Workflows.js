/* Monitoring owns navigation; the legacy child forms are AJAX-loaded partials. */
window.Workflows = (() => {
    'use strict';
    const initializers = new Map();
    const titles = {
        AlertBoard: 'Summary of Need Action Per Process',
        MachineSummary: 'Summary of Need Action With MCD',
        McdMonitoring: 'Summary of Need Action With MCD',
        BmAnalytics: 'Summary of Action With MCD',
        ActionCards: 'Check And Action Card',
        RawData: 'Raw Data', OcapViewer: 'OCAP',
        PmReport: 'BM Maintenance Record', BmReport: 'BM Maintenance Record',
        EvidenceMaking: 'BM Maintenance Evidence', TeEvidenceMaking: 'BM Maintenance Evidence'
    };
    let active = null;
    let opener = null;

    function register(name, initialize) { initializers.set(name, initialize); }

    function dispose() {
        active?.abort();
        active = null;
    }

    async function open(name, values = {}) {
        if (!Object.hasOwn(titles, name)) return;
        const dialog = document.getElementById('workflowDialog');
        const body = document.getElementById('workflowBody');
        if (!dialog.open) opener = document.activeElement;
        dispose();
        const controller = new AbortController();
        active = controller;
        const { signal } = controller;
        const params = new URLSearchParams(values);
        params.set('dialog', '1');
        dialog.dataset.workflow = name;
        document.getElementById('workflowTitle').textContent = titles[name];
        body.replaceChildren();
        const loading = document.createElement('p');
        loading.className = 'workflow-loading';
        loading.textContent = 'Loading…';
        body.append(loading);
        body.setAttribute('aria-busy', 'true');
        if (!dialog.open) dialog.showModal();
        document.body.classList.add('workflow-open');

        const context = {
            signal,
            fetch: (url, options = {}) => window.fetch(url, { ...options, signal }),
            setInterval(callback, delay) {
                const timer = window.setInterval(callback, delay);
                signal.addEventListener('abort', () => clearInterval(timer), { once: true });
                return timer;
            },
            setTimeout(callback, delay) {
                const timer = window.setTimeout(callback, delay);
                signal.addEventListener('abort', () => clearTimeout(timer), { once: true });
                return timer;
            },
            onRefresh(callback) {
                window.addEventListener('monitoring:refresh', callback, { signal });
            }
        };

        try {
            const response = await context.fetch(`/${name}?${params}`, {
                headers: { 'X-Requested-With': 'XMLHttpRequest' }
            });
            if (!response.ok) throw new Error('Unable to load this window. Close it and try again.');
            const html = await response.text();
            if (signal.aborted) return;
            body.innerHTML = html;
            const root = body.querySelector('[data-workflow]');
            if (!root || !initializers.has(name)) throw new Error('This window could not be initialized. Try again.');
            root.addEventListener('keydown', event => {
                if (!event.defaultPrevented && (event.key === 'Enter' || event.key === ' ') && event.target.matches('[role="button"]:not(button)')) {
                    event.preventDefault();
                    event.target.click();
                }
            });
            await initializers.get(name)(root, Object.fromEntries(params), context);
            if (!signal.aborted) body.setAttribute('aria-busy', 'false');
        } catch (error) {
            if (signal.aborted) return;
            body.replaceChildren();
            const message = document.createElement('p');
            message.className = 'workflow-error';
            message.setAttribute('role', 'alert');
            message.textContent = error.message;
            body.append(message);
            body.setAttribute('aria-busy', 'false');
        }
    }

    function close() { dispose(); document.getElementById('workflowDialog').close(); }

    async function selectMachine(params) {
        close();
        await window.applyMonitoringSelection(params instanceof URLSearchParams
            ? Object.fromEntries(params) : params);
    }

    document.addEventListener('DOMContentLoaded', () => {
        const dialog = document.getElementById('workflowDialog');
        document.getElementById('workflowClose').addEventListener('click', close);
        dialog.addEventListener('close', () => {
            if (dialog.open) return;
            dispose();
            document.body.classList.remove('workflow-open');
            document.getElementById('workflowBody').replaceChildren();
            if (opener?.isConnected) opener.focus();
        });
        // Native dialog supplies Escape handling, focus trapping, and an inert parent.
        const query = new URLSearchParams(location.search);
        const name = query.get('workflow');
        if (name) {
            query.delete('workflow');
            open(name, Object.fromEntries(query));
            history.replaceState(null, '', '/Monitoring');
        }
    });

    return { register, open, close, selectMachine };
})();
