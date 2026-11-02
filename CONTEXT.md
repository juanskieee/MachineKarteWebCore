# Project Overview

MachineKarteWebCore converts legacy E-Karte WinForms to the web. Forms 1–12 and
Form_Input are implemented; live-data and report-output parity remain incomplete.

- Stack: .NET 8 Razor Pages + MVC workflow controllers, SQL Server/`Microsoft.Data.SqlClient`, ClosedXML.
- `Pages/`: Monitoring and workflow partials; `Controllers/`: child workflow routes;
  `Models/`: contracts/view models; `Repositories/`: SQL; `Services/`:
  business/reports. Connection: `ConnectionStrings:DBConnection`.
- Monitoring is the only navigable screen. `Workflows.js` loads
  `Pages/Shared/Workflows/` partials into `_WorkflowDialog.cshtml`.
  `WorkflowController` serves `?dialog=1`; direct child URLs redirect through Monitoring.
  Eleven child controllers retain the existing `?handler=...` URLs through
  `WorkflowHandlerAttribute` with explicit HTTP methods and POST antiforgery checks.
  Child route pages/PageModels are removed; partials use `WorkflowViewModels.cs`
  only when server-rendered context is needed.
- `Assets/css/` and `Assets/js/` are outside `wwwroot`; use `Url.VersionedAsset`.
  `Pages/_ViewImports.cshtml` applies throughout Pages.
- Preserve `Legacy/`, `schema.sql`, and `Templates/`. `Form3CellMappingMatrix.txt`
  is a runtime dependency of `PmReportService`, included in build/publish output.

# Conversion Status

## Completed

| Legacy | Web implementation |
|---|---|
| Form1 | Monitoring |
| Form2 | AlertBoard / Repeat BM |
| Form3 / Form9 | PmReport / BmReport |
| Form4 / Form10 | EvidenceMaking / TeEvidenceMaking |
| Form5 / Form6 | RawData / OcapViewer |
| Form7 / Form11 | McdMonitoring / MachineSummary |
| Form8 / Form12 | BmAnalytics: default profile 8, explicit `legacyForm=12` |
| Form_Input | ActionCards |

Verified locally:

- Eleven child workflow redirects/partials, initialization, drilldowns,
  save/refresh navigation, cleanup, loading/retry, and keyboard behavior.
- All 31 MVC action routes retain their URL/handler/method contracts. Real HTTP
  checks cover partial/view-model binding, bookmark parameters, JSON validation,
  new Action Card preparation, five POST antiforgery guards and three file streams.
- Monitoring reset, asset versioning, TE labels/cells, shared scrolling,
  staircase positioning, modal dismissal, and synchronized blinking.
- OCAP mappings, including SING/ATC/MEMS, against `schema.sql`;
  Form11/Form12 white-cell layouts against both designers.
- Build with zero warnings/errors, SQL-schema/status fixtures, and reviewed
  desktop/mobile browser checks. Live SQL and workbook/PDF contents remain unverified.
- Summary reads use ordered batches with four shared read slots per app worker.
  AlertBoard/MachineSummary drilldowns reuse summary lists for 30 seconds, then
  use existing handlers with pending-request deduplication. Search/refresh clears
  snapshots; BmAnalytics retains its existing summary-backed drilldown.
  Designer metadata is static and machine lists render in one DOM batch.
  See `MONITORING_PERFORMANCE.md` for the audit and measurement limits.
- Seventy AlertBoard/Analytics query variants execute against LocalDB temporary
  fixtures with duplicate action cards and normalized active-MCD matches. Both
  scalar-subquery errors are reproduced first; corrected reads retain one row
  per BM record, matching filters and latest action status.
- Action Card repository/service checks pass in disposable LocalDB: all five
  modes, 40 concurrent-save attempts, stale New conflicts, TE BM-only guards,
  deterministic loads, and edits that preserve existing sibling duplicates.
  Browser workflow checks verify the loaded Seqno is submitted with Save.
- Monitoring HTML renders without SQL. The browser sends one antiforgery-protected
  TrackSession POST after window load; optional PC tracking has a two-second
  cancellation budget. OCAP uses asynchronous, request-cancelable reads.
  With a simulated unavailable SQL endpoint, first HTML improved from 8,395.8 ms
  to 220.4 ms locally; this is not an IIS/Visual Studio or production benchmark.

# Active Issues

P1 blocks data-parity sign-off; P2 requires compatibility resolution.

Database-impact audit: see `DATABASE_AUDIT.md` (2026-11-02). The application is
not read-only: Monitoring's after-load POST attempts PC-status writes, and Action Card
saves write shared legacy tables. Duplicate cards can break original Form2
scalar lookups; web SELECT corrections do not repair the original executable.
User-supplied diagnostics confirm PM BM `256152` / `SW-18` has two matching
cards (`Seqno` 55110/55111). The original web and legacy INSERTs were unchecked;
the originating application is unproven. Web new-card saves now atomically reject
duplicates using the legacy lookup key (TE: BM ID alone; others: BM ID/machine).
Existing web cards load the highest Seqno and edit that exact row only; stale
New forms and missing/mismatched edit identities fail without overwriting cards.
Desktop saves remain unprotected. No successful tool-initiated legacy
write was established from session records. The agent ran no SQL; the user
ran the SELECT-only diagnostics. No data repair has been applied.

| Issue | Impact | Priority |
|---|---|---|
| Confirmed duplicate PM Action Cards for BM 256152 / SW-18; database repair pending and desktop saves unprotected | Original Form2 still fails when this BM matches its filters; web saves prevent new duplicates and preserve existing rows; compare both cards before an owner-approved repair | P1 |
| Live SQL validation pending; SQL Server was unreachable during last validation | Real filtering, counts, null/duplicate behavior, saves, and report data remain unverified | P1 |
| Reports/evidence emit XLSX; legacy PDF/network-image parity incomplete | Documents require visual comparison and output-format reconciliation | P2 |

# Legacy Parity Rules

Explicit current requirements override legacy behavior. Otherwise, parity
overrides design preferences.

- Compare relevant `Legacy/*.vb.txt`, `*.Designer.vb.txt`, and active launch code.
  Preserve labels, grouping, order, palette, status meanings, and layouts.
  Do not activate commented-out/orphaned flows.
- Preserve SQL filters, ordering, user-specific action tables, and space-normalized
  active-MCD matching. TE processes: FL/FT/TP/EDS/MEMS; filter label: **Tester**.
- Summary Yes/No lookups use `EXISTS`; action-status reads select the highest
  `Seqno` with `OUTER APPLY TOP (1)` to avoid duplicate-card row multiplication.
  Retain the original correlations: AlertBoard TE matches BM ID only;
  Analytics TE matches BM ID and tester number. `BMContentID` is a primary key.
- Repeat Need Action counts qualifying group heads without actions, not BM rows.
  Preserve content/priority depth, TE `QUALITY BM`, and seven-day rules measured
  against the newest loaded BM. Separate Action status, repeat text, and capacity colors.
- Form2 follows the actual VB duplicate-row merge, stable formatted-date ordering,
  whole-day seven-day comparisons, and maximum TE depth of three subsequent rows.
  ATC/SING retain the legacy empty font-color result, so their repeats can qualify.
  Preserve its ten-entry query loop (including repeated `LSI3-1F`) separately
  from the nine designer columns. Its 54 white cells share one count/drilldown map;
  MP queries `LSI3-2F` but displays under `LSI3-1F`. Totals sum displayed cells once.
- MCD summaries count rows; deduplicate machine lists only. Forms7/8 retain reduced
  profiles; Forms11/12 retain distinct expanded mappings, including `ATC_65`.
  Keep query locations separate from designer-cell layouts.
- Initial dates: today −30 days through tomorrow; the 08:00 reset ends today.
  Refresh follows Monitoring's 600-second timer.
- Preserve Form3 templates/history/cell mappings. ATC PM reports are intentionally
  unsupported because legacy `ATCData` is empty.
- Report/evidence Generate and Download are separate explicit actions; opening
  a dialog must not trigger either.

# Modification Rules

- Make minimal changes; reuse existing handlers, services, and styles. Avoid
  unnecessary files, dependencies, and architectural rewrites.
- Preserve backend logic/queries unless required by the correction. Retain SQL
  parameterization, identifier allowlists, and antiforgery protection.
- Keep Monitoring-only navigation. Child controllers own AJAX, partials,
  redirects and downloads; preserve existing URLs and response contracts.
  Use explicit action verbs/handler selection, retain POST antiforgery checks,
  and pass presentation data through view models rather than controller instances.
- Scope workflow IDs, requests, timers, and refresh listeners; release on close.
  Late responses must not overwrite current/reset state.
- Keep static presentation in `Assets/css/`; use `.is-hidden`, designer colors,
  accessible controls, responsive overflow, and reduced-motion handling.
- Maintain current facts here; remove resolved Active Issues and omit patch/chat history.

Validation:

- Build: `dotnet build MachineKarteWebCore.csproj --nologo -v minimal`.
- SQL/status: `powershell -ExecutionPolicy Bypass -File scripts/Test-MonitoringQueries.ps1`.
- Form2 parity: `powershell -ExecutionPolicy Bypass -File scripts/Test-ActionSummaryParity.ps1`;
  compiles the checked-in VB business routines and compares 1,250 fixed/seeded fixtures.
- Duplicate SQL: `powershell -ExecutionPolicy Bypass -File scripts/Test-SummaryDuplicates.ps1`;
  requires SQL Server LocalDB and creates only session-local temporary tables in
  `tempdb`, without using the application connection string or database.
- Action Card writes: `powershell -ExecutionPolicy Bypass -File scripts/Test-ActionCardSaves.ps1`;
  real repository/service concurrency and edit tests use only a generated LocalDB
  test database, removed afterward. The script never loads appsettings or hosts the app.
- Startup: `powershell -ExecutionPolicy Bypass -File scripts/Test-Startup.ps1`;
  starts its own app with a loopback TCP sink that cannot execute SQL. Checks first
  HTML, tracking timeout/antiforgery, and page loads during tracking/OCAP waits.
- UI: `scripts/Test-Workflows.ps1`; requires the running app and Chromium debugging
  (default ports 5098/9227). `WorkflowBrowserChecks.js` supplies AJAX fixtures.
- Endpoints: `scripts/Test-WorkflowEndpoints.ps1`; requires the running app,
  uses real HTTP without database calls, and removes its temporary download fixtures.
- All edits: `git diff --check`. Documentation-only edits require no app rebuild.

# Monitoring Pages Standard

Applies to Monitoring, AlertBoard/Repeat BM, McdMonitoring, MachineSummary,
and BmAnalytics. Primary summaries share `Workflows.css`.

- Launches: Repeat BM → Form2; Need Action MCD → Form11; With Action → Form12.
  Card click → Form_Input; O → Form6; R/E → Forms3/4 or TE Forms9/10.
  O requires OCAP data; no Raw Data toolbar button.
- Primary summaries inherit only Monitoring's user/mode. Each open uses its own
  local today -30 days through tomorrow window; ignore incoming process, location,
  machine, and date selections. Show Search and grids before data arrives;
  omit Monitoring context and update-time/status labels. Show errors only when
  a load fails, and disable Search while loading. White counts/totals
  show `—` until success, including dormant TE cells. Failures retain placeholders
  and allow retry.
- Form11/Form12 retain twelve process rows and 54 designer white cells in both modes.
  Queries remain mode-specific; unavailable cells stay LightGray.
- Outside clicks close modals/drilldowns. Center drilldowns in their parent;
  Escape closes the drilldown first. Preserve focus trapping/restoration.
  Machine selection searches Monitoring in place.
- One board scrollbar; ten slots, 186px minimum. Unequal BM stacks and footers
  share a bottom baseline. Scroll to bottom after loading/layout, including
  simultaneous horizontal/vertical overflow.
- Preserve one-second alert toggles using the shared document clock.
- Clear Filters resets selections, dates, results, counters, pagination, and refresh.
  Clock stays below legend, outside filters; flex-center the floor overview.
- Palettes: Salmon/MistyRose cards, LightGray/yellow footers, Tomato AlertBoard,
  Blue/Azure MCD summaries, DarkOrange/Bisque action summaries.

# Known Technical Debt

- No permanent test project; validation uses PowerShell, a temporary console
  runner, and browser fixtures.
- Action Card tables guarantee uniqueness only for `Seqno`, not `(BMID, Machineno)`.
  Web INSERT guards use SQL range/update locks; without a lookup-key index these
  can acquire broad locks. Desktop writers can still create duplicates. Web
  existing-card updates match Seqno plus BM/machine and preserve sibling rows.
- The PM mapper interprets a subset of legacy expressions. Array/helper/runtime-derived
  values need representative fixture comparison.

# Investigation Checklist

- [ ] Restore SQL access; compare all five user modes, repeat/MCD counts, date
  boundaries, Action Card saves, and duplicate-record behavior with legacy data.
- [ ] Validate Form11/Form12 totals/drilldowns and the distinct Form8 profile.
- [ ] Compare Form3 computed/history cells and Forms4/9/10 workbooks, network images,
  and PDF requirements with legacy output.
