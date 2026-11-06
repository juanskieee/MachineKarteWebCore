# Database behavior and performance

See the [application guide](README.md) for workflow contracts and the complete
validation index. [Reporting](REPORTS.md) covers report data and template rules.

## Connections and writes

Repositories use `ConnectionStrings:DBConnection`. Some queries explicitly name
`eMaintenanceSystemDB`, so changing only the initial catalog on the same server
does not isolate every query. Use a separate test instance or the existing
isolated fixture runners for database testing.

| Path | Database effect |
| --- | --- |
| Monitoring initial HTML | No database work |
| `POST /Monitoring?handler=TrackSession` after browser load | May insert/update `MachineKarte_PCStatus`, keyed by the web host's machine name |
| Action Card Save | May insert/update the PM/TE/CC/EEE/VIS `MachineKarte_ActionCard*` table selected by user mode |
| Summaries, dashboards, OCAP, and report/evidence retrieval | Reads; Monitoring paging materializes grouped counts in a session-local temporary table; generated reports are returned directly to the browser without output-share writes |
| RawData | Shared recurrence CTEs; reads only, with no temporary-table writes |
| `MonitoringQueryBatch` | Schedules supplied reads; contains no SQL or connection logic |

The application has no enforced read-only connection mode. It does not execute
`schema.sql`, deploy migrations, or repair existing duplicate data at startup.
The behavior of uninspected live views/functions/triggers is outside the source
inventory.

Tracking is optional, antiforgery-protected, and limited by a linked two-second
cancellation budget through connection acquisition and command execution. It
does not block initial HTML. OCAP and other read paths propagate request
cancellation through connection opens, commands, and row reads.

## Action Card identity and duplicate handling

[ActionCardRepository](../Repositories/ActionCardRepository.cs) loads the highest
matching `Seqno`. That identity travels through the dialog and Save request.
An edit matches `Seqno`, BM ID, and machine number, and must affect one row.
Invalid modes, missing identifiers, and mismatched identities are rejected.
Existing older duplicate cards are preserved.

New cards use one conditional INSERT with a `NOT EXISTS` check under
`UPDLOCK, HOLDLOCK`. The lookup key follows the desktop Form2 expectation:

- PM/CC/EEE/VIS: BM ID plus machine number.
- TE: BM ID alone, including cards for another tester number.

Concurrent web saves serialize at SQL Server; a conflicting New or stale form
is rejected without replacing an existing card's fields. The checked-in schema
has a `Seqno` primary key rather than uniqueness on these lookup keys. No new
index or constraint is deployed. Without a suitable lookup index, the guarded
INSERT can take broad locks; measure write throughput on representative data.

These protections cover the web application. The preserved desktop INSERT path
still lacks this duplicate guard. Web summary SELECTs use existence checks and
a deliberately selected latest action row so duplicate action/MCD rows do not
multiply maintenance rows or trigger a scalar-subquery error. Those reads do
not fix the separately deployed desktop application or remove stored duplicates.

## Unresolved legacy duplicate incident

User-supplied diagnostic results from 2026-11-02 established two PM Action Cards
for BM `256152`, machine `SW-18`:

| Seqno | Stored DateCard | Stored Incharge |
| --- | --- | --- |
| 55110 | `11/02/2026 10:56:11` | `7302` |
| 55111 | `11/02/2026 10:59:10` | `7302` |

Both displayed `RUNNING (ONGOING INVESTIGATION/MONITORING)`. This can break the
original Form2 scalar lookup when the BM falls within the selected filters.
TE's equivalent scalar lookup correlates on BM ID alone.

No live repair was applied. The supplied fields do not establish which client
inserted either card: DateCard and Incharge are form values, and both original
clients could insert duplicates. The complete records and database-side
attribution remain unverified. Preserve their contents for owner review before
choosing a repair; do not infer that the higher Seqno is redundant.

[Audit-LegacyRepeatBm.sql](../scripts/Audit-LegacyRepeatBm.sql) is the retained
SELECT-only diagnostic for connection identity, duplicate candidates in all
five tables, and visible trigger metadata. Its results must be correlated with
the actual failing filters. The earlier audit found no confirmed successful
production write in the captured development output; incomplete client-side
logs do not establish that no other write occurred.

## Read scheduling and query behavior

### Repository cleanup and measured changes (2026-11-06)

All twelve repositories use file-scoped namespaces, ordered imports, a consistent
connection field/constructor, and the same missing-configuration error.
`RepositorySql` contains shared connection lookup, typed varchar/date parameters,
and result-set metadata helpers. SQL filters and output mappings remain owned
by the repositories; there is no generic repository layer or new dependency.
User-entered Action Card text retains its Unicode parameter binding and atomic
insert/exact-row edit behavior.

`ImportantDeviceQuery` removes duplicated MCD/Machine Summary SQL and execution.
Form7 still selects DISTINCT machines; Form11 keeps repeated BM rows for counts.
Their TE action exclusion matches BMID plus tester number. Monitoring/AlertBoard
TE action existence uses BMID alone, while Analytics also matches the tester.
These screen-specific legacy differences are intentional.

AlertBoard reads all floors once per process: 12 commands instead of 120 for
normal modes, or 5 instead of 50 for TE. Each process result is partitioned by
floor and processed in the original sequence, including LSI3-1F twice. Color
state resets before each pass, and each floor keeps its own solved-date
reference, repeat grouping, counts, and machine-list order. The shared scheduler
buffers at most four process results; this trades larger per-query results for
fewer round trips. It does not cache data between requests.

AlertBoard and Analytics replace SQL FORMAT with native CONVERT expressions
that return the same `MM/dd/yyyy HH:mm:ss` strings. Typed DateTime inputs retain
the previous whole-second truncation; string date ranges preserve supplied
fractions. PM history's existing text/date comparison is retained.

RawData shares one adjacent-row CTE for both Form5 queries. Frequency filters
dates before LEAD/LAG; monthly totals filter dates afterward. Moving the monthly
filter into the CTE would lose valid boundary repeats. Both results retain the
original columns, SQL types, NULL handling, excluded maintenance cases, and
ordering, while removing duplicate SQL and temporary-table writes.

BM report mapping reuses its captured raw-field dictionary for optional fields;
TE evidence inspects available columns once per result. Missing optional fields
no longer require repeated exceptions. OCAP tables and evidence views are also
allowlisted at the repository boundary, alongside existing service validation.

`Test-DataRetrieval.ps1 -Benchmark` compares actual isolated reads. Original Form5
SQL matches both CTE results. Full AlertBoard results in five modes match the
former per-floor processing, including duplicated floors. A synthetic LocalDB
warm median over seven rotating runs uses the same SQL, data and date range to
isolate scheduling and batching:

| Schedule | Commands | Median |
| --- | --- | --- |
| Sequential legacy floor order | 120 | 65.5 ms |
| Previous web schedule, four floor reads at a time | 120 | 30.8 ms |
| Current schedule, four process reads at a time | 12 | 6.1 ms |

That fixture measures 5.03x against the previous web schedule and 10.71x against
sequential reads. It includes reads and color processing; it is not a production
or all-repository speedup. No 100x claim or new production index is implied.
Live plans, I/O, network waits,
view costs, and concurrent-user behavior still need representative measurement.

### Monitoring parity audit

The missing `TE / FT / LSI2-3F / All` cards came from the TE card query's
`a.Process = '{process}'` predicate: doubled C# interpolation braces sent the
literal `{process}` to SQL Server. Both card branches now bind `@Process`.
The summary could therefore find testers while their card loads and floor
counters returned no BMs.

The mapping in [MonitoringRepository](../Repositories/MonitoringRepository.cs)
follows the active desktop queries in `Form1.LoadMCName`,
`Form1.LoadMCName1`, and `uc_Test.LoadMCName`:

| Rule | Preserved behavior |
| --- | --- |
| TE eligibility | Categories 1 or 4; exclude statuses 30, 31, 1, 2, 3, 7, 17; nonzero BMUnitID joined to BMTEUnit |
| TE identity | TesterNo for machine selection; MCType for the requested column; Process and Location retained |
| TE actions | Match BMID alone, including an action saved under another tester number; choose highest Seqno when duplicates exist |
| PM / CC / EEE / VIS | Original status/turnover filters and category 1; cards join BMCase for PM or Why4/BMWhy for the other modes; actions match BMID and machine |
| Content / OCAP | Effective accredited content ID retains the original fallback; BMContent text and OCAP count still use the original BMContentID/BMCaseID respectively |
| Dates | Inclusive BETWEEN boundaries; SQL datetime parameters; format returned dates as invariant MM/dd/yyyy HH:mm:ss in C# |

The replaced `CONVERT(...,120)` produced ISO timestamps that the legacy repeat
and solved-action parsers did not accept. Returning typed dates restores their
expected strings without SQL FORMAT overhead. MCD uses EXISTS, preserving one
BM row even when multiple active device spellings normalize to the same name.

FilteredBM restricts maintenance rows before scoped LatestAction ranking and
process/case-specific OCAP aggregation. The shared query builder removes the
duplicated TE and non-TE SQL bodies and obsolete commented alternatives; the
original desktop SQL remains in `Legacy/` and is executed by the parity runner.

Paging uses a grouped CTE, preserves the old EXCEPT projection's distinctness,
and materializes it once in `#MonitoringMachineCounts`. ROW_NUMBER selects the
page, and a second result set reads the total from the same rows. This replaces
three copies of the maintenance aggregation. Stable tie breakers prevent tied
counts from moving between pages. For the web's All-floor option, TesterType
must match each BM's Location; accepting every floor's metadata inflated counts.
The legacy single-floor metadata joins and default action line 4 are retained.

`Test-MonitoringParity.ps1` executes the original uc_Test and Form1 SQL against
the actual repository bodies in a disposable LocalDB catalog: 53 card variants
and 106 All/specific summary variants, plus pagination, duplicate records,
status/category/type filters, dates, counters, repeat colors, and cancellation.
It never loads application connection settings. Add `-Benchmark` for a synthetic
comparison using 10,006 selected cards and 50,001 action rows. A warm median
across seven alternating runs was 482.2 ms for the original SQL and
119.0 ms for the CTE query (4.05x). This includes SQL reads/materialization and
excludes C# coloring; it is not a production benchmark or a 10x/100x guarantee.
CTEs alone do not guarantee materialization; only the paging temporary table
explicitly reuses its stored aggregation.

Further production tuning needs actual plans and STATISTICS IO/TIME for the
same filters and dates. Check maintenance filter indexes, action lookup keys
`(BMID, Seqno)` for TE or `(BMID, MachineNo, Seqno)` for other modes, and the
underlying gridBMOCAP view plan before choosing indexes. No indexes, schema
changes, or live data repairs are deployed by this change.

[MonitoringQueryBatch](../Repositories/MonitoringQueryBatch.cs) provides four
shared read slots per application worker for participating dashboards, cards,
and reports. Ordered batches bound raw-row buffering; queued waits honor
cancellation and slots release on failure. This is not a global limit across
all repository calls or deployed workers. Independent reads use separate
connections without a new cross-query snapshot transaction.

| Path | Current implementation |
| --- | --- |
| Monitoring | Parameterized filtered card CTEs, scoped action/OCAP lookups, one stored paging aggregation, typed dates; cancel superseded searches; cache reader metadata and group floor rows once |
| AlertBoard | Ordered, bounded process reads, then original per-floor processing; 120/50 commands reduced to 12/5 |
| MachineSummary / BmAnalytics | Ordered, bounded process/location reads; retain mode-specific filters, row counts, distinct-list order, and Form8/Form12 differences |
| McdMonitoring | Shared important-device query and read slots; valid DISTINCT ordering on projected machine/tester numbers |
| BM report | Main row plus one batch for VR numbers and nine fixed lookups: at most two commands instead of eleven |
| PM evidence | Five category reads in one batch, plus confirmation/header: three commands instead of seven |
| TE evidence | Batched category and quality reads: eight commands including metadata inspection instead of fifteen |
| PM report | Typed current/history records, one allowlisted parameterized lookup batch, and cancellation before workbook construction |
| RawData | Shared recurrence CTE, retained date-filter placement, async typed result-set reads, table allowlist, and the correct `MEMSBMMaintenance` name |
| Action Cards / OCAP | Cancellable loads; Action Card writes retain their completion and identity protections |

Command counts describe source/fixture behavior, not measured production speed.
Process/table identifiers come from server-side allowlists or fixed maps;
dropdown choices alone are not the validation boundary.

## Browser snapshots and rendering

AlertBoard, MachineSummary, and McdMonitoring reuse the loaded summary's machine
lists for up to 30 seconds. Snapshots are modal-local and invalidated by a new
search/refresh; expired or absent lists use the existing fallback endpoints.
BmAnalytics uses its current loaded grid result.

Pending fallback requests are keyed by the full URL, including user/mode, dates,
and cell identity. Identical pending reads share one promise; settled or failed
requests are removed, and an older completion cannot remove a newer request.
Request sequence guards prevent late results from repainting a newer selection.
Action Card operations are not cached or deduplicated.

Static designer metadata is reused, grid values update existing cells, and large
lists/tables are built off the DOM. JavaScript shares cached element lookups,
request/error handling, report previews, and lifecycle cleanup; see the
[JavaScript guide](../Assets/js/README.md).

## Verification and remaining measurements

Offline schema/query checks and compiled-VB parity fixtures cover generated
queries and repeat rules. LocalDB runners exercise actual reads, duplicate/MCD
handling, concurrent saves, and exact-row edits without loading appsettings.
They use session-local tables or uniquely named disposable databases; retrieval
test copies redirect fully qualified application references and reject any
remaining production reference.

The startup runner hosts a separate app against a loopback listener that cannot
speak SQL. Browser checks use real HTML/assets with AJAX fixtures, testing cache
expiry, cancellation, failure/retry, late saves, and download URL cleanup.
Use an isolated server-side connection for those HTTP/browser runs as well.

Old local timings and simulated-I/O speedups are not production benchmarks.
Production connection waits, query plans, CPU/I/O, materialization, serialization
bytes/time, large-list rendering, and concurrent-user load remain unmeasured.
Representative live legacy-result comparisons and owner-approved duplicate
repair remain outstanding.
