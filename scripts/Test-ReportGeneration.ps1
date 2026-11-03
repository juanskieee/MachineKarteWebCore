param([string]$Dotnet = 'dotnet')
# Offline: real templates/images and compiled legacy mappings. Never hosts the app
# or loads appsettings, opens SQL, or writes into the source templates.
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'Update-LegacyReportMappings.ps1') -Check
if($LASTEXITCODE -ne 0){throw 'Mapping/source verification failed.'}
$directory=Join-Path $env:TEMP ('MachineKarte-Reports-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $directory | Out-Null
$project=[Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
 <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><UseAppHost>false</UseAppHost><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
 <ItemGroup><ProjectReference Include="$project" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $directory 'Checks.csproj')
@'
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using GemBox.Spreadsheet;
using MachineKarte.Reporting;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
using MachineKarte.Web.Services;
using MachineKarte.Web.Services.Reporting;

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var root=args[0]; var output=Path.Combine(AppContext.BaseDirectory,"artifacts"); Directory.CreateDirectory(output);
void Require(bool condition,string message){if(!condition)throw new Exception(message);}
string Images(bool te)=>Path.Combine(root,"Assets","images",te?"TEeMaintenanceSystems_images":"eMaintenanceSystems_images");

// Form4 and Form10 resolve the SQL image path under distinct legacy shares.
// Exercise this offline against temporary share-shaped directories, never a server.
var pmLegacyShare=Path.Combine(output,"legacy-PM-share");
var teLegacyShare=Path.Combine(output,"legacy-TE-share");
var pmUpload=Path.Combine(pmLegacyShare,"Upload","2026");
var teUpload=Path.Combine(teLegacyShare,"Upload","2026");
Directory.CreateDirectory(pmUpload); Directory.CreateDirectory(teUpload);
var pmImage=Path.Combine(pmUpload,"photo.png");
var teImage=Path.Combine(teUpload,"photo.png");
File.Copy(Path.Combine(Images(false),"noimage.png"),pmImage);
File.Copy(Path.Combine(Images(true),"noimage.png"),teImage);
var pmPlaceholder=Path.Combine(Images(false),"noimage.png");
var tePlaceholder=Path.Combine(Images(true),"noimage.png");
Require(LegacyReportExport.ResolveEvidenceImage(@"\Upload\2026\photo.png",pmLegacyShare,Images(false))==Path.GetFullPath(pmImage),
    "Form4 did not find the image in its original PM server folder");
Require(LegacyReportExport.ResolveEvidenceImage(@"~\Upload\2026\photo.png",teLegacyShare,Images(true),te:true)==Path.GetFullPath(teImage),
    "Form10 failed to strip ~ and find the image in its separate TE server folder");
Require(LegacyReportExport.ResolveEvidenceImage(@"~\Upload\2026\photo.png",pmLegacyShare,Images(false))==pmPlaceholder,
    "Form4 must not silently strip the Form10-only tilde");
Require(LegacyReportExport.ResolveEvidenceImage(@"\Upload\2026\missing.png",pmLegacyShare,Images(false))==pmPlaceholder,
    "Form4 missing image must use the original PM noimage mark");
Require(LegacyReportExport.ResolveEvidenceImage(@"\Upload\2026\missing.png",teLegacyShare,Images(true),te:true)==tePlaceholder,
    "Form10 missing image must use the original TE noimage mark");
Require(LegacyReportExport.ResolveEvidenceImage(@"\..\other.png",pmLegacyShare,Images(false))==pmPlaceholder &&
        LegacyReportExport.ResolveEvidenceImage(@"\\other-server\secret.png",teLegacyShare,Images(true),te:true)==tePlaceholder,
    "DB image paths must not escape their respective legacy system share");
var serverConfig=new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string,string?> { ["NetworkShare:Server"]="test-file-server" }).Build();
Require(LegacyReportExport.LegacyEvidenceRoot(serverConfig,false)==@"\\test-file-server\eMaintenanceSystem" &&
        LegacyReportExport.LegacyEvidenceRoot(serverConfig,true)==@"\\test-file-server\TEeMaintenanceSystem",
    "Web server path mapping diverged from Form4/Form10");
Console.WriteLine("PASS: original PM/TE server image roots, Form10 tilde, PM noimage and path boundaries.");

var date=new DateTime(2026,10,1,10,0,0);
Dictionary<string,object> Record(string profile){
 var record=LegacyReportWriter.RequiredFields(profile).ToDictionary(field=>field,field=>(object)"",StringComparer.OrdinalIgnoreCase);
 foreach(var field in record.Keys.ToArray()) {
  if(field.EndsWith("ID",StringComparison.OrdinalIgnoreCase)||field=="Why4")record[field]="1";
  if(field.Contains("Date",StringComparison.OrdinalIgnoreCase)&&!field.Contains("Data",StringComparison.OrdinalIgnoreCase))record[field]=date;
 }
 foreach(var field in new[]{"TimeOfRequest","OccurrenceDate","GLConfirmOPReqDate","PMConfirmGLOPReqDate","StartOfRepair","EndOfRepair"}) record[field]=date;
 record["PMConfirmGLOPReqDate"]=date.AddMinutes(15);record["StartOfRepair"]=date.AddMinutes(20);record["EndOfRepair"]=date.AddMinutes(50);
 record["TotalStopTime"]="7";record["WaitingTime1"]="15";record["RepairTime1"]="30";record["MCNo"]="FIXTURE-MC";record["MCStatus"]="Stop";
 record["RepairedByEnd"]="REPAIR";record["PMConfirmGLOPReqBy"]="REQUEST";
 return record;
}
Dictionary<string,string> Lookups(string profile,Dictionary<string,object> record){
 var lookups=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 foreach(var spec in LegacyReportWriter.LookupSpecs(profile).Select(value=>value.Split('|'))){
  var id=record.GetValueOrDefault(spec[0],"").ToString();
  lookups[$"{spec[1]}|{id}|{spec[3]}"]=spec[1]=="BMContent"?"MEKA":$"LOOKUP-{spec[1]}";
 }
 return lookups;
}
void Save(ExcelFile book,string name,bool preview=false){
 var xlsx=LegacyReportExport.Save(book,"xlsx");var pdf=LegacyReportExport.Save(book,"pdf");
 Require(xlsx[0]=='P'&&xlsx[1]=='K',name+" is not XLSX");Require(Encoding.ASCII.GetString(pdf,0,5)=="%PDF-",name+" is not real PDF");
 using(var zip=new ZipArchive(new MemoryStream(xlsx)))Require(zip.GetEntry("xl/styles.xml")!=null,name+" lost workbook styles");
 File.WriteAllBytes(Path.Combine(output,name+".xlsx"),xlsx);File.WriteAllBytes(Path.Combine(output,name+".pdf"),pdf);
 if(preview) book.Save(Path.Combine(output,name+".png"),new ImageSaveOptions{Format=ImageSaveFormat.Png,PageNumber=0});
 Console.WriteLine($"PASS: {name} real XLSX/PDF ({xlsx.Length}/{pdf.Length} bytes), sheets={book.Worksheets.Count}.");
}
var processes=new[]{"MEMS","WB","DB","DC","MP","PL","FT","SING","FL","TP","EDS"};
foreach(var process in processes){
 Require(PmReportRepository.TryGetDefinition(process,out var definition),"Missing PM definition");
 var book=LegacyReportExport.Load(Path.Combine(root,"Templates","PmReport",definition.TemplateFile));
 var data=Record(process);var writer=new LegacyReportWriter(Images(false),Lookups(process,data));
 try{writer.PopulatePm(book,process,data,new List<IDictionary<string,object>>());}
 catch(Exception ex){throw new Exception("PM fixture failed for "+process,ex);}
 Save(book,"PM-"+process,process is "DC" or "SING");
}
// Real regressions for the flattened mapper: only the matching option is marked,
// durations are calculated, and missing history never copies the current record.
var memsPath=Path.Combine(root,"Templates","PmReport","MEMS FT-TPTemplate.xlsx");
ExcelFile Mems(Dictionary<string,object> current,params IDictionary<string,object>[] history){
 var book=LegacyReportExport.Load(memsPath);new LegacyReportWriter(Images(false),Lookups("MEMS",current)).PopulatePm(book,"MEMS",current,history);return book;
}
var mems=Record("MEMS");mems["QCData"]="0,After|Before,After|1,2|3,4|5,6|7,8|9,10|11,12|13,14";
mems["MCStatus"]="EMGStop";mems["ddlAfterAR"]="Yes";
var initial=LegacyReportExport.Load(memsPath);var populated=Mems(mems);
Require(populated.Worksheets.ActiveWorksheet!.Cells["I8"].StringValue=="\u2714","BM category mark missing");
Require(populated.Worksheets.ActiveWorksheet!.Cells["K8"].Value?.ToString()==initial.Worksheets.ActiveWorksheet!.Cells["K8"].Value?.ToString(),"Unselected category was marked");
Require(populated.Worksheets.ActiveWorksheet!.Cells["AF22"].StringValue=="15"&&populated.Worksheets.ActiveWorksheet!.Cells["AF23"].StringValue=="30"&&populated.Worksheets.ActiveWorksheet!.Cells["AF25"].StringValue=="52","Legacy DateDiff/total calculation changed");
Require(populated.Worksheets.ActiveWorksheet!.Cells["G35"].Value?.ToString()==initial.Worksheets.ActiveWorksheet!.Cells["G35"].Value?.ToString(),"Missing history copied current repairer");
Require(populated.Worksheets.ActiveWorksheet!.Cells["M59"].StringValue=="---"&&populated.Worksheets.ActiveWorksheet!.Cells["AC61"].StringValue=="14","PM QC array/default mapping changed");
Require(populated.Worksheets.ActiveWorksheet!.Cells["I13"].Style.Font.UnderlineStyle==UnderlineStyle.Single&&populated.Worksheets.ActiveWorksheet!.Cells["AD15"].Style.FillPattern.PatternForegroundColor==SpreadsheetColor.FromName(ColorName.Red),"Conditional underline/emergency fill was lost");
Dictionary<string,object> History(string person)=>new(StringComparer.OrdinalIgnoreCase){["TimeOfRequest"]=date.AddDays(-1),["RepairedByEnd"]=person,["BMCase"]="CASE",["BMWhy4"]="CAUSE"};
var one=Mems(mems,History("FIRST"));var two=Mems(mems,History("FIRST"),History("SECOND"));
Require(one.Worksheets.ActiveWorksheet!.Cells["G35"].StringValue=="FIRST"&&one.Worksheets.ActiveWorksheet!.Cells["G36"].StringValue=="FIRST","One-record history differs from the original TOP 1/TOP 2 loops");
Require(two.Worksheets.ActiveWorksheet!.Cells["G35"].StringValue=="FIRST"&&two.Worksheets.ActiveWorksheet!.Cells["G36"].StringValue=="SECOND","Two-record history order changed");
Console.WriteLine("PASS: mutually exclusive category marks, QC arrays, conditional styles, computed minute totals, and 0/1/2 history records.");
// Missing descriptive lookup IDs should leave a blank cell; valid zero values are preserved.
var wbRecord=Record("WB"); wbRecord["BMStatusID"]="14"; wbRecord["AcredPMCategoryID"]="99999";
var wbLookups=Lookups("WB",wbRecord);
wbLookups.Remove("PMCategory|99999|PMCategory");
var wbTemplate=Path.Combine(root,"Templates","PmReport","WBTemplate.xlsx");
var missingLookupBook=LegacyReportExport.Load(wbTemplate);
new LegacyReportWriter(Images(false),wbLookups).PopulatePm(missingLookupBook,"WB",wbRecord,new List<IDictionary<string,object>>());
Require(missingLookupBook.Worksheets.ActiveWorksheet!.Cells["H65"].StringValue=="",
    "Missing category lookup wrote an incorrect zero in WB H65");
wbLookups["PMCategory|99999|PMCategory"]="0";
var realZeroBook=LegacyReportExport.Load(wbTemplate);
new LegacyReportWriter(Images(false),wbLookups).PopulatePm(realZeroBook,"WB",wbRecord,new List<IDictionary<string,object>>());
Require(realZeroBook.Worksheets.ActiveWorksheet!.Cells["H65"].StringValue=="0",
    "A genuine zero-valued lookup was incorrectly stripped");
var teNoLookup=Record("TE");
var teNoLookupBook=LegacyReportExport.Load(Path.Combine(root,"Templates","BmReport","TETemplate.xlsx"));
new LegacyReportWriter(Images(true),new Dictionary<string,string>(),new[]{"ONE","TWO","THREE"})
    .PopulateTe(teNoLookupBook,"FT",teNoLookup);
Require(teNoLookupBook.Worksheets.ActiveWorksheet!.Cells["K91"].StringValue=="",
    "Missing TE case lookup wrote an incorrect zero in K91");
Console.WriteLine("PASS: PM/TE missing lookup zeros removed, genuine zero lookup values preserved.");
foreach(var process in new[]{"FT","EDS"}){
 var profile=process=="EDS"?"TE_EDS":"TE";var data=Record(profile);data["WD1"]="DIARY-FIRST";data["WD10"]="DIARY-LAST";data["TESign"]="TE-SIGN";data["PDSign1"]="PD-SIGN";
 var book=LegacyReportExport.Load(Path.Combine(root,"Templates","BmReport",process=="EDS"?"EDS-CSPTemplate.xlsx":"TETemplate.xlsx"));
 new LegacyReportWriter(Images(true),Lookups(profile,data),new[]{"ONE","TWO","THREE"}).PopulateTe(book,process,data);
 Save(book,"TE-"+process,true);
 Require(book.Worksheets[1].Cells["B2"].Value?.ToString()==data["WD1"].ToString(),"Work diary sheet mapping changed");
 if(process=="FT") Require(book.Worksheets.ActiveWorksheet!.Cells["BE52"].StringValue=="TE-SIGN"&&book.Worksheets.ActiveWorksheet.Cells["BE53"].StringValue=="PD-SIGN","TE approval mapping changed");
}
EvidenceImageRow[] Group(int count)=>Enumerable.Range(0,count).Select(i=>new EvidenceImageRow{Description="NOTE-"+i,ImageFile=""}).ToArray();
var groups=new EvidenceImageGroups{ProblemPoint=Group(3),ActionTaken=Group(2),PartsReplaced=Group(2),DisAssembly=Group(2),QualityConfirm=Group(3)};
var header=new EvidenceMachineHeader{McNo="FIXTURE-MC",OccurrenceDate="10/01/2026",PmConfirmBy="FIXTURE"};
foreach(var process in processes){
 var config=ProcessTemplateMap.Get(process)!;var book=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking",config.TemplateFile));
 EvidenceMakingService.PopulateWorkbook(book,process,header,new TscConfirmation{Tsc1="No",Tsc6="N/A",RouteNo="SHOULD-NOT-PRINT",VersionNo="SHOULD-NOT-PRINT"},groups,Images(false),Images(false));
 Require(book.Worksheets.ActiveWorksheet!.Pictures.Count>=12,"Evidence placeholders were omitted");
 Require(book.Worksheets.ActiveWorksheet!.Cells[process=="SING"?"AF63":"AF62"].Value?.ToString()!="SHOULD-NOT-PRINT","Unconfirmed route printed");
 Save(book,"E-"+process,process=="SING");
}
var confirmed=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","EvidenceMaking_DC.xlsx"));
EvidenceMakingService.PopulateWorkbook(confirmed,"DC",header,new TscConfirmation{Tsc1="Yes",Tsc6="Yes",RouteNo="ROUTE",VersionNo="VERSION"},groups,Images(false),Images(false));
Require(confirmed.Worksheets.ActiveWorksheet!.Cells["S60"].StringValue=="\u2714"&&confirmed.Worksheets.ActiveWorksheet.Cells["AF62"].StringValue=="ROUTE"&&confirmed.Worksheets.ActiveWorksheet.Cells["AF41"].StringValue=="VERSION","Confirmed route/version or TSC mark lost");
var cwc=LegacyReportExport.Load(Path.Combine(root,"Templates","BmReport","CWC.xlsx"));
var teHeader=new TeEvidenceHeader{MCNo="FIXTURE-MC",TesterNo="FIXTURE-TE",StartOfRepair="10/01/2026 10:20:00",EndOfRepair="10/01/2026 10:50:00",TEAssign="TE1",Ideas="IDEA",TelekoCheckingData="1,2,3,4|5,6,7,8|9,10,11,12|13,14,15,16|17,18,19,20"};
TeEvidenceMakingService.PopulateWorkbook(cwc,teHeader,new(),new(){new(){Category="TEST BOARD",Condition="BEFORE",CN="CN3",ImgFile=""},new(){Category="TEST BOARD",Condition="AFTER",CN="CN1",ImgFile=""}},Enumerable.Range(0,4).Select(_=>new TelekoEvidenceImage()).ToList(),new(),Images(true),Images(true));
Require(cwc.Worksheets[0].Cells["G76"].StringValue=="20","TE QC array mapping failed");
Require(cwc.Worksheets[0].Cells["M11"].StringValue=="10:20:00 -AM","TE repair time format differs from legacy");
var pictures=cwc.Worksheets[0].Pictures;
Require(pictures.Any(p=>p.Position!.From!.Row.Index==48&&p.Position!.From!.Column.Index==1)&&pictures.Any(p=>p.Position!.From!.Row.Index==44&&p.Position!.From!.Column.Index==9),"CN3-before/CN1-after was mapped by list position instead of connector code");
foreach(var column in new[]{7,9,12,14}) Require(pictures.Any(p=>p.Position!.From!.Row.Index==69&&p.Position!.From!.Column.Index==column&&Math.Abs(p.Position!.GetWidth(LengthUnit.Inch)-1.54)<0.01&&Math.Abs(p.Position!.GetHeight(LengthUnit.Inch)-1.4)<0.01),"Teleko anchor/dimensions changed");
Save(cwc,"TE-EVIDENCE",true);
try{TeEvidenceMakingService.PopulateWorkbook(cwc,teHeader,new(),new(){new(){Category="TEST BOARD",CN="CN5"}},new(),new(),Images(true),Images(true));throw new Exception("Missing connector sheet was ignored");}
catch(InvalidDataException ex)when(ex.Message.Contains("second worksheet")){}
try{TeEvidenceMakingService.PopulateWorkbook(cwc,teHeader,new(),new(),new(),new(){new(){Category="BEFORE QUALITY CONDITION"}},Images(true),Images(true));throw new Exception("Missing quality sheet was ignored");}
catch(InvalidDataException ex)when(ex.Message.Contains("third worksheet")){}
Console.WriteLine("PASS: TE QC/time fields, connector/Teleko placement and explicit missing-sheet errors.");
Console.WriteLine("Report artifacts: "+output);
'@ | Set-Content -Encoding UTF8 (Join-Path $directory 'Program.cs')
& $Dotnet build (Join-Path $directory 'Checks.csproj') --nologo -v minimal -r win-x64 --self-contained false --property:UseAppHost=false -m:1 --nodeReuse:false --property:NuGetAudit=false --property:UseSharedCompilation=false
if($LASTEXITCODE -ne 0){throw 'Report fixture build failed.'}
& $Dotnet (Join-Path $directory 'bin/Debug/net8.0/win-x64/Checks.dll') $repo
if($LASTEXITCODE -ne 0){throw 'Report generation checks failed.'}
