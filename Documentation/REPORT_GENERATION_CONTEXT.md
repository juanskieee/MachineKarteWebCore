# Legacy report generation

Status: report mappings audited and presentation defects corrected on 2026-11-03.
See the [mapping audit and defect ledger](REPORT_MAPPING_AUDIT.md) for template
versions, cell/image corrections, validation and remaining capacity gaps.
Validation uses offline and isolated LocalDB fixtures; production data and desktop
output parity still need representative owner checks. No production SQL, stored procedure, view, table,
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
marks resolve locally. Both static image directories and templates are included
in build/publish output. Uploaded record photos are read from the existing server
shares described below; they do not need to be copied into the published app.

## Legacy uploaded-image storage and deployment

The uploaded-photo archive is separate from the static `images` and template
folders. The following production locations were supplied by the user:

| Location | Use |
|---|---|
| `\\172.26.220.161\eMaintenanceSystem\Upload` | Form4 PM evidence photographs |
| `\\172.26.220.161\TEeMaintenanceSystem\Upload` | Form10 TE evidence, including Teleko and quality-condition photographs |
| `\\172.26.220.161\Upload\Evidence` | Shared uploaded evidence archive, available to both workflows |

`NetworkShare:Server` selects the file server (currently `172.26.220.161`).
`LegacyReportExport` constructs these roots from that setting. Form4 resolves
`\Upload\...` under `eMaintenanceSystem`; Form10 resolves it under
`TEeMaintenanceSystem` and preserves the desktop's removal of `~`. Forward
slashes are also accepted. A path beginning `\Upload\Evidence\...` first checks
the workflow's original system share, then the shared `Upload\Evidence` archive
with the same remaining subdirectories. An absolute UNC path is accepted only
inside the workflow's own Upload directory or the shared archive. PM and TE
directories remain separate, and files are never selected by basename alone.

For example, a Form10 value `~/Upload/2026/photo.jpg` maps to
`\\172.26.220.161\TEeMaintenanceSystem\Upload\2026\photo.jpg`.
An explicit value `\\172.26.220.161\Upload\Evidence\2020\photo.jpg` reads that
exact archive photo. A relative `\Upload\Evidence\2020\photo.jpg` prefers the
original system share when both copies exist. Traversal and paths to other
servers/shares are rejected. Empty, missing or invalid paths use the original
form-specific local `noimage.png`, matching legacy fallback behavior. Because
`File.Exists` can return false for denied access, a placeholder may also indicate
a permissions problem; a file that is visible but cannot be opened can fail export.
`EvidenceMaking:ImagesRoot` is an unused older setting; it does not control
uploaded-photo lookup or provide share access.

Publishing the application enables this flow only when the **web server can
reach the file server and the account running the application can read the
shares**. Publishing does not grant Windows permissions or authenticate using
the `NetworkShare:UserName` and `NetworkShare:Password` settings; those settings
are not consumed by the web implementation. Unlike the desktop forms, the web
report services do not impersonate an account for each export.

For Windows/IIS deployment:

1. Keep `NetworkShare:Server` pointed at the file server, and permit SMB traffic
   from the hosting server to it (normally TCP 445).
2. Run the IIS application pool under an account recognized by the file server
   and grant that account Read at both the SMB-share and underlying NTFS levels
   for all three upload locations, including their subdirectories. A dedicated
   domain service account can be configured in Application Pools > the app's pool
   > Advanced Settings > Process Model > Identity. With ApplicationPoolIdentity
   in a domain, remote resources normally see the hosting computer's domain
   account (`DOMAIN\WEBHOST$`), so grant that account access instead when using
   that arrangement. See Microsoft's [application pool identity guidance](https://learn.microsoft.com/en-us/iis/manage/configuring-security/application-pool-identities#accessing-the-network).
3. Keep the published local templates/static images readable, and the local
   generated-report output directory writable by the application account.
4. Verify actual photograph reads under that same application identity. Success
   in a developer's or administrator's Explorer session does not establish the
   IIS worker's access. Mapped drive letters are unnecessary; the app uses UNC
   paths. Uploaded photos are read during report generation and embedded in the
   output; they do not need a public static-file mapping.
5. Generate representative XLSX and PDF reports through the deployed app and
   compare the images with the original records and network-share files:
   Form4 evidence, Form10 TE categories, Teleko photographs, quality-condition
   photographs, and historical uploaded repair evidence. Check the photograph
   itself rather than treating a successful export containing `noimage.png` as
   proof of access.

The supplied `CWC.xlsx` still has only one worksheet: quality-condition records
and CN5-CN8 records need the original additional worksheets before those reports
can be generated. Share access does not resolve that separate template limitation.

The production shares have not been accessed or inventoried during local
validation. Offline checks use temporary directories shaped like the three
shares, verify path boundaries and duplicate-name precedence, and inspect the
generated workbook's embedded image bytes for PM, TE, Teleko, historical and
quality-condition photographs. Those checks establish the local code behavior;
image-level production parity remains pending the deployed checks above.

## Why the matrix alone was insufficient

The former `Form3CellMappingMatrix.txt` recorded worksheet writes without reliably preserving
the enclosing If/Else, array/reader loops, calculations, style changes or sheet
selection. The old interpreter marked multiple alternatives, wrote formula
results incorrectly, and could copy the current repairer into absent history.
The PL section also contains an old commented block before the active mapping.

`Services/Reporting/LegacyReportMappings.vb` now compiles the active original Form3,
Form4, Form9 and Form10 mapping bodies. `scripts/Update-LegacyReportMappings.ps1`
extracts those bodies from the preserved `Legacy/*.vb.txt`, retaining statement
order, conditions, typed values, VB date/number semantics, arrays, pictures,
styles, margins and paper settings. SQL, impersonation, desktop UI, shell/file
launches and original server output paths are excluded. `-Check` verifies that
the compiled bodies still match the generator's extraction plus the documented
presentation corrections. The preserved legacy forms are the source reference;
the former matrix is not used at runtime.

## Template and source map

PM selects the named worksheet in `PmReportRepository.Definitions`. Six compatible
original-layout PM workbooks are stored in `Templates/EvidenceMaking/`; the
similarly named versions in `PmReport/` have different layouts and are not used.
Evidence templates
are `Templates/EvidenceMaking/EvidenceMaking_{PROCESS}.xlsx` for the same 11
processes. Form4 has a separate SING layout and common non-SING layout.

| Process | PM workbook relative to `Templates/` | Original Form3 writer | PM read view |
|---|---|---|---|
| MEMS | `PmReport/MEMS FT-TPTemplate.xlsx` | `MEMSData`, line 176 | `ViewMEMSData` |
| WB | `EvidenceMaking/WBTemplate.xlsx` | `WBData`, line 788 | `ViewWBData_Ver4` |
| DB | `EvidenceMaking/DBTemplate.xlsx` | `DBData`, line 1603 | `ViewDBData_Ver4` |
| DC | `PmReport/DCTemplate2.xlsx` | `DCData`, line 2350 | `ViewDCData_Ver4` |
| MP | `EvidenceMaking/MPTemplate.xlsx` | `MPData`, line 3058 | `ViewMPData_Ver4` |
| PL | `EvidenceMaking/PLTemplate.xlsx` | `PLData`, line 3797 | `ViewPLData_Ver2` |
| FT | `EvidenceMaking/FTTemplate.xlsx` | `FTData`, line 5201 | `ViewFTData` |
| SING | `PmReport/SINGTemplate.xlsx` | `SINGData`, line 6193 | `ViewSINGData` |
| FL | `EvidenceMaking/FLTemplate2.xlsx` | `FLData`, line 7005 | `ViewFLData_V2` |
| TP | `PmReport/TPTemplate.xlsx` | `TPData`, line 8135 | `ViewTPData` |
| EDS | `PmReport/EDSTemplate.xlsx` | `EDSData`, line 9080 | `ViewEDSData` |

ATC stays unsupported: the original `ATCData` writer is empty. PM history keeps
the original TOP 1/TOP 2 outcome: zero rows leaves the template slots alone;
one row appears in both slots; two rows fill newest then next-newest. EDS has no
history writer. Required SQL values keep DateTime/number/DBNull types and missing
required columns produce a clear error. Lookup helpers are resolved in one
parameterized, allowlisted batch, including BMWhy and other-unit arrays.

| Form | Local template | Preserved details |
|---|---|---|
| Form9, TE EDS | `Templates/BmReport/EDS-CSPTemplate.xlsx` | EDS branch, named TE Template main sheet, work diary at index 1, accessories at index 3, VR/lookup values, QC/approval fields; generated copy clears developer notes BK59:BL74 |
| Form9, other TE processes | `Templates/BmReport/TETemplate.xlsx` | Non-EDS branch, same secondary-sheet selection, original calculations and array loops |
| Form4, PM E | Process evidence template above | Twelve source-photo slots including SING Safety slot Z11, restored third-photo captions, missing-image marks, TSC Yes/No/N/A, conditional route/version and SING Andon/layout; aspect-preserving photos contained in merged regions |
| Form10, TE E | `Templates/BmReport/CWC.xlsx` | Before/after categories, connector-code placement, descriptions, five-row QC table, repair timestamps, Teleko at H70/J70/M70/O70 within 1.54 x 1.4 inch bounds; Ideas included in PDF print area |

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
PDF export explicitly selects the entire workbook, preserving sheet visibility;
the library's active-sheet default previously omitted TE supplementary sheets.
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
legacy cell writes. Their named main sheet and original secondary-sheet indices
are used; metadata dimensions alone are not used to remap cells.

The PM EDS workbook separately has twelve treatment rows followed by Turnover To.
Nonempty thirteenth-treatment data now raises an explicit template-capacity error
instead of overwriting that section. See the audit's remaining-capacity table.

## Verification

- `scripts/Audit-ReportTemplates.ps1`: all supplied template regions/drawings,
  selected bindings/source fields, layout comparisons and duplicate destinations.
- `scripts/Update-LegacyReportMappings.ps1 -Check`: extraction plus audited corrections.
- `scripts/Test-ReportGeneration.ps1`: offline real template/image fixtures,
  genuine XLSX/PDF signatures, workbook styles, every PM process, both Form9
  branches, every Form4 process, Form10, history 0/1/2, QC/defaults, durations,
  conditional styling, diary mapping, original/shared upload resolution and
  embedded photo bytes, plus explicit CWC missing-sheet errors. Quality-image
  checks add sheets only to an in-memory test workbook, never the source template.
  Artifacts are generated in a unique local TEMP folder. Preview PNGs have a
  transparent background and should be viewed on white.
- `scripts/Render-ReportPdf.ps1`: independent Windows PDF rendering of actual
  exported files for visual inspection.
- `scripts/Test-DataRetrieval.ps1`: uniquely named disposable LocalDB database;
  parameterized PM lookup batches, raw SQL types, TE CN and distinct misc rows,
  plus earlier cancellation/filter/batch regressions. It never loads appsettings.
- `scripts/Test-WorkflowEndpoints.ps1` and `scripts/Test-Workflows.ps1`: isolated
  app with an unreachable loopback SQL override, real download streams/MIME/
  filenames/inline preview, and mocked browser generation for both formats.

Live production record completeness, the full evidence-photo archive, installed
server fonts and comparison against actual desktop-generated files remain
unverified. Never host test runs using the application's live connection string.
