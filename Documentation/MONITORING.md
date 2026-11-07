# Monitoring filters and frozen panes

Machine summaries (`.mc-col-header`) stay frozen at the **bottom** of the board.
BM cards share one vertical scroller above them. Summary height is excluded from
vertical scroll content, and both rows move together horizontally.

## Date filters

The initial and Clear Filters range is 30 days ago through today. The To input
is required and uses the native date control's `max` constraint to disable
future dates. Razor renders today's default and maximum; JavaScript uses the
browser's local calendar date when initializing, resetting, focusing the input,
and starting a search. Refreshing the maximum on focus/search handles a page
left open across midnight.

`doSearch()` calls the To input's `reportValidity()` before changing search state
or issuing requests. Typed, URL-supplied, or programmatically assigned future
dates are rejected as well as empty values. Past dates and today remain valid.
Summary dialogs retain their own date defaults.

Header counts and card reads use the same inclusive calendar-day range:
`TimeOfRequest >= @Range1 AND TimeOfRequest < DATEADD(DAY, 1, @Range2)`.
This includes records throughout the To date and excludes the next day's
midnight. Ending the card query at the To date's midnight would hide that day's
cards while still counting them for column ranking.

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
stable ties, global ordering before pagination, and full-To-day boundaries.
`-Sql` optionally adds synthetic LocalDB date/tie-order checks. Neither mode
reads application settings or application databases.

For date-only changes, `window.runMonitoringDateChecks()` in the existing
[browser fixtures](../scripts/WorkflowBrowserChecks.js) checks today's default
and maximum, past dates, a stale maximum, rejected future/empty searches, and
date resets. It does not request search data.

The same browser fixtures include
sparse-column, single-column, full-page, and frozen-summary assertions. When
broader Monitoring coverage is needed, use `Test-Workflows.ps1 -MonitoringOnly`.
For a width-only correction, use a focused layout check; do not run unrelated
database, report, or complete workflow suites.
