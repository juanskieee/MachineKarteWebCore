# Legacy report generation

Status: assets supplied and report mappings restored on 2026-11-02. Validation
uses local fixtures only; production data and desktop output parity still need
representative owner checks. No production SQL, stored procedure, view, table,
legacy source, application setting or original workbook was changed.

## Assets and original paths

All 66 static references (41 images and 25 templates) in
[LEGACY_REPORT_ASSETS.csv](LEGACY_REPORT_ASSETS.csv) exist locally. The CSV now
maps the original server paths to the actual local files supplied by the user.
The server itself was not accessed.

| Original folder | Local folder |
|---|---|
| `\\172.26.220.161\eMaintenanceSystem\images\` | `Assets/images/eMaintenanceSystems_images/` |
| `\\172.26.220.161\TEeMaintenanceSystem\images\` | `Assets/images/TEeMaintenanceSystems_images/` |
| `\\172.26.220.161\eMaintenanceSystem\Templates\` | `Templates/PmReport/` and `Templates/EvidenceMaking/` |
| `\\172.26.220.161\TEeMaintenanceSystem\Templates\` | `Templates/BmReport/` |

Keep both image directories distinct, including PM `Inspection/` images. The
copied `Assets/images/` trees supply original static template marks and each
form's `noimage.png` fallback, **not** record-specific uploaded photographs.
For dynamic evidence, the web app now uses the original server path constructed
from `NetworkShare:Server` plus the SQL `BMEvidence_ImgFile` value, following
Form4 and Form10. Static template assets and workbook templates remain included
in build/publish output.

## Why the matrix alone was insufficient

`Form3CellMappingMatrix.txt` records worksheet writes without reliably preserving
the enclosing If/Else, array/reader loops, calculations, style changes or sheet
selection. The old interpreter marked multiple alternatives, wrote formula
results incorrectly, and could copy the current repairer into absent history.
The PL section also contains an old commented block before the active mapping.

`Reporting/LegacyReportMappings.vb` now compiles the active original Form3,
Form4, Form9 and Form10 mapping bodies. `scripts/Update-LegacyReportMappings.ps1`
extracts those bodies from the preserved `Legacy/*.vb.txt`, retaining statement
order, conditions, typed values, VB date/number semantics, arrays, pictures,
styles, margins and paper settings. SQL, impersonation, desktop UI, shell/file
launches and original server output paths are excluded. `-Check` verifies that
the compiled bodies still match the generator's extraction. The matrix remains
an unchanged reference, rather than executable report logic.

## Template and source map

Form3 reads the original active worksheet in each PM template. Evidence templates
are `Templates/EvidenceMaking/EvidenceMaking_{PROCESS}.xlsx` for the same 11
processes. Form4 has a separate SING layout and common non-SING layout.

| Process | PM template in `Templates/PmReport/` | Original Form3 writer | PM read view |
|---|---|---|---|
| MEMS | `MEMS FT-TPTemplate.xlsx` | `MEMSData`, line 176 | `ViewMEMSData` |
| WB | `WBTemplate.xlsx` | `WBData`, line 788 | `ViewWBData_Ver4` |
| DB | `DBTemplate.xlsx` | `DBData`, line 1603 | `ViewDBData_Ver4` |
| DC | `DCTemplate2.xlsx` | `DCData`, line 2350 | `ViewDCData_Ver4` |
| MP | `MPTemplate.xlsx` | `MPData`, line 3058 | `ViewMPData_Ver4` |
| PL | `PLTemplate.xlsx` | `PLData`, line 3797 | `ViewPLData_Ver2` |
| FT | `FTTemplate.xlsx` | `FTData`, line 5201 | `ViewFTData` |
| SING | `SINGTemplate.xlsx` | `SINGData`, line 6193 | `ViewSINGData` |
| FL | `FLTemplate2.xlsx` | `FLData`, line 7005 | `ViewFLData_V2` |
| TP | `TPTemplate.xlsx` | `TPData`, line 8135 | `ViewTPData` |
| EDS | `EDSTemplate.xlsx` | `EDSData`, line 9080 | `ViewEDSData` |

ATC stays unsupported: the original `ATCData` writer is empty. PM history keeps
the original TOP 1/TOP 2 outcome: zero rows leaves the template slots alone;
one row appears in both slots; two rows fill newest then next-newest. EDS has no
history writer. Required SQL values keep DateTime/number/DBNull types and missing
required columns produce a clear error. Lookup helpers are resolved in one
parameterized, allowlisted batch, including BMWhy and other-unit arrays.

| Form | Local template | Preserved details |
|---|---|---|
| Form9, TE EDS | `Templates/BmReport/EDS-CSPTemplate.xlsx` | EDS branch, active main sheet, work diary at index 1, accessories at index 3, VR/lookup values, QC/approval fields |
| Form9, other TE processes | `Templates/BmReport/TETemplate.xlsx` | Non-EDS branch, same secondary-sheet selection, original calculations and array loops |
| Form4, PM E | Process evidence template above | Original slot count/anchors/inch sizes, descriptions, missing-image marks, TSC Yes/No/N/A, conditional route/version and SING Andon/layout |
| Form10, TE E | `Templates/BmReport/CWC.xlsx` | Before/after categories, connector-code placement, descriptions, five-row QC table, repair timestamps, Teleko at H70/J70/M70/O70 with 1.54 x 1.4 inch images |

Original branch quirks remain explicit in the extracted code, including repeated
`PMCategoryID = "2"` branches. This restoration does not guess replacements for
legacy business rules. Form10 iterates all original distinct misc-data rows;
it does not reduce them to TOP 1 and lose an earlier nonempty QC table.

## Excel/PDF output and R styling

All four dialogs have an explicit Excel/PDF selector. Excel remains the default.
Generation and download remain separate user actions. All four document
workflows (PM/TE Record and PM/TE Evidence) now use the PM Evidence
`status-area`, `preview-panel`, `preview-toolbar`, and `preview-frame`
layout. Successful generation shows the green
`Evidence generated: {filename}.xlsx/.pdf` message and a toolbar with the
filename on the left and a styled Download link on the right. PDFs display in
an iframe; Excel intentionally shows only the toolbar. PM Record and TE
Evidence support `inline=true` for PDF-only preview responses, while ordinary
downloads retain their attachment disposition. The TE Record renders its
in-memory PDF blob directly; its object URL is revoked when replaced or closed.
No modal automatically generates or downloads a report.

`LegacyReportExport` uses GemBox.Spreadsheet 2026.10.101, the original form's
library family, for actual XLSX and PDF serialization. No Office installation,
external converter, synthetic template or renamed XLSX-as-PDF is involved.
The original `FREE-LIMITED-KEY` is retained. GemBox documents a 150-row/sheet,
five-sheet free-mode limit; the supplied templates and local fixtures export
within it. See the [official free-version limits](https://www.gemboxsoftware.com/spreadsheet/free-version)
and [ASP.NET Core export example](https://www.gemboxsoftware.com/spreadsheet/examples/asp-net-core-create-excel/5601).

## Supplied CWC limitation

`CWC.xlsx` has one worksheet (`FORMAT`). Original Form10 references `ws_2` for
CN5-CN8 test-board connectors and `ws_3` for quality-condition descriptions and
images, but the checked-in desktop routine never initializes those worksheets.
The web implementation uses them only if present and rejects records requiring
missing sheets. It does not fabricate sheets, overwrite the existing template,
or silently omit those records' evidence. Ordinary main-sheet CWC output works.

The TE templates also have small recorded used-range dimensions compared with
legacy cell writes. Their original active-sheet/index selection is retained;
metadata dimensions alone are not used to remap cells.

## Record-specific evidence images: original server path parity

**Source of truth:** `Legacy/Form4.vb.txt` and `Legacy/Form10.vb.txt`.

| Original form | Base folder from `My.Settings.PathServer` | SQL evidence path | Original image handling |
|---|---|---|---|
| Form4, PM Evidence | `\\\\{PathServer}\\eMaintenanceSystem` | `gridEvidenceMaking_Ver3.BMEvidence_ImgFile` | Concatenate the base and stored path; use PM `noimage.png` when absent |
| Form10, TE Evidence | `\\\\{PathServer}\\TEeMaintenanceSystem` | `TEBMEvidenceMaking.BMEvidence_ImgFile` / `TelekoEvidence` | Remove `~` from stored path before concatenation; use TE `noimage.png` when absent |

The web host uses the existing `NetworkShare:Server` setting in place of the
legacy desktop `PathServer`; no new upload directories, configurable upload
roots or duplicated photo archives are used. A previous
`EvidenceMaking:ImagesRoot` setting may remain in existing deployment config,
but the evidence services no longer read it. Paths are anchored to their
respective original share and reject attempts to traverse outside that share.
The Form10 tilde conversion does **not** apply to Form4. Static icons remain
local while dynamic images are read directly from the existing server.

Deploying the web app on the legacy file server may make these pictures
accessible, provided the ASP.NET Core process identity has **read access** to
both shares and the database contains correct relative paths. An IIS application
pool or service identity is not necessarily the same as the interactive desktop
identity; a separate authenticated share session may be required. On a
non-Windows web host, native Windows UNC paths will not automatically work and
the target shares must be mounted or served through an approved infrastructure
mechanism. Do not commit share credentials or impersonate a user through new
custom upload code.

### Deployment verification (read-only)

1. Under the **actual web-host process identity**, verify file read access to
   both original share folders, not only to the application directory.
2. Read representative stored `BMEvidence_ImgFile` records for PM and TE,
   keep the original leading `\\` / `~/` form, and confirm the constructed
   file exists under the matching legacy system folder.
3. Generate the same PM and TE evidence records in the original desktop forms
   and the deployed web app. Compare expected photo counts, positions and
   original no-image fallbacks. Distinguish an inaccessible share from a
   genuinely absent photograph.

The offline report test creates disposable share-shaped test directories and
checks Form4/Form10 resolution, Form10-only `~` removal, missing-file fallbacks
and traversal protections without contacting production. **No live network
share has been accessed or validated**. Missing photos may still appear until
deployment permissions and actual SQL paths are verified. The unrelated
`CWC.xlsx` missing-sheet limitation remains.

## Descriptive lookup zeroes (web output correction)

The desktop `getProcCodes` helper returns literal `"0"` when a descriptive
code cannot be resolved. The original mapping bodies also call this helper
for several cells, so the web conversion could print scattered zeroes for
missing PM/TE maintenance category, case, unit, work-content, point and cause
descriptions. This is not a numeric measurement of zero.

The web report writer now renders missing descriptive lookups as blank strings.
The TE BM report repository also no longer injects `"0"` for absent or null
lookups. A lookup that genuinely resolves to the string `"0"` is preserved;
numeric measures, legitimate formula zeroes, worksheet styles, original cell
addresses, and workbook templates are unchanged. The compiled legacy mapping
bodies remain generated verbatim from the original Form3/Form4/Form9/Form10
sources. This intentionally overrides only the desktop's missing-description
sentinel, following the explicit requirement to remove scattered zeroes.

Regression assertions were added for PM WB cell H65, TE report cell K91, and
missing/null lookup values returned by the isolated data retrieval runner.
These tests have not been executed in this session because a Windows .NET 8
and PowerShell environment is unavailable here. Do not mark production output
parity as verified until representative input/output comparisons pass.

## Verification

- `scripts/Update-LegacyReportMappings.ps1 -Check`: source-extraction parity.
- `scripts/Test-ReportGeneration.ps1`: offline real template/image fixtures,
  genuine XLSX/PDF signatures, workbook styles, every PM process, both Form9
  branches, every Form4 process, Form10, history 0/1/2, QC/defaults, durations,
  conditional styling, diary mapping and explicit CWC missing-sheet errors.
  Artifacts are generated in a unique local TEMP folder. Preview PNGs have a
  transparent background and should be viewed on white.
- `scripts/Test-DataRetrieval.ps1`: uniquely named disposable LocalDB database;
  parameterized PM lookup batches, raw SQL types, TE CN and distinct misc rows,
  plus earlier cancellation/filter/batch regressions. It never loads appsettings.
- `scripts/Test-WorkflowEndpoints.ps1` and `scripts/Test-Workflows.ps1`: isolated
  app with an unreachable loopback SQL override, real download streams/MIME/
  filenames/inline preview, and mocked browser generation for both formats.

Live production record completeness, the full evidence-photo archive, installed
server fonts and comparison against actual desktop-generated files remain
unverified. Never host test runs using the application's live connection string.
