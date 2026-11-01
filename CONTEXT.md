# MachineKarteWebCore Conversion Context

## Purpose

This ASP.NET Core Razor Pages application converts the legacy E-Karte WinForms
application to the web. The legacy VB files in `Legacy/` are the source of
truth for SQL, filtering, ordering, status rules, and user-visible behavior.
Read this file and the relevant legacy form before changing converted logic.

## Stack and conventions

- .NET 8 ASP.NET Core Razor Pages
- `Microsoft.Data.SqlClient` with repository/service separation
- ClosedXML for Excel report generation
- `Pages/` contains Razor pages and page models.
- `Repositories/` contains SQL access.
- `Services/` contains business logic and report generation.
- `Models/` contains request, response, and view models.
- `Assets/js` and `Assets/css` contain page-specific browser assets.
- SQL Server connection: `ConnectionStrings:DBConnection` in `appsettings.json`.
- No test project currently exists. The baseline validation command is:
  `dotnet build MachineKarteWebCore.csproj --nologo -v minimal`

## Monitoring-only application workflow

The current user requirement is stricter than the general conversion rule below:
**Monitoring is the only navigable application screen. All other converted forms
open as modal workflows within Monitoring, including reports and analytics.**

- `Pages/Monitoring.cshtml` owns the full document and the filter/card board.
  `Pages/Shared/_Layout.cshtml` contains only that application shell; there is no
  separate Home/Privacy navigation.
- Child UI lives in `Pages/Shared/Workflows/_*.cshtml`. The small `.cshtml` files
  beside the existing page models retain their AJAX and download routes.
  `WorkflowPageModel` returns a partial for `?dialog=1`; direct child URLs redirect
  to `/Monitoring?workflow=<name>` with the original parameters preserved.
- `Assets/js/Workflows.js` loads partials into the shared native `<dialog>` in
  `Pages/Shared/_WorkflowDialog.cshtml`. Dialog scripts register scoped initializers,
  use namespaced control IDs, and release requests, timers, and refresh listeners
  on close. Native dialog behavior supplies focus trapping and Escape dismissal;
  closing restores focus to the launching control.
- Machine selections update Monitoring's filters and search in place. Action-card
  saves close the dialog and refresh Monitoring. Child summaries follow the
  `monitoring:refresh` event from Monitoring's 600-second clock.
- Active legacy launch code is authoritative: Form1 `Button5` opens Form2,
  `btnMCD` opens Form11, and `btnWithAction` opens Form12. The last two map to
  the MachineSummary modal and BmAnalytics modal with `legacyForm=12` respectively.
  Form7 and Form8 remain available through the other-summary dialog controls.
- A BM card opens Form_Input. Its `R` action opens Form3 for non-TE and Form9
  for TE; `E` opens Form4 or Form10; `O` opens the read-only Form6 dialog.
  Raw Data opens Form5 using Monitoring's selected process, location, and dates.

### Designer-based web styling

Inspect each form's designer, code-behind, and launch code before changing its
structure. Preserve its grouping, control order, data hierarchy, status meanings,
and palette while using responsive CSS, accessible labels, and scrollable grids.
Do not replace the designer palette with a generic dashboard theme.

| Legacy designer | Web structure and palette |
|---|---|
| Form1 | `#343434` top command deck, floor totals and child-form buttons at left, filters at right, white scrolling machine board, dark machine summaries beneath the cards; Green Search, DeepSkyBlue pagination, PowderBlue page number |
| Form2 | Process rows by location columns, white counts, LightGray unavailable/header cells, MistyRose process labels, Tomato headings/actions, local machine lookup panel |
| Form7 and Form11 | Same matrix/lookup structure with Blue headings/actions and Azure process labels |
| Form8 and Form12 | Same matrix/lookup structure with DarkOrange headings/actions and Bisque process labels; retain their distinct location profiles |
| Form_Input | Dark panel, Salmon problem/employee/date header, full-width investigation then action rows, three paired comment/reply rows in DeepSkyBlue, MediumPurple, and LimeGreen, MC Status and Green Save at the bottom |
| Form5 | Two vertically stacked chart/table pairs, chart at left and source table at right, roughly 72/28 desktop proportions, gray chart surround; stack panels on narrow screens |
| Form6 | Full-width read-only five-column OCAP grid with gray headers/alternating rows and the designer's column proportions |
| Form3, Form4, Form9, Form10 | Document dialogs matching the dynamically populated legacy report/evidence windows; retain workbook generation and download behavior |

`Assets/css/site.css` defines the designer palette and common controls.
`Monitoring.css`, `Workflows.css`, `ActionCard.css`, `RawData.css`, and
`OcapViewer.css` are the active stylesheets loaded by Monitoring. The previous
standalone summary/report stylesheets were removed; their modal styles live in
`Workflows.css`.
Raw Data uses native SVG stacked charts, so this flow requires no chart CDN.
The existing `.xlsx` output remains the report/evidence viewing path; a browser
PDF preview is only shown when a generator actually supplies a PDF.

## Modal-First Conversion Rule

Before creating a new Razor Page, determine how the legacy WinForms form was
actually used. Inspect its `Designer.vb` (stored here as `*.Designer.vb.txt`),
code-behind, and calling code to establish intent.

Do not automatically create a dedicated page when the legacy form:

- Was only opened from another form.
- Acted as a popup dialog.
- Acted as a lookup window.
- Acted as a detail viewer.
- Acted as a confirmation screen.
- Had no independent navigation path.
- Had no standalone workflow.

Prefer the following for dialog workflows:

- Controller/API endpoint.
- Partial view.
- Razor component.
- Shared modal.
- AJAX-loaded dialog.

Create a standalone page only when the legacy form functioned as a true screen
with its own workflow, filtering, navigation, refresh cycle, or reporting
process.

Answer these questions before creating a page:

1. How is this form launched in legacy?
2. Is `ShowDialog` used?
3. Is it only a detail popup?
4. Does it return data to the parent form?
5. Can users reach it directly in legacy?
6. Does it have independent business behavior?

If the answers indicate dialog behavior, convert it as a modal workflow rather
than a new page.

**Goal: Preserve user workflow, not WinForms form count.**

## Form conversion tracker

| Legacy form | Current conversion | Status | Source/current files |
|---|---|---|---|
| Form1 | Machine Monitoring | Converted and reconciled | `Legacy/Form1.vb.txt`, `Legacy/Form1.Designer.vb.txt`, `Pages/Monitoring.cshtml`, `Pages/Monitoring.cshtml.cs`, `Repositories/MonitoringRepository.cs`, `Services/MonitoringService.cs`, `Models/MonitoringModels.cs`, `Assets/js/Monitoring.js` |
| Form2 | Repeat BM Alert Board | Converted and reconciled | `Legacy/Form2.vb.txt`, `Legacy/Form2.Designer.vb.txt`, `Pages/AlertBoard.cshtml`, `Pages/AlertBoard.cshtml.cs`, `Repositories/AlertBoardRepository.cs`, `Services/AlertBoardService.cs`, `Models/AlertBoardModels.cs`, `Assets/js/AlertBoard.js` |
| Form3 | PM Report | Converted and reconciled | `Legacy/Form3.vb.txt`, `Legacy/Form3.Designer.vb.txt`, `Pages/PmReport.cshtml`, `Pages/PmReport.cshtml.cs`, `Repositories/PmReportRepository.cs`, `Services/PmReportService.cs`, `Models/PmReportModels.cs`, `Assets/js/PmReport.js`, `Form3CellMappingMatrix.txt` |
| Form4 | Evidence Making | Converted and reconciled | `Legacy/Form4.vb.txt`, `Pages/EvidenceMaking.cshtml`, `Repositories/EvidenceRepository.cs`, `Services/EvidenceMakingService.cs`, `Models/EvidenceMakingModels.cs`, `Assets/js/EvidenceMaking.js` |
| Form5 | Raw Data | Converted and reconciled | `Legacy/Form5.vb.txt`, `Pages/RawData.cshtml`, `Repositories/RawDataRepository.cs`, `Services/RawDataService.cs`, `Models/RawDataModels.cs`, `Assets/js/RawData.js` |
| Form6 | OCAP Viewer | Converted and reconciled | `Legacy/Form6.vb.txt`, `Pages/OcapViewer.cshtml`, `Repositories/OcapViewerRepository.cs`, `Services/OcapViewerService.cs`, `Models/OcapViewerModels.cs`, `Assets/js/OcapViewer.js` |
| Form7 | MCD Monitoring | Converted and reconciled | `Legacy/Form7.vb.txt`, `Pages/McdMonitoring.cshtml`, `Repositories/McdMonitoringRepository.cs`, `Services/McdMonitoringService.cs`, `Models/McdMonitoringModels.cs`, `Assets/js/McdMonitoring.js` |
| Form8 | Summary of Action With MCD | Converted and reconciled | `Legacy/Form8.vb.txt`, `Legacy/Form8.Designer.vb.txt`, `Pages/BmAnalytics.cshtml`, `Repositories/BmAnalyticsRepository.cs`, `Services/BmAnalyticsService.cs`, `Models/BmAnalyticsModels.cs`, `Assets/js/BmAnalytics.js` |
| Form9 | BM Report | Converted and reconciled; live SQL and workbook QA remain | `Legacy/Form9.vb.txt`, `Pages/BmReport.cshtml`, `Repositories/BmReportRepository.cs`, `Services/BmReportService.cs`, `Templates/BmReport/` |
| Form10 | TE Evidence Making | Converted and reconciled; live SQL and workbook/image QA remain | `Legacy/Form10.vb.txt`, `Legacy/Form10.Designer.vb.txt`, `Pages/TeEvidenceMaking.cshtml`, `Repositories/TeEvidenceRepository.cs`, `Services/TeEvidenceMakingService.cs`, `Models/TeEvidenceMakingModels.cs`, `Templates/BmReport/CWC.xlsx` |
| Form11 | Machine Summary / MCD machine list | Converted and reconciled; live SQL validation remains | `Legacy/Form11.vb.txt`, `Pages/MachineSummary.cshtml`, `Repositories/MachineSummaryRepository.cs`, `Services/MachineSummaryService.cs`, `Models/MachineSummaryModels.cs`, `Assets/js/MachineSummary.js` |
| Form12 | BM Analytics / With Action path | Converted with explicit profile; live SQL validation remains | `Legacy/Form12.vb.txt`, `Pages/BmAnalytics.cshtml`, `Pages/BmAnalytics.cshtml.cs`, `Repositories/BmAnalyticsRepository.cs`, `Services/BmAnalyticsService.cs`, `Models/BmAnalyticsModels.cs`, `Assets/js/BmAnalytics.js` |
| Form_Input | Action Cards | Converted and reconciled | `Legacy/Form_Input.vb.txt`, `Legacy/Form_Input.Designer.vb.txt`, `Pages/ActionCards.cshtml`, `Pages/ActionCards.cshtml.cs`, `Repositories/ActionCardRepository.cs`, `Services/ActionCardService.cs`, `Models/ActionCardModels.cs`, `Assets/js/ActionCard.js` |

## Legacy reusable-control audit

- `Legacy/uC_BMCaseWName.vb.txt` defines a standalone BM-case history widget,
  but no legacy form instantiates it or calls `LoadMain`. The active Form1
  machine cards instantiate `uC_Test` instead; the current web equivalent is
  the Monitoring machine-card path.
- If used independently, `uC_BMCaseWName.LoadMain` queries
  `[<process>BMMaintenance]` joined to `BMCase`, filters by the requested
  machine, date range, and `Pmcategoryid = '1'`, and orders by machine,
  request time, and BM case ascending. The legacy `@Location` parameter is
  supplied but not used in the SQL, so no location filter should be invented.
- The widget displays at most the first thirteen returned rows and colors
  adjacent same-`BMCase` runs: four equal cases are red, three are orange,
  and two are dark green. Its helper methods are local WinForms label logic,
  not a separate application workflow.
- `Legacy/uc_BMCaseWName.Designer.vb.txt` contains only the visual template
  (`BM Case`, date, and hidden color label). `Legacy/uc_BMCase.vb.txt`
  contains empty event handlers and no data or business logic.
- No web patch was added for these orphaned controls. Creating a new route,
  query surface, or visible component would expand scope beyond the active
  Forms 1–12 behavior. Revisit only if a future requirement identifies a
  current page that must expose this widget.

## Legacy `uc_Form` control audit

- `Legacy/uc_Form.vb.txt` contains no active implementation. Its database
  field, `LoadMain`, load handler, machine-history query, paging query, and
  label-population logic are all commented out.
- `Legacy/uc_Form.Designer.vb.txt` is only the designer layout for the
  abandoned prototype: a large static panel containing machine labels and
  BM-case/date label slots. The designer does not provide runtime behavior.
- No legacy form instantiates `uC_Form` or calls its commented `LoadMain`.
  Form1's active machine-card path creates `uC_Test`, and the converted
  Monitoring page already represents that active path.
- The commented prototype would have queried
  `[<process>BMMaintenance]` joined to `BMCase`, filtered by date,
  location/selected machine, and `Pmcategoryid = 1`, then displayed grouped
  machine history. Because the entire implementation is disabled and no
  current screen depends on it, these commented queries are not treated as
  a conversion requirement.
- No web patch was added. Creating a route or component from this unused
  designer would introduce behavior that is not active in the legacy
  application.

## Legacy `uc_Panel` control audit

- `Legacy/uc_Panel.vb.txt` is a simple active display control used by Form1's
  `uC_Test` path. Form1 creates one per displayed machine and assigns the
  machine number, location, machine type, and `NeedActionCount`.
- `Legacy/uc_PanelDesigner.vb.txt` confirms the visible fields: location,
  machine number, machine type, and the red `Need Action` counter. Its
  one-second timer has no active handler in the code-behind.
- The legacy `NeedActionCount` is incremented once for each qualifying repeat
  group (the first row only), when the repeat color is not `Black` and the row
  has no action card. The converted repository applies the legacy repeat-color
  algorithm; the web response counts repeat-group starts rather than every
  colored row.
- The converted Monitoring machine-column header now exposes
  `Need Action: <count>` alongside the existing machine/BM count. This is the
  web equivalent of the legacy bottom `Panel4`/`uC_Panel` summary without
  duplicating the machine-card query.

## Legacy `uc_Test` reconciliation

- `uc_Test.LoadMain` is the active Form1 machine/tester card control. Its
  queries preserve the selected process, location, machine/tester, date range,
  user-specific turnover/status filters, action-card table, MCD lookup,
  content-ID fallback, OCAP count, and legacy ordering.
- The converted Monitoring repository already applies the legacy repeat-color
  algorithm for normal and TE rows. The `NeedActionCount` calculation now
  matches the active control more closely: one count per repeat-group start,
  excluding `Black`, action-card-present, and solved/green rows.
- Legacy action-card metadata displays four optional header markers:
  `AC` from `Action`, `SM` from `Comment`, `DM` from `Comment2`, and `DV`
  from `Comment3`. The converted card query now returns these fields and the
  web card renders the same markers with matching color roles.
- Legacy `R→`, `E→`, and conditional `O→` actions are already represented by
  the Monitoring card footer and route to the converted PM report, evidence,
  and OCAP pages. Card clicks continue to open the existing Action Card flow.

## Form1 baseline and reconciliation

- Initial dates are legacy `dt1 = now - 30 days` and `dt2 = now + 1 day`.
- The 08:00 refresh path calls `SetDate`, which resets `dt2` to today and
  `dt1` to 30 days before today.
- PM, CC, EEE, and VIS use machine processes; TE uses tester processes.
- TE processes are `FL`, `FT`, `TP`, `EDS`, and `MEMS`.
- Machine processes are `DC`, `DB`, `WB`, `MP`, `PL`, `FL`, `ATC`, `SING`,
  `FT`, `TP`, `EDS`, and `MEMS`.
- Machine dropdowns read `MCList` joined to `Process`; TE dropdowns read
  non-empty `TEBMMaintenance.TesterNo`.
- Machine summary pagination is ten columns using the legacy `TOP` plus
  `EXCEPT` query shape and its legacy ordering.
- BM card queries preserve the legacy user filters, status exclusions,
  action-card table selection, MCD lookup, content-ID fallback, and ordering.
- Form1 floor index mapping is:
  `1=LSI1-1F`, `2=LSI1-2F`, `3=LSI2-1F`, `4=LSI2-2F`,
  `5=LSI2-3F`, `6=LSI3-2F`, `7=LSI3-4F`, `8=LSI3-3F`,
  `9=LSI3-1F`.
- Process floor lists are defined by `LoadMCName1`; MP ends at `LSI3-1F`,
  while DC/DB/WB end at `LSI3-2F`.
- MCD status is based on active `MachineKarte_MCD` device matching after
  removing spaces. Action-card status is user-type-specific.
- Legacy repeat-color logic is date-sensitive and must not be replaced by a
  generic repeat counter without comparing `BMCase`, content/priority,
  machine/tester, action status, and seven-day behavior.

- Corrected MP process floor mapping in
  `Repositories/MonitoringRepository.cs`.
- Corrected TE floor-summary MCD counting to use the same active-device match
  as Form1.
- Corrected the browser 08:00 date reset in `Assets/js/Monitoring.js` to use
  today, matching legacy `SetDate`.

## Form2 behavior baseline

- Form2 is the Repeat BM alert board opened from Form1's `Button5`.
- It reads the active Form1 user mode and date range, then queries every
  configured process and location. Normal processes are
  `DC`, `DB`, `WB`, `MP`, `PL`, `FL`, `FT`, `TP`, `MEMS`, `EDS`, `ATC`,
  and `SING`; TE processes are `FL`, `FT`, `TP`, `MEMS`, and `EDS`.
- Normal rows use the selected user turnover/status filter and corresponding
  action-card table. TE rows use `TEBMMaintenance`, `BMTEUnit`,
  `Pmcategoryid` 1 or 4, and status exclusions
  `30,31,1,2,3,7,17`.
- Form2 preserves the content-ID fallback, action-card existence check, and
  legacy ordering. It only counts repeated BM cases after the seven-day
  comparison and excludes rows with an action card or solved (`Green`) state.
- Normal repeat coloring checks up to five subsequent rows using
  `BMContentID` and `BMCase`. TE repeat coloring checks up to four subsequent
  rows using `Priority` and `BMCase`; TE qualifying repeats are red.
- The drilldown maps a process/location cell to machine/tester values and
  returns to Monitoring with the selected machine, user mode, and date range.

## Form2 reconciliation completed

- Corrected TE repeat detection to inspect four subsequent rows, matching
  `LoadColor_TE`.
- Matched Alert Board defaults to Form1's `dt1`/`dt2` range (`-30 days` to
  `+1 day`).
- Preserved user mode and dates when navigating from Alert Board drilldown to
  Monitoring.
- Wired the Monitoring 600-second timer to signal Alert Board's auto-refresh,
  matching Form2's dependency on `Form1.lblTimer`.

## Form3 behavior baseline

- Form3 is the non-TE `R→` PM maintenance-record report opened from a
  monitoring BM card. TE `R→` continues to use the Form9 BM report.
- `LoadData` dispatches by process to the process-specific workbook and view:
  `DC/ViewDCData_Ver4`, `DB/ViewDBData_Ver4`, `WB/ViewWBData_Ver4`,
  `MP/ViewMPData_Ver4`, `PL/ViewPLData_Ver2`, `FT/ViewFTData`,
  `FL/ViewFLData_V2`, `SING/ViewSINGData`, `TP/ViewTPData`,
  `MEMS/ViewMEMSData`, and `EDS/ViewEDSData`.
- Current data is selected by `bmID`. Where the legacy writer requests
  history, it selects earlier records for the same machine/tester, excluding
  the current BM, before `TimeOfRequest`, ordered by descending `bmID`, with
  the first two records available to the workbook mapping.
- `Form3CellMappingMatrix.txt` preserves the legacy process-specific cell
  assignments and style changes. The web mapper now preserves direct field
  concatenation, short date/time formatting, formatted `ToString` dates, and
  simple field-based `If` expressions.

## Form3 reconciliation completed

- Routed non-TE Monitoring `R→` actions to `/PmReport` with process, BM ID,
  request time, and machine/tester context.
- Preserved legacy date/time formatting and simple conditional cell values
  while applying the Form3 mapping matrix.
- Confirmed that legacy `ATCData` is an empty procedure, so no ATC PM report
  template/view was fabricated in the web conversion.

## Form_Input behavior baseline

- Form_Input is the Check And Action Card opened by clicking a BM card in
  `uc_Test`. A normal `R→` opens Form3; TE `R→` opens Form9. Clicking the BM
  card opens Form_Input with BM ID, machine/tester number, user type, action
  status, card color, and BM case.
- User type selects the action-card table: PM uses
  `MachineKarte_ActionCard`, TE uses `_TE`, and CC/VIS/EEE use their matching
  suffix tables. Load, insert, and update use `BMID` plus `Machineno` as the
  key.
- New cards initialize `DateCard` to `MM/dd/yyyy hh:mm:ss`, leave status at
  `-SELECT-`, and map the incoming card color to the corresponding machine
  status. Existing cards load all fields, but legacy then reselects the
  machine status from the incoming color when that color is recognized.
- Save rejects `-SELECT-`, inserts all header, investigation, action,
  division-comment, and PM/TE-reply fields, converts `-SELECT-` to an empty
  `MCStatus`, and refreshes Form1 after success. Existing cards update the
  same fields by BM ID and machine/tester without changing `DateCard`.
- Leaving any non-empty comment or reply writes the current
  `MM/dd/yyyy hh:mm:ss` timestamp; clearing it clears its date.
- The commented `MachineKarte_ReBM.isDone` update is not active legacy
  behavior and remains intentionally unimplemented.

## Form_Input reconciliation completed

- Corrected existing-card loading to apply the recognized incoming card-color
  status after database fields are loaded, matching `LoadData` in
  `Form_Input.vb`.
- Confirmed the converted table mapping, parameterized key predicates,
  insert/update field set, color-to-status mapping, auto-date handlers, and
  post-save monitoring refresh match the active legacy workflow.

## Form4 behavior baseline and reconciliation

- Form4 `EMakingPrint(ProcessCase, bmid)` selects a process-specific workbook,
  evidence view, and maintenance-data view for DC, DB, WB, MP, PL, FL, SING,
  FT, TP, EDS, and MEMS.
- It reads the same five `gridEvidenceMaking_Ver3` categories and limits:
  problem point `TOP 3`, action taken `TOP 2`, parts replaced `TOP 2`,
  disassembly/reassembly `TOP 2`, and quality confirmation `TOP 3`, ordered by
  `BMEvidenceID`.
- It reads the process view's route/version, nine TSC confirmations, and
  SING-only `SignalTowerKanban`, `SafetyTags`, and `Bypass` fields.
- The converted workbook writer preserves the legacy header cells, SING versus
  non-SING image/text cell placements, fallback `noimage.png` behavior, and TSC
  checkmark locations.
- Corrected the SING template filename to the checked-in
  `EvidenceMaking_Sing.xlsx` name and changed generated-file links to use the
  page's validated download handler rather than an unserved filesystem URL.

## Form5 behavior baseline and reconciliation

- Form5 reads the process-specific maintenance table joined to `BMCase`,
  excludes null occurrence dates and BM cases containing `corrective` or
  `preventive`, filters by location and occurrence date range, and preserves
  the legacy window-function ordering and recurrence comparison.
- The legacy `Machine` argument is accepted but is not used in the active SQL;
  the converted request preserves that behavior.
- Frequency analysis checks 2x through 5x recurrence runs, carries the legacy
  `FreqCount` into `CreatedTable`, and groups duplicate month/frequency rows
  using the sum of those counts.
- BM-case monthly results preserve the legacy `Same as next row` filter,
  grouping, chronological ordering, and descending count ordering.
- Corrected frequency aggregation to sum the legacy recurrence counter instead
  of merely counting grouped rows.

## Form6 behavior baseline and reconciliation

- Form6 loads OCAP checklist rows from `gridBMOCAP` by `BMCaseID` and process,
  ordered by `BMOCAPID`, and displays the five legacy columns.
- It loads `OCAPData` from the process-specific maintenance table using `BMID`.
  The stored format is `remarks1,remarks2,...|judgement1,judgement2,...`;
  saved values overlay the corresponding checklist rows only when both pipe
  sections are present.
- When no saved values exist, the active legacy query initializes judgement to
  `test1` and remarks to `test2`; the converted service now preserves those
  defaults.
- The legacy viewer is read-only and clears row selection after loading; the web
  table is likewise display-only.

## Form7 behavior baseline and reconciliation

- Form7's active `NewLoadColor` path counts rows only when the device matches an
  active `MachineKarte_MCD` row and no matching action card exists. The legacy
  recurrence `LoadColor` path is commented out and is not active behavior.
- Standard mode queries maintenance rows with `Pmcategoryid = '1'`, excludes
  BM statuses `30` and `31`, excludes `BMCaseID = 0`, and filters by location
  and the Form1 date range. TE mode uses `teBMMaintenance`, `BMTEUnit`,
  categories `1` or `4`, and excludes statuses `30,31,1,2,3,7,17`.
- The converted queries preserve active-MCD matching, action-card suppression,
  process-specific table selection, and legacy descending order by machine,
  request time, and BM case/unit.
- Corrected Form7's process-location configuration to match its active legacy
  loop: four locations for DC/DB/WB/MP, two for PL and ATC, four FL/FT/TP/SING
  locations, and single EDS/MEMS locations. The retained ATC/SING arrays are
  intentional legacy behavior.

## Form8 behavior baseline and reconciliation

- Form8 is the legacy `"Summary of Action With MCD"` screen and maps to the
  converted `BmAnalytics` page.
- Its active `NewLoadColor` path counts only maintenance rows where the device
  matches an active MCD record and a matching action card exists
  (`HaveAction = 'Yes'`). Machine/tester values are deduplicated for the
  selectable summary lists.
- Standard and TE queries retain Form8's process-specific maintenance tables,
  status/category exclusions, date/location filters, MCD matching, action-card
  joins, and descending machine/request-time/BM ordering.
- The process/location mappings now follow Form8's active loop: DC/DB/WB/MP
  use four locations, PL and ATC use two, FL/FT/TP/SING use four, and
  EDS/MEMS use one. TE FL/FT/TP use four locations; TE EDS/MEMS use one.
- Form8 uses the Form1 date range defaults (`now - 30 days` through
  `now + 1 day`) and preserves mode/date values when opening Monitoring.
  Refresh timing follows the legacy timer cycle rather than the previous
  independent 60-second refresh.

## Form12 behavior baseline and reconciliation

- Form12 uses the same active `NewLoadColor` condition as Form8:
  `isMCD = 'Yes'` and `HaveAction = 'Yes'`. The commented recurrence
  `LoadColor` path is not active behavior.
- Form12 uses an explicit profile on `BmAnalytics` (`/BmAnalytics?legacyForm=12`)
  so the previously reconciled Form8 profile remains the default. The profile
  preserves Form12's expanded EEE/TE slots: five locations for DC/DB/WB/MP,
  five for FL/FT/TP, three for PL, five displayed SING slots, seven ATC slots,
  and the single EDS/MEMS slots.
- Form12's ATC mapping preserves the legacy `ATC_65` label/list slot for
  `LSI3-1F` and the final `ATC_7` slot for `LSI3-3F`. Counts increment for
  every qualifying row; only machine-number list values are deduplicated by
  the legacy `RemoveDuplicates` behavior.
- The Form12 profile sends its form identity with each grid request, uses the
  same Form1 date defaults, and preserves mode/date values when navigating to
  Monitoring. Live SQL and fixture validation remain pending.

## Known limitations and next work

- Form9 is the legacy `"BM Maintenance Record"` workbook/report screen and
  maps to `Pages/BmReport.cshtml`, `Repositories/BmReportRepository.cs`, and
  `Services/BmReportService.cs`. Its active query remains
  `ViewTEData_Test WHERE bmID = @bmID`, with the `VRNumber` lookup and the
  same PM-category, BM taxonomy, serialized QC/check-item, changed-parts,
  work-description, external-accessory, and EDS/TE template branches.
- Form9 reconciliation confirmed the template cell routing for EDS and TE,
  including VR slots, quality-BM fields, setup/operation markers, machine
  state, five-why data, 4M data, QC data, changed parts, WD diary rows, and
  accessory-sheet data. Incomplete serialized QC payloads are now ignored
  rather than aborting the entire workbook, matching the legacy per-record
  error boundary.
- Form10 is the legacy `"BM Maintenance Evidence"` TE evidence workbook
  screen and maps to `TeEvidenceMaking`. Its read-only queries preserve the
  `ViewTEData` assignment/header lookup, the six evidence category TOP limits
  (`6, 6, 6, 20, 6, 6`), four Teleko images, one image for each quality
  condition, distinct category descriptions, and the five-row Teleko QC
  table. The checked-in `CWC.xlsx` template is under `Templates/BmReport`,
  and configuration now points the converted service there.
- Form10's legacy GemBox path embedded network-share images and saved a PDF.
  The converted path produces an `.xlsx` download and skips unavailable local
  images; live SQL, network-share image, PDF, and visual workbook comparison
  remain pending.
- Form11 is the legacy machine-summary/MCD list screen. Its active
  `NewLoadColor` path counts every qualifying row with active MCD and no action
  card, while `RemoveDuplicates` applies only to the displayed machine lists.
  The converted repository now preserves raw row counts and deduplicates
  machine numbers only in the returned lists. Its process/location mappings,
  status/category filters, action-card tables, MCD matching, date range, and
  Monitoring navigation remain aligned with the legacy flow. Its browser
  refresh now follows the shared Monitoring timer-zero signal instead of an
  independent 60-second interval, matching `tmrRefresh_Tick`.
- The converted report downloads `.xlsx` through the web response. The
  legacy GemBox path produced a local/server `.pdf` and inserted image assets
  from a network share; PDF conversion, image-level comparison, and live
  workbook validation remain pending because SQL Server and the legacy share
  are unavailable here.

- Local SQL Server is unreachable in this environment. SQL execution, live
  query results, null behavior, duplicate-row behavior, and generated report
  output therefore remain unverified. `schema.sql` confirms the referenced
  tables and Form3 views exist, but it is not a data fixture.
- Form1's legacy WinForms images and interactive layout are represented by web
  controls/CSS rather than copied designer controls.
- Form1's repeat-color implementation now follows the legacy content/priority
  depth, BM case depth, machine/tester identity, seven-day date marking, and
  TE `QUALITY BM` rules. Representative fixture comparison against live data
  is still recommended because no test project or live SQL connection is
  available.
- Form1's exact labels/list boxes and live Form11/Form12 totals and color paths
  still require fixture comparison. Form11's verified `PL`, `SING`, and `ATC`
  location slot mappings now match `Legacy/Form11.vb.txt`; Form8 keeps its
  separate reduced mapping while Form12 uses its explicit expanded profile.
- Form2's legacy location array contains a duplicate `LSI3-1F` entry; the web
  board intentionally uses the nine distinct designer locations. This remains
  an unresolved compatibility decision.
- Form4 currently produces an `.xlsx` download through ClosedXML. The legacy
  GemBox workflow saved a PDF-oriented output; PDF conversion and visual
  workbook comparison remain pending.
- BM Report still needs live SQL validation and visual workbook QA.
- Form3's referenced process views are present in `schema.sql`, but fields
  derived from legacy arrays, helper lookups, or runtime calculations still
  need live fixture comparison. No ATC report view is present, matching the
  legacy empty `ATCData` procedure.
- Action Card table schemas contain the expected fields, but only `Seqno` is
  the primary key. There is no schema-level uniqueness constraint on
  `(BMID, MachineNo)`, so duplicate records could cause the current update
  predicate to update more than one row. Live data validation is required.
- Action Card SQL and refresh behavior still need live SQL validation because
  the local SQL Server is unreachable.

## Recent UI/CSS conversion context

- JavaScript presentation logic was moved into CSS classes across the page
  scripts. Visibility changes now use the shared `.is-hidden` utility in
  `Assets/css/site.css`; page-specific styles remain in their corresponding
  files under `Assets/css`. Runtime progress-bar width updates in
  `Assets/js/AlertBoard.js` and `Assets/js/McdMonitoring.js` remain in
  JavaScript because their values are data-driven.
- Razor pages were scanned for inline presentation styling. Static `style`
  attributes were removed from the Action Card page and replaced with classes
  in `Assets/css/ActionCard.css`. TE evidence now uses the shared document
  workflow styles in `Workflows.css` and status classes in `site.css`.
- Monitoring loads the OCAP and Raw Data stylesheets for their workflow
  partials. All Razor pages are expected to keep styling in `Assets/css` and
  should not add inline `style` attributes or embedded `<style>` blocks for
  static presentation.
- After these CSS extraction changes, the baseline validation command passed
  with zero warnings and zero errors:
  `dotnet build MachineKarteWebCore.csproj --nologo -v minimal`.
- The earlier Action Card-only modal has been replaced by the shared workflow
  dialog described above. All child forms now use scoped partials and modal
  initialization; there is no separate standalone Action Card interface.

## File cleanup (2026-11-01)

- Removed eight unreferenced standalone stylesheets: `AlertBoard.css`,
  `BmAnalytics.css`, `BmReport.css`, `EvidenceMaking.css`, `MachineSummary.css`,
  `McdMonitoring.css`, `PmReport.css`, and `TeEvidenceMaking.css`. Monitoring's
  six active application stylesheets remain under `Assets/css`.
- Every script under `Assets/js` is loaded by Monitoring and participates in
  the current workflows. Keep the eleven child `.cshtml`/page-model pairs:
  their small route views still enable redirects, partials, AJAX, and downloads.
- Removed the redundant `Pages/Shared/_ViewStart.cshtml`; Monitoring explicitly
  selects its layout and the child views explicitly disable layouts. Keep
  `_ViewImports.cshtml`, `_Layout.cshtml`, and all workflow partials.
- Removed unused default `wwwroot/css/site.css`, `wwwroot/js/site.js`, jQuery,
  validation libraries, Bootstrap JavaScript, and unused Bootstrap CSS variants.
  The layout still requires `bootstrap.min.css`; its source map, Bootstrap
  license, and `wwwroot/favicon.ico` remain.
- Removed the generated `tree.txt` directory snapshot. All workbook templates,
  the report cell mapping, `schema.sql`, validation scripts, and the entire
  `Legacy/` folder are retained.
- `scripts/Test-Workflows.ps1` also checks that every local stylesheet and
  script referenced by the rendered Monitoring document returns successfully.
- Cleanup validation passed the baseline build with zero warnings/errors,
  all 20 local asset requests, all eleven workflow checks, and desktop/mobile
  layout checks. SHA-256 comparisons confirmed all 37 Legacy files, all 50
  workbook templates, and all retained application assets/views were unchanged.

## Validation

- The project has repeatedly passed:
  `dotnet build MachineKarteWebCore.csproj --nologo -v minimal`
- Documentation and source patches should also pass:
  `git diff --check`
- `scripts/Test-Workflows.ps1` checks all eleven child redirects/partials and
  exercises the real UI in an installed Chromium browser with its debugging
  port enabled. `scripts/WorkflowBrowserChecks.js` injects AJAX fixtures only
  into that test browser; production handlers and data are unchanged. These
  checks cover modal initialization, parent drilldown, Form11/Form12 routing,
  saving/refresh, timer cleanup, error states, duplicate IDs, and desktop/mobile
  overflow. They do not validate SQL results or generated workbook contents.
- The Monitoring-only conversion passed the baseline build with zero warnings
  and zero errors, all eleven redirect/partial checks, real OCAP parameter
  validation and Action Card antiforgery rejection, fixture-driven dialog and
  parent-selection/save checks, native focus trapping/Escape/focus restoration,
  and desktop (1440px) / mobile (390px) layout checks. The captured layouts were
  visually reviewed; SQL and workbook validation remain subject to the existing
  environment limitations above.
- No test project currently exists; live SQL and fixture-based behavior tests
  remain future work.
