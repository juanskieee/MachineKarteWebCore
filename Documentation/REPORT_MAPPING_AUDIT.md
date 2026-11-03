# Report mapping audit — 2026-11-03

The local audit found incorrect template versions, overlapping bindings, omitted
photographs, photo sizes exceeding their placeholders, and two sources of stray
zeros. Confirmed presentation defects were corrected without modifying the
original workbooks, preserved VB forms, SQL, schema, category limits, ordering,
history rules, or time calculations. Production sign-off is still outstanding.

## Scope and evidence

The inventory covers all 50 supplied `.xlsx` files, including 25 selected by the
current services. There are 73 readable OOXML worksheets and one additional file
that is not an OOXML ZIP workbook: the unreferenced
`Templates/BmReport/FLTemplate.xlsx`. Its internal format needs separate owner
inspection before using it. It is not selected by an export pipeline.

The reference specification available here consists of `Legacy/Form3.vb.txt`,
`Form4.vb.txt`, `Form9.vb.txt`, `Form10.vb.txt`, `schema.sql`, the printed labels
and merged regions in the supplied workbooks, and the repository/service/model
contracts. No separate signed business specification, production dataset, or
desktop-generated acceptance sample was supplied for this audit. Legacy source
parity alone was not treated as proof that a template version or binding was correct.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Audit-ReportTemplates.ps1`
to regenerate the optional review inventories under `TempData/ReportAudit/Inventory`.
These generated CSV files are excluded from Git:

| Artifact | Contents |
|---|---|
| [Templates.csv](../TempData/ReportAudit/Inventory/Templates.csv) | All files, selected pipeline, worksheet name/index/active state, dimensions, merged-region counts and original SHA-256 hashes. |
| [Regions.csv](../TempData/ReportAudit/Inventory/Regions.csv) | All populated cells, formulas/cached values, merged regions and defined names, including unused workbooks and stale names. |
| [Drawings.csv](../TempData/ReportAudit/Inventory/Drawings.csv) | All 733 existing drawing anchors: workbook, worksheet/part, cell anchors, text and original drawing XML. These include static shapes, not just photos. |
| [Bindings.csv](../TempData/ReportAudit/Inventory/Bindings.csv) | Selected workbook, worksheet, destination, merged region, original placeholder text, source fields, dataset, expression, conditions and generated-source line. Includes literal and derived values, styles, images, Teleko slots and audited range corrections. |
| [BindingConflicts.csv](../TempData/ReportAudit/Inventory/BindingConflicts.csv) | Unconditional direct source assignments sharing one cell/merged destination. An empty result is not proof that every conditional business branch is correct. |
| [LayoutCandidates.csv](../TempData/ReportAudit/Inventory/LayoutCandidates.csv) | Comparison of supplied workbook versions against PM writers. Counts are review leads, not automatic business decisions; some template text is an intentional input prompt. |

The binding inventory traces variable assignments and conditional source fields.
It is a static review aid, not a VB interpreter. A repeated destination may be
intentional in mutually exclusive branches. Named ranges containing `#REF!` or
developer text are inventoried; they are not all visible output defects and were
not indiscriminately deleted.

## Dataset-to-output paths

| Pipeline | Retrieval and model | Writer and serialization |
|---|---|---|
| PM R, Form3 | `PmReportRepository`: process view in `Templates.csv` / `Bindings.csv`, parameterized current/history reads and lookup batches; typed values in `PmReportRecord.Values`. | `PmReportService` selects the configured folder/file/named worksheet; `LegacyReportWriter.PopulatePm`; `LegacyReportExport.Save` writes XLSX or PDF. |
| PM E, Form4 | `EvidenceRepository`: five `gridEvidenceMaking_Ver3` categories with TOP 3/2/2/2/3 and `BMEvidenceID` order; process header/evidence views in `ProcessTemplateMap`; `EvidenceImageGroups`, `EvidenceMachineHeader`, `TscConfirmation`. | `EvidenceMakingService` maps each category/slot and its description; original process workbook; `PopulateEvidence`; shared Excel/PDF serializer. |
| TE R, Form9 | `BmReportRepository`: `ViewTEData_Test`, VR numbers and nine supplemental lookup result sets; typed values in `BmReportData.Values`. | `BmReportService`; named `TE Template` main sheet, work details index 1 and accessories index 3; `PopulateTe`; shared serializer. |
| TE E, Form10 | `TeEvidenceRepository`: `ViewTEData`, six ordered categories from `TEBMEvidenceMaking`, CN/condition, Teleko TOP 4, three quality categories and all distinct misc-data rows; corresponding TE image/header DTOs. | `TeEvidenceMakingService`; `CWC.xlsx`; `PopulateTeEvidence`; generated-file download handler and shared serializer. |

Photo paths pass through `LegacyReportExport.ResolveEvidenceImage` for the
workflow's own upload share and the shared evidence archive. Static grades,
inspection marks, logos and the missing-photo mark remain local assets. See
[share deployment requirements](REPORT_GENERATION_CONTEXT.md#legacy-uploaded-image-storage-and-deployment).

## Confirmed defects and corrections

All corrected rows below affect both Excel and PDF unless stated otherwise.
Original input files remain intact. Corrections to extracted presentation code
live in `Update-LegacyReportMappings.ps1`; `-Check` verifies that the compiled VB
contains those documented corrections.

| ID | Template and cell/control | Expected source | Actual defect | Correction / result |
|---|---|---|---|---|
| 01 | PM DB: `PmReport/DBTemplate.xlsx`, e.g. G50/AC50/AG50 | Separate `Treatment12`, before and after values from `ViewDBData_Ver4`. | Selected workbook merged these writes into the `Current Related Adjustment Date / In-charge` heading. | Select the supplied `EvidenceMaking/DBTemplate.xlsx`, sheet `DB`, whose treatment, parts and QC slots match the writer. |
| 02 | PM WB: `PmReport/WBTemplate.xlsx`, e.g. AU47, BB47, AN51:BD54 | RPM before/after, four parts rows, treatment/QC/accreditation fields from `ViewWBData_Ver4`. | Revised headings/rows caused values and grades to overwrite other sections; RPM values overwrote Before/After headings. | Select `EvidenceMaking/WBTemplate.xlsx`, sheet `Front Blank (2)`. |
| 03 | PM MP: `PmReport/MPTemplate.xlsx`, e.g. AB56:AX60 | Five parts rows and fourteen QC rows from `ViewMPData_Ver4`. | Selected layout moved the table and QC rows; parts data overwrote package/device questions. | Select `EvidenceMaking/MPTemplate.xlsx`, sheet `MP`. |
| 04 | PM PL: `PmReport/PLTemplate.xlsx`, e.g. AN63:BI67 | Five parts rows, treatments and QC from `ViewPLData_Ver2`. | Parts rows landed in the After Maintenance Checking heading/questions; other sections had different row positions. | Select `EvidenceMaking/PLTemplate.xlsx`, sheet `PL`. |
| 05 | PM FT: `PmReport/FTTemplate.xlsx`, main sheet and parts/QC controls | Original FT fields and markers from `ViewFTData`. | The selected two-sheet version had a different main form and merged regions. | Select `EvidenceMaking/FTTemplate.xlsx`, sheet `FT`. |
| 06 | PM FL: `PmReport/FLTemplate2.xlsx`, treatment/parts/inspection controls | Original FL fields and grades from `ViewFLData_V2`. | Selected revised layout placed legacy writes in PM Reply, inspection and other headings. | Select `EvidenceMaking/FLTemplate2.xlsx`, sheet `FL`. |
| 07 | PM MEMS: AF27, AF28, AF29 | `ProdTreatLotNo`, `ProdTreatPackage`, `ProdTreatInspResult` from `ViewMEMSData`. | Lot overwrote Package/Device; package occupied the inspection-result region; result occupied the quantity region. | Bind to AA27, AK27 and AC28 respectively. Preserve the printed labels. |
| 08 | PM MP: AH36 and G50 | `PmRelatedBM` and `LotNoForInspection`. | Answer overwrote its label; inspection lot was overwritten by `Treatment9`. | Use AL36 and G55; retain current LOTNo as the historical fallback when inspection lot is blank. Treatment9 remains at C50. |
| 09 | PM PL: AX71/AZ71 and BD27 | `OtherUnitCheck` row 3 NG count and action; `GLConfirmOPReqBy` under GL Confirmation. | NG and action shared AZ71; `ReqToOtherSec` later overwrote the GL employee. | NG uses AX71, action uses AZ71. Keep the GL employee at BD27; other-section request already uses AU56. |
| 10 | PM R / TE R parts tables | Type, quantity and new/reworked values only for an existing named part. | Empty parts with SQL defaults printed `0` or `0.00`; reference comparison `IsNot ""` was an unreliable empty-string test. | Blank dependent cells for absent/whitespace names; retain genuine zero quantities for named parts. No global zero suppression. |
| 11 | TE EDS: `BmReport/EDS-CSPTemplate.xlsx`, BK59:BL74 | No report binding. | Template contained developer row indices 0–15 and `strData`/textbox code. These remained visible in Excel. | Clear only that scratch range in the generated workbook. Original template and business quantities are unchanged. |
| 12 | All PM evidence templates and CWC photo controls, e.g. C11/P11/AC11/C68/O68 | Category/slot-specific uploaded photograph. | Fixed legacy dimensions exceeded merged image regions; e.g. C11 requested 210 points in a 204.75-point region. Photos could cross borders and stretch. | Fit the photo's natural aspect ratio within both the legacy maximum size and merged placeholder; retain the start cell and use MoveAndSize anchoring. Static grade/icon sizing is unchanged. |
| 13 | PM SING evidence: Z19; other evidence: AD21 caption | `ProblemPoint[2].Description`. | Third-photo descriptions were omitted in both layouts. The third photo itself was already rendered. | Add the SING caption at Z19 and common-layout caption at AD21. All twelve existing photo slots are checked for correct bytes and a single picture per anchor. |
| 14 | CWC: B78 and print area/footer | `TEBMEvidenceMaking.Ideas`. | Excel contained Ideas at B78, but the PDF print area ended at row 76. Exposing it also revealed overlap with the footer under zero bottom margins. | Extend the generated print area through Q80, label and merge the Ideas region, and reserve separate body/footer margins. |
| 15 | CWC TEST BOARD, repeated condition/CN | Last row in the existing ordered category result at each connector. | Multiple rows created overlapping pictures at the same anchor. | Render one picture per condition/CN, retaining the last ordered row, which was the legacy visible result. Queries, TOP 20 and order are unchanged. |
| 16 | PM MEMS AG14, AD33, AH33, AN33 | Current occurrence-unit/dummy fields, with historical equivalents when current values are blank. | Two unconditional assignments shared each destination; blank new fields erased older valid values. | Prefer nonblank current values, otherwise use `OccurrenceFreqLotFrame` / `ProdTreat*` equivalents. Preserve explicit current zeros. |
| 17 | PM/TE main worksheet selection | The named process/TE main form. | Writer depended on workbook saved-active-tab metadata, allowing writes into another sheet. | PM selects `WorksheetName`; both TE writers explicitly use `TE Template`. |
| 18 | All PDF exports; TE work-detail/accessories worksheets | All printable worksheets containing the same workbook data as Excel. | `SaveOptions.PdfDefault.SelectionType` was ActiveSheet. TE XLSX contained populated supplementary sheets while PDF had only one page. | Explicit `PdfSaveOptions.SelectionType = EntireFile`. Preserve template sheet visibility. Tests require at least three TE PDF pages and independent PDF rendering checks the exported pages. |

## Capacity gaps and legacy decisions still requiring owner input

| Template / source | Finding and current handling | Required completion evidence |
|---|---|---|
| CWC, CN5–CN8 and quality conditions | Only `FORMAT` exists. The original writer addresses worksheets 1 and 2, but no corresponding production sheets were supplied. Existing guards reject those records explicitly. Tests use temporary sheets solely to verify the data/photo path. | Supply the original connector and quality worksheets with their intended layout, then validate affected records in both formats. |
| PM `EDSTemplate.xlsx`, treatment row 13 | The supplied form has twelve treatment rows followed by `Turnover To` at F51:M51. Legacy treatment 13 overwrote that heading; its before/after writes shared the turnover row. Empty row-13 values now leave the form intact; nonempty row-13 data produces an explicit capacity error. | An approved EDS template containing the thirteenth treatment row, or a business decision about its intended placement. |
| MEMS Situation; WB inspection lot; FT duplicate completion employee | Early assignments were unconditionally overwritten by Why1, Treatment9 and PKGDev1B respectively. Those dead assignments were removed. FT's completion employee remains in the header. The MEMS and WB forms have no distinct identified slots for the other two fields. | Identify approved distinct destinations if Situation and WB inspection lot must also be displayed. Their previous exports already omitted them. |
| PM category branches | Some original routines repeat category `2` for a later alternative. This is a legacy business-rule ambiguity, not evidence for choosing a new category ID. | Owner-approved category specification before altering branching. |
| TE category and description capacity | Existing combined TOP limits, three before/three after generic photo slots, distinct description iteration and legacy row order remain. A result exceeding one condition's slots needs an approved layout/selection policy; a static audit cannot certify every possible production dataset. | Representative imbalanced-category and duplicate-description records plus the expected desktop output. |
| Shares, fonts and desktop parity | Production archive access, historical path coverage, server-installed fonts and comparison with actual accepted desktop reports are unverified. | Validate under the deployed app identity using real record IDs and approved desktop exports. |

## Validation and reproducibility

`Test-ReportGeneration.ps1` runs without loading application settings, hosting
the app or opening production SQL. It uses all 25 active templates, distinctive
slot markers, a supplied repair JPEG, realistic machine/package/lot values, null
and empty data, missing lookups, zero/default parts rows, historical data and
duplicate connector rows. It writes 26 XLSX/PDF pairs, including an additional
uploaded-photo fixtures, to a unique local TEMP directory printed by the script.

Assertions cover every PM evidence slot, all generic TE before/after photo
slots, connector and Teleko source bytes/anchors, quality photo bytes on an
in-memory test sheet, merged photo bounds, cell anchoring, XLSX round-trip
geometry and absence of geometry changes during PDF serialization. Specific
MEMS/MP/PL bindings, real-zero quantities, missing lookups, history 0/1/2,
durations, QC and styling remain checked. CWC/EDS capacity failures are explicit.
TE PDFs must contain the visible supplementary sheets, not just the active main form.

`Test-DataRetrieval.ps1` passed against a uniquely named, disposable LocalDB
fixture database and removed it afterward. It exercises actual parameterized
repository reads, raw SQL types, all eleven PM lookup profiles, evidence
category limits/order/empty slots, TE CN identifiers and distinct misc rows.
The first restricted run could not start LocalDB; the permitted isolated rerun
passed. No production database was contacted.

`Render-ReportPdf.ps1 -Pdf <generated.pdf> -OutputDirectory <directory>` renders
the actual exported PDF through Windows' PDF engine, independently of GemBox's
workbook-to-image renderer. Use these pages for visual review; workbook preview
PNGs can have transparent backgrounds and are not proof of PDF appearance.

Passing fixture checks establishes the tested code paths. It does not establish
production image-level parity or owner acceptance for every historical record.
Keep the capacity gaps above open until the required templates/specification and
deployed acceptance evidence are available.

Final local results: 26 XLSX/PDF pairs passed with zero build warnings/errors;
the two TE report PDFs each contain three visible-sheet pages. All exported PDFs
were independently rendered (30 pages in the final set), with representative
PM/evidence/TE/auxiliary pages visually inspected. The final inventory contains
4,481 binding statements and no remaining unconditional direct-field destination
conflicts. Isolated HTTP checks passed exact download bytes, MIME types, filenames
and inline PDF behavior. Release publish succeeded and includes all six corrected
PM template selections. Original templates, legacy forms and `schema.sql` have no
changes. Review the [generated Excel/PDF samples](../TempData/ReportAudit/Outputs/)
and [rendered PDF pages](../TempData/ReportAudit/After/) locally; these generated
artifacts are excluded from Git.
