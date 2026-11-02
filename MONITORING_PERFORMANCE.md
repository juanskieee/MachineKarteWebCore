# Monitoring performance audit

Scope: Form2/AlertBoard, Form11/MachineSummary, and Form12/BmAnalytics;
the existing Form8 profile is also preserved. Application changes only.

Database-safety scope clarification (2026-11-02): the PASS statements below
refer to the summary transformations, not the entire application or every test
request. Monitoring's browser-initiated TrackSession POST attempts INSERT/UPDATE
of `MachineKarte_PCStatus` after page load; initial Monitoring HTML performs no
SQL. Action Card saves write shared legacy tables. The browser check mocks the
tracking POST along with other AJAX handlers. Earlier checks before the startup
fix could attempt tracking during HTML requests. See `DATABASE_AUDIT.md` for the full repository and
available execution-history audit, including connection failures, LocalDB-only
fixture writes, and the original Form2 duplicate-card failure. That audit did
not connect to SQL or execute its prepared SELECT-only diagnostics.

The user subsequently supplied diagnostic results from the legacy database:
PM BM `256152`, machine `SW-18`, has two matching Action Cards (`Seqno`
55110/55111). This confirms a duplicate lookup that breaks original Form2
when the BM matches its filters. The web summary SELECT corrections tolerate
these rows; they neither remove them nor repair desktop saves. Both applications
originally INSERTed without a duplicate guard. Web saves now use atomic guarded
INSERTs (`UPDLOCK, HOLDLOCK`), reject stale New forms, and update only the loaded
Seqno. TE guards match BM ID alone; other modes also match machine. Desktop saves
remain unprotected. The originating application remains unproven, and no live
data repair has been applied. The save fix is separate from the summary changes.

## Startup responsiveness

The supplied IIS Express debugger output shows unavailable-SQL and pooled
connection waits, including PC tracking awaited by Monitoring's initial GET
and synchronous OCAP connection opens. The module-loading messages alone do
not measure startup duration. The correction is limited to those request paths:

- Initial Monitoring HTML has no database work. The browser sends the existing
  optional tracking data through an antiforgery-protected POST once window load
  completes. That handler awaits its own work; no detached task or queue is added.
- Tracking passes a linked two-second cancellation budget through connection
  open and both commands, and skips optional tracking on failure.
- OCAP keeps its SQL and response fields, using asynchronous opens, command
  execution and row reads. Request cancellation propagates through both reads,
  allowing a closed dialog's request to abandon pending connection/query waits.

`scripts/Test-Startup.ps1` launches a separate app whose connection points to a
loopback TCP listener that never speaks SQL. The eight-second connection wait
and one-slot pool are deliberate fixtures. The runner never contacts a SQL
server or changes the configured application connection string.

| Measurement | Before | After |
|---|---:|---:|
| Process launch to listening | 838.2 ms | 845.4 ms |
| First Monitoring HTML, unavailable SQL fixture | 8,395.8 ms | 220.4 ms |
| Monitoring while optional tracking waits | Unmeasured | 12.4 ms |
| Monitoring while OCAP waits | Unmeasured | 7.8 ms |
| Best-effort tracking POST | Unmeasured | 2,025 ms |

These are local direct-host measurements, including first-request rendering/JIT.
They are not Visual Studio F5/IIS Express timings, production measurements, or
a claim that SQL-dependent data is available off the intranet. Build checks,
31 workflow action contracts/schema/status fixtures, and desktop/mobile browser
checks passed. Browser fixtures assert one after-load tracking POST; the startup
runner also checks real antiforgery rejection and valid tracking completion.

Cancellation follows the [SqlClient async API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.openasync).
The SQL/schema and report business rules are unchanged by this startup fix.

The three summaries now inherit only the selected user and open with their own
today -30 days through tomorrow window. Monitoring process/location/machine/date
selections do not constrain them. Context and update-time labels are removed;
loading and failure/retry feedback remain.

Form2's counting port has been corrected against the actual checked-in VB
routines: duplicate-row copying/merge, formatted-date ordering, whole-day date
comparisons, one content branch per group head, and TE's three-row lookahead.
ATC/SING retain the original empty font-color result, allowing their repeats to count.
Its 54 white cells and drilldowns share the designer mapping; totals sum the
displayed cells once. The legacy ten-location query sequence, including the
repeated `LSI3-1F`, is retained. This increases Form2 reads from 108/45 to 120/50
(normal/TE); the four-slot scheduler remains unchanged. The earlier batching
fixtures described below compared the previous counting implementation with
itself and did not establish parity with the VB business logic.

AlertBoard and BmAnalytics now use `EXISTS` for action/MCD flags and select
one latest action status with `OUTER APPLY TOP (1) ORDER BY Seqno DESC`.
Duplicate cards or active MCD entries therefore cannot raise scalar-subquery
error 512 or multiply BM rows. User, machine/tester, date/status and normalized
active-MCD filters are retained. The earlier SQL-unchanged statements below
refer to the batching optimization; these duplicate-record corrections are
separate. `BMContentID` remains a primary-key lookup.

## Evidence and timing limits

Production SQL access is unavailable in this environment. No production query
duration, CPU breakdown, payload size, or end-to-end speedup is claimed. The
checks below use fixed inputs and local browser AJAX fixtures.

| Stage | Finding and measurement status |
|---|---|
| SQL | Each process/location query previously awaited the previous query. Batching preserves the read sequence; current Form2 read counts reflect the parity correction above. Actual SQL execution time is unmeasured. Connection acquisition, command execution, and row transfer must be measured separately on an authorized live-data environment. |
| Repository processing | AlertBoard maps every row and previously scanned reader column names for `Priority` on every row. This scan now runs once per reader. Real materialization time is unmeasured; `ReadAsync` time also includes network/server waits and must not be presented as pure mapping time. |
| DTO construction | AlertBoard repeat/color logic, MachineSummary row counts plus distinct lists, and BmAnalytics qualification/totals remain in their original aggregation order. Eleven before/after fixtures produced exactly equal serialized results and read inputs. Production DTO construction time is unmeasured. |
| Serialization | Handlers use `JsonResult` and the existing AJAX envelopes. Response fields and serializer settings are unchanged. Actual serialization time and response bytes are unmeasured. |
| UI rendering | All three grids fill existing cells; MachineSummary/BmAnalytics already rebuild layout only when the mode changes. Machine lists now use a detached document fragment and one DOM replacement. Desktop/mobile browser checks passed. Browser fixture click-to-paint timings include test polling and are not production render benchmarks. |
| Drilldown generation | AlertBoard previously rebuilt the entire board; MachineSummary repeated a cell query and distinct mapping. Both now use the loaded summary for up to 30 seconds, then fall back to their existing endpoints. Browser checks assert zero AJAX calls on the snapshot path, expiry, concurrent-request deduplication, failure/retry, and late-response isolation. BmAnalytics already used its loaded result. |

The scheduling check measured 16 independent simulated 30 ms reads:
**535.7 ms sequential versus 134.3 ms bounded parallel (3.99x)**. This measures
the scheduler with artificial I/O, not SQL Server performance. Under uniform
query times with spare database capacity, approximately fourfold reduction in
the query-wait portion is an upper-bound expectation, not a throughput promise.

The final local browser fixture run measured cached click-to-paint at 19 ms
(AlertBoard), 34 ms (MachineSummary), and 32 ms (BmAnalytics), including the test
helper's polling and paint wait. All three made zero drilldown AJAX requests.
These one-machine fixture measurements do not characterize large live lists.

| Workflow/profile | Queries per current summary | Batches of at most four |
|---|---:|---:|
| AlertBoard normal/CC/EEE/VIS | 120 | 30 |
| AlertBoard TE | 50 | 13 |
| MachineSummary EEE | 52 | 13 |
| MachineSummary TE | 17 | 5 |
| BmAnalytics Form12 EEE | 52 | 13 |
| BmAnalytics Form12 TE | 17 | 5 |
| BmAnalytics Form8 EEE | 38 | 10 |
| BmAnalytics Form8 TE | 14 | 4 |

There is one shared four-slot semaphore per application worker for these summary
batches. Each query owns its connection/command/reader. Multiple workers have
separate limits. SQL traffic is bounded, but parallel reads can still increase
instantaneous database load; live concurrency/load validation remains required.
Existing standalone drilldown endpoints retain their behavior.

## Findings

PASS below means the application transformation preserves contracts and results
for the same fixed read inputs, or the existing behavior was retained. It does
not replace the live legacy-parity sign-off tracked in `CONTEXT.md`.

### 1. Sequential summary queries

- **LOCATION:** `Repositories/AlertBoardRepository.cs::LoadBoardAsync`, `Services/MachineSummaryService.cs::BuildSummaryAsync`, `Services/BmAnalyticsService.cs::BuildGridAsync`.
- **CURRENT BEHAVIOR:** Nested process/location loops originally executed one independent read at a time.
- **BOTTLENECK:** Every round trip added to elapsed summary latency.
- **ESTIMATED IMPACT:** High.
- **SAFE OPTIMIZATION:** Implemented `MonitoringQueryBatch.ReadAsync`: `Task.WhenAll` within batches of four, shared read slots, and ordered sequential aggregation. Raw rows are buffered in batches rather than collecting the entire date range before processing. Failures propagate after the current batch finishes; slots release in `finally`.
- **RISK:** Medium. Higher instantaneous read load and different read timing; the previous implementation also had no cross-query snapshot transaction. Measure under representative concurrent usage.
- **LEGACY COMPATIBILITY:** PASS. Same process/location inputs, SQL, filters, grouping, count/list semantics, and aggregation order; eleven exact serialized-response fixtures passed.
- **DATABASE SAFETY:** PASS. Existing reads and isolation retained; no database object, SQL-text, hint, write, or schema changes.

### 2. AlertBoard rebuilds its board for a drilldown

- **LOCATION:** `Assets/js/AlertBoard.js::openDrilldown`, `Controllers/AlertBoardController.cs::DrilldownAsync`, `Services/AlertBoardService.cs::LoadBoardAsync/GetDrilldown`.
- **CURRENT BEHAVIOR:** The handler rebuilds the full board even though `LoadBoard` already returned `cells[key].machines`.
- **BOTTLENECK:** Another 120 or 50 reads plus repeat detection, mapping, aggregation, serialization, and network latency for one list.
- **ESTIMATED IMPACT:** High.
- **SAFE OPTIMIZATION:** Implemented a modal-local 30-second summary snapshot. Use its machine list and the existing cell-title mapping; after expiry, use the original handler. Each Search/refresh discards the snapshot; close releases workflow timers/references. No extra machine-list payload was added.
- **RISK:** Low. The list intentionally matches the displayed summary within the allowed temporary-cache window; it is not a fresh database read during those 30 seconds. Fresh-read fallback remains available afterward.
- **LEGACY COMPATIBILITY:** PASS. Exact summary machine values, order, case handling, titles, and Monitoring navigation retained. Cache freshness follows the explicitly permitted temporary snapshot policy.
- **DATABASE SAFETY:** PASS. Snapshot drilldowns execute no SQL; fallback SQL is unchanged.

### 3. MachineSummary repeats work already included in its response

- **LOCATION:** `Assets/js/MachineSummary.js::search/onCellClick`, `Controllers/MachineSummaryController.cs::MachineListAsync`, `Services/MachineSummaryService.cs::GetCellAsync`.
- **CURRENT BEHAVIOR:** The grid response contains `rows[].cells[location].machines`, but clicking repeats that cell query and case-insensitive `Distinct` mapping.
- **BOTTLENECK:** Avoidable repository work, DTO construction, and an AJAX round trip.
- **ESTIMATED IMPACT:** Medium.
- **SAFE OPTIMIZATION:** Implemented a 30-second modal-local snapshot indexed by process and location. Expiry or missing machine data uses the original handler. New searches and timer refreshes invalidate the snapshot before loading.
- **RISK:** Low. The same temporary freshness window as AlertBoard applies.
- **LEGACY COMPATIBILITY:** PASS. Row counts remain separate from distinct machine lists; first-seen ordering, filters, titles, and navigation are unchanged.
- **DATABASE SAFETY:** PASS. No SQL on snapshot clicks; unchanged reads after expiry.

### 4. Duplicate fallback requests on repeated clicks

- **LOCATION:** `Assets/js/AlertBoard.js::requestDrilldown`, `Assets/js/MachineSummary.js::requestCell`; all three summary search/refresh entry points.
- **CURRENT BEHAVIOR:** Searches already disable their Search button and reject overlapping Search/timer calls. Repeated cell clicks previously started duplicate drilldown requests.
- **BOTTLENECK:** Duplicate reads and rendering attempts for the same request.
- **ESTIMATED IMPACT:** Medium.
- **SAFE OPTIMIZATION:** Retained search guards. Added modal-local maps of pending drilldown promises keyed by full endpoint URL, including mode/user, dates, process/location or cell key. Remove entries on success or failure and clear them on Search/refresh. Completion of an older request cannot remove a newer map entry. Existing sequence guards discard late responses; workflow abort signals still apply.
- **RISK:** Low. Only identical pending reads are shared; failed requests can retry immediately.
- **LEGACY COMPATIBILITY:** PASS. No cross-user/mode/date/profile reuse; Search and refresh still request fresh summaries. Action Card operations are not cached or deduplicated.
- **DATABASE SAFETY:** PASS. Removes redundant read requests without altering SQL.

### 5. Rebuilt static metadata

- **LOCATION:** `Services/AlertBoardService.cs::ResolveCellTitle`, `Assets/js/AlertBoard.js::resolveProcessLocation`, `Models/MachineSummaryModels.cs::MachineSummaryConfig`, `Models/BmAnalyticsModels.cs::BmAnalyticsMappings`, the three summary Razor partials.
- **CURRENT BEHAVIOR:** Query mapping arrays were already static. AlertBoard recreated title/navigation maps on drilldown; Form11/Form8/Form12 partials rebuilt and serialized designer metadata whenever opened.
- **BOTTLENECK:** Repeated small allocations and layout serialization.
- **ESTIMATED IMPACT:** Low.
- **SAFE OPTIMIZATION:** Server title maps and AlertBoard display arrays are now static; the navigation map is created once per modal. Designer layout JSON is computed once per application profile and reused. Per-user antiforgery markup and AJAX results remain request-specific.
- **RISK:** Low. Static metadata contains no user data; changing it requires the same application restart as other compiled mappings.
- **LEGACY COMPATIBILITY:** PASS. Mode-specific query locations, Form8/Form12 differences, designer white cells, process ordering, and `ATC_65` retained.
- **DATABASE SAFETY:** PASS. No database access involved.

### 6. Per-item mutations of a live machine list

- **LOCATION:** `Assets/js/AlertBoard.js::openDrilldown`, `Assets/js/MachineSummary.js::onCellClick`, `Assets/js/BmAnalytics.js::openFlyout`.
- **CURRENT BEHAVIOR:** Each list item was appended directly to the visible list. Grid layouts and delegated cell handlers were already reused.
- **BOTTLENECK:** Many live DOM mutations for large drilldowns. No alternating layout reads/writes were found, so a forced reflow per append is not claimed.
- **ESTIMATED IMPACT:** Medium for large lists; Low for small lists.
- **SAFE OPTIMIZATION:** Implemented detached `DocumentFragment` construction followed by one `replaceChildren`. Existing selection handlers and keyboard semantics are retained.
- **RISK:** Low.
- **LEGACY COMPATIBILITY:** PASS. Same labels, item ordering, selection, double-click behavior where supported, panel dismissal, and navigation; desktop/mobile browser checks passed.
- **DATABASE SAFETY:** PASS. Client rendering only.

### 7. Repeated reader-schema scans

- **LOCATION:** `Repositories/AlertBoardRepository.cs::FetchRowsAsync`.
- **CURRENT BEHAVIOR:** Every materialized row scanned column names to determine whether TE `Priority` exists.
- **BOTTLENECK:** Repeated metadata lookup proportional to the number of fetched rows.
- **ESTIMATED IMPACT:** Low.
- **SAFE OPTIMIZATION:** Implemented one `HasColumn("Priority")` check per reader and reused its result. Removed the unused `ColumnCount` shim.
- **RISK:** Low. A reader's schema is fixed for its result set.
- **LEGACY COMPATIBILITY:** PASS. Same values and null behavior; no change to repeat/priority rules or selected columns.
- **DATABASE SAFETY:** PASS. Existing SQL is unchanged.

### 8. Payload size, serialization, and remaining CPU work

- **LOCATION:** `Models/AlertBoardModels.cs`, `Models/MachineSummaryModels.cs`, `Models/BmAnalyticsModels.cs`; the three summary controllers; `AlertBoardRepository.ApplySolveLogic/ApplyColorNormal/ApplyColorTE`, `BmAnalyticsService.ApplyNewLoadColor`.
- **CURRENT BEHAVIOR:** Summary responses contain machine lists and some repeated/unused metadata. AlertBoard has nested repeat comparisons and repeated date parsing. BmAnalytics performs list membership checks while preserving row counts. Standalone AlertBoard/BmAnalytics drilldown handlers still rebuild full results.
- **BOTTLENECK:** Potential serialization/transfer, CPU, and expired-snapshot drilldown costs; their actual shares are unmeasured.
- **ESTIMATED IMPACT:** Low for small responses; potentially Medium/High for large date ranges, pending profiling.
- **SAFE OPTIMIZATION:** Retained payload contracts and all business calculations. Existing machine lists now serve the snapshot path. Do not remove those lists while using that optimization. Measure payload bytes, JSON execution/write time, and CPU first; consider transport compression or a contract-preserving collection optimization only if measurements justify it. Do not narrow fallback queries without proving identical cross-cell results.
- **RISK:** Medium for unmeasured follow-up changes; no such changes implemented.
- **LEGACY COMPATIBILITY:** PASS. Existing response fields, SQL, joins, repeat detection, MCD matching, and action logic retained.
- **DATABASE SAFETY:** PASS. No query rewrite, new isolation mode, or database deployment proposed.

## Validation and remaining work

- `dotnet build MachineKarteWebCore.csproj --nologo -v minimal`.
- `powershell -ExecutionPolicy Bypass -File scripts/Test-MonitoringQueries.ps1`:
  schema/status fixtures, four-slot scheduling, ordering, failure/retry, and
  Form12 qualifying-row counts versus distinct machine lists.
- `powershell -ExecutionPolicy Bypass -File scripts/Test-ActionSummaryParity.ps1`:
  1,250 fixtures compared with compiled Form2 VB routines, 54 designer white cells,
  query-location sequence, and displayed-column totals. No database is required.
- `powershell -ExecutionPolicy Bypass -File scripts/Test-SummaryDuplicates.ps1`:
  actual SQL Server LocalDB execution against session-local temporary tables;
  reproduces both old error 512 cases and checks 70 corrected summary query
  variants with duplicate cards/MCDs, correlation differences and row counts.
- `powershell -ExecutionPolicy Bypass -File scripts/Test-Workflows.ps1`:
  real Razor/assets with AJAX fixtures, all summary modes, loading/error/retry,
  snapshot reuse/expiry, pending-request deduplication, late responses, keyboard
  behavior, desktop/mobile captures, and existing workflow regressions.
- A temporary before/after runner compared the original source captured before
  this task with the changed orchestration. All eleven summary/profile fixtures
  had exactly equal JSON and read inputs, including duplicates and case variants.
- MachineSummaryRepository remains unchanged. AlertBoard and BmAnalytics SQL
  include the duplicate-record corrections above. Form2 repeat/color/cell/totals
  methods are verified separately against compiled VB routines.

Live SQL execution, repository/DTO CPU attribution, serialization bytes/time,
large-list responsiveness on real data, and concurrent-user database load remain
unmeasured. Complete those checks before claiming production speedup or 100%
live legacy parity. Form2's duplicate-location sequence now matches the original;
its query results still require comparison against live legacy data.
