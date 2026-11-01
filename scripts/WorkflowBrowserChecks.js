// UI-only fixtures. This script is injected by Test-Workflows.ps1 and is never served by the app.
(() => {
    const nativeFetch = window.fetch.bind(window);
    const requests = [];
    const errors = [];
    let downloadClicks = 0;
    document.addEventListener('click', event => {
        if (event.target.closest('a[download]')) { downloadClicks++; event.preventDefault(); }
    }, true);
    const summaryNames = ['AlertBoard', 'MachineSummary', 'BmAnalytics'];
    let summaryGate = null;
    let releaseSummaryGate = null;
    window.addEventListener('error', event => errors.push(event.message));
    window.addEventListener('unhandledrejection', event => errors.push(String(event.reason)));
    const locations = ['LSI1-1F','LSI1-2F','LSI2-1F','LSI2-2F','LSI2-3F','LSI3-1F','LSI3-2F','LSI3-3F','LSI3-4F'];
    const processes = ['DC','DB','WB','MP','PL','ATC','FL','SING','FT','TP','EDS','MEMS'];
    const card = { bmId:123, bmCaseId:'1', process:'DC', machineNo:'TEST-MC-01', machineType:'TEST', location:'LSI1-1F', timeOfRequest:'11/01/2026 08:30:00', bmCase:'TEST: Sensor Fault', bmContent:'Test fixture for dialog navigation.', haveAction:'Yes', fontColor:'Red', ocapCount:1, isMCD:'Yes', actionMarker:'Yes', sectionMarker:'Yes', departmentMarker:'Yes', divisionMarker:'Yes' };
    const query = { userType:'PM', userMode:'EEE', process:'DC', location:'LSI1-1F', machine:'TEST-MC-01', dateFrom:'2026-10-01', dateTo:'2026-11-02', bmId:'123', mcNo:'TEST-MC-01', status:'Existing', colorName:'Red', headerCase:'TEST: Sensor Fault', bmProcess:'DC', bmCaseId:'1', legacyForm:'12' };
    const assert = (condition, message) => { if (!condition) throw new Error(message); };
    const tick = () => new Promise(resolve => setTimeout(resolve, 30));
    const nextPaint = () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
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
        if (summaryGate && summaryNames.includes(url.pathname.slice(1)) && ['LoadBoard','Summary','LoadGrid'].includes(handler)) await summaryGate;
        if (window.__failWorkflowData) return new Response(JSON.stringify({ success:false, message:'TEST: Connection unavailable. Try again.' }), { headers:{'content-type':'application/json'} });
        if (url.pathname === '/Monitoring') {
            if (handler === 'ProcessList') return json(['DC','FL']);
            if (handler === 'FloorList') return json(locations);
            if (handler === 'MachineList') return json({ machines:['All','TEST-MC-01'] });
            if (handler === 'Search') {
                if (window.__monitoringSearchGate) await window.__monitoringSearchGate;
                const page = JSON.parse(options.body).page;
                const machineSummaries = window.__monitoringEmpty ? [] : window.__monitoringStairs
                    ? (window.__monitoringCounts || [4,3,1]).map((bmCount, index) => ({ machineNo:`TEST-MC-0${index+1}`, machineType:'TEST', bmCount, actionLine:16 }))
                    : [{ machineNo:'TEST-MC-01', machineType:'TEST', bmCount:1, actionLine:4 }];
                return json({ machineSummaries, currentPage:page, totalPages:3, floorSummaries:locations.map((floorCode,index) => ({ floorCode, repeatBMCount:index===0?2:0, needActionCount:index===0?3:0, withActionCount:index===0?4:0, repeatMachines:['TEST-MC-01'], needActionMachines:['TEST-MC-01'], withActionMachines:['TEST-MC-01'] })) });
            }
            if (handler === 'MachineCard') {
                if (window.__monitoringCardGate) await window.__monitoringCardGate;
                const count = window.__monitoringStairs ? (window.__monitoringCounts || [4,3,1])[Number(url.searchParams.get('machineNo').match(/\d+$/)[0])-1] : 1;
                const bmCards = Array.from({length:count}, (_, index) => ({...card, bmId:123+index, haveAction:index===0?'Yes':'No', needsAction:index===1, mcdNeedsAction:index>1, fontColor:index===1?'Blue':'None', machineNo:url.searchParams.get('machineNo'), timeOfRequest:`10/${String(30-index).padStart(2,'0')}/2026 08:30:00`}));
                return json({ bmCards, actionLine:window.__monitoringActionLine ?? 16, needActionCount:bmCards.filter(row=>row.needsAction).length });
            }
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
    async function open(name, values = query) {
        await Workflows.open(name, values);
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
    window.runMonitoringChecks = async () => {
        Workflows.close();
        await applyMonitoringSelection(query);
        window.__monitoringStairs = true;
        await doSearch(1);
        const columns = [...document.querySelectorAll('.machine-col')];
        const rects = columns.map(col => [...col.querySelectorAll('.bm-card')].map(node => node.getBoundingClientRect()));
        assert(rects[0].length===4 && rects[1].length===3 && rects[2].length===1, 'Missing BM cards');
        assert(Math.abs(rects[1][0].top-rects[0][0].top-120)<1 && Math.abs(rects[2][0].top-rects[1][0].top-240)<1, 'Staircase is reversed or top-aligned');
        assert(rects.every(rows => Math.abs(rows.at(-1).bottom-rects[0].at(-1).bottom)<1), 'BM stacks have different baselines');
        const columnWidth = Math.max(186, document.getElementById('cardScroller').clientWidth/10);
        assert(columns.every(col => Math.abs(col.getBoundingClientRect().width-columnWidth)<1), 'Sparse results stretch machine columns');
        document.getAnimations().forEach(animation => { animation.pause(); animation.currentTime=0; });
        assert(getComputedStyle(columns[0].querySelector('.bm-card-content')).backgroundColor==='rgb(250, 128, 114)', 'Missing Salmon card header');
        assert(getComputedStyle(columns[0].querySelector('.bm-card-case')).backgroundColor==='rgb(255, 228, 225)', 'Missing MistyRose case panel');
        assert(getComputedStyle(columns[0].querySelector('.mc-no')).backgroundColor==='rgb(252, 227, 138)', 'Missing machine summary palette');
        assert(getComputedStyle(columns[0].querySelector('.badge-action')).backgroundColor==='rgb(255, 0, 0)', 'Action status does not use MCStatus');
        assert(getComputedStyle(columns[0].querySelectorAll('.bm-card-case')[1]).color==='rgb(0, 0, 255)', 'Repeat text color missing');
        assert([...columns[0].querySelectorAll('.bm-card:first-child .bm-command')].map(node => node.dataset.command).join(',')==='ocap,report,evidence', 'Wrong O/R/E command order');
        const blink = (node, expected, property='backgroundColor') => {
            const animation = node.getAnimations()[0];
            assert(animation && animation.effect.getTiming().duration===2000, 'Missing legacy one-second blink');
            animation.pause(); animation.currentTime=1000;
            assert(getComputedStyle(node)[property]===expected, `Wrong blink color: ${getComputedStyle(node)[property]}`);
            animation.currentTime=0;
        };
        blink(document.querySelector('.summary-repeat .has-count'), 'rgb(255, 0, 0)');
        blink(document.querySelector('.summary-need .has-count'), 'rgb(0, 0, 255)');
        blink(columns[0].querySelector('.needs-action .bm-card-content'), 'rgb(255, 255, 255)');
        blink(columns[0].querySelector('.needs-action .bm-card-time'), 'rgb(255, 255, 255)');
        blink(columns[0].querySelector('.repeat-blue .bm-card-case'), 'rgb(0, 0, 0)', 'color');
        blink(columns[0].querySelector('.needs-mcd-action'), 'rgb(139, 0, 0)');
        const steadyStar = columns[0].querySelector('.badge-mcd:not(.needs-mcd-action)');
        assert(steadyStar.textContent==='\u2605\u2605\u2605' && steadyStar.getAnimations().length===0, 'MCD stars or steady state missing');
        for (const content of ['', 'Long BM content '.repeat(30)]) {
            const testCard = buildBMCard({...card, bmContent:content, mcdNeedsAction:true}, 16, 'PM');
            columns[0].querySelector('.bm-card-list').appendChild(testCard);
            const star = testCard.querySelector('.badge-mcd');
            assert(Math.abs(star.getBoundingClientRect().right-testCard.querySelector('.bm-card-content').getBoundingClientRect().right)<1, 'MCD stars are not pinned right');
            assert(star.textContent==='\u2605\u2605\u2605' && star.getAttribute('aria-label').includes('needs action'), 'Blinking MCD stars lost their meaning');
            testCard.remove();
        }
        assert(!buildBMCard({...card, isMCD:'No'}, 16, 'PM').querySelector('.badge-mcd'), 'Non-MCD card shows stars');
        assert(columns[0].querySelector('.need-action-badge').textContent==='1' && columns[2].querySelector('.need-action-badge').textContent==='0', 'Footer ignores returned Need Action counts');
        assert(document.querySelector('#floorSummaryGrid tbody').rows.length===3, 'Missing summary counter rows');
        assert([...document.querySelectorAll('#floorSummaryGrid tbody tr')].map(row => row.cells[1].textContent).join(',')==='2,3,4', 'Summary counter types are mixed');
        assert(!document.querySelector('.monitoring-deck button[onclick*="RawData"]') && !document.querySelector('#floorSummaryGrid button'), 'Unexpected toolbar/counter buttons');
        assert(document.getElementById('lblPageNumber').textContent==='1/3' && document.getElementById('btnPrev').disabled, 'First page state is incorrect');
        await doSearch(3);
        assert(document.getElementById('lblPageNumber').textContent==='3/3' && document.getElementById('btnNext').disabled, 'Last page state is incorrect');
        const searches = requests.filter(request=>request.handler==='Search').length;
        changePage(1);
        assert(requests.filter(request=>request.handler==='Search').length===searches, 'Next ran past final page');
        window.__monitoringCounts = [12,7,5];
        await doSearch(1);
        const scroller = document.getElementById('cardScroller');
        const histories = [...document.querySelectorAll('.bm-card-list')];
        assert(histories[0].scrollHeight>histories[0].clientHeight && scroller.scrollHeight<=scroller.clientHeight+1, 'BM history makes the entire board scroll vertically');
        assert(histories.every(list => Math.abs(list.scrollTop+list.clientHeight-list.scrollHeight)<1), 'Histories do not start at the bottom');
        assert(histories.every(list => getComputedStyle(list).scrollbarWidth==='none' && list.tabIndex===0), 'History scrollbar is prominent or keyboard scrolling is unavailable');
        const footerTop = document.querySelector('.mc-col-header').getBoundingClientRect().top;
        const secondScroll = histories[1].scrollTop;
        histories[0].scrollTop=0;
        await nextPaint();
        assert(histories[0].scrollTop===0 && histories[1].scrollTop===secondScroll && document.querySelector('.mc-col-header').getBoundingClientRect().top===footerTop, 'Scrolling moves other columns or the footer');
        window.__monitoringCounts = [8,7,5];
        window.__monitoringActionLine = 2;
        await doSearch(1);
        const tierCards = [...document.querySelectorAll('#cards_0 .bm-card')];
        assert(['rgb(240, 255, 240)','rgb(230, 230, 250)','rgb(224, 255, 255)','rgb(255, 228, 225)'].every(color => tierCards.some(node => getComputedStyle(node.querySelector('.bm-card-case')).backgroundColor===color)), 'Capacity color tiers missing');
        window.__monitoringActionLine = undefined;
        const mc = { machineNo:'TEST-MC-01', machineType:'TEST', actionLine:16 };
        let list = document.getElementById('cards_0');
        window.__failWorkflowData = true;
        await loadMachineCard(mc, buildFilter(), list);
        assert(list.querySelector('[role="alert"]')?.textContent.includes('Connection unavailable') && list.querySelector('button'), 'Failure/retry state missing');
        window.__failWorkflowData = false;
        list.querySelector('button').click();
        await waitFor(()=>list.querySelector('.bm-card'));
        let release;
        window.__monitoringCardGate = new Promise(resolve=>{ release=resolve; });
        const pending = loadMachineCard(mc, buildFilter(), list);
        list.closest('.machine-col').remove();
        window.__monitoringCardGate = null;
        await doSearch(1);
        const replacement = document.getElementById('cards_0');
        const before = replacement.innerHTML;
        release();
        await pending;
        assert(replacement.innerHTML===before, 'Late card response overwrote the current search');
        window.__monitoringEmpty = true;
        await doSearch(1);
        assert(!document.getElementById('noResultMsg').classList.contains('is-hidden'), 'Missing empty search state');
        window.__monitoringEmpty = false;
        let resumeSearch;
        window.__monitoringSearchGate = new Promise(resolve => { resumeSearch=resolve; });
        const searching = doSearch(1);
        assert(document.getElementById('noResultMsg').classList.contains('is-hidden'), 'Previous no-machines message remains while searching');
        window.__monitoringSearchGate = null;
        resumeSearch();
        await searching;
        window.__monitoringCounts = undefined;
        window.__monitoringStairs = false;
        await doSearch(1);
        assert(Math.abs(document.querySelector('.machine-col').getBoundingClientRect().width-columnWidth)<1, 'One machine stretches across the board');
        window.__monitoringStairs = true;
        window.__monitoringCounts = Array(10).fill(1);
        scroller.style.width = '2200px';
        await doSearch(1);
        assert(Math.abs([...scroller.querySelectorAll('.machine-col')].reduce((sum, col) => sum+col.getBoundingClientRect().width, 0)-scroller.clientWidth)<1, 'Ten columns do not fill the wider parent');
        scroller.style.width = '';
        window.__monitoringCounts = undefined;
        window.__monitoringStairs = false;
        await doSearch(1);
        onFloorCellClick('LSI1-1F', ['TEST-MC-01']);
        const chooser = document.getElementById('machineListSidebar');
        const box = chooser.getBoundingClientRect();
        assert(chooser.open && Math.abs(box.left+box.width/2-innerWidth/2)<1 && Math.abs(box.top+box.height/2-innerHeight/2)<1, 'Machine chooser is not centered');
        assert(document.getElementById('machineListGo').disabled, 'Machine chooser has no selection guard');
        chooser.querySelector('.summary-machine-item').click();
        document.getElementById('machineListGo').click();
        await waitFor(()=>!chooser.open && document.querySelector('.bm-card'));
        assert(errors.length===0, errors.join('; '));
        return 'PASS: ten-slot BM sizing for sparse/full results, right-docked steady/blinking MCD stars, legacy blink phases, O/R/E order, scrolling, counts, pagination, retry, and stale-card isolation';
    };
    window.runDocumentChecks = async () => {
        let recordToolbarStyle = null;
        const flows = [
            {name:'PmReport', command:'report', user:'PM', trigger:'[data-id="pmGenerate"]', download:'[data-id="pmDownload"]'},
            {name:'BmReport', command:'report', user:'TE', trigger:'[data-id="btnDownload"]', download:'[data-id="reportDownload"]'},
            {name:'EvidenceMaking', command:'evidence', user:'PM', trigger:'[data-id="btnGenerate"]', download:'[data-id="downloadLink"]'},
            {name:'TeEvidenceMaking', command:'evidence', user:'TE', trigger:'[data-id="btnGenerate"]', download:'[data-id="download"]'}
        ];
        for (const flow of flows) {
            Workflows.close();
            const before = requests.filter(request=>request.path===`/${flow.name}`).length;
            openBMCommand(flow.command, {...card, process:flow.user==='TE'?'FT':'DC'}, flow.user);
            await waitFor(()=>document.querySelector(`#workflowBody [data-workflow="${flow.name}"]`));
            await tick();
            const root = document.querySelector(`#workflowBody [data-workflow="${flow.name}"]`);
            assert(requests.filter(request=>request.path===`/${flow.name}`).length===before, `${flow.name} generated on opening`);
            assert(downloadClicks===0, `${flow.name} downloaded on opening`);
            assert(root.getBoundingClientRect().height<260, `${flow.name} has unused document space`);
            const toolbar = root.querySelector('.pm-report-header, .document-toolbar');
            const toolbarStyle = JSON.stringify(styleValues(toolbar, ['display','padding','backgroundColor','height']));
            if (recordToolbarStyle===null) recordToolbarStyle = toolbarStyle;
            assert(toolbarStyle===recordToolbarStyle, `${flow.name} context bar does not match R`);
            assert(Math.abs(root.querySelector(flow.trigger).getBoundingClientRect().right-toolbar.getBoundingClientRect().right+8)<1, `${flow.name} Generate is not on the right`);
            if (flow.command==='evidence') {
                assert(!root.querySelector('input:not([type="hidden"])'), 'Evidence context still uses form fields');
                assert(root.querySelector('[data-id="recordProcess"]').textContent===(flow.user==='TE'?'FT':'DC') && root.querySelector('[data-id="recordBmId"]').textContent==='123', 'Evidence lost the clicked record');
                root.querySelector('[data-id="recordBmId"]').textContent='999';
                root.querySelector('[data-id="recordProcess"]').textContent='WB';
            }
            root.querySelector(flow.trigger).click();
            const link = root.querySelector(flow.download);
            await waitFor(()=>!link.hidden && !link.closest('.is-hidden'));
            assert(downloadClicks===0, `${flow.name} downloaded automatically after generation`);
            assert(requests.filter(request=>request.path===`/${flow.name}`).length===before+1, `${flow.name} generation was not explicit`);
            if (flow.name==='EvidenceMaking') {
                const payload = JSON.parse(requests.filter(request => request.path==='/EvidenceMaking').at(-1).body);
                assert(payload.process==='DC' && payload.bmId===123, 'Evidence generation did not use the clicked record');
            }
        }
        Workflows.close();
        await Workflows.open('TeEvidenceMaking', {...query, autoGenerate:'1'});
        await tick();
        assert(!document.querySelector('[data-id="download"]').hasAttribute('href'), 'Legacy autoGenerate option still triggers generation');
        Workflows.close();
        openBMCommand('ocap', card, 'PM');
        await waitFor(()=>document.querySelector('[data-workflow="OcapViewer"] [data-id="recordMachine"]'));
        const ocap = document.querySelector('[data-workflow="OcapViewer"]');
        const ocapRecord = ocap.querySelector('.ocap-record');
        assert(getComputedStyle(ocapRecord).backgroundColor==='rgb(221, 221, 221)' && getComputedStyle(ocapRecord).padding==='8px' && getComputedStyle(ocapRecord).borderTopWidth==='0px', 'OCAP context strip does not match R/E');
        assert(ocap.querySelector('[data-id="recordBmId"]').textContent==='123' && ocap.querySelector('[data-id="recordMachine"]').textContent.includes(card.machineNo) && ocap.querySelector('[data-id="recordCase"]').textContent===card.bmCase && ocap.querySelector('[data-id="recordRequested"]').textContent===card.timeOfRequest && ocap.querySelector('[data-id="recordLocation"]').textContent===card.location, 'OCAP does not identify the selected BM record');
        Workflows.close();
        assert(errors.length===0, errors.join('; '));
        return 'PASS: R/E map to Forms 3/9 and 4/10, all four require explicit generation and separate download, compact document dialogs';
    };
    function holdSummaryData() {
        releaseSummaryGate?.();
        summaryGate = new Promise(resolve => { releaseSummaryGate = resolve; });
    }
    function releaseSummaryData() {
        releaseSummaryGate?.();
        releaseSummaryGate = null;
        summaryGate = null;
    }
    function summaryRoot() { return document.querySelector('#workflowBody [data-workflow]'); }
    function summaryRows(root) { return root.querySelectorAll('.ab-proc-row:not(.ab-totals-row), .ms-grid tbody tr, .bma-table tbody tr:not(.bma-total-row)'); }
    async function openMachinePanel(root) {
        root.querySelector('.ab-alert, .ms-active, .bma-cell-active').click();
        await waitFor(() => root.querySelector('.summary-machine-item'));
    }
    function styleValues(node, properties) {
        const style = getComputedStyle(node);
        return properties.map(property => style[property]);
    }
    window.runSummaryChecks = async () => {
        let panelStyle = null;
        try {
            for (const name of summaryNames) {
                holdSummaryData();
                await open(name);
                const root = summaryRoot();
                const button = root.querySelector('.summary-search button');
                const loading = root.querySelector('[data-id="summaryLoading"]');
                assert(!root.querySelector('select, input:not([type="hidden"])'), `${name} has independent filters`);
                const request = requests.filter(request => request.path===`/${name}`).at(-1);
                const filter = request.body ? JSON.parse(request.body) : request.query;
                assert((filter.dateFrom || '').includes('2026') && (filter.user || filter.userMode || filter.mode || 'PM')===(name==='AlertBoard'?'PM':'EEE'), `${name} did not inherit Monitoring filters`);
                assert(summaryRows(root).length === 12, `${name} grid was blank before data arrived`);
                assert(button.disabled && !loading.hidden && root.getAttribute('aria-busy') === 'true', `${name} missing loading state`);
                assert(!root.querySelector('.ab-progress, .ms-progress'), `${name} still uses a line progress indicator`);
                const firstCell = root.querySelector('[data-cell], .ms-grid tbody [data-location], .bma-table [data-key]');
                const before = requests.length;
                button.click();
                window.dispatchEvent(new Event('monitoring:refresh'));
                await tick();
                assert(requests.length === before, `${name} submitted another search while loading`);
                releaseSummaryData();
                await waitFor(() => !button.disabled);
                assert(loading.hidden && root.getAttribute('aria-busy') === 'false', `${name} did not finish loading`);
                assert(root.contains(firstCell), `${name} replaced its grid instead of filling values`);
                await openMachinePanel(root);
                const panel = root.querySelector('.summary-machine-panel');
                if (name==='AlertBoard') {
                    const child = panel.getBoundingClientRect();
                    const parent = document.getElementById('workflowDialog').getBoundingClientRect();
                    assert(Math.abs(child.left+child.width/2-parent.left-parent.width/2)<1 && Math.abs(child.top+child.height/2-parent.top-parent.height/2)<1, 'AlertBoard drilldown is not centered in its parent dialog');
                }
                const styles = JSON.stringify([
                    styleValues(panel, ['width','backgroundColor','borderTop','boxShadow']),
                    styleValues(panel.querySelector('.summary-panel-header'), ['display','padding','backgroundColor','fontSize']),
                    styleValues(panel.querySelector('.summary-machine-item'), ['padding','minHeight','fontSize']),
                    styleValues(panel.querySelector('.summary-panel-footer'), ['display','padding','justifyContent','backgroundColor']),
                    styleValues(panel.querySelector('.summary-panel-go'), ['padding','minHeight','fontSize'])
                ]);
                if (panelStyle === null) panelStyle = styles;
                assert(styles === panelStyle, `${name} machine panel styling differs`);
                const go = panel.querySelector('.summary-panel-go');
                assert(go.disabled, `${name} allows navigation without a machine selection`);
                const item = panel.querySelector('.summary-machine-item');
                item.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter',bubbles:true}));
                assert(!go.disabled && item.getAttribute('aria-pressed') === 'true', `${name} keyboard selection failed`);
                panel.querySelector('.summary-panel-close').click();
                assert(getComputedStyle(panel).display === 'none', `${name} machine panel did not close`);
                window.__failWorkflowData = true;
                button.click();
                await waitFor(() => !button.disabled);
                assert(root.querySelector('.summary-status').textContent.includes('Connection unavailable'), `${name} missing error feedback`);
                assert(summaryRows(root).length === 12 && loading.hidden, `${name} lost its grid or stayed loading on error`);
                window.__failWorkflowData = false;
                button.click();
                await waitFor(() => !button.disabled);
                assert(!root.querySelector('.summary-status').textContent.includes('Connection unavailable'), `${name} failed to recover`);
                if (name !== 'AlertBoard') {
                    for (const userType of ['PM','CC','EEE','VIS','TE']) {
                        holdSummaryData();
                        await open(name, {...query, userType, process:'WB', location:'LSI2-3F', machine:'OTHER-MC'});
                        const filteredRoot = summaryRoot();
                        const rowLabels = () => [...summaryRows(filteredRoot)].map(row => row.cells[0].textContent).join(',');
                        assert(rowLabels()===processes.join(','), `${name} ${userType} lost process rows while loading`);
                        releaseSummaryData();
                        await waitFor(() => filteredRoot.getAttribute('aria-busy')==='false');
                        assert(rowLabels()===processes.join(','), `${name} ${userType} filters changed the process list`);
                        if (userType==='TE') {
                            const dc = summaryRows(filteredRoot)[0];
                            assert(!dc.querySelector('.ms-active, .bma-cell-active') && dc.querySelector('.ms-na, .bma-cell-na'), `${name} queries unsupported TE processes`);
                        }
                    }
                }
                Workflows.close();
                await tick();
            }
            assert(errors.length === 0, errors.join('; '));
            return 'PASS: three summary layouts before data, spinner/search locking, in-place values, shared machine panels, keyboard selection, TE modes, failure and retry';
        } finally {
            releaseSummaryData();
            window.__failWorkflowData = false;
        }
    };
    window.prepareSummaryCapture = async (name, state) => {
        releaseSummaryData();
        if (state === 'loading') holdSummaryData();
        await open(name);
        const root = summaryRoot();
        if (state === 'panel') {
            await waitFor(() => root.getAttribute('aria-busy') === 'false');
            await openMachinePanel(root);
            root.querySelector('.summary-machine-item').click();
        }
        const toolbar = root.querySelector('.summary-toolbar');
        toolbar.scrollIntoView({block:'nearest'});
        const filters = toolbar.querySelector('.summary-context').getBoundingClientRect();
        const button = toolbar.querySelector('.summary-search button').getBoundingClientRect();
        assert(button.right <= toolbar.getBoundingClientRect().right + 1, `${name} search overflows`);
        assert(Math.abs(button.right - (toolbar.getBoundingClientRect().right - 8)) < 2, `${name} search is not at the right`);
        assert(filters.left <= button.left, `${name} Monitoring context is not on the left`);
        assert(document.documentElement.scrollWidth <= innerWidth + 1, `${name} overflows the viewport`);
        if (state === 'panel') root.querySelector('.summary-machine-panel').scrollIntoView({block:'nearest'});
        await nextPaint();
        return `${name} ${state} ${innerWidth}px: PASS`;
    };
    window.prepareWorkflowCapture = async name => {
        if (document.getElementById('machineListSidebar').open) closeSidebar();
        if(name==='Monitoring') { Workflows.close(); window.__monitoringStairs=true; await doSearch(1); await tick(); }
        else if(name==='MachineList') { Workflows.close(); onFloorCellClick('LSI1-1F', ['TEST-MC-01','TEST-MC-02']); }
        else if(name==='OcapViewer') { openBMCommand('ocap', card, 'PM'); await waitFor(()=>document.querySelector('[data-workflow="OcapViewer"] [data-id="recordCase"]')); await tick(); }
        else await open(name);
        const overflow=document.documentElement.scrollWidth > innerWidth + 1;
        assert(!overflow, `${name} overflows the viewport`);
        assert(errors.length===0, errors.join('; '));
        await nextPaint();
        return `${name} ${innerWidth}px: PASS`;
    };
})();
