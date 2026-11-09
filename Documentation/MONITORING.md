# Monitoring filters and frozen panes

Machine summaries (`.mc-col-header`) stay frozen at the **bottom** of the board.
BM cards share one vertical scroller above them. Summary height is excluded from
vertical scroll content, and both rows move together horizontally.

## Heading and optional celebration

The heading's refresh clock is hidden. Its `lblTimer` counter remains in the DOM,
and the existing 600-second timer, automatic search, morning date reset, and
shared workflow refresh continue to run.

The E-Karte logo is a static image by default. To temporarily unlock its cat
celebration, enter this command in the browser developer console:

```javascript
enableCatParty()
```

For 60 seconds the logo supports clicks, Enter, and Space. The celebration can
be dismissed with Escape or a backdrop click. At expiry the overlay is removed,
the background blur is cleared, and the console logs
`Cat celebration expired after 60 seconds. Logo disabled.` once. The logo becomes
static again. Repeating the command starts a new 60-second
window. Reloading the page leaves the feature locked; no setting is persisted.

Use `Test-Workflows.ps1 -HeadingOnly` for the focused browser check and desktop /
mobile captures. It verifies the hidden refresh logic and uses Chromium's
accelerated clock to exercise the native 60-second timer, overlay removal, and
expiry log without waiting a minute. It does not run database or unrelated
workflow checks.

## Date filters

The initial and Clear Filters range is 30 days ago through tomorrow, matching
`Form1_Load`. The required To input has no maximum: past dates, today, tomorrow,
and later dates are valid. Razor renders tomorrow's default; JavaScript uses the
browser's local calendar when initializing and resetting. Both inputs retain
the opening clock time in `data-time`, just as the desktop DateTimePickers do
despite displaying only MM/dd/yyyy. Selecting another date retains that time.
Focus and search do not introduce a date cap.

`doSearch()` calls the To input's `reportValidity()` before changing search state
or issuing requests. Empty values are rejected; typed, URL-supplied, and
programmatically assigned future dates remain valid. Forms 2/11/12 use the same
Monitoring dates and times when opened and on refresh. Their queries are not
restricted to Monitoring's process/location/machine selections.

Header counts, card reads, and summary queries use the original inclusive
endpoints: `TimeOfRequest >= @Range1 AND TimeOfRequest <= @Range2`, equivalent
to the desktop's `BETWEEN @Range1 AND @Range2`. The browser serializes the picker
values using the original `MM/dd/yyyy hh:mm:ss` convention, without AM/PM.
Consequently 21:06:37 becomes 09:06:37, and midnight becomes 12:00:00. Seconds
must survive every summary request. SQL respects those received endpoints
without adding a day. Direct API date-only parameters still parse as midnight.

The legacy morning refresh calls `SetDate` only between 08:00 and 08:58 when To
is not already today. It sets both picker times to midnight and To to today;
the original 12-hour SQL format then sends noon. Subsequent refreshes with To
already today retain their current picker values.

| Active legacy path | Web mapping |
| --- | --- |
| `Form1_Load`: AddDays(-30)/AddDays(1) on picker values | `resetDates` retains the opening local time |
| `Form1.btnSearch_Click_1` and `LoadMCName1` date parameters | `monitoringDateRange` and `buildFilter` retain hh:mm:ss |
| Form2/Form11/Form12 `LoadMCName`: `Form1.dt1`/`Form1.dt2` | `Workflows.open` shares Monitoring's range; refresh reads it again |
| `Form1.Timer1_Tick` / `SetDate` | `resetMorningDates` applies the same clock/date guard before summary refresh |

## Count parity

Form1's Important Device counters and Form12's Action With MCD cells count
qualifying BM rows, with unique machine lists used only for drilldowns.
Form2 counts qualifying repeat-group heads. Keep these rules separate.
Midnight-only From values can include extra early records that the desktop's
retained time excludes. Independent dialog ranges can also count different
records. Preserve the complete parameter flow before comparing row/group counts.

Form11's Need Action With MCD query retains its BMCase/BMTEUnit join. Its
`TotalLabel` formula for LSI3-3F adds ATC and SING twice; the web preserves this
legacy footer behavior. Process totals and the grand total count each cell once.
`Test-MachineSummary.ps1` checks the repeated footer terms with an isolated
ATC/SING fixture; it does not reproduce the user's live totals.

## Staircase order

The legacy [Form1.LoadMCName](../Legacy/Form1.vb.txt) orders machines by
`MACHINENAME DESC` (BM count), then places each column at `width * index`.
Non-TE ties use machine number ascending; TE uses machine type ascending.
[uc_Test.LoadMCName](../Legacy/uc_Test.vb.txt) docks its 120px cards at the bottom.
Columns follow descending header counts and share a bottom baseline.

The repository retains these count-first orders, with additional deterministic
tie breakers. `MonitoringService.SearchDashboardAsync()` preserves the query's
complete order before slicing pages. Both `machineCards` and the requested
page's `machineSummaries` follow that same order. Do not re-sort by displayed
card count: Form1 ranks header groups, while uc_Test loads the history using
different rules.

| Original path | Web behavior |
| --- | --- |
| Form1 non-TE header query | Join Process, MCType and BMCase; group by machine, raw action line and type; order by count descending, machine ascending |
| uc_Test PM column | Join BMCase; select all eligible rows for the machine/location in the process table, including older, blank or unregistered types |
| uc_Test CC/EEE/VIS column | Join Why4 to BMWhy, retain turnover/status rules; include every type for the machine/location |
| uc_Test TE column | Retain Process, TesterNo, MCType and Location filters, with the BMTEUnit join |
| Card order and repeat colors | Newest request first, then case; non-TE type changes do not split a machine's repeat/solved history |
| Default action-line metadata | Group NULL and explicit 4 separately, then deduplicate their projected default values as the original EXCEPT does |

The former grouped web read split every mode by machine type and applied a type
filter to individual non-TE columns. A machine with eight current-type BMs and
one older-type BM therefore displayed eight instead of nine. Sorting by those
incomplete stacks then changed column positions. The header query also omitted
the original BMCase join, allowing orphan case IDs to inflate its ranking.
These restrictions and the displayed-count sort have been removed. Non-TE card
reads use the selected process table, as the original does, without an extra
Process-field restriction; content lookup still uses the selected process.

Header counts can legitimately differ from stack sizes: non-TE headers group by
type, but their histories include all types; CC/EEE/VIS headers join BMCase while
their cards join BMWhy. Preserve this distinction instead of forcing every
visible stack into descending order.

`renderCurrentPage()` takes ten consecutive columns from this ordered snapshot;
`renderMachineCards()` appends headers and card columns in that order. Do not
sort headers/cards independently or reorder just the current page. CSS bottom
alignment controls the shared baseline, not machine ranking.

## BM card colors and blinking

The active reference is `uc_Test`, not `uc_BMCase` or Form2's summary coloring.
The original sequence is: query the column, create labels/action buttons,
`LoadProcessCapa(ActionLine, Rowcount)`, `CheckIfSolve`, `LoadColor` or
`LoadColor_TE`, then enable the 1000 ms timer. Each step affects a different
part of the card. The web retains that separation.

| Original source | Rule and web mapping |
| --- | --- |
| `Form1.LoadMCName` → `uc_Test.LoadMain` | Pass the aggregate `MACHINENAME` as `Rowcount`; `MachineCardResult.BMCount` carries this to rendering. It is not the number of loaded cards. |
| `uc_Test.LoadMCName`, header creation | PM/CC/EEE/VIS show accredited content when present, otherwise original content, in uppercase. Status 15 shows `OCAP BM RESOLVED`, initially white. TE shows Priority. `DisplayContent` and `BMStatusId` retain these rules. |
| Action-button initialization | Exact stored statuses map to DarkGray, DarkOrange, Red, LightGreen, Green, Violet or Aqua; other values default to Red. All Action text is white. Status matching does not trim or change case. |
| `CheckIfSolve` → `GetDuplicateRowsByColumn` → `AreOtherRowsSame` | Use the newest loaded BM as the reference and the newest date in each repeated case. An existing action becomes green when that case is at least seven whole days old relative to the reference, or the action row is at least seven whole days older than its case head. Singletons do not become solved. |
| `LoadColor` | Scan the existing date/case order, comparing consecutive effective content IDs, then cases, up to five following rows. Already-colored members are skipped. Only an eligible head increments Need Action and flashes its content/date backgrounds. |
| `LoadColor_TE` | Use Priority and case, up to four following rows. Only `QUALITY BM` produces red repeat text and a Need Action head. Equipment BMs follow the original MCD fallback branches. |
| `CheckifSevendays` | Compare `TimeSpan.Days` between the head and candidate: 0 through 7 inclusive, retaining sub-day truncation. Preserve the terminal multi-row iteration's absolute `dt.Rows(count)` lookup and relative `index + count` write; simplifying it to a normal relative scan changes some member colors. |
| Non-TE three-content branch | With at least five following rows, three matching content rows and a different next case, the original has no MCD fallback. Its MCD stays steady; a general “no action means blink” rule is incorrect. |
| `LoadColor` / `LoadColor_TE`, final assignment | Blue/Red/Black apply to both case and content text. Content text stays steady. Blue/Red case text blinks; Black and uncolored case text stay steady. Repeat text can replace the resolved header's initial white. |
| `Timer1_Tick_1` | Every second, toggle case Blue ↔ Desktop or Red ↔ InactiveCaptionText, alert content/date Red ↔ White, and eligible MCD backgrounds WhiteSmoke ↔ DarkRed. MCD text stays blue. Action badges, capacity case backgrounds and ordinary content text do not blink. |

Non-TE color groups include every type for the machine/location. TE groups keep
types separate. Shared reads normalize case and trailing varchar spaces for
machine/type/location keys to match the original SQL comparisons, while retaining
the original field values in returned cards. A padded or differently cased
machine number must not lose cards or split a repeat group.

| Process | Blue repeat content ID | Red repeat content ID |
| --- | --- | --- |
| DC | 48 | 49 |
| DB | 18 | 19 |
| WB | 45 | 46 |
| MP | 24 | 25 |
| PL | 42 | 43 |
| FL / SING / ATC | 1 | 2 |
| FT | 31 | 33 |
| TP | 9 | 30 |
| EDS | 13 | 16 |
| MEMS | 72 | 74 |

Other non-TE content IDs use Black when they form a repeat. TE uses Priority,
not these content IDs.

| Exact stored MCStatus | Action background |
| --- | --- |
| `RUNNING (ONGOING INVESTIGATION/MONITORING)` | DarkGray |
| `RUNNING (UNDER 100% INSPECTION)` | DarkOrange |
| `STOP (ONGOING INVESTIGATION)` | Red |
| `RUNNING (LOT EVALUATION)` | LightGreen |
| `NO RE-OCCURRENCE AFTER 7 DAYS` | Green |
| `STOP (WAITING FOR INSPECTION RESULTS)` | Violet |
| `STOP (WAITING FOR QC INSTRUCTION)` | Aqua |
| Other / blank | Red |

The seven-day solved check can override these backgrounds with Green. It does
not change the stored MCStatus or make the badge blink.

### Capacity colors

Capacity coloring uses the header count, before repeat-head alert backgrounds.
All cards initially have Salmon content/date backgrounds and MistyRose case
backgrounds. When `ActionLine < BMCount`, the boundary index is
`BMCount - 1 - ActionLine`. From that boundary toward index zero, the first two
cards use SM colors, the next two use DM colors, and the remaining cards use DV
colors. Cards below the boundary retain their defaults. This includes the
original boundary overlap and its one-card/two-card edge branches.

| Band | Content/date | Case background |
| --- | --- | --- |
| Default | Salmon | MistyRose |
| SM | DeepSkyBlue | LightCyan |
| DM | MediumPurple | Lavender |
| DV | LimeGreen | Honeydew |

For example, with header count 8, nine loaded BMs and ActionLine 4, indices 0–1
are DM, 2–3 are SM, and 4–8 retain the default. Using nine as the capacity count
incorrectly makes index 0 DV and shifts the other bands. A Need Action head then
flashes only its content/date red and white; its case background keeps its band.
WinForms bottom docking preserves query index zero at the top of the stack and
the last, oldest row at the bottom; the web keeps that order.

The web starts a shared blink epoch when a search finishes loading, preserving
the initial colors for the first second. Cached page changes reuse that epoch.
Starting animations at document time zero can show a freshly loaded alert in
its alternate color immediately. Reduced-motion preferences retain the initial
colors without animation.

### Color verification

`Test-MonitoringColors.ps1` compiles the actual original routines and header/action
initialization against in-memory WinForms controls. It opens no desktop windows
or database connections. Its 362 cases cover all process content-color maps,
repeat depths, mixed cases/content, exact statuses, solved actions, equipment and
quality BMs, MCD branches, date boundaries, padded identities and capacity counts
that differ from history lengths. It writes temporary `cards.json` expectations.

With the isolated local app and Chromium debugging session described in the
validation guide, `Test-Workflows.ps1 -ColorsOnly` consumes those expectations.
It checks real DOM/CSS colors, content text and blink synchronization for 2,364
cards at initial, 1000 ms and 2000 ms positions, without waiting for real timer
cycles or running other workflows. These are source-parity fixtures, not a live
database comparison. No rule is specific to SP-33.

## Column sizing

Every machine uses one of ten column slots, even on a partially filled page.
One result uses one column; three results use three columns. Unused space stays
on the right. Adding or removing results does not stretch the remaining cards.

[Monitoring.css](../Assets/css/Monitoring.css) shares this track definition
between the card viewport and frozen summary row:

```css
--machine-columns: repeat(var(--machine-column-count, 1), minmax(186px, 10%));
```

Each column is 10% of the row's available content width, with a 186px minimum.
The count controls how many columns exist; it does not divide the available
space among the returned machines. Do not replace `10%` with `1fr` or use
`100% / column-count`, which would expand sparse results across the whole board.
BM cards fill their own machine column. On narrower screens, the minimum width
causes horizontal overflow only when the actual result columns need it.

## Structure and scrolling

[Monitoring.cshtml](../Pages/Monitoring.cshtml) keeps both rows outside each other:

```text
cardContainer                         horizontal scrolling for both rows
└─ machineBoard                       Grid: minmax(0, 1fr) auto
   ├─ cardScroller.machine-scroll-area shared vertical scrolling; row 1
   │  └─ machine-col × result count
   │     └─ bm-card-list
   │        └─ bm-card
   └─ machineHeaderRow                 frozen bottom summaries; row 2
      └─ mc-col-header × result count
```

Both rows use `grid-template-columns: var(--machine-columns)` and matching
`scrollbar-gutter: stable`. The summary row uses `overflow: hidden` to reserve
the matching gutter without showing another scrollbar. Its `min-width:
min-content` establishes enough board width for the actual columns and gutter.
The board uses `width: min-content; min-width: 100%` so sparse results leave blank
space while wider results can scroll horizontally. Individual machine columns
and card lists do not create scrollbars.

Summaries retain their 104px height. Cards retain their 120px height and
bottom-aligned staircase, with the newest loaded position shown at the top.
The existing responsive breakpoints continue to size the outer container.

## Rendering

[Monitoring.js](../Assets/js/monitoring/Monitoring.js) renders up to ten machines
from the cached search response. `renderMachineCards()` builds separate fragments
for summaries and card columns in matching order, sets `--machine-column-count`,
and replaces each row's children. CSS calculates the widths without JavaScript
measurement or scroll synchronization.

`renderMachineCard(data, filter, container, header)` receives the corresponding
summary explicitly to update its location and Need Action count. The summary is
no longer a descendant of `.machine-col`. `clearCards()` clears both persistent
rows, hides the board, and resets scrolling. Card commands, cached pagination,
batched construction, fixed dimensions, and blink synchronization keep their
existing paths. This refactor retains the current `content-visibility` setting
and intrinsic-size/visibility-event hooks.

Razor markup is compiled. Rebuild/restart after changing the markup or page
model so the rendered defaults and element IDs load with the CSS and JavaScript.

## Focused verification

Check one, three, and ten results at narrow and wide board widths. Columns must
measure `max(186px, row-content-width / 10)`, all cards and summaries must align,
and unused slots must remain blank. The summary row must stay below the card
viewport during vertical scrolling. A long 52-card history has a scroll height
of `52 * 120 = 6240px`, excluding the 104px summary.

Use `Test-MonitoringStaircase.ps1` (or `Test-MonitoringParity.ps1 -StaircaseOnly`)
for the focused SQL check. It compares actual repository/service results with
the preserved Form1 and uc_Test queries in a disposable LocalDB catalog. Its
synthetic SP-33 has nine BMs across two types and remains at page 1, column 9.
It covers PM/CC/EEE/VIS, both pages, specific-machine selection, orphan case
IDs, repeat/solved-action colors, date/location boundaries, duplicate default action-line
metadata and TE type isolation. It never reads application settings or
application databases; the fixture does not establish live SP-33 data or totals.

For date-only UI changes, `Test-Workflows.ps1 -DatesOnly` runs
`window.runMonitoringDateChecks()` in the existing
[browser fixtures](../scripts/WorkflowBrowserChecks.js). It checks tomorrow's
default, past/future dates without a maximum, empty-value rejection, retained
times at midnight/noon/afternoon, URL/drilldown timestamps, shared summary
dates/seconds, refresh, and the morning-reset guard. AJAX results are fixtures;
these checks validate requests and behavior, not live database totals.

The same browser fixtures include
sparse-column, single-column, full-page, and frozen-summary assertions. When
broader Monitoring coverage is needed, use `Test-Workflows.ps1 -MonitoringOnly`.
For a width-only correction, use a focused layout check; do not run unrelated
database, report, or complete workflow suites.
