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
| Summaries, dashboards, OCAP, and report/evidence retrieval | SELECTs; report generation also writes output files to the separate PM/TE network shares |
| RawData | Existing session-local `#freq_raw` / `#bmcase_raw` tables in SQL Server `tempdb`, then result reads and cleanup |
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

[MonitoringQueryBatch](../Repositories/MonitoringQueryBatch.cs) provides four
shared read slots per application worker for participating dashboards, cards,
and reports. Ordered batches bound raw-row buffering; queued waits honor
cancellation and slots release on failure. This is not a global limit across
all repository calls or deployed workers. Independent reads use separate
connections without a new cross-query snapshot transaction.

| Path | Current implementation |
| --- | --- |
| Monitoring | Cancel superseded searches/filter requests; validate process identifiers; cache reader metadata per result set and group floor rows once |
| AlertBoard / MachineSummary / BmAnalytics | Ordered, bounded process/location reads; retain mode-specific filters, row counts, distinct-list order, and Form8/Form12 differences |
| McdMonitoring | Shared read slots; valid DISTINCT ordering on projected machine/tester numbers |
| BM report | Main row plus one batch for VR numbers and nine fixed lookups: at most two commands instead of eleven |
| PM evidence | Five category reads in one batch, plus confirmation/header: three commands instead of seven |
| TE evidence | Batched category and quality reads: eight commands including metadata inspection instead of fifteen |
| PM report | Typed current/history records, one allowlisted parameterized lookup batch, and cancellation before workbook construction |
| RawData | Async result-set reads, retained temp-table cleanup, table allowlist, and the correct `MEMSBMMaintenance` name |
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
