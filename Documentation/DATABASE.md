# Database behavior and performance

See the [application guide](README.md) for workflow contracts and the complete
validation index. [Reporting](REPORTS.md) covers report data and template rules.

## Connections and writes

Repositories obtain connections from the hosted singleton
[`DatabaseConnectionResolver`](../Services/DatabaseConnectionResolver.cs).
`MachineDBConnection` is primary; `IntranetDBConnection` is used automatically
when the primary cannot open or fails a connectivity probe. Discovery starts
with the application, before normal database requests, and caches its selection.
Page rendering never waits for discovery. No business query or write is replayed.

SQL connections originate from the web server, so the selected endpoint reflects
the server's connectivity rather than the browser user's network/IP. All users
of the same application worker share the resolved selection.

Configure [appsettings.json](../appsettings.json) with both endpoints:

```json
"ConnectionStrings": {
  "MachineDBConnection": "Data Source=172.26.220.152;Initial Catalog=eMaintenanceSystemDB;User ID=eMaintUser;Password=<password>;TrustServerCertificate=True;",
  "IntranetDBConnection": "Data Source=10.27.35.222;Initial Catalog=eMaintenanceSystemDB;User ID=eMaintUser;Password=<password>;TrustServerCertificate=True;"
},
"DatabaseConnection": {
  "ValidationTimeoutSeconds": 2,
  "PrimaryRetrySeconds": 30
}
```

Existing deployments that supply only `ConnectionStrings:DBConnection` still
work: the resolver treats that legacy value as the primary only when
`MachineDBConnection` is absent. A configured new primary always takes
precedence. Update environment variables to
`ConnectionStrings__MachineDBConnection` and
`ConnectionStrings__IntranetDBConnection` when migrating to the new appsettings.
Blank/malformed/missing primary settings also permit fallback. If both endpoints
fail, resolution logs an error and throws before repository SQL executes.

Register the same instance as both resolver and background service in
[Program.cs](../Program.cs):

```csharp
builder.Services.AddSingleton<DatabaseConnectionResolver>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<DatabaseConnectionResolver>());
```

Existing repository and business-service registrations remain scoped. No options
class or additional package is required; the resolver reads its settings through
`IConfiguration`. Restart the application after changing connection settings.
Repository constructors now receive the resolver instead of configuration:

```csharp
private readonly DatabaseConnectionResolver _connections;

public MonitoringRepository(DatabaseConnectionResolver connections) =>
    _connections = connections;

// Inside an existing asynchronous repository method:
await using var conn = await _connections.OpenConnectionAsync(cancellationToken);
// Execute existing SQL using conn; it is already open on the selected endpoint.
```

`GetConnectionStringAsync(cancellationToken)` is also available to consumers that
need the selected original string. After discovery it returns an already-completed
cached task: no database access, timeout, probe, or selection lock. The
`ActiveConnectionName` property identifies Machine or Intranet without exposing
credentials. `InitializeAsync` allows an explicit wait when needed; concurrent
cold callers share one discovery, and cancelling a wait does not cancel discovery
for everyone else.

Repositories keep calling `OpenConnectionAsync`. It opens the cached endpoint
without executing `SELECT 1`. If opening fails, the resolver selects and validates
the alternate before handing a connection to repository SQL. Each caller owns
and disposes its own connection; the singleton stores no shared open connection
or transaction. `ImportantDeviceQuery` takes the same resolver for
Machine Summary and MCD reads. It shares
[RepositorySupport.cs](../Repositories/RepositorySupport.cs) with the
`RepositorySql` parameter/metadata helpers and `MonitoringQueryBatch` scheduler;
none of these helpers selects a database endpoint.

Performance and reliability considerations:

- Startup discovery opens and validates Machine first with `SELECT 1`, then
  Intranet only if needed. It runs asynchronously so HTML remains responsive.
  The first database operation may still wait for unfinished discovery; later
  operations reuse its cached decision. This warms SQL connection pooling too.
- Normal acquisitions perform only their required `SqlConnection.OpenAsync`;
  the repeated `SELECT 1` round trip is removed. Configured connection strings,
  SqlClient pooling and repository query/command behavior are preserved.
- The hosted service checks connectivity every `PrimaryRetrySeconds` (default
  30 seconds), always preferring Machine. Health probes and primary recovery do
  not delay cached lookups or successful connection opens on the current server.
  Recovered Machine becomes primary automatically. There is no common request
  lock; only discovery, health checks and failure selection are serialized.
- Each connection open/probe retains the configured two-second budget. Caller
  cancellation does not trigger fallback. A newly failed connection can still
  cost its timeout before fallback; the change removes repeated discovery from
  healthy request paths rather than eliminating physical network latency.
- When neither endpoint is available, database operations fail quickly using
  the cached unavailable state. Background checks continue attempting recovery;
  requests do not each repeat both connection timeouts. The host remains up.
- Information logs identify primary selection; warning logs identify fallback
  and individual validation failures. Failure diagnostics include the setting
  name, exception type, validation budget, and SQL number/state/class where
  available. Connection strings, passwords and raw exception messages are
  excluded. Selection logs occur when the selected endpoint changes.
- Background connectivity validation proves login/database access and a trivial query.
  It does not prove every application table permission or schema contract.
  Errors after a connection is handed to a repository propagate normally;
  transactions and writes are never retried on a different endpoint.
- Both servers must expose the intended compatible application data and permit
  its writes. This helper does not replicate data or coordinate transactions
  across servers. In-flight operations keep their existing connection; separate
  acquisitions during failover/recovery may use different servers.

Some queries explicitly name `eMaintenanceSystemDB`, so changing only the initial
catalog on the same server does not isolate every query. Use a separate test
instance or the existing isolated fixture runners for database testing. Fixtures
provide only their isolated primary; the startup check overrides both endpoints
with its loopback non-SQL sink.

Run the focused offline selection check with
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-DatabaseConnections.ps1`.
It exercises primary preference, fallback, missing/malformed configuration,
legacy compatibility, shared startup discovery, cached lookup, probe-free opens,
background recovery, timeout/cancellation, total-outage caching and safe diagnostics.
One offline run completed 100,000 cached string lookups in 2.15 ms (about 21.5 ns
per lookup), with zero additional connection/probe calls. This measures resolver
lookup overhead, not SQL query or HTTP response speed; a system-wide 100x speedup
has not been measured. The check loads no application settings and contacts no
live database. `Test-Startup.ps1` verifies SQL-independent HTML and bounded tracking.

Original connection-migration file inventory (startup/cache optimization itself
only changes the resolver, Program registration, focused check and documentation):

| Responsibility | Files |
| --- | --- |
| Resolver, configuration, DI | `Services/DatabaseConnectionResolver.cs`, `appsettings.json`, `Program.cs` |
| Repository constructor and connection acquisition | `Repositories/ActionCardRepository.cs`, `Repositories/AlertBoardRepository.cs`, `Repositories/BmAnalyticsRepository.cs`, `Repositories/BmReportRepository.cs`, `Repositories/EvidenceRepository.cs`, `Repositories/MachineSummaryRepository.cs`, `Repositories/McdMonitoringRepository.cs`, `Repositories/MonitoringRepository.cs`, `Repositories/OcapViewerRepository.cs`, `Repositories/PmReportRepository.cs`, `Repositories/RawDataRepository.cs`, `Repositories/TeEvidenceRepository.cs` |
| Shared SQL helpers and read scheduling | `Repositories/RepositorySupport.cs` (`RepositorySql`, `ImportantDeviceQuery`, `MonitoringQueryBatch`) |
| Focused check and existing fixture compatibility | `scripts/Test-DatabaseConnections.ps1`, `scripts/Test-Startup.ps1`, `scripts/Test-ActionCardSaves.ps1`, `scripts/Test-DataRetrieval.ps1`, `scripts/Test-MachineSummary.ps1`, `scripts/Test-MonitoringParity.ps1` |
| Documentation | `README.md`, `Documentation/README.md`, `Documentation/DATABASE.md` |

Preserved `Legacy/*.txt` desktop sources retain their original historical
settings references; compiled application code resolves connections centrally.

| Path | Database effect |
| --- | --- |
| Monitoring initial HTML | No blocking database work; application-start discovery runs independently |
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

Monitoring's Location filter requires a specific floor. The page handlers reject
blank, All, and unknown locations before calling the machine-list, dashboard, or
machine-card service. The floor-overview aggregation retains its internal
cross-location query; it is not an unrestricted machine-search option.

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

For a SELECT-only incident audit, capture connection identity, duplicate
candidates in all five tables, and visible trigger metadata. Correlate the
results with the actual failing filters. The earlier audit found no confirmed
successful production write in the captured development output; incomplete client-side
logs do not establish that no other write occurred.

## Read scheduling and query behavior

### Repository cleanup and preserved conventions (2026-11-08)

The folder retains twelve workflow repositories and one shared support source,
[RepositorySupport.cs](../Repositories/RepositorySupport.cs). The former
`RepositorySql.cs`, `ImportantDeviceQuery.cs`, and `MonitoringQueryBatch.cs` are
consolidated there; their internal type names and execution behavior remain intact.
They are static helpers, not additional repositories or DI services:

| Helper | Purpose and retained benefit |
| --- | --- |
| `RepositorySql` | Typed varchar/date parameters and case-insensitive result-set metadata/optional-column reads; keeps parameter binding and sparse-view handling consistent |
| `ImportantDeviceQuery` | Shared MCD/Machine Summary eligibility and batched process/location SQL; retains distinct machine lists for Form7 and repeated BM rows for Form11 counts |
| `MonitoringQueryBatch` | Four shared read slots per worker, ordered batches and bounded raw-row buffering; cancellable waits and slot release on failure; interactive reports/evidence bypass it |

All twelve repositories use file-scoped namespaces, ordered imports, the
`_connections` resolver field and constructor injection, and asynchronous
connection/command disposal. Connection selection, caching and configuration
errors belong to `DatabaseConnectionResolver`, not `RepositorySql`. Each
repository continues to own its workflow SQL, row mapping, ordering and identifier
allowlists; no generic repository layer or new dependency is introduced. Request
cancellation flows through read operations. Action Card saves retain their atomic
insert/exact-row edit behavior and Unicode text parameters.

Uniform structure does not erase legacy query differences. MCD/Machine Summary
TE action exclusion matches BMID plus tester number. Monitoring/AlertBoard TE
action existence uses BMID alone, while Analytics also matches the tester. Date
boundaries, category/status filters, Form8/Form12 process/location profiles, and
distinct-list versus BM-row counts retain their workflow-specific semantics.
The existing batching, duplicate-safe action/MCD reads, typed parameters and
machine-list cache remain in place; reducing the support-file count does not
change their query count or concurrency limits.

The cleanup also removes Monitoring's unused location-index lookup and unused
TE summary-builder argument, and inlines PM report's one-use single-row wrapper.
SQL filters, selected columns, parameter types/values and legacy report mappings
are preserved. Fixture source-copy lists use the consolidated support file; the
TE header fixture checks assignment formatting directly instead of calling the
previously removed assignment-only query.

Cleanup validation used the application build, syntax-equivalence checks for all
three consolidated helpers, and unchanged SQL-fragment checks across all twelve
repositories (normalizing source line endings). The existing offline scheduler
checks passed for four concurrent reads, ordering, failure recovery and queued
cancellation. No database connection or production timing measurement was used.

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

### Machine Summary and BM Analytics (2026-11-07)

Machine Summary reads the complete grid in one command rather than 12 EEE or
5 TE commands. EEE combines the configured process tables with `UNION ALL`;
TE reads its shared maintenance table once with process-specific floor filters.
The request uses one shared read slot. Eligibility, descending machine order,
BM-row counts, and distinct drilldown lists are retained. The query remains
read-only and does not copy the action history into temporary tables.

BM Analytics reads all configured floors once per process: Form8 EEE drops from
38 to 12 commands and Form12 EEE from 52 to 12; TE drops from 14/17 to 5.
The active NewLoadColor path needs only process, location, and machine number.
Its grid query filters MCD/action eligibility with duplicate-safe `EXISTS`
and retains the case/unit join, without ranking action history, joining content,
or formatting dates that the grid never uses. Each floor is still processed in
its legacy order; Form8/Form12 mappings, `ATC_65`, row counts and machine-list
deduplication remain separate. The previous raw-row query is retained for
diagnostics and independent parity comparisons.

`Test-MachineSummary.ps1` checks both summary modes and all four Analytics
mode/profile combinations in a disposable LocalDB database. `-Benchmark` adds
50,000 unrelated action rows and takes three alternating measurements of the
previous and current read paths. The measured EEE medians were:

| Path | Previous | Current | Commands |
| --- | --- | --- | --- |
| Machine Summary | 12.6 ms | 6.0 ms | 12 to 1 |
| BM Analytics, Form12 | 585.9 ms | 19.0 ms | 52 to 12 |

This small maintenance fixture isolates scheduling and action-history overhead;
it does not establish production timings or large-result/concurrent-user speed.
The configured live database could not be reached during that change. The user
subsequently reported Machine Summary at 1.88s (from 4.72s) and BM Analytics at
2.91s (from 16.26s); these are user-supplied production observations.

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
| Dates | The date-only web range includes the full To day: `>= @Range1 AND < DATEADD(DAY,1,@Range2)` for both headers and cards; SQL datetime parameters; invariant MM/dd/yyyy HH:mm:ss output |

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

`MonitoringQueryBatch` in [RepositorySupport.cs](../Repositories/RepositorySupport.cs) provides four
shared read slots per application worker for participating dashboards and cards.
Interactive report/evidence reads bypass this queue so monitoring refreshes
cannot delay generation. Each evidence request still has a fixed set of three
PM or up to six TE concurrent reads. Ordered dashboard batches bound raw-row buffering; queued waits honor
cancellation and slots release on failure. This is not a global limit across
all repository calls or deployed workers. Independent reads use separate
connections without a new cross-query snapshot transaction.

| Path | Current implementation |
| --- | --- |
| Monitoring | Two concurrent reads per search: all ordered machine headers and shared BM rows for counters/columns; one complete browser snapshot, ten columns rendered per page, no per-column/page requests |
| AlertBoard | Ordered, bounded process reads, then original per-floor processing; 120/50 commands reduced to 12/5 |
| MachineSummary | One full-grid command; EEE table union and one TE table read; retains per-process floor eligibility, BM counts and distinct-list order |
| BmAnalytics | Bounded process reads across configured floors; only qualifying MCD/action rows and the three fields used by the grid; retains Form8/Form12 differences |
| McdMonitoring | Shared important-device query and read slots; valid DISTINCT ordering on projected machine/tester numbers |
| BM report | Main row plus one batch for VR numbers and nine fixed lookups: at most two commands instead of eleven |
| PM evidence | Five category reads in one batch, plus confirmation/header: three commands instead of seven |
| TE evidence | Batched category and quality reads; assignment reuses the header: seven commands including metadata inspection instead of fifteen |
| PM report | Typed current/history records, one allowlisted parameterized lookup batch, and cancellation before workbook construction |
| RawData | Shared recurrence CTE, retained date-filter placement, async typed result-set reads, table allowlist, and the correct `MEMSBMMaintenance` name |
| Action Cards / OCAP | Cancellable loads; Action Card writes retain their completion and identity protections |

Command counts describe source/fixture behavior, not measured production speed.
Process/table identifiers come from server-side allowlists or fixed maps;
dropdown choices alone are not the validation boundary.

MachineList uses direct location equality for specific floors, avoiding the
optional-filter `OR` in the normal lookup. Direct repository callers retain
their existing All-floor behavior. Its successful raw lists are cached for
30 seconds per application worker, database resolver, process, floor and PM/TE
family through ASP.NET Core `IMemoryCache`. PM/CC/EEE/VIS share their identical
machine lookup; TE uses a separate entry. Each caller receives a fresh list,
so adding the service's All option cannot mutate cached data. Failures and
cancellations are not cached. Metadata changes become visible after expiry;
the first request and cache refills still pay connection validation/query time.
The cache can retain a pre-failover machine list for up to 30 seconds.

`Test-MonitoringParity.ps1 -MachineListOnly` runs only isolated machine-list SQL,
cache separation/mutation/refill/cancellation checks and TE header-assignment
equivalence, skipping board/parity fixtures. One LocalDB run measured 136.93 ms
for its first list and 1.09 ms for the cached call. These are fixture timings,
not new timings for the reported 2.27-second HTTP request. The handler exposes
its measured duration in `Server-Timing` and the application log.

## Browser snapshots and rendering

Monitoring Search returns all matching columns for the selected floor in
`machineCards`, ordered exactly like the existing ten-column pages. The
`machineSummaries` property still contains the requested page for compatibility.
The service reuses the BM rows already loaded for floor counters rather than
querying each column again. Header and BM reads use separate connections through
the shared scheduler. Coloring runs independently per machine/type/location so
interleaved floors cannot split a column's repeat group.

The browser keeps this search snapshot and inserts only the active ten columns.
Next/Previous perform no HTTP or SQL requests. Search, changed filters, the
600-second refresh, Action Card refresh and Clear Filters replace/discard the
snapshot. Completed columns are constructed off-DOM and inserted together;
there are no independent column loading completions. Fixed card heights and
batched insertion bound layout work. The current stylesheet uses
`content-visibility: visible`; intrinsic-size and visibility-event hooks remain
available. See [Monitoring](MONITORING.md) for column sizing and frozen bottom
summaries. The initial response includes all matching columns, while other
floors return only their counters/lists.

Focused validation is `Test-MonitoringParity.ps1 -BoardOnly` (78 actual grouped
columns across all five modes against existing individual web reads, page order,
duplicates, interleaved floors, tester types and cancellation), and
`Test-Workflows.ps1 -MonitoringOnly` (real browser/HTML/assets with AJAX fixtures).
The browser fixture checks zero per-column/page requests, error/retry,
cancellation, counts, colors, commands, staircase geometry and desktop/mobile
captures. Local fixture timings are not production benchmarks.

Legacy SQL takes timestamp boundaries with inclusive `BETWEEN`; the web controls
provide calendar dates. Header and card queries therefore use the same
half-open range through the end of the selected To day. The legacy parity
oracle uses that day's final SQL datetime when comparing date-only requests.
The service orders all columns by actual displayed card count before pagination,
preserving SQL order for ties; see [staircase ordering](MONITORING.md#staircase-order).

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
