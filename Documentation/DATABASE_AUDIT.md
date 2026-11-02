# Database impact audit — 2026-11-02

Scope: all 13 C# files in `Repositories/` (12 repositories and one batching
helper), their callers, application startup/configuration, all six checked-in
validation scripts, the relevant legacy forms/schema, Git history, and the
available local Codex session records. Times below are UTC+08:00.

The original findings below describe the code at the time of the source audit.
Subsequent application corrections are documented separately next; they change
application behavior without changing live data, schema, or desktop source.

## Subsequent repository-wide read optimization

The 2026-11-02 extension changes application reads only. Request cancellation
now reaches connection opens, commands, row reads and queued summary batches.
MCD summaries and report/dashboard card reads reuse the four-slot scheduler.
Report/evidence SELECTs are batched with multiple result sets; category TOP
limits, ordering, lookup/null rules and response contracts are retained.
Raw Data now reads the correct result set asynchronously and maps MEMS to
`MEMSBMMaintenance`. MCD's invalid DISTINCT ordering is corrected to descending
projected machine/tester numbers. Those two findings later in this historical
audit are therefore resolved in web source. Monitoring/Analytics process
identifiers and Raw Data table identifiers are checked before interpolation.

No production connection or diagnostic query was executed for this extension.
No settings, stored procedure, view, permanent table, index, `schema.sql`, legacy
file or template was modified. The application writers retain their SQL and
guards; Action Card loads alone gain cancellation. Raw Data still creates/drops
only its existing session-local `#` tables in tempdb when a user requests it.

Validation used offline fixtures, LocalDB and an explicitly isolated app.
`Test-DataRetrieval.ps1` copies the relevant repository bodies into a temporary
runner, redirects fully qualified reads to its generated LocalDB fixture catalog,
rejects surviving production references and drops only that generated catalog.
Its first sandboxed attempt failed opening LocalDB; the permitted rerun passed
and removed its fixture database. Summary duplicate/save regressions likewise
passed using LocalDB only. Startup uses a loopback listener that cannot execute
SQL; endpoint/browser checks force unreachable localhost SQL and mock browser
data requests. These runs cannot establish live-data parity or production speed.
See `MONITORING_PERFORMANCE.md` for path coverage and the remaining measurement limits.

## Subsequent startup correction

Monitoring's initial HTML GET no longer attempts PC-status tracking. After
window load, the browser sends an antiforgery-protected TrackSession POST;
that optional request has a two-second cancellation budget covering connection
open and command execution. It still may write PC-status data on the configured
server when reachable, but page rendering is independent of that operation.
OCAP reads now use asynchronous, request-cancelable SQL operations, with the
same SELECT text, fields and process allowlist.

The startup regression uses a loopback TCP sink that cannot execute SQL, and
browser regression uses an unreachable local connection plus AJAX fixtures.
Neither validation connects to the legacy database. Historical findings about
tracking during the initial GET apply to the pre-correction code only. See
`MONITORING_PERFORMANCE.md` for the measured startup improvement and its limits.

## Subsequent web save correction

The web repository now performs one conditional INSERT statement whose
`NOT EXISTS` check uses `UPDLOCK, HOLDLOCK`. This makes competing web saves
serialize at SQL Server, across worker processes, without a new index or
constraint. Non-TE tables check BM ID and machine; TE checks BM ID alone,
matching the original Form2 lookup. A conflicting New submission is rejected
and leaves the existing card's fields unchanged.

Existing-card loads select the highest Seqno. The response carries that identity
through the modal and Save request; UPDATE matches Seqno, BM ID and machine.
An absent/mismatched identity is rejected. Older duplicate rows are preserved,
and an edit never updates every matching card. Invalid save modes and missing
BM/machine identifiers are rejected before writing.

This protects web saves only. The legacy INSERT path and existing live
duplicates remain unchanged. The lack of a lookup-key index may make SQL Server
take broad locks during the guarded INSERT, so the short statement should be
measured under representative load before making throughput claims. The code
does not deploy schema changes or perform duplicate cleanup.

The locking behavior follows Microsoft's [table-hint documentation](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table)
and [conditional INSERT pattern](https://devblogs.microsoft.com/azure-sql/the-insert-if-not-exists-challenge-a-solution/).
`scripts/Test-ActionCardSaves.ps1` exercises the actual repositories/services in
a uniquely named disposable LocalDB database, including concurrent New saves,
TE matching, stale forms, and edits with preexisting duplicates. It does not
load appsettings, start the web app, or connect to the legacy database.

Validation completed: the application and regression runner build with zero
warnings/errors. The LocalDB runner passed all five modes, 40 concurrent-save
attempts, stale New conflicts, TE matching, single-row edits with preexisting
duplicates, and invalid/mismatched identities. Its generated test database was
dropped afterward. Browser workflow checks also verify that Save submits the
loaded Seqno; the test app uses an unreachable localhost SQL override and AJAX
fixtures, so its page-tracking attempts cannot reach the legacy database.

## Answer

**The web application is not read-only.** Monitoring's after-load tracking POST
can insert/update `MachineKarte_PCStatus`. Saving an Action Card can insert/update one of the five
`MachineKarte_ActionCard*` tables used by the original application.

**No successful write to the legacy database was established by this audit.**
Earlier sessions did attempt connections, including the automatic PC-status
write path. Thirteen captured runtime output records show that path failing
while opening its SQL connection. Those particular attempts could not reach
the INSERT/UPDATE. This does not prove that every other app request, manual
test, or session also failed.

**The user's diagnostic-results screenshot confirms two PM Action Cards for
BM 256152 / SW-18 in the legacy database.** These rows break the original PM
Form2 scalar lookup whenever that BM is included in its process/location/date
filters. The web application's unchecked INSERT is one possible source; the
original Form_Input also has an unchecked INSERT. The screenshot does not
establish which application inserted either row. Database-side attribution
evidence and the complete contents of both cards have not been inspected.

This audit connected to no SQL instance, sent no application HTTP requests, and
ran no application/browser/SQL test. Only audit documentation and an unexecuted
SELECT-only diagnostic file were added/updated. Application code, connection
settings, legacy source, and `schema.sql` were left unchanged.

## User-supplied diagnostic results

The user ran `scripts/Audit-LegacyRepeatBm.sql` and supplied a screenshot of
its three result sets. This is supplied database evidence, not an SQL execution
performed by the coding agent.

- Connected server: `APCSPRO-DB`; connection database: `eMaintenanceSystemDB`;
  target database ID: `12`.
- One duplicate PM lookup group is shown in the supplied output:

| UserType | bmID | MachineNo | Match count | Seqno | DateCard (stored text) | Incharge |
|---|---|---|---|---|---|---|
| PM | 256152 | SW-18 | 2 | 55110 | 11/02/2026 10:56:11 | 7302 |
| PM | 256152 | SW-18 | 2 | 55111 | 11/02/2026 10:59:10 | 7302 |

Both rows display `RUNNING (ONGOING INVESTIGATION/MONITORING)` as MCStatus.
The dates are card field values, not independently verified insertion times.
Incharge is also form data, not proof of the SQL login, person who submitted
the request, or originating application. Consecutive Seqno values and similar
dates do not establish whether the web or desktop application wrote them.

The trigger result set is empty. No matching triggers were visible to that
query/account; metadata visibility still limits any stronger conclusion.

This confirms the duplicate data condition behind the PM scalar-lookup failure.
It does not establish that BM 256152 was included in the exact failed run's
filters, that other user modes fail for this reason, or that every field in the
two cards is redundant. The diagnostic is SELECT-only and did not create these
rows. The first source audit's lack of a confirmed successful tool-initiated
write remains an attribution limit, not a claim that the table has no writes.

Compare the complete records before choosing a repair. This additional query
is prepared for manual review and has not been executed by the coding agent:

```sql
SELECT *
FROM [eMaintenanceSystemDB].[dbo].[MachineKarte_ActionCard]
WHERE bmID = 256152 AND MachineNo = 'SW-18'
ORDER BY Seqno;
```

Preserve both records and review their problem/action/comments/replies before
any data repair. Do not assume the higher Seqno should be deleted or that
matching displayed statuses mean all fields match. An owner-approved repair
must preserve the needed information and leave one matching PM card; the
unchecked save paths must also be protected against creating another match.
No live record deletion/update or schema change was applied. The subsequent
web save correction above does not resolve these already stored duplicates.

## Original findings (before the web save correction)

### 1. Action Card saves can create data that breaks legacy recurrence reads

- `Repositories/ActionCardRepository.cs:89` always INSERTs for a new card.
  There is no existence check, uniqueness enforcement in this method, or
  idempotency mechanism.
- `Services/ActionCardService.cs:69` chooses UPDATE only when the client sends
  `StatusId == "Existing"`; every other value follows the INSERT path after
  status validation. The server does not independently resolve existing cards.
- `Controllers/ActionCardsController.cs:78` exposes this through the antiforgery
  protected `POST /ActionCards?handler=Save`. Normal UI use invokes it on form
  submission through `Assets/js/ActionCard.js:132`. Opening/preparing a new
  card alone does not INSERT anything.
- The checked-in schema gives all five Action Card tables a primary key on
  `Seqno`, not the legacy lookup keys. See `schema.sql:12592`, `13241`, `33762`,
  `33801`, and `33875`. Multiple rows with the same BM/machine are permitted by
  the shown definitions.
- Two users opening the same new card, stale browser state, or resubmission can
  therefore create multiple matches if saves reach this database. The disabled
  Save button is not a database uniqueness guarantee.
- `Legacy/Form_Input.vb.txt:62` / `:81` also INSERTs without a duplicate guard;
  its Save click chooses that path from `StatusID` at line 342. Both applications
  can create this condition, so the diagnostic rows alone cannot identify the
  source. A prevention fix confined to the web app cannot protect desktop saves.

The key distinction is essential: normal Form2 modes match `(bmID, MachineNo)`;
TE Form2 matches **bmID alone**. Two TE cards for different machine numbers can
still break that TE lookup. A uniqueness rule only on `(bmID, MachineNo)` would
not fully address TE's legacy expectation.

The entire ActionCardRepository has the same Git blob in initial commit
`a295c57` and current HEAD `d65d3ca`. These write methods were already in the
web application's initial commit of 2026-10-28; they were not introduced by
the latest summary optimization. This dates the code, not any database write.

### 2. Action Card updates can overwrite multiple existing records

`Repositories/ActionCardRepository.cs:130` updates all rows matching
`BMID = @BmId AND Machineno = @McNo`, replacing the supplied problem, action,
status, comments, replies, and related fields. It does not select one `Seqno`
or check the affected-row count. If duplicates exist, one save can change
multiple cards. `LoadAsync` reads the first matching row without an ORDER BY,
so the displayed card and the latest status selected by summaries need not
come from the same duplicate row. This is a real write exposure; actual
affected records remain unverified.

### 3. Opening Monitoring automatically writes PC-status data

The call path is:

`Pages/Monitoring.cshtml.cs:38` (`OnGetAsync`) → `TrackSessionAsync` →
`Services/MonitoringService.cs:139` →
`Repositories/MonitoringRepository.cs:426` (`UpsertPCStatusAsync`).

The repository SELECTs by PCName, then INSERTs or UPDATEs
`[eMaintenanceSystemDB].[dbo].[MachineKarte_PCStatus]`. It writes `IPAdd`,
`LastOpen`, `Version`, and `UserType` (`PM`), keyed by the web server's
`Environment.MachineName`. Existing rows for that host can be overwritten;
concurrent first visits can also race because the shown primary key is SeqNo.

This path does not directly change Action Cards or BM maintenance records and
does not explain Form2's duplicate-card lookup error on its own. Any additional
effect through live server triggers remains unverified. The write method is
unchanged from the initial commit after whitespace normalization.

`scripts/Test-Workflows.ps1:49` fetches `/Monitoring`; its browser also navigates
to that page. AJAX fixtures cannot intercept the server-side page handler.
Consequently, this browser check can attempt PC-status writes when the app uses
the legacy connection. Calling a test fixture-based does not make its page
requests read-only.

### 4. A Monitoring input-validation gap can extend the SQL risk

`MonitoringRepository.GetPagedMachineSummariesAsync` and `GetBMCardsAsync`
interpolate the request's process into table identifiers and, for card reads,
SQL string literals. `Pages/Monitoring.cshtml.cs` checks that the process is
nonblank, but neither it nor `MonitoringService` enforces a server-side process
allowlist before those interpolations. The fixed dropdown is not that check.

Normal UI requests build SELECT statements. A crafted process value could
alter the SQL batch and, if the configured account has permission, go beyond
reads. This was identified by source inspection; no exploit or database query
was executed, and no evidence of exploitation was found. Other repositories
generally obtain their identifiers from fixed maps/configuration or explicit
allowlists; BmReport's lookup column names are fixed at its current callers.

### 5. Recent summary SQL changes do not repair the original forms

The uncommitted changes in `AlertBoardRepository` and `BmAnalyticsRepository`
replace scalar Yes/No lookups with `EXISTS` and action-card joins with
`OUTER APPLY TOP (1) ... ORDER BY Seqno DESC`. These are SELECT changes. They
neither insert/delete duplicates nor alter database objects.

They allow the web summaries to read data that the original scalar queries
cannot handle. They do not change the checked-in VB files or the separately
running WinForms program. `Legacy/` is unchanged from its initial committed
version. `schema.sql` is also unchanged in the working tree; it was added to
the main branch in `f4bb815`, and no application startup/build hook that executes
it was found.

The batching helper runs at most four summary reads concurrently per app worker.
This can increase instantaneous load/blocking relative to sequential reads,
but does not directly create duplicate cards or produce SQL error 512.

## Original repository-by-repository inventory

Classification describes SQL constructed by the present code for normal inputs.
It is not a claim about live permissions, server objects, or past execution.

| File in `Repositories/` | SQL behavior and target | Permanent data/object changes |
|---|---|---|
| `ActionCardRepository.cs` | SELECT, INSERT, UPDATE of PM/TE/CC/EEE/VIS `MachineKarte_ActionCard*`; target is the connection's current database (`dbo`). | **Yes**, on Save; duplicate and multi-row-update risks above. |
| `MonitoringRepository.cs` | SELECT machine lists, BM records, counts, action/MCD/OCAP/content lookups; automatic INSERT/UPDATE of explicitly qualified `MachineKarte_PCStatus`. | **Yes**, on a Monitoring page visit; process-validation gap above. |
| `AlertBoardRepository.cs` | SELECT process/TE BM tables, case/unit/content and action status; repeat calculations happen in memory. | No write statement found. |
| `BmAnalyticsRepository.cs` | SELECT process/TE BM tables, active MCD existence and action status. | No write statement found. |
| `MachineSummaryRepository.cs` | SELECT machine/tester numbers using active MCD EXISTS and action NOT EXISTS; validates process. | No write statement found. |
| `McdMonitoringRepository.cs` | SELECT DISTINCT machine/tester numbers, MCD/action existence; validates process. | No write statement found. |
| `BmReportRepository.cs` | SELECT `ViewTEData_Test`, VR numbers and allowlisted lookup tables. | No write statement found. |
| `PmReportRepository.cs` | SELECT current/history rows from fixed process-to-view mappings. | No write statement found. |
| `EvidenceRepository.cs` | SELECT image references, confirmation fields and machine headers; view names come from `ProcessTemplateMap`. | No write statement found; document generation writes local output files. |
| `TeEvidenceRepository.cs` | SELECT TE views/evidence records and `INFORMATION_SCHEMA.COLUMNS`. | No write statement found; schema inspection does not modify schema. |
| `OcapViewerRepository.cs` | SELECT checklist rows and saved OCAP data; service maps process to a fixed table name. | No write statement found. |
| `RawDataRepository.cs` | SELECT INTO `#freq_raw` / `#bmcase_raw`, SELECT results, DROP those local temporary tables. | Writes temporary data/objects in the connected SQL Server's `tempdb`; no permanent table mutation found. |
| `MonitoringQueryBatch.cs` | Schedules supplied read delegates with a shared semaphore; contains no SQL/connection code. | No direct database operation. |

All repositories use `ConnectionStrings:DBConnection`. The checked-in default
targets `eMaintenanceSystemDB` on a non-LocalDB server using SQL authentication.
Server, database, account, password, encryption and certificate-trust settings
match the inspected initial/second/latest main-branch versions. Credentials
were compared without including their values in this report.

There is no application-level read-only mode or enforced read-only connection.
Fully qualified references still name `eMaintenanceSystemDB`, even if only the
connection's initial catalog is changed. A different catalog on the same server
is therefore insufficient to isolate all queries. Effective overrides for
another app process/IIS/deployment and the account's actual permissions remain
unknown; no connection override was present in this audit shell.

No EF migration/EnsureCreated path, database deployment/startup hook, scheduled
database worker, bulk copy, stored-procedure invocation, permanent DELETE,
TRUNCATE, or ALTER was found in the current application SQL paths. This does
not certify the contents of live views/functions/triggers.

## Validation scripts and observed earlier activity

| Script | Database exposure |
|---|---|
| `Test-MonitoringQueries.ps1` | Parses the schema and tests generated SQL/business routines in a temporary console program. It does not open SQL connections or host the web app. |
| `Test-ActionSummaryParity.ps1` | Compiles selected VB business routines and compares in-memory fixtures. Does not compile/run Form2's data-loading query. |
| `Test-SummaryDuplicates.ps1` | **Executes SQL** against `(localdb)\MSSQLLocalDB`, catalog `tempdb`, integrated authentication; creates/populates/updates only session-local `#` tables. It rewrites table references and rejects any surviving application-database reference. Does not read the app connection string. |
| `Test-Workflows.ps1` | Real HTTP/page loads can attempt **PC-status writes**. Browser fetch fixtures replace Action Card saves and other handler data; the initial HTML request is outside that interception. |
| `WorkflowBrowserChecks.js` | Injected browser fixtures return a fake Saved response for Action Card submissions instead of forwarding those fetches. Not served as application code. |
| `Test-WorkflowEndpoints.ps1` | Tests redirects/partials without following redirects, prepares New cards in memory, and checks rejected/invalid saves. Its currently supplied saves fail antiforgery, null-card or status validation before reaching a writer. |

The local history review covered 17 prior workspace session files, with 440
tool calls and their captured outputs. All 544 shell-call references were
accounted for: 541 literal commands extracted for inspection, plus three
dynamic commands inspected separately (asset cleanup/log/process inspection).
Other tool calls were file edits, image reads, and process-output polling.

Specific evidence:

- **2026-11-01 14:34:03** and subsequent runs: captured app output reports
  `PC status tracking failed` with connection-establishment errors and an
  all-zero ClientConnectionId. Thirteen runtime output records contain this
  combination. Another occurrence is printed source code, not a runtime event.
- **2026-11-01 15:52**: two explicit connection-check commands used the app's
  connection string. One had connection-builder errors; the corrected attempt
  also reported connection failure. Neither command contains a SQL statement.
- Some earlier browser-test launches explicitly used an unreachable local
  `WorkflowChecks` connection override. Other launches used no explicit override;
  they cannot all be treated as isolated merely because they were browser tests.
- **2026-11-02 11:28:08**: a LocalDB duplicate-test runner completed successfully,
  reporting two reproduced error-512 cases and 70 corrected query variants.
  Its fixture writes are confined to the local temporary tables described above.
- No direct production INSERT/UPDATE/DDL command or successful production
  Action Card save was identified in the reviewed tool history. The single
  captured `Successfully Saved` text was service source printed for inspection,
  not a successful save response.

These are partial client-side records, not SQL Server audit/transaction logs.
They do not cover unrecorded manual usage, every earlier development session,
other users/applications, or all requests to independently running app processes.
Silence in application logs also does not prove that a write did not happen.

## Screenshot diagnosis and non-destructive confirmation

The failing `da.Fill(datatable1)` matches `Legacy/Form2.vb.txt:476`.

- TE query, line 418: `ISNULL((SELECT 'Yes' FROM MachineKarte_ActionCard_TE
  WHERE bmid = a.bmID), 'No')`.
- Other user modes, line 445: the equivalent lookup in the selected Action Card
  table, matching BM ID and machine number.
- Normal mode also has a BMContent scalar subquery at line 459; its key is
  unique in the checked-in schema (`schema.sql:11215`). Live schema divergence
  or a different deployed query cannot be ruled out from a screenshot.

Returning two `'Yes'` rows is still two rows, even when their values are
identical. SQL Server rejects that scalar expression. The accompanying LEFT
JOIN can additionally multiply BM rows. This error is different from a network
connection failure; network switching does not itself turn a SELECT into DML.

The same scalar lookup pattern also occurs in other checked-in legacy forms,
including Form1, Forms8/12 and `uc_Test`, so the exposure can extend beyond the
specific button. The supplied diagnostic now identifies BM 256152 / SW-18 as
a confirmed duplicate PM lookup; matching the failed run's filters remains
necessary to identify it as the record encountered in that execution.

`scripts/Audit-LegacyRepeatBm.sql` was prepared for the database owner to review
and run using a SELECT-only account. **The user supplied its execution results;
the coding agent has not executed it.** It returns:

1. The connected server/database identity.
2. Candidate duplicate-card rows from all five tables, counting TE by BM ID
   alone and other modes by BM ID plus machine number.
3. Trigger metadata/definitions for the tables the app can directly write.

It scans the Action Card tables but contains no data/object-changing statement,
temporary table, dynamic execution, transaction, or NOLOCK hint. Candidates
must be matched to the exact failing user/process/location/date range; an
unrelated duplicate does not prove it triggered this particular execution.
Empty results do not explain a different deployed query or uninspected live
object. Row dates and Seqno can help investigate but cannot establish the
inserting application; DateCard is client-supplied text. Attribute changes
using existing SQL Audit/Extended Events/application access logs or a DBA's
backup/log analysis if available. Do not delete cards as an audit step.

## Network switching and next actions

Using the hotspot can make the intranet SQL server unreachable. Reconnecting
to intranet Wi-Fi restores access for later page loads/saves in a running app.
No application-owned offline write queue/replay worker was found. Connectivity
is not an isolation boundary for subsequent testing.

Before more live testing, use a separate test SQL instance with its own copied
data and account, or a database-enforced SELECT-only account for read checks.
Avoid Action Card saves on the shared legacy connection while investigating.
Opening Monitoring on that connection still attempts the PC-status write.

After confirming the affected rows, resolve the intended Action Card
cardinality and protect new saves atomically across every client. Update the
actual deployed legacy query to an existence check and a deliberately chosen
single action-status row if that preserves the required business behavior.
The web-only query correction cannot fix the original executable. No such
application/database repair was applied as part of this audit.

## Additional read-path defects found

These findings are independent of the reported database-change concern and
were not changed or exercised against SQL:

- `McdMonitoringRepository.cs:67` and `:94`: SELECT DISTINCT projects only a
  machine/tester number, but ORDER BY also names non-projected date/case
  columns. SQL Server rejects those query shapes; this would be a separate
  read error, not the screenshot's scalar-subquery error.
- `Services/RawDataService.cs:233`: MEMS maps to `MEMSBMaintenance`, while the
  checked-in table is `MEMSBMMaintenance`. This can cause an invalid-object
  read error.

## Audit checks and limits

- Read every repository's SQL/execution paths and traced both permanent writers.
- Inspected startup/project hooks, configuration names, scripts, relevant
  schema keys, legacy queries, working-tree differences and historical writers.
- Recorded SHA-256 hashes for all 13 repository files, all 37 legacy files,
  schema, both settings files and Program.cs, and checked them after the audit.
- Validated the diagnostic file offline against the checked-in table/column
  definitions and checked that its executable text contains no mutation path.
- No application build or database/browser tests were needed for these audit
  artifacts. User-supplied output establishes the duplicate rows described
  above; no direct SQL inspection was performed by the coding agent. Live
  permissions, complete card contents and database-side attribution remain
  unverified. The source audit is finished; database repair has not been applied.
