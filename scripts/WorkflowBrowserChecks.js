// UI-only fixtures. This script is injected by Test-Workflows.ps1 and is never served by the app.
(() => {
    const nativeFetch = window.fetch.bind(window);
    const requests = [];
    const errors = [];
    let downloadClicks = 0;
    document.addEventListener('click', event => {
        if (event.target.closest('a[download]')) { downloadClicks++; if (!window.__allowFixtureDownloads) event.preventDefault(); }
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
    function generatedReport(format, fileName) {
        const response = new Response(`TEST:${fileName}`, { headers: {
            'content-type': format === 'pdf' ? 'application/pdf' : 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
            'content-disposition': `attachment; filename*=UTF-8''${encodeURIComponent(fileName)}`
        } });
        // Reproduce Chromium's low-disk streamed-Blob failure while leaving the response bytes readable.
        if (window.__streamedBlobUnavailable)
            response.blob = async () => { throw new TypeError('Failed to fetch'); };
        return response;
    }
    window.fetch = async (input, options = {}) => {
        const url = new URL(input, location.href);
        const handler = url.searchParams.get('handler');
        if (!handler) return nativeFetch(input, options);
        requests.push({ path:url.pathname, handler, method:options.method || 'GET', query:Object.fromEntries(url.searchParams), body:options.body, signal:options.signal });
        if (summaryGate && summaryNames.includes(url.pathname.slice(1)) && ['LoadBoard','Summary','LoadGrid'].includes(handler)) await summaryGate;
        if (window.__summaryDrillGate && [...summaryNames, 'McdMonitoring'].includes(url.pathname.slice(1)) && ['Drilldown','MachineList'].includes(handler)) await window.__summaryDrillGate;
        if (window.__failWorkflowData) return new Response(JSON.stringify({ success:false, message:'TEST: Connection unavailable. Try again.' }), { headers:{'content-type':'application/json'} });
        if (url.pathname === '/Monitoring') {
            if (handler === 'TrackSession') return new Response(null, { status:204 });
            if (handler === 'ProcessList') {
                if (window.__monitoringFilterGate) await window.__monitoringFilterGate;
                return json(['DC','FL']);
            }
            if (handler === 'FloorList') return json(locations);
            if (handler === 'MachineList') return json({ machines:['All','TEST-MC-01'] });
            if (handler === 'Search') {
                if (window.__monitoringSearchGate) await window.__monitoringSearchGate;
                const requestedPage = JSON.parse(options.body).page;
                let summaries = window.__monitoringEmpty ? [] : window.__monitoringStairs
                    ? (window.__monitoringCounts || [4,3,1]).map((bmCount, index) => ({ machineNo:`TEST-MC-0${index+1}`, machineType:'TEST', bmCount, actionLine:16 }))
                    : [{ machineNo:'TEST-MC-01', machineType:'TEST', bmCount:1, actionLine:4 }];
                if(window.__monitoringPages) summaries=Array.from({length:23},(_,index)=>({...summaries[index%summaries.length],machineNo:`PAGE-MC-${index+1}`}));
                const totalPages=Math.max(1,Math.ceil(summaries.length/10));
                const page=Math.min(requestedPage,totalPages);
                const machineCards=summaries.map(mc=>{
                    const bmCards=Array.from({length:mc.bmCount},(_,index)=>({...card,bmId:123+index,haveAction:index===0?'Yes':'No',needsAction:index===1,mcdNeedsAction:index>1,fontColor:index===1?'Blue':'None',machineNo:mc.machineNo,timeOfRequest:`10/${String(30-index).padStart(2,'0')}/2026 08:30:00`}));
                    return {...mc,bmCards,actionLine:window.__monitoringActionLine??mc.actionLine,needActionCount:bmCards.filter(row=>row.needsAction).length};
                });
                return json({ machineSummaries:summaries.slice((page-1)*10,page*10),machineCards,currentPage:page,totalPages,floorSummaries:locations.map((floorCode,index) => ({ floorCode, repeatBMCount:index===0?2:0, needActionCount:index===0?3:0, withActionCount:index===0?4:0, repeatMachines:['TEST-MC-01'], needActionMachines:['TEST-MC-01'], withActionMachines:['TEST-MC-01'] })) });
            }
            if (handler === 'MachineCard') {
                if (window.__monitoringCardGate) await window.__monitoringCardGate;
                const count = window.__monitoringStairs ? (window.__monitoringCounts || [4,3,1])[Number(url.searchParams.get('machineNo').match(/\d+$/)[0])-1] : 1;
                const bmCards = Array.from({length:count}, (_, index) => ({...card, bmId:123+index, haveAction:index===0?'Yes':'No', needsAction:index===1, mcdNeedsAction:index>1, fontColor:index===1?'Blue':'None', machineNo:url.searchParams.get('machineNo'), timeOfRequest:`10/${String(30-index).padStart(2,'0')}/2026 08:30:00`}));
                return json({ bmCards, actionLine:window.__monitoringActionLine ?? 16, needActionCount:bmCards.filter(row=>row.needsAction).length });
            }
        }
        if (url.pathname === '/ActionCards') {
            if (handler === 'Load') return json({ seqno:42, problemContent:'TEST: Sensor Fault', inCharge:'TEST', dateCard:'11/01/2026 08:30:00', investigationDone:'Fixture investigation', action:'Fixture action', mcStatus:'STOP (ONGOING INVESTIGATION)' });
            return json('Saved');
        }
        if (url.pathname === '/AlertBoard') {
            if (handler === 'Drilldown') return json({ machines:['TEST-MC-01'], title:'TEST machine list' });
            const te = JSON.parse(options.body).user === 'TE';
            const counts = te ? { FL_1:1 } : { DB_3:4, DB_4:23, DB_6:8, WB_2:1, WB_3:2, WB_4:4, FL_2:5, FL_3:1, FL_5:4, SING_3:1, SING_4:38, FT_2:4, FT_5:5, TP_4:3, EDS_1:4 };
            return json({ cells:Object.fromEntries(Object.entries(counts).map(([key,count])=>[key,{count,machines:['TEST-MC-01']}])),
                locationTotals:te ? {'LSI1-2F':1} : {'LSI1-1F':0,'LSI1-2F':0,'LSI2-1F':10,'LSI2-2F':8,'LSI2-3F':68,'LSI3-1F':0,'LSI3-2F':8,'LSI3-3F':9,'LSI3-4F':4},
                grandTotal:te ? 1 : 107 });
        }
        if (url.pathname === '/MachineSummary' || url.pathname === '/McdMonitoring') {
            if (handler === 'MachineList') return json({ process:'DC', location:'LSI1-1F', machines:['TEST-MC-01'] });
            const te = url.searchParams.get('userMode')==='TE' || url.searchParams.get('userRole')==='TE';
            const activeProcess = te ? 'FL' : 'DC';
            const activeLocation = te ? 'LSI1-2F' : 'LSI1-1F';
            return json({ columns:locations, rows:processes.map(process=>({ process, rowTotal:process===activeProcess?1:0, cells:Object.fromEntries(locations.map((loc,locIndex)=>[loc,{ count:process===activeProcess&&loc===activeLocation?1:0,key:`${process}_${locIndex+1}`,machines:process===activeProcess&&loc===activeLocation?['TEST-MC-01']:[] }])) })), locationTotals:{[activeLocation]:1}, grandTotal:1 });
        }
        if (url.pathname === '/BmAnalytics') {
            const te = JSON.parse(options.body).mode==='TE';
            return json({ cells:{[te?'FL_1':'DC_1']:{count:1,mcnos:['TEST-MC-01']}}, locationTotals:{[te?'LSI1-2F':'LSI1-1F']:1}, processTotals:{[te?'FL':'DC']:1}, grandTotal:1 });
        }
        if (url.pathname === '/OcapViewer') return json({ rows:[
            {checkItem:'TEST: Safety interlock',checkingMethod:'Fixture check',treatmentAction:'Fixture action',judgement:'OK',remarks:'Test data only'},
            {checkItem:'TEST: Machine condition',checkingMethod:'Visual check',treatmentAction:'Confirm operation',judgement:'OK',remarks:'Alternating row fixture'}
        ] });
        if (url.pathname === '/RawData') return json({ frequencyTable:[{frequency:'2x',dateLabel:'Oct 2026',total:2}],bmCaseTable:[{bmCase:'TEST: Sensor Fault',dateMonth:'October',dateYear:2026,bmCount:2}],frequencyChart:[{name:'2x',points:[{label:'Oct 2026',value:2}]}],bmCaseChart:[{name:'TEST: Sensor Fault',points:[{label:'Oct 2026',value:2}]}] });
        if (url.pathname === '/PmReport') {
            const format=url.searchParams.get('format')||'xlsx';
            return generatedReport(format,`${url.searchParams.get('process')}_${url.searchParams.get('bmId')}.${format}`);
        }
        if (url.pathname === '/EvidenceMaking') {
            const body=JSON.parse(options.body);
            return generatedReport(body.format,`${body.process}Evidence_${body.bmId}.${body.format}`);
        }
        if (url.pathname === '/TeEvidenceMaking') {
            const format=url.searchParams.get('format')||'xlsx';
            return generatedReport(format,`TE_Evidence_${url.searchParams.get('bmId')}.${format}`);
        }
        if (url.pathname === '/BmReport') {
            const format=options.body.get('format');
            return generatedReport(format,`TE_${options.body.get('bmId')}.${format}`);
        }
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
        const tracking = requests.filter(request=>request.path==='/Monitoring'&&request.handler==='TrackSession');
        assert(tracking.length===1 && tracking[0].method==='POST', 'Session tracking must run once as a POST after page load');
        await applyMonitoringSelection(query);
        await waitFor(()=>document.querySelector('.bm-card'));
        for (const name of ['AlertBoard','MachineSummary','McdMonitoring','BmAnalytics','ActionCards','RawData','OcapViewer','PmReport','BmReport','EvidenceMaking','TeEvidenceMaking']) { await open(name); Workflows.close(); await tick(); }
        document.getElementById('btnMCD').click(); await waitFor(()=>document.querySelector('[data-workflow="MachineSummary"]'));
        await waitFor(()=>document.querySelector('.ms-active'));
        document.querySelector('.ms-active').click(); await waitFor(()=>document.querySelector('.ms-panel-item'));
        document.querySelector('.ms-panel-item').click(); document.querySelector('[data-id="msPanelGo"]').click();
        await waitFor(()=>!document.getElementById('workflowDialog').open && document.getElementById('cbMachine').value==='TEST-MC-01');
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
        assert(JSON.parse(requests.findLast(request=>request.path==='/ActionCards'&&request.handler==='Save').body).card.seqno===42, 'Save lost the loaded Action Card Seqno');
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
    window.runMonitoringDateChecks = async () => {
        const dateFrom = document.getElementById('dt1');
        const dateTo = document.getElementById('dt2');
        const previous = [dateFrom.value, dateTo.value, dateTo.max, dateFrom.dataset.time, dateTo.dataset.time];
        try {
            resetDates();
            const now = new Date();
            const tomorrow = new Date(now); tomorrow.setDate(tomorrow.getDate()+1);
            assert(dateTo.value===formatDate(tomorrow) && !dateTo.hasAttribute('max'), 'Monitoring To must default to tomorrow without a maximum');
            assert(dateTo.checkValidity(), 'Tomorrow is rejected');
            const yesterday = new Date(now); yesterday.setDate(yesterday.getDate()-1);
            dateTo.value = formatDate(yesterday);
            assert(dateTo.checkValidity(), 'Past To dates are rejected');
            dateTo.dispatchEvent(new Event('focus'));
            assert(!dateTo.hasAttribute('max'), 'Focusing To introduced a date limit');
            const future = new Date(now); future.setDate(future.getDate()+90);
            dateTo.value = formatDate(future);
            assert(dateTo.checkValidity(), 'A future To date is rejected');
            const requestCount = requests.length;
            const searchId = State.searchId;
            const selects = ['cbUser','cbProcess','cbFloor','cbMachine'].map(id=>document.getElementById(id));
            const selected = selects.map(el=>el.innerHTML);
            try {
                ['PM','MP','LSI2-3F','All'].forEach((value,index)=>selects[index].innerHTML=`<option selected>${value}</option>`);
                await doSearch(1);
                assert(State.searchId===searchId+1 && requests.length>requestCount && State.lastFilter.dateTo===toApiDate(monitoringDateRange().dateTo), 'Future To date did not reach the search with its retained time');
                assert(!dateTo.hasAttribute('max'), 'Searching introduced a To-date cap');
            } finally {
                selects.forEach((el,index)=>el.innerHTML=selected[index]);
            }
            const afterSearch = requests.length;
            const afterSearchId = State.searchId;
            dateTo.value = '';
            await doSearch(1);
            assert(dateTo.validity.valueMissing && requests.length===afterSearch && State.searchId===afterSearchId, 'Empty To date started a search');
            resetDates();
            assert(dateTo.value===formatDate(tomorrow) && !dateTo.hasAttribute('max'), 'Reset did not restore the unrestricted tomorrow default');
            // Form1 uses hh:mm:ss without AM/PM, not HH:mm:ss or midnight boundaries.
            for (const [hour, expected] of [[0,'12'],[12,'12'],[13,'01'],[23,'11']]) {
                resetDates(new Date(2026,10,8,hour,6,37));
                const range = monitoringDateRange();
                assert(range.dateFrom===`2026-10-09T${expected}:06:37` && range.dateTo===`2026-11-09T${expected}:06:37`, 'Default picker time differs from Form1_Load/SQL parameters');
                dateTo.value='2026-12-20';
                assert(monitoringDateRange().dateTo===`2026-12-20T${expected}:06:37`, 'Selecting a date lost the picker time');
            }
            await applyMonitoringSelection({dateFrom:'2026-10-02T15:07:41',dateTo:'2026-11-15T16:08:42'});
            assert(buildFilter().dateFrom==='10/02/2026 03:07:41' && buildFilter().dateTo==='11/15/2026 04:08:42', 'URL/drilldown timestamps were discarded');
            const checkSummaryDates = (name) => {
                const request = requests.filter(request=>request.path===`/${name}`).at(-1);
                const filter = request.body ? JSON.parse(request.body) : request.query;
                const apiDate = value => /^\d{2}\//.test(value) ? value : toApiDate(value.replace(' ','T'));
                const expected = buildFilter();
                assert(apiDate(filter.dateFrom)===expected.dateFrom && apiDate(filter.dateTo)===expected.dateTo, `${name} did not use Form1 dates/times/seconds`);
                assert(!('process' in filter) && !('location' in filter) && !('machine' in filter), `${name} was restricted to Monitoring's process/location/machine`);
            };
            for (const name of summaryNames) {
                await open(name);
                checkSummaryDates(name);
                setMonitoringDate('dt2','2026-12-21T23:08:43');
                window.dispatchEvent(new Event('monitoring:refresh'));
                await tick();
                checkSummaryDates(name);
                Workflows.close();
            }
            resetDates(new Date(2026,10,8,21,6,37));
            resetMorningDates(new Date(2026,10,8,8,30,0));
            assert(buildFilter().dateFrom==='10/09/2026 12:00:00' && buildFilter().dateTo==='11/08/2026 12:00:00', 'Morning SetDate did not preserve the original midnight-to-12 SQL formatting');
            dateTo.dataset.time='21:06:37';
            resetMorningDates(new Date(2026,10,8,8,30,0));
            assert(dateTo.dataset.time==='21:06:37', 'Morning refresh reset To even though it was already today');
            return 'PASS: unrestricted To, retained picker time, original 12-hour SQL parameters, shared Form1 dates in all three summaries/refresh/drilldowns, and morning reset';
        } finally {
            Workflows.close();
            [dateFrom.value, dateTo.value] = previous;
            [dateFrom.dataset.time, dateTo.dataset.time] = previous.slice(3);
            if (previous[2]) dateTo.max=previous[2]; else dateTo.removeAttribute('max');
        }
    };
    window.runMonitoringChecks = async () => {
        Workflows.close();
        await window.runMonitoringDateChecks();
        const clock = document.querySelector('.refresh-clock');
        const legend = document.querySelector('.monitoring-legend');
        assert(!document.getElementById('toolbar').contains(clock) && clock.previousElementSibling===legend, 'Refresh clock is not below the legend');
        assert(clock.getBoundingClientRect().top>=legend.getBoundingClientRect().bottom, 'Refresh clock shares a row with the legend');
        const overviewStyle = getComputedStyle(document.querySelector('.monitoring-overview'));
        assert(overviewStyle.display==='flex' && overviewStyle.alignItems==='center', 'Floor overview is not flex-centered');
        assert(!document.getElementById('btnClearFilters').hasAttribute('onclick'), 'Clear Filters still depends on an inline global handler');
        await applyMonitoringSelection(query);
        window.__monitoringStairs = true;
        await doSearch(1);
        const columns = [...document.querySelectorAll('.machine-col')];
        const headers = [...document.querySelectorAll('.mc-col-header')];
        const earlyBlink = columns[0].querySelector('.needs-action .bm-card-content').getAnimations()[0];
        await tick();
        renderMachineCard(State.dashboard.machineCards[1], buildFilter(), columns[1].querySelector('.bm-card-list'), headers[1]);
        await nextPaint();
        synchronizeBlinkAnimations(columns[1]);
        const lateBlink = columns[1].querySelector('.needs-action .bm-card-content').getAnimations()[0];
        assert(earlyBlink.startTime===0 && lateBlink.startTime===0, 'AJAX card arrivals do not share the document blink clock');
        const rects = columns.map(col => [...col.querySelectorAll('.bm-card')].map(node => node.getBoundingClientRect()));
        assert(rects[0].length===4 && rects[1].length===3 && rects[2].length===1, 'Missing BM cards');
        assert(Math.abs(rects[1][0].top-rects[0][0].top-120)<1 && Math.abs(rects[2][0].top-rects[1][0].top-240)<1, 'Staircase is reversed or top-aligned');
        assert(rects.every(rows => Math.abs(rows.at(-1).bottom-rects[0].at(-1).bottom)<1), 'BM stacks have different baselines');
        const initialScroller = document.getElementById('cardScroller');
        assert(Math.abs(rects[0].at(-1).bottom-initialScroller.getBoundingClientRect().top-initialScroller.clientHeight)<1, 'Short staircase is not clamped to the board bottom');
        assert(Math.abs(headers[0].getBoundingClientRect().top-initialScroller.getBoundingClientRect().bottom)<1, 'Frozen headers do not sit below the card viewport');
        assert(!initialScroller.querySelector('.mc-col-header'), 'Headers are still included in the vertical scrolling content');
        const alignedColumns = () => [...document.querySelectorAll('.machine-col')].every((col, index) => {
            const header = document.querySelectorAll('.mc-col-header')[index].getBoundingClientRect();
            const cards = col.getBoundingClientRect();
            return Math.abs(header.left-cards.left)<1 && Math.abs(header.width-cards.width)<1 && cards.width>=186;
        });
        assert(alignedColumns(), 'Frozen headers and sparse card columns are misaligned');
        const columnWidth = Math.max(186, initialScroller.clientWidth/10);
        assert(columns.every(col => Math.abs(col.getBoundingClientRect().width-columnWidth)<1), 'Sparse results stretch past their machine column slots');
        assert(columns.every(col => [...col.querySelectorAll('.bm-card')].every(card => Math.abs(card.getBoundingClientRect().width-col.getBoundingClientRect().width)<1)), 'BM cards do not stay within their machine column width');
        document.getAnimations().forEach(animation => { animation.pause(); animation.currentTime=0; });
        assert(getComputedStyle(columns[0].querySelector('.bm-card-content')).backgroundColor==='rgb(250, 128, 114)', 'Missing Salmon card header');
        assert(getComputedStyle(columns[0].querySelector('.bm-card-case')).backgroundColor==='rgb(255, 228, 225)', 'Missing MistyRose case panel');
        assert(getComputedStyle(headers[0].querySelector('.mc-no')).backgroundColor==='rgb(252, 227, 138)', 'Missing machine summary palette');
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
        assert(headers[0].querySelector('.need-action-badge').textContent==='1' && headers[2].querySelector('.need-action-badge').textContent==='0', 'Header ignores returned Need Action counts');
        assert(document.querySelector('#floorSummaryGrid tbody').rows.length===3, 'Missing summary counter rows');
        assert([...document.querySelectorAll('#floorSummaryGrid tbody tr')].map(row => row.cells[1].textContent).join(',')==='2,3,4', 'Summary counter types are mixed');
        assert(!document.querySelector('.monitoring-deck button[onclick*="RawData"]') && !document.querySelector('#floorSummaryGrid button'), 'Unexpected toolbar/counter buttons');
        window.__monitoringPages=true;
        await doSearch(1);
        assert(document.getElementById('lblPageNumber').textContent==='1/3' && document.getElementById('btnPrev').disabled, 'First page state is incorrect');
        const pageRequests=requests.filter(request=>request.path==='/Monitoring').length;
        assert(document.querySelectorAll('.machine-col').length===10 && document.querySelector('.machine-col').dataset.mcno==='PAGE-MC-1','First page is not limited to ten columns');
        const pageClock=performance.now();
        await changePage(1);
        assert(document.querySelectorAll('.machine-col').length===10 && document.querySelector('.machine-col').dataset.mcno==='PAGE-MC-11','Second page has the wrong cached columns');
        await changePage(1);
        assert(document.querySelectorAll('.machine-col').length===3 && document.querySelector('.machine-col').dataset.mcno==='PAGE-MC-21','Final page has the wrong cached columns');
        assert(document.querySelectorAll('.mc-col-header').length===3 && document.querySelector('.mc-no').textContent==='PAGE-MC-21' && alignedColumns(), 'Pagination retained stale or misaligned headers');
        assert(requests.filter(request=>request.path==='/Monitoring').length===pageRequests,'Changing pages repeated a server request');
        assert(!requests.some(request=>request.handler==='MachineCard'),'Board still requests each machine separately');
        window.__monitoringPageMilliseconds=(performance.now()-pageClock)/2;
        assert(document.getElementById('lblPageNumber').textContent==='3/3' && document.getElementById('btnNext').disabled, 'Last page state is incorrect');
        const searches = requests.filter(request=>request.handler==='Search').length;
        changePage(1);
        assert(requests.filter(request=>request.handler==='Search').length===searches, 'Next ran past final page');
        window.__monitoringPages=false;
        window.__monitoringCounts = [12,7,5];
        await doSearch(1);
        const scroller = document.getElementById('cardScroller');
        const histories = [...document.querySelectorAll('.bm-card-list')];
        assert(scroller.scrollHeight>scroller.clientHeight && getComputedStyle(scroller).overflowY==='auto', 'Board has no shared vertical scrollbar');
        assert(Math.abs(scroller.scrollTop+scroller.clientHeight-scroller.scrollHeight)<1, 'Board does not start at the bottom');
        assert(histories.every(list => list.scrollHeight<=list.clientHeight+1 && getComputedStyle(list).overflowY==='visible' && list.tabIndex===-1), 'A history still scrolls independently');
        assert(scroller.tabIndex===0, 'Shared board is not keyboard scrollable');
        const headerTops = [...document.querySelectorAll('.mc-col-header')].map(node=>node.getBoundingClientRect().top);
        const cardTops = histories.map(list=>list.querySelector('.bm-card').getBoundingClientRect().top);
        const boardScroll = scroller.scrollTop;
        scroller.scrollTop=0;
        await nextPaint();
        assert([...document.querySelectorAll('.mc-col-header')].every((node,index)=>Math.abs(node.getBoundingClientRect().top-headerTops[index])<1), 'Frozen headers moved during vertical scrolling');
        assert(histories.every((list,index)=>Math.abs(list.querySelector('.bm-card').getBoundingClientRect().top-cardTops[index]-boardScroll)<1), 'Shared scroll does not move all card columns together');
        window.__monitoringCounts = [52,27,12,9,7,5,4,3,2,1];
        await doSearch(1);
        const tallColumns = [...document.querySelectorAll('.machine-col')];
        const horizontal = document.getElementById('cardContainer');
        const baseline = scroller.getBoundingClientRect().top+scroller.clientHeight;
        assert(horizontal.scrollWidth>horizontal.clientWidth && scroller.scrollHeight>scroller.clientHeight, 'Missing combined horizontal and vertical overflow fixture');
        assert(scroller.scrollHeight===52*120, 'Frozen header height is included in vertical scrollHeight');
        assert(Math.abs(scroller.scrollTop+scroller.clientHeight-scroller.scrollHeight)<1, 'Long staircase does not start clamped at the bottom');
        assert(tallColumns.every((col,index) => {
            const cards = [...col.querySelectorAll('.bm-card')];
            return Math.abs(cards.at(-1).getBoundingClientRect().bottom-baseline)<1 &&
                Math.abs(cards[0].getBoundingClientRect().top-baseline+window.__monitoringCounts[index]*120)<1;
        }), 'Long/unequal BM histories lost their bottom staircase');
        horizontal.scrollLeft=horizontal.scrollWidth;
        await nextPaint();
        assert(horizontal.scrollLeft>0 && alignedColumns(), 'Horizontal scrolling misaligns headers and cards');
        scroller.scrollTop=0;
        await nextPaint();
        assert(alignedColumns() && Math.abs(document.querySelector('.mc-col-header').getBoundingClientRect().top-scroller.getBoundingClientRect().bottom)<1, 'Frozen bottom headers moved while scrolling long histories');
        window.__monitoringCounts = [0,1,0];
        await doSearch(1);
        assert(document.querySelectorAll('.no-bm').length===2 && document.querySelectorAll('.mc-col-header').length===3 && alignedColumns(), 'Machines without BM records lost their frozen headers');
        assert(horizontal.scrollLeft===0 && scroller.scrollHeight===scroller.clientHeight, 'Short histories retained overflow from the previous page');
        window.__monitoringCounts = [8,7,5];
        window.__monitoringActionLine = 2;
        await doSearch(1);
        const tierCards = [...document.querySelectorAll('#cards_0 .bm-card')];
        assert(['rgb(240, 255, 240)','rgb(230, 230, 250)','rgb(224, 255, 255)','rgb(255, 228, 225)'].every(color => tierCards.some(node => getComputedStyle(node.querySelector('.bm-card-case')).backgroundColor===color)), 'Capacity color tiers missing');
        window.__monitoringActionLine = undefined;
        window.__failWorkflowData = true;
        await doSearch(1);
        assert(document.getElementById('toastMsg').textContent.includes('Connection unavailable') && !State.dashboard, 'Failure state retained stale page data');
        window.__failWorkflowData = false;
        await doSearch(1);
        assert(document.querySelector('.bm-card'), 'Search retry did not restore the board');
        let release;
        window.__monitoringSearchGate = new Promise(resolve=>{ release=resolve; });
        const pending = doSearch(1);
        window.__monitoringSearchGate = null;
        await doSearch(1);
        const replacement = document.getElementById('cards_0');
        const before = replacement.innerHTML;
        release();
        await pending;
        assert(replacement.innerHTML===before, 'Late search response overwrote the current search');
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
        assert(Math.abs(document.querySelector('.machine-col').getBoundingClientRect().width-Math.max(186, scroller.clientWidth/10))<1 && alignedColumns(), 'One machine stretches past one column slot');
        window.__monitoringStairs = true;
        window.__monitoringCounts = Array(10).fill(1);
        document.getElementById('machineBoard').style.width = '2200px';
        await doSearch(1);
        assert(Math.abs([...scroller.querySelectorAll('.machine-col')].reduce((sum, col) => sum+col.getBoundingClientRect().width, 0)-scroller.clientWidth)<1 && alignedColumns(), 'Ten columns do not fill the wider parent');
        document.getElementById('machineBoard').style.width = '';
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
        document.getElementById('cbUser').value='TE';
        await onUserChanged();
        assert(document.querySelector('label[for="cbMachine"]').textContent==='Tester' && document.getElementById('lblProcessLabel').textContent==='Process', 'TE labels differ from Form1');
        await applyMonitoringSelection(query);
        let releaseSearch;
        window.__monitoringSearchGate = new Promise(resolve=>{releaseSearch=resolve;});
        const beforeClearSearch = doSearch(2);
        let releaseFilter;
        window.__monitoringFilterGate = new Promise(resolve=>{releaseFilter=resolve;});
        const beforeClearFilter = onUserChanged();
        const searchSignal = State.searchController.signal;
        const filterSignal = State.filterController.signal;
        document.getElementById('btnClearFilters').click();
        assert(searchSignal.aborted && filterSignal.aborted, 'Clear Filters did not cancel SQL-backed search/filter requests');
        window.__monitoringSearchGate = window.__monitoringFilterGate = null;
        releaseSearch(); releaseFilter();
        await Promise.all([beforeClearSearch, beforeClearFilter]);
        assert(!document.querySelector('.machine-col') && !State.lastFilter && !val('cbUser') && !val('cbMachine') && val('cbProcess')==='-Select-' && val('cbFloor')==='-Select-', 'Clear Filters retained filters or late results');
        assert(!document.querySelector('.mc-col-header') && document.getElementById('machineBoard').classList.contains('is-hidden'), 'Clear Filters retained frozen headers');
        assert(document.getElementById('lblPageNumber').textContent==='1/1' && document.getElementById('btnPrev').disabled && document.getElementById('btnNext').disabled, 'Clear Filters did not reset pagination');
        assert([...document.querySelectorAll('#floorSummaryGrid tbody td')].every(cell=>cell.textContent==='0'), 'Clear Filters retained floor totals');
        assert(!document.getElementById('monitoringWelcome').classList.contains('is-hidden') && document.getElementById('loadingSpinner').classList.contains('is-hidden') && document.getElementById('lblMachineLabel').textContent==='Machine', 'Clear Filters did not restore the initial screen');
        const now = new Date();
        const tomorrow = new Date(now.getFullYear(),now.getMonth(),now.getDate()+1);
        assert(val('dt1')===formatDate(new Date(now.getTime()-30*86400000)) && val('dt2')===formatDate(tomorrow), 'Clear Filters did not reset dates');
        await applyMonitoringSelection(query);
        assert(errors.length===0, errors.join('; '));
        return `PASS: one grouped search, zero per-column/page requests, ten-column lazy page rendering (${window.__monitoringPageMilliseconds.toFixed(1)} ms/page in browser fixture), frozen headers, shared scrolling without header height, column alignment, staircase, counts/colors, commands, retry, cancellation and stale-search isolation`;
    };
    window.runRetrievalChecks = async () => {
        const originalNow = Date.now;
        try {
            await open('McdMonitoring');
            const root = document.querySelector('[data-workflow="McdMonitoring"]');
            const button = root.querySelector('[data-id="btnSearch"]');
            await waitFor(() => !button.disabled);
            const active = () => root.querySelector('.mcd-cell-hot');
            const list = () => root.querySelector('.mcd-list-item');
            const count = () => requests.filter(request => request.path==='/McdMonitoring' && request.handler==='MachineList').length;
            let before = count();
            active().click();
            await waitFor(list);
            assert(count()===before, 'MCD snapshot click repeated a database read');
            assert(list().textContent==='TEST-MC-01', 'MCD snapshot lost its machine');

            Date.now = () => originalNow()+31000;
            let release;
            window.__summaryDrillGate = new Promise(resolve => { release = resolve; });
            active().click(); active().click();
            await tick();
            assert(count()===before+1, 'MCD expired snapshot did not deduplicate concurrent reads');
            window.__summaryDrillGate = null; release();
            await waitFor(list);
            before = count();
            window.__failWorkflowData = true;
            active().click();
            await waitFor(() => root.querySelector('.mcd-list-error'));
            window.__failWorkflowData = false;
            active().click();
            await waitFor(list);
            assert(count()===before+2, 'MCD fallback failure prevented retry');

            Date.now = originalNow;
            button.click();
            await waitFor(() => !button.disabled);
            before = count();
            active().click(); await waitFor(list);
            assert(count()===before, 'MCD refresh did not replace its expired snapshot');
            root.querySelector('[data-id="selUser"]').value = 'TE';
            root.querySelector('[data-id="selUser"]').dispatchEvent(new Event('change'));
            await waitFor(() => !button.disabled);
            assert(active().closest('.mcd-grid-row').dataset.process==='FL', 'MCD retained another mode snapshot');
            const beforeClose = requests.length;
            Workflows.close();
            window.dispatchEvent(new Event('monitoring:refresh'));
            await tick();
            assert(requests.length===beforeClose, 'Closed MCD retained a refresh listener');
            assert(errors.length===0, errors.join('; '));
            return 'PASS: MCD snapshot makes zero drilldown requests, expires, deduplicates fallback reads, retries failures, refreshes and isolates modes/closed windows; Monitoring reset aborts pending requests.';
        } finally {
            Date.now = originalNow;
            window.__failWorkflowData = false;
            window.__summaryDrillGate = null;
            Workflows.close();
        }
    };
    window.runDocumentChecks = async () => {
        const create=URL.createObjectURL.bind(URL), revoke=URL.revokeObjectURL.bind(URL);
        const liveUrls=new Set();
        URL.createObjectURL=blob=>{const url=create(blob);liveUrls.add(url);return url;};
        URL.revokeObjectURL=url=>{liveUrls.delete(url);revoke(url);};
        window.__streamedBlobUnavailable=true;
        try {
            for (const flow of [
                {name:'PmReport',trigger:'pmGenerate',download:'pmDownload',stem:'DC_123'},
                {name:'BmReport',trigger:'btnDownload',download:'reportDownload',stem:'TE_123'},
                {name:'EvidenceMaking',trigger:'btnGenerate',download:'downloadLink',stem:'DCEvidence_123'},
                {name:'TeEvidenceMaking',trigger:'btnGenerate',download:'download',stem:'TE_Evidence_123'}
            ]) {
                const before=requests.length;
                await open(flow.name);
                const root=document.querySelector(`[data-workflow="${flow.name}"]`);
                const byId=id=>root.querySelector(`[data-id="${id}"]`);
                assert(requests.length===before,`${flow.name} fetched stored output on opening`);
                assert(!root.querySelector('.report-history'),`${flow.name} still exposes server history`);
                let previous=null;
                for(const format of ['xlsx','pdf']) {
                    byId('outputFormat').value=format;
                    const clicks=downloadClicks;
                    byId(flow.trigger).click();
                    await waitFor(()=>!byId(flow.trigger).disabled && byId(flow.download).hasAttribute('href'));
                    const link=byId(flow.download);
                    assert(link.href.startsWith('blob:') && link.download===`${flow.stem}.${format}`,`${flow.name} did not create a named local download`);
                    assert(liveUrls.has(link.href) && (!previous || !liveUrls.has(previous)),`${flow.name} leaked a regenerated report URL`);
                    assert(await (await nativeFetch(link.href)).text()===`TEST:${flow.stem}.${format}`,`${flow.name} changed file bytes`);
                    assert(downloadClicks===clicks,`${flow.name} downloaded before the separate Download action`);
                    const frame=byId('previewFrame');
                    assert(format==='pdf' ? frame.src===link.href : !frame.hasAttribute('src'),`${flow.name} PDF preview is not browser-local`);
                    link.click();
                    assert(downloadClicks===clicks+1,`${flow.name} Download action did not fire`);
                    previous=link.href;
                }
                window.__failWorkflowData=true;
                byId(flow.trigger).click();
                await waitFor(()=>!byId(flow.trigger).disabled);
                assert(root.querySelector('.status-error') && liveUrls.size===0,`${flow.name} retained old bytes or hid its generation error`);
                window.__failWorkflowData=false;
                Workflows.close();
                await open(flow.name);
                assert(!document.querySelector(`[data-id="${flow.download}"]`).hasAttribute('href'),`${flow.name} retained a closed dialog's report`);
                Workflows.close();
                assert(liveUrls.size===0,`${flow.name} leaked report bytes after closing`);
            }
            assert(!requests.some(request=>['History','Download'].includes(request.handler)), 'Reports still call server storage handlers');
            assert(errors.length===0,errors.join('; '));
            return 'PASS: all four local XLSX/PDF downloads with streamed Blob storage unavailable, filenames/bytes, browser-only PDF previews, separate Download actions, errors, replacement and close cleanup; no server storage requests';
        } finally {
            window.__streamedBlobUnavailable=false;
            window.__failWorkflowData=false;
            Workflows.close();
            liveUrls.forEach(revoke);
            URL.createObjectURL=create;URL.revokeObjectURL=revoke;
        }
    };
    window.runCleanupChecks = async () => {
        const fixtureFetch = window.fetch;
        const nativeCreateUrl = URL.createObjectURL;
        const nativeRevokeUrl = URL.revokeObjectURL;
        const objectUrls = new Set();
        let releaseSave;
        try {
            window.fetch = async () => new Response(JSON.stringify({ success: true }), {
                status: 503, headers: { 'content-type': 'application/json' }
            });
            let httpError = null;
            try { await Karte.requestJson('/test-http-error'); } catch (error) { httpError = error; }
            assert(httpError?.message.includes('503'), 'A failed HTTP response was accepted as a successful result');

            const controller = new AbortController();
            window.fetch = async () => ({
                ok: true,
                async json() { controller.abort(); return { success: true }; }
            });
            let abortError = null;
            try { await Karte.requestJson('/test-cancelled-json', { signal: controller.signal }); }
            catch (error) { abortError = error; }
            assert(abortError?.name === 'AbortError', 'Cancellation during JSON decoding accepted a stale result');

            const saveGate = new Promise(resolve => { releaseSave = resolve; });
            let savePending = false;
            window.fetch = async (input, options) => {
                const url = new URL(input, location.href);
                if (url.pathname === '/ActionCards' && url.searchParams.get('handler') === 'Save') {
                    savePending = true;
                    await saveGate;
                }
                return fixtureFetch(input, options);
            };
            await open('ActionCards');
            const saveButton = document.querySelector('[data-id="btnSave"]');
            saveButton.click();
            await waitFor(() => savePending);
            await open('OcapViewer');
            const searches = requests.filter(request => request.handler === 'Search').length;
            releaseSave();
            await waitFor(() => !saveButton.disabled);
            assert(document.getElementById('workflowDialog').open && document.querySelector('[data-workflow="OcapViewer"]'),
                'A late save closed the replacement workflow');
            assert(requests.filter(request => request.handler === 'Search').length === searches,
                'A cancelled save refreshed Monitoring');
            window.fetch = fixtureFetch;

            URL.createObjectURL = blob => {
                const url = nativeCreateUrl.call(URL, blob);
                objectUrls.add(url);
                return url;
            };
            URL.revokeObjectURL = url => {
                objectUrls.delete(url);
                nativeRevokeUrl.call(URL, url);
            };
            await open('BmReport');
            const button = document.querySelector('[data-id="btnDownload"]');
            const download = document.querySelector('[data-id="reportDownload"]');
            button.click();
            await waitFor(() => !button.disabled && download.hasAttribute('href'));
            const firstUrl = download.href;
            assert(objectUrls.has(firstUrl), 'The generated report has no local object URL');
            button.click();
            await waitFor(() => !button.disabled && download.hasAttribute('href'));
            assert(!objectUrls.has(firstUrl) && objectUrls.size===1, 'Regenerating a report leaked its previous object URL');
            Workflows.close();
            assert(objectUrls.size === 0, 'Closing a report retained its object URL');
            assert(errors.length === 0, errors.join('; '));
            return 'PASS: HTTP errors, cancellation during JSON decoding, late saves, and report object URL cleanup';
        } finally {
            releaseSave?.();
            Workflows.close();
            window.fetch = fixtureFetch;
            URL.createObjectURL = nativeCreateUrl;
            URL.revokeObjectURL = nativeRevokeUrl;
            objectUrls.forEach(url => nativeRevokeUrl.call(URL, url));
        }
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
    function summaryRows(root) { return root.querySelectorAll('.ab-table tbody tr, .ms-grid tbody tr, .bma-table tbody tr:not(.bma-total-row)'); }
    async function openMachinePanel(root) {
        root.querySelector('.ab-alert, .ms-active, .bma-cell-active').click();
        await waitFor(() => root.querySelector('.summary-machine-item'));
    }
    function styleValues(node, properties) {
        const style = getComputedStyle(node);
        return properties.map(property => style[property]);
    }
    function assertSummaryPlaceholders(root, name) {
        const cells = root.querySelectorAll('.ab-cell:not(.ab-cell-na), .ms-cell:not(.ms-na), .ms-td-rowtotal, .ms-tf-cell, .ms-tf-grand, .bma-cell:not(.bma-cell-na), .bma-row-total, .bma-col-total, .bma-grand-total');
        assert(cells.length>0 && [...cells].every(cell=>cell.textContent==='—'), `${name} shows counts before its search finishes`);
    }
    window.runSummaryChecks = async () => {
        let panelStyle = null;
        const drillTimings = [];
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
                const apiDate = value => /^\d{2}\//.test(value) ? value : toApiDate(value.replace(' ','T'));
                assert(apiDate(filter.dateFrom)===buildFilter().dateFrom && apiDate(filter.dateTo)===buildFilter().dateTo, `${name} did not use Monitoring dates/times`);
                assert(!('process' in filter) && !('location' in filter) && !('machine' in filter), `${name} inherited Monitoring selections`);
                assert((filter.user || filter.userMode || filter.mode || 'PM')===(name==='AlertBoard'?'PM':'EEE'), `${name} lost its user mode`);
                assert(!root.querySelector('[data-id="summaryContext"], [data-id="lblStatus"], [data-id="msStatus"], [data-id="boardStatus"]'), `${name} retained context/status labels`);
                assert(summaryRows(root).length === 12, `${name} grid was blank before data arrived`);
                assert(button.disabled && !loading.hidden && root.getAttribute('aria-busy') === 'true', `${name} missing loading state`);
                assertSummaryPlaceholders(root, name);
                assert(!root.querySelector('.ab-progress, .ms-progress'), `${name} still uses a line progress indicator`);
                const firstCell = root.querySelector('[data-cell], .ms-grid tbody [data-location], .bma-table [data-key]');
                const before = requests.length;
                button.click();
                window.dispatchEvent(new Event('monitoring:refresh'));
                await tick();
                assert(requests.length === before, `${name} submitted another search while loading`);
                assertSummaryPlaceholders(root, name);
                releaseSummaryData();
                await waitFor(() => !button.disabled);
                assert(loading.hidden && root.getAttribute('aria-busy') === 'false', `${name} did not finish loading`);
                assert(root.contains(firstCell), `${name} replaced its grid instead of filling values`);
                if (name === 'AlertBoard') {
                    assert([...summaryRows(root)].map(row=>row.cells[0].textContent).join(',')===processes.join(','), 'Form2 process order differs from the original');
                    assert(root.querySelectorAll('tbody .ab-cell-active').length===54 && root.querySelectorAll('tbody .ab-cell-na').length===54, 'Form2 white/gray matrix differs from the original');
                    for (const [key,column,count] of [['FL_5',8,4],['EDS_1',9,4],['DB_6',7,8]]) {
                        const cell = root.querySelector(`[data-cell="${key}"]`);
                        assert(cell.cellIndex===column && cell.textContent===String(count), `${key} count is under the wrong floor`);
                    }
                    for (const row of summaryRows(root)) assert([...row.querySelectorAll('.ab-cell-active')].reduce((sum,cell)=>sum+Number(cell.textContent),0)===Number(row.lastElementChild.textContent), 'Form2 process total disagrees with its cells');
                    assert(root.querySelector('[data-id="grandTotal"]').textContent==='107', 'Form2 sample grand total differs from the original screenshot');
                    assert(getComputedStyle(root.querySelector('.ab-cell-na')).backgroundColor==='rgb(211, 211, 211)', 'Form2 unavailable cells lost LightGray');
                }
                const beforeDrill = requests.length;
                const drillStart = performance.now();
                await openMachinePanel(root);
                await nextPaint();
                assert(requests.length === beforeDrill, `${name} re-queried machines already present in its summary`);
                drillTimings.push(`${name} ${Math.round(performance.now()-drillStart)}ms`);
                const panel = root.querySelector('.summary-machine-panel');
                {
                    const child = panel.getBoundingClientRect();
                    const parent = document.getElementById('workflowDialog').getBoundingClientRect();
                    assert(Math.abs(child.left+child.width/2-parent.left-parent.width/2)<1 && Math.abs(child.top+child.height/2-parent.top-parent.height/2)<1, `${name} drilldown is not centered in its parent dialog`);
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
                await openMachinePanel(root);
                root.querySelector('.summary-toolbar').click();
                assert(getComputedStyle(panel).display === 'none' && document.getElementById('workflowDialog').open, `${name} outside click did not dismiss only the drilldown`);
                window.__failWorkflowData = true;
                button.click();
                await waitFor(() => !button.disabled);
                assert(!root.querySelector('.summary-error').hidden && root.querySelector('.summary-error').textContent.includes('Connection unavailable'), `${name} missing error feedback`);
                assert(summaryRows(root).length === 12 && loading.hidden, `${name} lost its grid or stayed loading on error`);
                window.__failWorkflowData = false;
                button.click();
                await waitFor(() => !button.disabled);
                assert(root.querySelector('.summary-error').hidden, `${name} failed to recover`);
                if (name !== 'BmAnalytics') {
                    const originalNow = Date.now;
                    let releaseDrill;
                    let releaseReplacement;
                    try {
                        let advancedNow = originalNow() + 31000;
                        Date.now = () => advancedNow;
                        window.__summaryDrillGate = new Promise(resolve => { releaseDrill = resolve; });
                        const active = root.querySelector('.ab-alert, .ms-active');
                        const beforeExpired = requests.length;
                        active.click();
                        active.click();
                        await tick();
                        assert(requests.length === beforeExpired + 1, `${name} did not expire its snapshot or deduplicate its drilldown`);
                        panel.querySelector('.summary-panel-close').click();
                        releaseDrill();
                        await tick();
                        assert(!panel.querySelector('.summary-machine-item'), `${name} accepted a late response after drilldown dismissal`);
                        window.__summaryDrillGate = null;
                        window.__failWorkflowData = true;
                        active.click();
                        await waitFor(() => panel.textContent.includes('Connection unavailable'));
                        window.__failWorkflowData = false;
                        await openMachinePanel(root);
                        assert(requests.length === beforeExpired + 3, `${name} retained a failed drilldown request`);
                        panel.querySelector('.summary-panel-close').click();

                        window.__summaryDrillGate = new Promise(resolve => { releaseDrill = resolve; });
                        const beforeReplacement = requests.length;
                        active.click();
                        button.click();
                        await waitFor(() => !button.disabled);
                        advancedNow += 31000;
                        window.__summaryDrillGate = new Promise(resolve => { releaseReplacement = resolve; });
                        active.click();
                        assert(requests.length === beforeReplacement + 3, `${name} reused a drilldown from the previous search`);
                        releaseDrill();
                        await tick();
                        assert(!panel.querySelector('.summary-machine-item'), `${name} displayed a previous search's late drilldown`);
                        active.click();
                        assert(requests.length === beforeReplacement + 3, `${name} cleared a newer pending request when an older one finished`);
                        releaseReplacement();
                        await waitFor(() => panel.querySelector('.summary-machine-item'));
                    } finally {
                        releaseDrill?.();
                        releaseReplacement?.();
                        window.__summaryDrillGate = null;
                        window.__failWorkflowData = false;
                        Date.now = originalNow;
                        panel.querySelector('.summary-panel-close').click();
                    }
                }
                {
                    for (const userType of ['PM','CC','EEE','VIS','TE']) {
                        holdSummaryData();
                        await open(name, {...query, userType, process:'WB', location:'LSI2-3F', machine:'OTHER-MC'});
                        const filteredRoot = summaryRoot();
                        const rowLabels = () => [...summaryRows(filteredRoot)].map(row => row.cells[0].textContent).join(',');
                        assert(rowLabels()===processes.join(','), `${name} ${userType} lost process rows while loading`);
                        assertSummaryPlaceholders(filteredRoot, `${name}/${userType}`);
                        releaseSummaryData();
                        await waitFor(() => filteredRoot.getAttribute('aria-busy')==='false');
                        assert(rowLabels()===processes.join(','), `${name} ${userType} filters changed the process list`);
                        if (userType==='TE') {
                            const dc = summaryRows(filteredRoot)[0];
                            assert(!dc.querySelector('.ab-alert, .ms-active, .bma-cell-active') && dc.querySelector('.ab-cell-na, .ms-na, .bma-cell-na'), `${name} queries unsupported TE processes`);
                            const whiteCells = filteredRoot.querySelectorAll('tbody .ab-cell-active, tbody .ms-cell:not(.ms-na), tbody .bma-cell:not(.bma-cell-na)');
                            assert(whiteCells.length===54, `${name} TE designer white cells missing: ${whiteCells.length}/54`);
                            const firstWhite = dc.querySelector('.ab-cell-active, .ms-cell:not(.ms-na), .bma-cell:not(.bma-cell-na)');
                            assert(firstWhite.textContent==='0' && getComputedStyle(firstWhite).backgroundColor==='rgb(255, 255, 255)', `${name} TE dormant process lost its white zero cell`);
                            assert(filteredRoot.querySelector('.ab-alert, .ms-active, .bma-cell-active')?.textContent==='1', `${name} TE supported process lost its count`);
                        }
                    }
                }
                Workflows.close();
                await tick();
            }
            assert(errors.length === 0, errors.join('; '));
            return 'PASS: three summary layouts, spinner/search locking, in-place values, keyboard selection, TE modes, failure/retry; 30s snapshot expiry, drilldown request deduplication, late-response isolation; cached drilldowns use 0 AJAX requests. Fixture click-to-paint (includes test polling): ' + drillTimings.join(', ');
        } finally {
            releaseSummaryData();
            window.__failWorkflowData = false;
        }
    };
    window.prepareSummaryCapture = async (name, state, userType='PM') => {
        releaseSummaryData();
        if (state === 'loading') holdSummaryData();
        await open(name, {...query, userType});
        const root = summaryRoot();
        if (state === 'panel') {
            await waitFor(() => root.getAttribute('aria-busy') === 'false');
            await openMachinePanel(root);
            root.querySelector('.summary-machine-item').click();
        }
        const toolbar = root.querySelector('.summary-toolbar');
        toolbar.scrollIntoView({block:'nearest'});
        const button = toolbar.querySelector('.summary-search button').getBoundingClientRect();
        assert(button.right <= toolbar.getBoundingClientRect().right + 1, `${name} search overflows`);
        assert(Math.abs(button.right - (toolbar.getBoundingClientRect().right - 8)) < 2, `${name} search is not at the right`);
        assert(!toolbar.querySelector('.summary-context, .summary-status'), `${name} retained context/status labels`);
        assert(document.documentElement.scrollWidth <= innerWidth + 1, `${name} overflows the viewport`);
        if (state === 'panel') root.querySelector('.summary-machine-panel').scrollIntoView({block:'nearest'});
        await nextPaint();
        return `${name} ${state} ${innerWidth}px: PASS`;
    };
    window.prepareWorkflowCapture = async name => {
        if (document.getElementById('machineListSidebar').open) closeSidebar();
        if(name==='Monitoring' || name==='MonitoringOverflow') {
            Workflows.close();
            window.__monitoringStairs=true;
            window.__monitoringCounts = name==='MonitoringOverflow' ? [52,27,12,9,7,5,4,3,2,1] : undefined;
            await doSearch(1); await tick();
            const scroller=document.getElementById('cardScroller');
            const header=document.querySelector('.mc-col-header').getBoundingClientRect();
            assert(Math.abs(scroller.scrollTop+scroller.clientHeight-scroller.scrollHeight)<1 && Math.abs(header.top-scroller.getBoundingClientRect().bottom)<1, `${name} cards are not bottom-aligned above frozen bottom headers at ${innerWidth}px`);
            assert([...document.querySelectorAll('.machine-col')].every((col,index)=>Math.abs(col.getBoundingClientRect().left-document.querySelectorAll('.mc-col-header')[index].getBoundingClientRect().left)<1), `${name} headers are misaligned at ${innerWidth}px`);
            window.__monitoringCounts=undefined;
        }
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
