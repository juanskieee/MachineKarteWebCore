// UI-only fixtures. This script is injected by Test-Workflows.ps1 and is never served by the app.
(() => {
    const nativeFetch = window.fetch.bind(window);
    const requests = [];
    const errors = [];
    window.addEventListener('error', event => errors.push(event.message));
    window.addEventListener('unhandledrejection', event => errors.push(String(event.reason)));
    const locations = ['LSI1-1F','LSI1-2F','LSI2-1F','LSI2-2F','LSI2-3F','LSI3-1F','LSI3-2F','LSI3-3F','LSI3-4F'];
    const processes = ['DC','DB','WB','MP','PL','ATC','FL','SING','FT','TP','EDS','MEMS'];
    const card = { bmId:123, bmCaseId:'1', process:'DC', machineNo:'TEST-MC-01', machineType:'TEST', location:'LSI1-1F', timeOfRequest:'11/01/2026 08:30:00', bmCase:'TEST: Sensor Fault', bmContent:'Test fixture for dialog navigation.', haveAction:'Yes', fontColor:'Red', ocapCount:1, isMCD:'Yes', actionMarker:'Yes', sectionMarker:'Yes', departmentMarker:'Yes', divisionMarker:'Yes' };
    const query = { userType:'PM', userMode:'EEE', process:'DC', location:'LSI1-1F', machine:'TEST-MC-01', dateFrom:'2026-10-01', dateTo:'2026-11-02', bmId:'123', mcNo:'TEST-MC-01', status:'Existing', colorName:'Red', headerCase:'TEST: Sensor Fault', bmProcess:'DC', bmCaseId:'1', legacyForm:'12' };
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const tick = () => new Promise(resolve => setTimeout(resolve, 30));
    async function waitFor(predicate) {
        for (let count=0; count<100; count++) { if (predicate()) return; await tick(); }
        throw new Error('Timed out waiting for UI state');
    }
    const json = data => new Response(JSON.stringify({ success:true, data }), { headers:{'content-type':'application/json'} });
    window.fetch = async (input, options = {}) => {
        const url = new URL(input, location.href);
        const handler = url.searchParams.get('handler');
        if (!handler) return nativeFetch(input, options);
        requests.push({ path:url.pathname, handler, query:Object.fromEntries(url.searchParams), body:options.body });
        if (window.__failWorkflowData) return new Response(JSON.stringify({ success:false, message:'TEST: Connection unavailable. Try again.' }), { headers:{'content-type':'application/json'} });
        if (url.pathname === '/Monitoring') {
            if (handler === 'ProcessList') return json(['DC','FL']);
            if (handler === 'FloorList') return json(locations);
            if (handler === 'MachineList') return json({ machines:['TEST-MC-01'] });
            if (handler === 'Search') return json({ machineSummaries:[{ machineNo:'TEST-MC-01', machineType:'TEST', bmCount:1, actionLine:4 }], floorSummaries:locations.map((floorCode,index) => ({ floorCode, bmCount:index === 0 ? 1 : 0, mcdCount:0, machineCount:index === 0 ? 1 : 0 })) });
            if (handler === 'MachineCard') return json({ bmCards:[card], actionLine:4, needActionCount:1 });
        }
        if (url.pathname === '/ActionCards') {
            if (handler === 'Load') return json({ problemContent:'TEST: Sensor Fault', inCharge:'TEST', dateCard:'11/01/2026 08:30:00', investigationDone:'Fixture investigation', action:'Fixture action', mcStatus:'STOP (ONGOING INVESTIGATION)' });
            return json('Saved');
        }
        if (url.pathname === '/AlertBoard') return json(handler === 'Drilldown' ? { machines:['TEST-MC-01'], title:'DC / LSI1-1F' } : { cells:{DC_0:{count:1,machines:['TEST-MC-01']}}, locationTotals:{'LSI1-1F':1}, grandTotal:1 });
        if (url.pathname === '/MachineSummary' || url.pathname === '/McdMonitoring') {
            if (handler === 'MachineList') return json({ process:'DC', location:'LSI1-1F', machines:['TEST-MC-01'] });
            return json({ columns:locations, rows:processes.map((process,index)=>({ process, rowTotal:index===0?1:0, cells:Object.fromEntries(locations.map((loc,locIndex)=>[loc,{ count:index===0&&locIndex===0?1:0,key:`${process}_${locIndex+1}` }])) })), locationTotals:{'LSI1-1F':1}, grandTotal:1 });
        }
        if (url.pathname === '/BmAnalytics') return json({ cells:{DC_1:{count:1,mcnos:['TEST-MC-01']}}, locationTotals:{'LSI1-1F':1}, processTotals:{DC:1}, grandTotal:1 });
        if (url.pathname === '/OcapViewer') return json({ rows:[{checkItem:'TEST: Safety interlock',checkingMethod:'Fixture check',treatmentAction:'Fixture action',judgement:'OK',remarks:'Test data only'}] });
        if (url.pathname === '/RawData') return json({ frequencyTable:[{frequency:'2x',dateLabel:'Oct 2026',total:2}],bmCaseTable:[{bmCase:'TEST: Sensor Fault',dateMonth:'October',dateYear:2026,bmCount:2}],frequencyChart:[{name:'2x',points:[{label:'Oct 2026',value:2}]}],bmCaseChart:[{name:'TEST: Sensor Fault',points:[{label:'Oct 2026',value:2}]}] });
        if (url.pathname === '/PmReport') return json({ success:true,downloadUrl:'data:application/octet-stream;base64,VEVTVA==',fileName:'TEST.xlsx' });
        if (url.pathname === '/EvidenceMaking') return json({ fileName:'TEST.xlsx', downloadUrl:'data:application/octet-stream;base64,VEVTVA==' });
        if (url.pathname === '/TeEvidenceMaking') return new Response(JSON.stringify({success:true}), {headers:{'content-type':'application/json'}});
        if (url.pathname === '/BmReport') return new Response('TEST', { headers:{'content-type':'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'} });
        throw new Error(`Unmocked handler ${url}`);
    };
    async function open(name) {
        await Workflows.open(name, query);
        await tick();
        assert(document.querySelector(`#workflowBody [data-workflow="${name}"]`), `${name} did not open`);
        assert(!document.querySelector('#workflowBody .workflow-error'), `${name} initialization failed`);
        assert(location.pathname === '/Monitoring', `${name} navigated away`);
        const ids = [...document.querySelectorAll('[id]')].map(node=>node.id);
        assert(new Set(ids).size === ids.length, `${name} duplicate IDs`);
    }
    window.runWorkflowChecks = async () => {
        await applyMonitoringSelection(query);
        await waitFor(()=>document.querySelector('.bm-card'));
        for (const name of ['AlertBoard','MachineSummary','McdMonitoring','BmAnalytics','ActionCards','RawData','OcapViewer','PmReport','BmReport','EvidenceMaking','TeEvidenceMaking']) { await open(name); Workflows.close(); await tick(); }
        document.getElementById('btnMCD').click(); await waitFor(()=>document.querySelector('[data-workflow="MachineSummary"]'));
        await waitFor(()=>document.querySelector('.ms-active'));
        document.querySelector('.ms-active').click(); await waitFor(()=>document.querySelector('.ms-panel-item'));
        document.querySelector('.ms-panel-item').click(); document.querySelector('[data-id="msPanelGo"]').click();
        await waitFor(()=>!document.getElementById('workflowDialog').open);
        assert(document.getElementById('cbMachine').value==='TEST-MC-01', 'Drilldown did not update parent');
        document.getElementById('btnWithAction').click(); await waitFor(()=>document.querySelector('[data-id="legacyForm"]'));
        assert(document.querySelector('[data-id="legacyForm"]').value==='12', 'With Action did not use Form12');
        Workflows.close(); await tick();
        await open('ActionCards');
        const comment=document.querySelector('[data-comment-idx="1"]'); comment.value='TEST'; comment.dispatchEvent(new Event('blur'));
        assert(document.querySelector('[data-id="tbdate_1"]').value, 'Comment date missing');
        document.querySelector('[data-id="btnSave"]').click();
        await waitFor(()=>!document.getElementById('workflowDialog').open);
        assert(requests.some(request=>request.path==='/ActionCards'&&request.handler==='Save'), 'Save did not call backend');
        const refreshes=requests.filter(request=>request.handler==='Summary').length;
        await open('MachineSummary'); window.dispatchEvent(new Event('monitoring:refresh')); await tick();
        assert(requests.filter(request=>request.handler==='Summary').length===refreshes+2, 'Shared refresh did not fire once');
        Workflows.close(); await tick(); window.dispatchEvent(new Event('monitoring:refresh')); await tick();
        assert(requests.filter(request=>request.handler==='Summary').length===refreshes+2, 'Closed dialog retained refresh listener');
        window.__failWorkflowData=true; await open('OcapViewer');
        assert(document.querySelector('[data-id="ocap-error"]').textContent.includes('Connection unavailable'), 'Missing failure state');
        window.__failWorkflowData=false; Workflows.close(); await tick();
        assert(errors.length===0, `Browser errors: ${errors.join('; ')}`);
        return 'PASS: all 11 dialogs, parent drilldown, Form11/Form12 mappings, save/refresh, timer cleanup, errors, and unique IDs';
    };
    window.prepareWorkflowCapture = async name => {
        if(name==='Monitoring') { Workflows.close(); await tick(); }
        else await open(name);
        const overflow=document.documentElement.scrollWidth > innerWidth + 1;
        assert(!overflow, `${name} overflows the viewport`);
        assert(errors.length===0, errors.join('; '));
        return `${name} ${innerWidth}px: PASS`;
    };
})();
