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

Keep both image directories distinct, including PM `Inspection/` images. Static
marks resolve locally. Record-specific photos resolve under the configured
`EvidenceMaking:ImagesRoot` or supplied image directory; absent photos use that
form's original `noimage.png`. The copied folders contain sample photos, not a
verified complete production evidence archive. Both templates and images are
included in build/publish output.

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
Generation and download remain separate user actions. PM R uses the same
`.btn-download` styling as E. Download extensions/MIME types match the chosen
format; PM evidence PDF previews use an inline response.

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

## Optional legacy uploaded-photo roots (pending production validation)

The web evidence services now accept optional configuration arrays
`EvidenceMaking:LegacyPmUploadRoots` and `EvidenceMaking:LegacyTeUploadRoots`.
Each entry is an existing directory containing uploaded photographs, for example
an administrator-mounted share or a locally mirrored `Upload` directory.
Configure these in deployment environment configuration, not in committed
credentials. PM and TE roots are kept separate. The resolver uses only these
trusted roots, the existing evidence directory, and static image assets;
missing photos still use the legacy placeholder. Full database-stored UNC paths
are reduced to their relative suffix below `Upload` rather than opened directly.

No network share has been accessed or verified by this change. Verify that the
application's deployment identity has read-only access and compare representative
Form4/Form10 output with the desktop originals. Missing/incorrect cell mappings,
scattered zeroes, and the incomplete CWC workbook remain unresolved until
representative source data and output comparisons are available.

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
