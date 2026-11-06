# Reporting guide

PM/TE maintenance records and evidence use the supplied workbooks with compiled
VB mappings and GemBox.Spreadsheet. All four dialogs support genuine XLSX/PDF
output, default to Excel, and require separate Generate and Download actions.
PDF output includes all printable worksheets while retaining sheet visibility.

Related guides: [application and validation](README.md),
[database behavior](DATABASE.md), and [JavaScript conventions](../Assets/js/README.md).

## Source and output paths

| Workflow | Retrieval | Writer and output |
| --- | --- | --- |
| PM R / Form3 | `PmReportRepository`: current/history records, typed values, parameterized lookup batch | `PmReportService`, `LegacyReportWriter.PopulatePm`, `LegacyReportExport.Save` |
| PM E / Form4 | `EvidenceRepository`: five ordered `gridEvidenceMaking_Ver3` categories, process header, TSC confirmations | `EvidenceMakingService`, `PopulateEvidence`, file/download handler |
| TE R / Form9 | `BmReportRepository`: `ViewTEData_Test`, VR numbers, supplemental lookups | `BmReportService`, `PopulateTe`, controller saves the generated copy and returns the binary HTTP response |
| TE E / Form10 | `TeEvidenceRepository`: `ViewTEData`, ordered evidence categories, connector/condition, Teleko, quality and distinct misc rows | `TeEvidenceMakingService`, `PopulateTeEvidence`, file/download handler |

[LegacyReportMappings.vb](../Services/Reporting/LegacyReportMappings.vb) contains
extracted active Form3/4/9/10 mapping bodies. The
[mapping generator](../scripts/Update-LegacyReportMappings.ps1) preserves statement
order, conditions, typed VB values, array loops, dates/numbers, pictures, styles,
and print settings, with explicit audited presentation corrections. SQL,
impersonation, desktop UI, and shell/file launches are excluded.

Make mapping changes in the generator, then regenerate and run `-Check` rather
than editing generated VB alone. The preserved forms are the source reference;
the former flattened cell matrix is not a runtime dependency.

[LegacyReportExport.cs](../Services/LegacyReportExport.cs) owns format validation,
MIME types, serialization, static-image roots, and uploaded-photo resolution.
The project pins GemBox.Spreadsheet and currently initializes it with
`FREE-LIMITED-KEY`. Exports use that library directly without Office or an
external converter.

## Generated-report storage

PM and TE outputs use separate existing server roots, each with a `Print Out`
directory and a subfolder for the selected process:

| Workflow | Output location |
| --- | --- |
| PM maintenance records and evidence | `\\172.26.220.161\eMaintenanceSystem\TempData\Print Out\<PROCESS>\` |
| TE maintenance records and evidence | `\\172.26.220.161\TEeMaintenanceSystem\TempData\Print Out\<PROCESS>\` |

For example, a PM DB report is `Print Out\DB\DB_<BMID>.pdf`; TE FT evidence is
`Print Out\FT\TE_Evidence_<BMID>.xlsx` under the TE root. File names and explicit
Generate/Download behavior remain unchanged. The process now travels in
generation/download URLs so the same folder is used for both operations.

`ReportOutput:PmTempRoot` and `ReportOutput:TeTempRoot` configure the two
`TempData` roots in `appsettings.json`. The common resolver appends `Print Out`,
an allowlisted uppercase process, and a validated XLSX/PDF file name. If a root
override is absent, it derives the corresponding system share from
`NetworkShare:Server`. `EvidenceMaking:OutputRoot` is no longer used.

All four Generate actions save their output to the appropriate share. There
is no local `TempData/GeneratedPdf` fallback or public static-file mapping for
reports. Downloads and inline previews go through the validated controller
handlers. Startup does not open or create remote output folders; storage is
accessed only when generating or downloading a report.

The application's Windows identity needs share and NTFS read/write access to
the process output folders. Generation creates a missing process directory
under its configured root and reports a failure when that storage is unavailable;
it does not silently save elsewhere. These files are generated output, not
source templates. Audit/test artifacts still belong in OS temporary storage.

## Template selection

PM definitions live in [PmReportRepository](../Repositories/PmReportRepository.cs).
Select the named worksheet, not the workbook's saved active tab. Six PM forms
use compatible supplied workbooks in `EvidenceMaking/`; similarly named files
in `PmReport/` have different layouts.

Paths below are relative to `Templates/`:

| Process | PM workbook | Main worksheet | Read view |
| --- | --- | --- | --- |
| DC | `PmReport/DCTemplate2.xlsx` | `DC` | `ViewDCData_Ver4` |
| DB | `EvidenceMaking/DBTemplate.xlsx` | `DB` | `ViewDBData_Ver4` |
| WB | `EvidenceMaking/WBTemplate.xlsx` | `Front Blank (2)` | `ViewWBData_Ver4` |
| MP | `EvidenceMaking/MPTemplate.xlsx` | `MP` | `ViewMPData_Ver4` |
| PL | `EvidenceMaking/PLTemplate.xlsx` | `PL` | `ViewPLData_Ver2` |
| FT | `EvidenceMaking/FTTemplate.xlsx` | `FT` | `ViewFTData` |
| FL | `EvidenceMaking/FLTemplate2.xlsx` | `FL` | `ViewFLData_V2` |
| TP | `PmReport/TPTemplate.xlsx` | `TP` | `ViewTPData` |
| SING | `PmReport/SINGTemplate.xlsx` | `SING` | `ViewSINGData` |
| MEMS | `PmReport/MEMS FT-TPTemplate.xlsx` | `MEMS` | `ViewMEMSData` |
| EDS | `PmReport/EDSTemplate.xlsx` | `EDS` | `ViewEDSData` |

ATC has no PM report implementation because the original `ATCData` routine is
empty. PM history retains the original slot behavior: zero rows leaves slots
alone, one row appears in both history slots, and two rows fill newest then
next-newest. EDS has no history writer. Preserve raw DateTime/number/DBNull
values and explicit missing-column errors.

PM evidence templates are `EvidenceMaking/EvidenceMaking_{PROCESS}.xlsx` for
these eleven processes, with `EvidenceMaking_Sing.xlsx` for SING. The source
view map is in [EvidenceMakingModels](../Models/EvidenceMakingModels.cs).
Five categories retain their TOP limits `3/2/2/2/3`, `BMEvidenceID` ordering,
twelve photo slots, SING/common-layout differences, and TSC confirmations.

TE R uses `BmReport/EDS-CSPTemplate.xlsx` for EDS and `BmReport/TETemplate.xlsx`
otherwise. Both select the named `TE Template` main sheet; work details use
worksheet index 1 and accessories index 3. TE E uses
`EvidenceMaking/CWC FILES/CWC.xlsx` and
retains its six category limits `6/6/6/20/6/6`, four Teleko images, five-row QC
table, connector routing, and iteration of all distinct misc rows.

### Retained CWC version

The supplied `CWC - Copy (3).xlsx` is retained byte-for-byte as
[CWC FILES/CWC.xlsx](<../Templates/EvidenceMaking/CWC FILES/CWC.xlsx>).
Its SHA-256 is
`D56FAB6C34BC6EF6A831EDB26D11253FE485CD5B9897AFFB5BFE1D9432C3AB7C`.

| Supplied version | Finding and decision |
| --- | --- |
| `CWC - Copy (3).xlsx` | `FORMAT`, `TESTBOARD`, `DATASHEET`; main-sheet contents match the previous template and the additional sheets match connector/quality destinations. Retained as canonical `CWC.xlsx`. |
| `CWC - Copy.xlsx`, `CWC - Copy (2).xlsx` | Identical files with `FORMAT`, an unrelated hidden `Sheet1`, and `TESTBOARD`; no quality datasheet. Removed. |
| Supplied single-sheet `CWC.xlsx` and old `EvidenceMaking/CWC.xlsx` / `BmReport/CWC.xlsx` | Identical `FORMAT`-only workbooks, missing connector/quality sheets. Superseded and removed. |

The writer selects `FORMAT`, `TESTBOARD`, and `DATASHEET` by name so saved tab
order cannot route evidence into the wrong sheet. CN5-CN8 images use the actual
`TESTBOARD` slots, and before/after/ideal quality images and descriptions use
`DATASHEET`. Generated DATASHEET print area covers A1:S8. Explicit missing-sheet
validation remains for accidentally replaced/incomplete templates.

## Audited presentation rules

These corrections apply to generated workbooks; retain the original supplied
templates and preserved forms.

| Area | Correction to preserve |
| --- | --- |
| PM template versions | Keep the six compatible DB/WB/MP/PL/FT/FL selections above; revised layouts put legacy writes into headings or unrelated fields |
| MEMS treatment fields | Production lot, package, and inspection result use AA27, AK27, and AC28 |
| MP fields | Related-BM answer uses AL36; inspection lot uses G55 with historical LOTNo fallback; Treatment9 remains C50 |
| PL fields | Other-unit NG/action use AX71/AZ71 separately; GL employee stays BD27; other-section request remains AU56 |
| PM/TE parts | Blank dependent cells for absent/whitespace part names; retain real zero quantities for named parts |
| TE EDS scratch cells | Clear developer indices/code only in the generated copy's BK59:BL74 range |
| Evidence photos | Fit natural aspect ratio inside both the legacy maximum and merged placeholder, retaining the anchor and MoveAndSize behavior; do not resize static marks this way |
| Third-photo captions | SING's third problem description uses Z19; the common layout uses AD21 |
| CWC Ideas | Keep B78 visible through print area Q80, with separate body/footer space |
| Repeated connector images | Render one image per condition/CN using the last ordered row, matching the legacy visible result |
| MEMS fallback fields | AG14, AD33, AH33, AN33 prefer nonblank current values, then their occurrence/production-treatment historical equivalents; retain current zero values |
| Main worksheets | Select the configured PM worksheet and named TE main sheet explicitly |
| PDF serialization | Use `SelectionType.EntireFile` so TE supplemental sheets are not omitted |

The generator also removes unconditional dead assignments that were immediately
overwritten. It does not invent destinations or change ambiguous repeated
category-2 business branches. A static binding inventory is a review aid, not
an interpreter or proof of every conditional production path.

## Images and deployment

Static marks and templates ship with the application:

| Legacy source group | Local assets |
| --- | --- |
| PM `eMaintenanceSystem\images` | `Assets/images/eMaintenanceSystems_images/`, including `Inspection/` |
| TE `TEeMaintenanceSystem\images` | `Assets/images/TEeMaintenanceSystems_images/` |
| PM templates | `Templates/PmReport/` and `Templates/EvidenceMaking/` |
| TE templates | `Templates/BmReport/` |

Uploaded record photographs remain on the file server selected by
`NetworkShare:Server`:

| Share root | Use |
| --- | --- |
| `\\<server>\eMaintenanceSystem\Upload` | PM evidence |
| `\\<server>\TEeMaintenanceSystem\Upload` | TE evidence, including Teleko and quality conditions |
| `\\<server>\Upload\Evidence` | Shared historical evidence archive |

Relative `\Upload\...` paths resolve under the workflow's own system share.
For `\Upload\Evidence\...`, that original location takes precedence over the
shared archive. Absolute UNC paths must remain under the workflow's own Upload
root or shared archive. TE preserves legacy `~` removal; forward slashes are
accepted. Traversal, unrelated shares, and basename-only lookup are rejected.
Missing/invalid photos use the workflow's local `noimage.png`.

Deploy with network reachability and share/NTFS Read access for the account
running the app. `NetworkShare:UserName` and `NetworkShare:Password` do not
authenticate the current implementation, and `EvidenceMaking:ImagesRoot` does
not control uploaded-photo lookup. Keep local templates/static images readable
and output storage writable. Uploaded photos are embedded during generation;
they need no public static-file mapping or mapped drive.

Validate actual photographs under the deployed app identity. A developer's file
access or an export containing `noimage.png` does not prove the app can read the
archive; denied access may appear as a missing file. Check representative PM,
TE, Teleko, quality-condition, and historical records in both formats.

## Outstanding acceptance work

| Gap | Current behavior and required input |
| --- | --- |
| PM EDS treatment 13 | Supplied template has twelve treatments followed by Turnover To. Nonempty thirteenth-treatment data raises a capacity error. An approved layout or placement decision is needed. |
| MEMS Situation and WB inspection lot | Original assignments were overwritten, with no approved distinct destination identified. Specify destinations if these fields must be displayed. FT's duplicate completion employee remains in the header. |
| Category and photo capacity | Preserve existing limits, row order, combined BEFORE/AFTER behavior, distinct descriptions, and ambiguous category branches until an owner approves a change. Validate imbalanced and duplicate-description records. |
| Deployment and desktop parity | Production archive completeness/access, installed fonts, live data, and comparisons with approved desktop-generated reports remain unverified. |

The unselected `Templates/BmReport/FLTemplate.xlsx` is not a readable OOXML ZIP
workbook. Inspect its actual format before considering it for a pipeline.

## Reproducing validation artifacts

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Update-LegacyReportMappings.ps1 -Check
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-ReportGeneration.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Audit-ReportTemplates.ps1
```

The mapping check is offline. Report fixtures use real templates/images, typed
values, empty/zero/history cases, photo bytes/anchors, workbook geometry, genuine
file signatures, supplemental PDF sheets, and explicit capacity errors. Connector
and quality checks use the retained CWC's real sheets, including a reordered copy
to check named routing; incomplete copies exercise missing-sheet rejection. Tests
write/read every process folder under separate local PM/TE root overrides and
verify UNC path construction without connecting to the file server. The
runner prints its unique OS temporary output directory.

The audit defaults to `MachineKarte-ReportAudit/Inventory` under OS temporary
storage and accepts `-OutputDirectory`. It regenerates `Templates.csv`,
`Regions.csv`, `Drawings.csv`, `Bindings.csv`, `BindingConflicts.csv`, and
`LayoutCandidates.csv` from current templates and source. No archived copy in
`TempData/ReportAudit/` is needed, and documentation does not link to disposable
output files.

`Test-DataRetrieval.ps1` adds isolated LocalDB validation of repository reads;
HTTP/browser tools verify stream types, filenames, preview headers, and explicit
generation/download behavior. For endpoint checks, configure the test app's
`ReportOutput__PmTempRoot` / `ReportOutput__TeTempRoot` to local absolute paths
and pass those same paths as `-PmTempRoot` / `-TeTempRoot` to
`Test-WorkflowEndpoints.ps1`. The endpoint fixtures reject network output roots.
For independent PDF appearance checks, use
`Render-ReportPdf.ps1 -Pdf <file> -OutputDirectory <directory>`. Fixture success
does not close the acceptance gaps above.
