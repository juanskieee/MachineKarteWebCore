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
| PM E / Form4 | `EvidenceRepository`: five ordered `gridEvidenceMaking_Ver3` categories, process header, TSC confirmations | `EvidenceMakingService`, `PopulateEvidence`, binary generation response |
| TE R / Form9 | `BmReportRepository`: `ViewTEData_Test`, VR numbers, supplemental lookups | `BmReportService`, `PopulateTe`, controller returns the binary HTTP attachment response directly |
| TE E / Form10 | `TeEvidenceRepository`: `ViewTEData`, ordered evidence categories, connector/condition, Teleko, quality and distinct misc rows | `TeEvidenceMakingService`, `PopulateTeEvidence`, binary generation response |

[LegacyReportMappings.vb](../Reporting/LegacyReportMappings.vb) contains
extracted active Form3/4/9/10 mapping bodies. The
[mapping generator](../scripts/Update-LegacyReportMappings.ps1) preserves statement
order, conditions, typed VB values, array loops, dates/numbers, pictures, styles,
and print settings, with explicit audited presentation corrections. SQL,
impersonation, desktop UI, and shell/file launches are excluded.

Make mapping changes in the generator, then regenerate and run `-Check` rather
than editing generated VB alone. The preserved forms are the source reference;
the former flattened cell matrix is not a runtime dependency.

The compiled writer is owned by the top-level `Reporting/LegacyReports.vbproj`
class library. The web project references that project once; C# services perform
data retrieval and orchestration, and the VB library receives loaded records.
The solution, mapping generator, and template audit use this same project path.

[LegacyReportExport.cs](../Services/LegacyReportExport.cs) owns format validation,
MIME types, in-memory serialization, static-image roots, and uploaded-photo resolution.
The project pins GemBox.Spreadsheet and currently initializes it with
`FREE-LIMITED-KEY`. Exports use that library directly without Office or an
external converter.

## Local downloads

All four generation handlers return the generated XLSX/PDF bytes directly in
an HTTP attachment response with the original filename and `Cache-Control:
no-store`. Workbooks are serialized in memory. Generated reports are never
written to a server folder, network share, database, or server-side cache.
There is no generated-file history, version archive, or saved-file download API.
The former output-root settings have been removed.

The dialog holds the response in a browser Blob URL for its separate Download
action and, for PDFs, its inline preview. The browser saves the file in the
user's Downloads folder or the location chosen in its Save As settings. No
report is downloaded merely by opening a dialog. Generating again releases the
previous Blob URL; closing the dialog cancels pending requests and releases its
current report bytes. Reopening starts with no generated report. Files already
saved on the user's computer remain there.

The shared browser helper reads generation responses with `arrayBuffer()` before
creating the preview/download Blob. Reading directly with `response.blob()` can
fail in Edge/Chrome after `200 OK` when Chromium cannot allocate temporary Blob
storage on a low-disk machine. This was reproduced locally with 12 MiB and 32 MiB
responses; both sizes succeeded through the memory-buffered path. The same helper
covers all four dialogs and both formats. Reports must fit browser memory; very
large exports would require a native streaming download flow. Chromium documents
its [Blob storage and disk limits](https://chromium.googlesource.com/chromium/src/+/main/storage/browser/blob/README.md).

The application still reads source templates and uploaded evidence photos from
their existing locations. Per-export photo staging uses OS temporary storage
and is cleaned up after the export; it does not archive generated reports.

### Generation performance

`LegacyReportExport.Load` caches parsed source templates and gives each export an
independent deep clone. The cache reloads a template when its last-write time or
length changes; BM values and populated workbooks are never cached. Shared
template cloning is locked, while all population and serialization happen on
the private workbook outside that lock.

PM/TE evidence validates photo paths without network access, then opens and copies
each distinct source directly to a per-export OS temporary directory, with at
most four concurrent filesystem operations. A separate SMB `File.Exists` request
before every copy has been removed. Resolution and copying form one pipeline;
absolute/relative aliases share the same in-flight copy. Unavailable candidates
continue to the archive root or placeholder. The same validated PM/TE/archive
roots, precedence, missing-image marks, category ordering, TOP limits, connector
routing, and worksheet mappings still apply. The compiled writer reads these local copies and memoizes its
picture existence checks; it never mutates a workbook concurrently. Form10's
tilde normalization applies to stored paths before resolution, preserving literal
tildes in resolved local staging directories. Temporary photos are removed on
success, failure, or cancellation. Fresh generations read
current database rows and photos rather than reusing a previous report.

TE generation reads its assignment from the already-loaded header instead of
querying `ViewTEData` a second time. The header projects only its needed fields
from the wide view; a whitelisted metadata projection retains support for older
views with missing optional columns. It stages only the final visible row for
each test-board condition/connector, preserving the writer's last-row behavior
without fetching photographs that it would immediately overwrite. Both evidence
services load/clone their templates while independent SQL reads are in flight.
Image bytes, image quality, workbook mappings and PDF selection remain unchanged.

Successful PM/TE generations log separate milliseconds for data/template work,
photo staging, workbook population, serialization and total service time. These
timings identify SQL/share delays separately from PDF conversion without logging
photo paths or report contents. Monitoring's MachineList response also includes
its service duration in the `Server-Timing` header.

The offline regression runner compares fresh-template loading with cached
cloning using real templates and photo bytes. `-EvidenceOnly` covers all eleven
PM evidence processes and the TE main/connector/quality sheets, path validation,
photo bytes, cleanup, clones and both formats. See
[validation commands](#reproducing-validation-artifacts) for the appropriate
focused run. Fixture timings exclude production SQL/share latency; complete
generation durations still require representative deployment measurements.

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
`EvidenceMaking/CWC.xlsx` and
retains its six category limits `6/6/6/20/6/6`, four Teleko images, five-row QC
table, connector routing, and iteration of all distinct misc rows.

### Retained CWC version

The supplied `CWC - Copy (3).xlsx` is retained byte-for-byte as
[CWC.xlsx](<../Templates/EvidenceMaking/CWC.xlsx>).
Its SHA-256 is
`D56FAB6C34BC6EF6A831EDB26D11253FE485CD5B9897AFFB5BFE1D9432C3AB7C`.

| Supplied version | Finding and decision |
| --- | --- |
| `CWC - Copy (3).xlsx` | `FORMAT`, `TESTBOARD`, `DATASHEET`; main-sheet contents match the previous template and the additional sheets match connector/quality destinations. Retained as canonical `CWC.xlsx`. |
| `CWC - Copy.xlsx`, `CWC - Copy (2).xlsx` | Identical files with `FORMAT`, an unrelated hidden `Sheet1`, and `TESTBOARD`; no quality datasheet. Removed. |
| Supplied single-sheet `CWC.xlsx` and former `EvidenceMaking/CWC.xlsx` / `BmReport/CWC.xlsx` copies | Identical `FORMAT`-only workbooks, missing connector/quality sheets. Superseded by the retained three-sheet workbook, now at `EvidenceMaking/CWC.xlsx`. |

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
| CWC optional fields | Exclude Wafer No. (M7:Q7), Remarks (M14:Q15), and TTL Stoppage (Q10:Q11) from the supplied template's pink blank-cell rule in generated reports; retain other required-field rules and source-template bytes |
| CWC heading and request | Remove the floating T.E EVIDENCE CONFIRMATION (CWC) text box; B5 uses the record's RequestContent (for example TESTERPROBLEM), rather than its failed-test number |
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
and uploaded photos readable. Uploaded photos are embedded during generation;
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
to check named routing; incomplete copies exercise missing-sheet rejection. The
runner prints its unique OS temporary artifact directory. Its focused
`-DownloadOnly` mode checks real in-memory XLSX/PDF responses without running the
full mapping suite.
Use `-EvidenceOnly` for PM/TE evidence and source-photo performance changes.

The audit defaults to `MachineKarte-ReportAudit/Inventory` under OS temporary
storage and accepts `-OutputDirectory`. It regenerates `Templates.csv`,
`Regions.csv`, `Drawings.csv`, `Bindings.csv`, `BindingConflicts.csv`, and
`LayoutCandidates.csv` from current templates and source. No archived copy in
`TempData/ReportAudit/` is needed, and documentation does not link to disposable
output files.

`Test-DataRetrieval.ps1` adds isolated LocalDB validation of repository reads;
HTTP/browser tools verify stream types, filenames, preview headers, and explicit
generation/download behavior. Use `Test-WorkflowEndpoints.ps1 -ReportsOnly` for
report-route checks and `Test-Workflows.ps1 -ReportsOnly` for browser-local
downloads, previews, and Blob URL cleanup. These checks require no output-share
configuration.
For independent PDF appearance checks, use
`Render-ReportPdf.ps1 -Pdf <file> -OutputDirectory <directory>`. Fixture success
does not close the acceptance gaps above.
