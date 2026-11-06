# Application guide

This is the current implementation guide. Read the relevant preserved form and
designer in [Legacy](../Legacy/) before changing business behavior. The active
legacy flow, approved web interaction changes, compiled mappings, and regression
checks take precedence over abandoned controls or commented-out routines.

Related guides: [database and performance](DATABASE.md), [reporting](REPORTS.md),
and [JavaScript conventions](../Assets/js/README.md).

## Structure

| Location | Responsibility |
| --- | --- |
| [Pages/Monitoring.cshtml](../Pages/Monitoring.cshtml) and its page model | Main Razor screen and Monitoring AJAX handlers |
| [Controllers](../Controllers/) | Child workflow routes, JSON handlers, and report downloads |
| [Pages/Shared/Workflows](../Pages/Shared/Workflows/) | Eleven modal partials |
| [Repositories](../Repositories/) | SqlClient queries and bounded read scheduling |
| [Services](../Services/) | Business rules and workbook generation |
| [Models](../Models/) | Requests, responses, designer layouts, and process mappings |
| [Services/Reporting](../Services/Reporting/) | Compiled VB report writers and generated mappings |
| [Assets](../Assets/) | Application JavaScript, CSS, and static report images |
| [Templates](../Templates/) | Original supplied workbooks |
| [scripts](../scripts/) | Offline, LocalDB, HTTP, and browser validation tools |
| [schema.sql](../schema.sql) | Reference schema snapshot; application startup does not execute it |

The application targets .NET 8, uses Razor Pages for Monitoring and MVC controllers
for child workflows, and exports workbooks with GemBox.Spreadsheet. It has no
permanent test project; the validation scripts create their own runners.

## Navigation and lifecycle

Monitoring is the only primary application screen. Child controllers inherit
[WorkflowController](../Controllers/WorkflowController.cs): `?dialog=1` returns
the appropriate partial, while a direct child URL redirects to
`/Monitoring?workflow=<name>` with its parameters. `WorkflowHandlerAttribute`
preserves the existing `?handler=...` API contract.

[shared/Workflows.js](../Assets/js/shared/Workflows.js) owns the native dialog.
Each opening has a cancellation signal, scoped element lookup, JSON helpers,
timers, and refresh listeners. Closing releases those resources and restores
focus to the launcher. Escape dismisses a local machine drilldown before its
parent dialog; backdrop clicks also dismiss dialogs and drilldowns.

Machine selections update Monitoring in place. Successful Action Card saves
close their dialog and refresh the board. All child auto-refresh behavior follows
Monitoring's `monitoring:refresh` event on the 600-second cycle.

| Legacy form | Web workflow | Launch or distinguishing behavior |
| --- | --- | --- |
| Form1 | Monitoring | Filters, ten-column machine paging, floor summaries, shared refresh clock |
| Form2 | AlertBoard | Repeat-BM summary; Form1 `Button5` |
| Form3 | PmReport | Non-TE card `R` command |
| Form4 | EvidenceMaking | Non-TE card `E` command |
| Form5 | RawData | Read-only charts and source tables; no Monitoring toolbar button |
| Form6 | OcapViewer | Conditional card `O` command |
| Form7 | McdMonitoring | Alternate server-defined MCD dashboard |
| Form8 | BmAnalytics, profile `8` | Alternate action-with-MCD summary |
| Form9 | BmReport | TE card `R` command |
| Form10 | TeEvidenceMaking | TE card `E` command |
| Form11 | MachineSummary | Need-action-with-MCD summary; Form1 `btnMCD` |
| Form12 | BmAnalytics, profile `12` | Expanded action-with-MCD summary; Form1 `btnWithAction` |
| Form_Input | ActionCards | Click a BM card to load or edit its action |

Report/evidence dialogs require explicit Generate and Download actions. Excel is
the default; PDF generation enables an inline preview. Opening a dialog does not
generate or download a report. Do not add standalone pages for these child flows.

## Behavior to preserve

- Monitoring defaults to 30 days ago through tomorrow. Its legacy morning reset
  uses today as the end date. PM/CC/EEE/VIS use machine processes; TE uses
  FL/FT/TP/EDS/MEMS tester processes. Clear Filters cancels pending requests and
  resets dates, filters, results, counters, paging, and refresh state.
- BM histories use the active `uc_Test` flow. Need Action counts qualifying
  repeat-group heads, excluding black, action-present, and solved/green groups;
  it does not count every BM row. Preserve case/content or TE priority matching,
  ordering, the seven-day comparisons, and TE QUALITY BM rules.
- Solved-action checks compare against the newest loaded BM and latest matching
  case, not the current wall clock. The Action badge's status color, repeat-case
  text color, and ActionLine capacity bands have separate meanings.
- Card commands remain O, R, E, with O shown only when OCAP data exists. AC/SM/DM/DV
  markers reflect action and successive comment fields. Existing Action Card
  edits retain the loaded `Seqno`; nonempty comment/reply blur stamps the legacy
  12-hour timestamp, and clearing the text clears its date.
- AlertBoard, MachineSummary, and BmAnalytics open with their own today-minus-30
  through tomorrow range and selected user. Monitoring's process, location,
  machine, and chosen dates do not constrain them. McdMonitoring retains its
  own editable filters.
- Primary summary layouts appear before data arrives. Search stays disabled
  during a load; white cells and totals show an em dash until success. Failure
  retains the layout for retry. All twelve process rows remain visible in TE
  mode; designer-only white cells become zero and unavailable cells stay gray.
- Form2 retains its ten-entry query-location sequence, including repeated
  `LSI3-1F`, while its displayed matrix uses nine distinct locations. Totals sum
  displayed cells once. Its compiled-VB parity checks are the reference for
  repeat/count behavior.
- MCD summaries count qualifying rows separately from their deduplicated machine
  lists. Forms 7/8 use reduced location profiles; Forms 11/12 retain expanded
  mappings. Form12's `ATC_65` slot is intentional. Do not merge these profiles.
- RawData retains the legacy recurrence grouping and date-filter placement.
  Its machine argument does not add a SQL machine filter. OCAP is read-only,
  overlays serialized remarks/judgements only when both sections exist, and
  retains legacy `test1`/`test2` defaults when no saved values exist.
- `uc_Test` and `uc_Panel` belong to the active Monitoring flow. The unreferenced
  `uC_BMCaseWName`, commented `uc_Form`, and empty `uc_BMCase` do not justify new
  routes or queries.

## Presentation and assets

Preserve designer grouping, ordering, status meanings, and palettes while keeping
controls accessible and responsive. The main deck is dark; BM cards use
Salmon/MistyRose with blue, purple, and green capacity tiers. AlertBoard uses
Tomato/MistyRose, need-action summaries Blue/Azure, and action summaries
DarkOrange/Bisque. Action Card comments/replies retain their blue/purple/green
division rows.

Machine columns occupy ten board slots with a 186px minimum width, stay left
aligned for sparse results, and share one scrolling board. Unequal histories
form a bottom-aligned staircase; scroll to the bottom after requests and layout
finish. Blink animations share the document clock, and reduced-motion settings
keep static alert colors. RawData uses native SVG charts without a chart CDN.

Static presentation belongs in `Assets/css`. Shared workflow/document styles
are in `Workflows.css`; OCAP, RawData, ActionCard, and Monitoring retain their
own active stylesheets. `Url.VersionedAsset` versions external application
CSS/JS using file timestamps. Update the script list in Monitoring whenever
moving JavaScript files.

## Validation

Run `dotnet build MachineKarteWebCore.csproj --no-restore` after restoring packages,
and `git diff --check` for source/documentation changes. Choose further checks
for the affected behavior:

| Script in `scripts/` | Scope and environment |
| --- | --- |
| `Test-MonitoringQueries.ps1` | Generated SQL/schema references, statuses, designer mappings, scheduling, and Need Action fixtures; no database |
| `Test-MonitoringParity.ps1` | Actual Monitoring repository/dashboard reads compared with original Form1/uc_Test SQL, TE/FT regression, pagination and duplicate safety; disposable LocalDB catalog; optional `-Benchmark` |
| `Test-ActionSummaryParity.ps1` | Form2 counts/colors against compiled original VB routines; no database |
| `Test-ReportGeneration.ps1` | Real templates, photos, compiled mappings, XLSX/PDF output, and capacity errors; offline |
| `Update-LegacyReportMappings.ps1 -Check` | Generated mappings match extraction and audited corrections; offline |
| `Audit-ReportTemplates.ps1` | Reproducible template/binding inventories; offline, output defaults to OS temporary storage |
| `Test-SummaryDuplicates.ps1` | Real summary SQL against session-local tables in LocalDB |
| `Test-ActionCardSaves.ps1` | Concurrent inserts, duplicate guards, loaded identities, and edits; disposable LocalDB database |
| `Test-DataRetrieval.ps1` | Actual repository reads, raw types, report lookups, evidence limits, and cancellation; disposable LocalDB database |
| `Test-Startup.ps1` | Starts its own app with a loopback non-SQL sink; HTML responsiveness and bounded tracking |
| `Test-WorkflowEndpoints.ps1` | Existing local app: redirects/partials, rejected requests, download streams and inline headers |
| `Test-Workflows.ps1` | Existing local app and Chromium debugging port: real partials/assets with AJAX fixtures, all eleven workflows, desktop/mobile geometry, cancellation, and resource cleanup |
| `Render-ReportPdf.ps1` | Independent Windows PDF rendering; requires an exported PDF and output directory |

Invoke PowerShell tools from the repository root, for example
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-MonitoringQueries.ps1`.
HTTP/browser tools accept `-BaseUrl`; the browser runner also accepts
`-DevToolsPort` and `-ArtifactDirectory`. Use a separately hosted test app with
an unreachable loopback SQL override for fixture UI checks. Browser mocking
does not configure the application's server-side connection.

Fixture results cover the tested paths, not production SQL performance or full
desktop output parity. The remaining database and report acceptance work is
recorded in the related guides.

## Temporary output

Generated reports are stored in the separate PM/TE server
`TempData\Print Out\<PROCESS>` folders described in [report storage](REPORTS.md#generated-report-storage).
The application does not create a local report folder or contact those shares
at startup. Removing generated files removes the corresponding downloads until
regenerated. Keep source workbooks in `Templates/` and static images in
`Assets/images/`. No `TempData/ReportAudit/` archive is required.

Audit inventories, rendered review pages, browser captures, and validation logs
belong in OS temporary storage or an explicitly chosen external output folder.
They are reproducible artifacts, not checked-in documentation.
