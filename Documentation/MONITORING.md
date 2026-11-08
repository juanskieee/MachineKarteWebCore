# Monitoring filters and frozen panes

Machine summaries (`.mc-col-header`) stay frozen at the **bottom** of the board.
BM cards share one vertical scroller above them. Summary height is excluded from
vertical scroll content, and both rows move together horizontally.

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
The intended result is the tallest stack on the left, descending toward the
right, with a shared bottom baseline.

The repository retains these count-first orders, with additional deterministic
tie breakers. `MonitoringService.SearchDashboardAsync()` stably orders the
complete machine list by each column's actual eligible card count before slicing
pages. This keeps the visible staircase descending even when mode-specific case
joins make aggregate header counts differ from displayed cards. Equal displayed
counts retain their existing SQL order. Both `machineCards` and the requested
page's `machineSummaries` follow that same order.

`renderCurrentPage()` takes ten consecutive columns from this ordered snapshot;
`renderMachineCards()` appends headers and card columns in that order. Do not
sort headers/cards independently or reorder just the current page. CSS bottom
alignment controls the shared baseline, not machine ranking.

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
bottom-aligned staircase, with the newest loaded position shown at the bottom.
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

Use `Test-MonitoringStaircase.ps1` for the focused offline ordering/date check.
It executes the production sort statement against synthetic columns, checking
stable ties, global ordering before pagination, and legacy inclusive endpoints.
`-Sql` optionally adds synthetic LocalDB date/tie-order checks. Neither mode
reads application settings or application databases.

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
