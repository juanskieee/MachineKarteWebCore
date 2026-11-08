param([string]$Dotnet = 'dotnet', [switch]$DownloadOnly, [switch]$CwcOnly, [switch]$EvidenceOnly, [switch]$PhotoBenchmark, [switch]$PerformanceOnly)
# Offline: real templates/images and compiled legacy mappings. DownloadOnly hosts
# a loopback file-response fixture; no appsettings, SQL, or source-template writes.
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

CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
var root=args[0]; var output=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"artifacts")); Directory.CreateDirectory(output);
void Require(bool condition,string message){if(!condition)throw new Exception(message);}
string Images(bool te)=>Path.Combine(root,"Assets","images",te?"TEeMaintenanceSystems_images":"eMaintenanceSystems_images");

if(args.Contains("performance-only")) {
 LegacyReportExport.NormalizeFormat("xlsx");
 var add=typeof(LegacyReportWriter).GetMethod("AddEvidencePicture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
 using var bitmap=new SkiaSharp.SKBitmap(2400,1600);
 using(var canvas=new SkiaSharp.SKCanvas(bitmap)) {
  canvas.Clear(SkiaSharp.SKColors.Blue);
  using var paint=new SkiaSharp.SKPaint { Color=SkiaSharp.SKColors.Red };
  canvas.DrawRect(0,0,1200,800,paint);
 }
 using var image=SkiaSharp.SKImage.FromBitmap(bitmap);
 using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,90);
 var jpeg=encoded.ToArray();
 foreach(var origin in Enumerable.Range(1,8)) {
  var exif=Convert.FromHexString("45786966000049492A0008000000010012010300010000000000000000000000");
  exif[24]=(byte)origin;
  var source=Path.Combine(output,$"orientation-{origin}.jpg");
  using(var bytes=new MemoryStream()) {
   bytes.Write(jpeg,0,2); bytes.Write(new byte[]{255,225,0,(byte)(exif.Length+2)}); bytes.Write(exif); bytes.Write(jpeg,2,jpeg.Length-2);
   File.WriteAllBytes(source,bytes.ToArray());
  }
  var book=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","EvidenceMaking_DC.xlsx"));
  var sheet=book.Worksheets[0];
  var original=sheet.Pictures.Add(source,"C11");
  var ratio=original.Position!.Width/original.Position.Height;
  sheet.Pictures.Remove(original);
  var clock=System.Diagnostics.Stopwatch.StartNew();
  var picture=(ExcelPicture)add.Invoke(new LegacyReportWriter(Images(false),new Dictionary<string,string>()),
   new object[]{sheet,source,"C11",2.0,1.6,LengthUnit.Inch})!;
  using var content=new MemoryStream(); picture.PictureStream.Position=0; picture.PictureStream.CopyTo(content);
  using var codec=SkiaSharp.SKCodec.Create(new SkiaSharp.SKMemoryStream(content.ToArray()));
  Require(Math.Abs(picture.Position!.Width/picture.Position.Height-ratio)<0.001,"Photo aspect ratio changed for EXIF "+origin);
  Require(codec.Info.Width<2400 && codec.Info.Height<1600 && content.Length<jpeg.Length/2,"EXIF photo kept its oversized original: "+origin);
  Require((int)codec.EncodedOrigin==origin,"Resizing lost EXIF orientation "+origin);
  using(var pixels=SkiaSharp.SKBitmap.Decode(content.ToArray()))
   Require(pixels.GetPixel(pixels.Width/4,pixels.Height/4).Red>200 && pixels.GetPixel(3*pixels.Width/4,3*pixels.Height/4).Blue>200,"Resizing changed raw pixel orientation "+origin);
  Console.WriteLine($"MEASURE: EXIF {origin}: population={clock.Elapsed.TotalMilliseconds:F1}ms, original={new FileInfo(source).Length:N0} bytes, embedded={content.Length:N0} bytes, pixels={codec.Info.Width}x{codec.Info.Height}, origin={codec.EncodedOrigin}, sheet ratio={ratio:F3}.");
  foreach(var format in new[]{"xlsx","pdf"}) {
   var bytes=LegacyReportExport.Save(book,format);
   Require(format=="pdf"?Encoding.ASCII.GetString(bytes,0,5)=="%PDF-":bytes[0]=='P'&&bytes[1]=='K',"Oriented photo export failed");
  }
 }
 Console.WriteLine("PASS: all EXIF orientations retain sheet aspect ratio and export both formats.");
}

if(args.Contains("cwc-only")) {
 var source=Path.Combine(root,"Templates","EvidenceMaking","CWC.xlsx");
 var book=LegacyReportExport.Load(source);
 TeEvidenceMakingService.PopulateWorkbook(book,new TeEvidenceHeader { TestNoNG="TESTER PROBLEM" },new(),new(),new(),new(),Images(true),Images(true));
 void CheckCwc(ExcelFile report) {
  var sheet=report.Worksheets["FORMAT"];
  Require(!sheet.Shapes.Any(shape=>shape.Text.ToString().Contains("T.E EVIDENCE CONFIRMATION",StringComparison.OrdinalIgnoreCase)),"CWC title text box remains");
  Require(sheet.Cells["B5"].StringValue=="TESTER PROBLEM","CWC Request Content must use legacy TestNoNG");
  Require(sheet.Cells["Q10"].Value is null,"Empty TTL Stoppage was populated");
  var rule=sheet.ConditionalFormatting.Single();
  Require(rule.AppliesTo.Contains("M10:P11") && !rule.AppliesTo.Contains("M10:Q11") &&
   !rule.AppliesTo.Contains("M14:Q15") && !rule.AppliesTo.Contains("M6:Q8"),"Optional CWC fields still inherit pink blank formatting");
 }
 CheckCwc(book);
 using(var stream=new MemoryStream(LegacyReportExport.Save(book,"xlsx"))) CheckCwc(ExcelFile.Load(stream));
 var pdf=LegacyReportExport.Save(book,"pdf");
 Require(Encoding.ASCII.GetString(pdf,0,5)=="%PDF-","CWC PDF export failed");
 var empty=LegacyReportExport.Load(source);
 TeEvidenceMakingService.PopulateWorkbook(empty,new(),new(),new(),new(),new(),Images(true),Images(true));
 Require(empty.Worksheets["FORMAT"].Cells["B5"].StringValue=="","Empty TestNoNG gained fabricated text");
 Require(Regex.Matches(Encoding.Latin1.GetString(pdf),@"/Type\s*/Page\b").Count==1,"CWC PDF includes supplemental sheets");
 Console.WriteLine("PASS: CWC title removal, legacy TestNoNG, empty optional fields, retained required formatting, XLSX round-trip and single-page PDF export.");
 return;
}

if(args.Contains("download-only")) {
 LegacyReportExport.NormalizeFormat("xlsx"); // Initialize the production export license.
 var book=new ExcelFile();book.Worksheets.Add("Report").Cells["A1"].Value="Local download fixture";
 var controller=new ReportResponseProbe { ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext {
  HttpContext=new Microsoft.AspNetCore.Http.DefaultHttpContext() } };
 foreach(var format in new[]{"xlsx","pdf"}) {
  var bytes=LegacyReportExport.Save(book,format);
  Require(format=="pdf" ? Encoding.ASCII.GetString(bytes,0,5)=="%PDF-" : bytes[0]=='P' && bytes[1]=='K',"Invalid exported file bytes");
  foreach(var stem in new[]{"DC_123","DCEvidence_123","TE_123","TE_Evidence_123"}) {
   var response=controller.Generate(bytes,stem+"."+format);
   Require(response.FileContents.SequenceEqual(bytes) && response.FileDownloadName==stem+"."+format &&
    response.ContentType==LegacyReportExport.ContentType(format),"Direct download changed bytes, filename or MIME type");
   Require(controller.Response.Headers.CacheControl=="no-store","Generated downloads may be cached");
  }
 }
 // Exercise the real MVC file executor and HTTP body, not just FileContentResult properties.
 var expectedBodies=new[]{"xlsx","pdf"}.ToDictionary(format=>format,format=>LegacyReportExport.Save(book,format));
 var builder=WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName="Development", ContentRootPath=output });
 builder.Logging.ClearProviders();
 builder.WebHost.UseUrls("http://127.0.0.1:0");
 builder.Services.AddControllers();
 await using var app=builder.Build();
 app.MapMethods("/report",new[]{"GET","POST"},async (Microsoft.AspNetCore.Http.HttpContext context)=>{
  var format=context.Request.Query["format"].ToString();
  var name=context.Request.Query["name"].ToString()+"."+format;
  var probe=new ReportResponseProbe { ControllerContext=new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext=context } };
  var result=probe.Generate(expectedBodies[format],name);
  await result.ExecuteResultAsync(new Microsoft.AspNetCore.Mvc.ActionContext(context,new(),new()));
 });
 await app.StartAsync();
 using var http=new HttpClient(new HttpClientHandler { UseProxy=false });
 foreach(var format in new[]{"xlsx","pdf"}) foreach(var stem in new[]{"DC_123","DCEvidence_123","TE_123","TE_Evidence_123"}) {
  using var response=await http.PostAsync(app.Urls.Single()+"/report?format="+format+"&name="+stem,null);
  var body=await response.Content.ReadAsByteArrayAsync();
  Require(response.IsSuccessStatusCode && body.SequenceEqual(expectedBodies[format]),"HTTP transfer changed report bytes");
  Require(response.Content.Headers.ContentLength==body.Length && response.Content.Headers.ContentType?.MediaType==LegacyReportExport.ContentType(format),"HTTP length or MIME type changed");
  var disposition=response.Content.Headers.ContentDisposition;
  Require(disposition?.FileNameStar==stem+"."+format && response.Headers.CacheControl?.NoStore==true,"HTTP filename or no-store header changed");
 }
 await app.StopAsync();
 Require(typeof(LegacyReportExport).GetMethod("SaveReportAsync") is null && typeof(LegacyReportExport).GetMethod("ReportPath") is null,
  "Server output persistence was not removed");
 Console.WriteLine("PASS: real in-memory XLSX/PDF bytes, all eight HTTP response bodies/lengths/filenames/types, no-store responses, and removed server output persistence.");
 return;
}

// Form4 and Form10 resolve the SQL image path under distinct legacy shares.
// Exercise this offline against temporary share-shaped directories, never a server.
var pmLegacyShare=Path.Combine(output,"legacy-PM-share");
var teLegacyShare=Path.Combine(output,"legacy-TE-share");
var pmUpload=Path.Combine(pmLegacyShare,"Upload","2026");
var teUpload=Path.Combine(teLegacyShare,"Upload","2026");
Directory.CreateDirectory(pmUpload); Directory.CreateDirectory(teUpload);
var pmImage=Path.Combine(pmUpload,"photo.png");
var teImage=Path.Combine(teUpload,"photo.png");
File.Copy(Path.Combine(Images(false),"A.png"),pmImage);
File.Copy(Path.Combine(Images(true),"B.png"),teImage);
var sharedArchive=Path.Combine(output,"legacy-shared","Upload","Evidence");
Directory.CreateDirectory(Path.Combine(sharedArchive,"2020"));
var archivedImage=Path.Combine(sharedArchive,"2020","photo.png");
File.Copy(Path.Combine(Images(false),"C.png"),archivedImage);
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
foreach(var te in new[]{false,true}) {
 var share=te?teLegacyShare:pmLegacyShare;
 var placeholder=te?tePlaceholder:pmPlaceholder;
 var archivedResult=LegacyReportExport.ResolveEvidenceImage(@"\Upload\Evidence\2020\photo.png",share,Images(te),te,sharedArchive);
 Require(archivedResult==Path.GetFullPath(archivedImage),
     $"Shared historical evidence was not resolved for PM/TE: expected {archivedImage}, got {archivedResult}");
 Require(LegacyReportExport.ResolveEvidenceImage(archivedImage,share,Images(te),te,sharedArchive)==archivedImage,
     "An absolute archive path inside an allowed root was rejected");
 foreach(var invalid in new[]{@"\Upload\Evidence\..\2020\photo.png",@"\Upload\Evidence\2020\photo.png:stream",@"\\other-server\Upload\Evidence\2020\photo.png"})
  Require(LegacyReportExport.ResolveEvidenceImage(invalid,share,Images(te),te,sharedArchive)==placeholder,
      "Shared evidence accepted traversal, alternate streams or another file server");
}
Require(LegacyReportExport.ResolveEvidenceImage(pmImage,pmLegacyShare,Images(false),sharedEvidenceRoot:sharedArchive)==pmImage &&
        LegacyReportExport.ResolveEvidenceImage(teImage,teLegacyShare,Images(true),te:true,sharedEvidenceRoot:sharedArchive)==teImage,
    "Absolute upload paths inside the workflow's own root were rejected");
Require(LegacyReportExport.ResolveEvidenceImage(teImage,pmLegacyShare,Images(false),sharedEvidenceRoot:sharedArchive)==pmPlaceholder,
    "PM evidence must not read a TE photo with the same filename");
var collision=Path.Combine(pmLegacyShare,"Upload","Evidence","2020","photo.png");
Directory.CreateDirectory(Path.GetDirectoryName(collision)!); File.Copy(pmImage,collision);
Require(LegacyReportExport.ResolveEvidenceImage(@"\Upload\Evidence\2020\photo.png",pmLegacyShare,Images(false),sharedEvidenceRoot:sharedArchive)==collision &&
        LegacyReportExport.ResolveEvidenceImage(archivedImage,pmLegacyShare,Images(false),sharedEvidenceRoot:sharedArchive)==archivedImage,
    "Share-relative paths must prefer their original system share; explicit archive paths must stay in the archive");
File.Copy(archivedImage,Path.Combine(sharedArchive,"archive-only.png"));
Require(LegacyReportExport.ResolveEvidenceImage(@"\Upload\2026\archive-only.png",pmLegacyShare,Images(false),sharedEvidenceRoot:sharedArchive)==pmPlaceholder,
    "Evidence must not guess another photo by filename alone");
var serverConfig=new Microsoft.Extensions.Configuration.ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string,string?> { ["NetworkShare:Server"]="test-file-server" }).Build();
Require(LegacyReportExport.LegacyEvidenceRoot(serverConfig,false)==@"\\test-file-server\eMaintenanceSystem" &&
        LegacyReportExport.LegacyEvidenceRoot(serverConfig,true)==@"\\test-file-server\TEeMaintenanceSystem" &&
        LegacyReportExport.SharedEvidenceRoot(serverConfig)==@"\\test-file-server\Upload\Evidence",
    "Web server path mapping diverged from Form4/Form10");
Console.WriteLine("PASS: PM/TE and shared archive roots, absolute/relative paths, collisions, Form10 tilde, placeholders and path boundaries.");

var cacheTemplate=Path.Combine(output,"cache-template.xlsx");
var cacheSource=new ExcelFile();cacheSource.Worksheets.Add("Cache").Cells["A1"].Value="original";cacheSource.Save(cacheTemplate);
var firstClone=LegacyReportExport.Load(cacheTemplate);firstClone.Worksheets[0].Cells["A1"].Value="private";
Require(LegacyReportExport.Load(cacheTemplate).Worksheets[0].Cells["A1"].StringValue=="original","Template cache leaked another export's values");
cacheSource.Worksheets[0].Cells["A1"].Value="updated";cacheSource.Save(cacheTemplate);File.SetLastWriteTimeUtc(cacheTemplate,DateTime.UtcNow.AddSeconds(2));
Require(LegacyReportExport.Load(cacheTemplate).Worksheets[0].Cells["A1"].StringValue=="updated","Template cache ignored an updated template");
var timings=new List<double[]>();
var benchmarkTemplate=Path.Combine(root,"Templates","EvidenceMaking","CWC.xlsx");
LegacyReportExport.Load(benchmarkTemplate);
for(var i=0;i<5;i++) {
 var clock=System.Diagnostics.Stopwatch.StartNew();ExcelFile.Load(benchmarkTemplate);var uncached=clock.Elapsed.TotalMilliseconds;
 clock.Restart();LegacyReportExport.Load(benchmarkTemplate);timings.Add(new[]{uncached,clock.Elapsed.TotalMilliseconds});
}
Console.WriteLine($"MEASURE: CWC template median load={timings.Select(t=>t[0]).Order().ElementAt(2):F1}ms; cached clone={timings.Select(t=>t[1]).Order().ElementAt(2):F1}ms (local fixture, not production network timing).");

// Staging deduplicates paths while preserving root precedence, bytes, placeholders and cleanup.
string stagedPath;
using(var photos=await LegacyReportExport.StagedPhotos.LoadAsync(new[]{@"\Upload\2026\photo.png",pmImage,@"\Upload\2026\missing.png",@"\Upload\Evidence\2020\photo.png"},pmLegacyShare,Images(false),false,sharedArchive)) {
 stagedPath=photos.Paths[pmImage];
 Require(stagedPath==photos.Paths[@"\Upload\2026\photo.png"] && File.ReadAllBytes(stagedPath).SequenceEqual(File.ReadAllBytes(pmImage)),"Staging duplicated a source or changed photo bytes");
 Require(photos.Paths[@"\Upload\2026\missing.png"]==pmPlaceholder && File.ReadAllBytes(photos.Paths[@"\Upload\Evidence\2020\photo.png"]).SequenceEqual(File.ReadAllBytes(collision)),"Staging changed placeholder or root precedence");
}
Require(!File.Exists(stagedPath),"Export photo staging was not cleaned up");
Console.WriteLine("PASS: independent template clones, invalidation, staged photo bytes, deduplication, root precedence and cleanup.");
if(args.Contains("performance-only")) return;

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
 record["PkgName"]="QFN-48";record["DeviceName"]="DEVICE-ALPHA";record["LOTNo"]="LOT-20261103-A";
 for(var part=1;part<=5;part++) {
  if(record.ContainsKey("ChangePartsName"+part)) {
   record["ChangePartsName"+part]="";record["ChangePartsQty"+part]="0.00";
   record["ChangePartsType"+part]="0";record["ChangePartsNewReworked"+part]="0";
  }
 }
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
 string Positions(ExcelFile file)=>string.Join("|",file.Worksheets.SelectMany(sheet=>sheet.Pictures.Select(p=>
  $"{sheet.Name}:{Math.Round(p.Position!.Left,2)}:{Math.Round(p.Position.Top,2)}:{Math.Round(p.Position.Width,2)}:{Math.Round(p.Position.Height,2)}:{p.Position.Mode}")));
 var before=Positions(book);
 var geometry=new List<string>{"Sheet,Anchor,MergedRange,WidthPoints,HeightPoints,AvailableWidthPoints,AvailableHeightPoints,Overflow"};
 foreach(var sheet in book.Worksheets) foreach(var picture in sheet.Pictures) {
  var position=picture.Position!; var from=position.From!;
  var range=sheet.Cells[from.Row.Index,from.Column.Index].MergedRange;
  if(range is null)continue;
  var width=Enumerable.Range(from.Column.Index,range.LastColumnIndex-from.Column.Index+1).Sum(c=>sheet.Columns[c].GetWidth(LengthUnit.Point));
  var height=Enumerable.Range(from.Row.Index,range.LastRowIndex-from.Row.Index+1).Sum(r=>sheet.Rows[r].GetHeight(LengthUnit.Point));
  var overflow=position.Width>width+0.75||position.Height>height+0.75;
  if(name.StartsWith("E-")||name=="TE-EVIDENCE") Require(!overflow,name+" photo exceeds merged placeholder "+range.Name);
  geometry.Add($"\"{sheet.Name}\",{sheet.Cells[from.Row.Index,from.Column.Index].Name},{range.Name},{position.Width},{position.Height},{width},{height},{overflow}");
 }
 File.WriteAllLines(Path.Combine(output,name+"-images.csv"),geometry);
 var clock=System.Diagnostics.Stopwatch.StartNew();
 var xlsx=LegacyReportExport.Save(book,"xlsx");var xlsxMs=clock.Elapsed.TotalMilliseconds;
 clock.Restart();var pdf=LegacyReportExport.Save(book,"pdf");var pdfMs=clock.Elapsed.TotalMilliseconds;
 Require(Positions(book)==before,name+" PDF serialization changed picture geometry");
 Require(Positions(ExcelFile.Load(new MemoryStream(xlsx)))==before,name+" XLSX round trip changed picture geometry");
 Require(xlsx[0]=='P'&&xlsx[1]=='K',name+" is not XLSX");Require(Encoding.ASCII.GetString(pdf,0,5)=="%PDF-",name+" is not real PDF");
 if(name.StartsWith("TE-")) Require(Regex.Matches(Encoding.Latin1.GetString(pdf),@"/Type\s*/Page\b").Count==1,
     name+" PDF includes supplemental worksheets instead of the legacy main page");
 using(var zip=new ZipArchive(new MemoryStream(xlsx)))Require(zip.GetEntry("xl/styles.xml")!=null,name+" lost workbook styles");
 File.WriteAllBytes(Path.Combine(output,name+".xlsx"),xlsx);File.WriteAllBytes(Path.Combine(output,name+".pdf"),pdf);
 if(preview) book.Save(Path.Combine(output,name+".png"),new ImageSaveOptions{Format=ImageSaveFormat.Png,PageNumber=0});
 Console.WriteLine($"PASS: {name} real XLSX/PDF ({xlsx.Length}/{pdf.Length} bytes), sheets={book.Worksheets.Count}.");
 Console.WriteLine($"MEASURE: {name} serialization XLSX={xlsxMs:F1}ms, PDF={pdfMs:F1}ms (local fixture).");
}
bool ContainsPhoto(ExcelFile book,string photo){
 var expected=File.ReadAllBytes(photo);
 return book.Worksheets.SelectMany(sheet=>sheet.Pictures).Any(picture=>{
  var stream=picture.PictureStream;var previous=stream.Position;stream.Position=0;
  using var bytes=new MemoryStream(); stream.CopyTo(bytes);stream.Position=previous;
  return bytes.ToArray().SequenceEqual(expected);
 });
}
void PhotoAt(ExcelFile book,string anchor,string photo){
 var expected=File.ReadAllBytes(photo);var cell=book.Worksheets.ActiveWorksheet!.Cells[anchor];
 var picture=book.Worksheets.ActiveWorksheet.Pictures.Single(p=>p.Position!.From!.Row.Index==cell.Row.Index&&p.Position.From.Column.Index==cell.Column.Index);
 var stream=picture.PictureStream;var previous=stream.Position;stream.Position=0;
 using var bytes=new MemoryStream();stream.CopyTo(bytes);stream.Position=previous;
 Require(bytes.ToArray().SequenceEqual(expected),"Wrong source photograph at "+anchor);
}
var processes=new[]{"MEMS","WB","DB","DC","MP","PL","FT","SING","FL","TP","EDS"};
if(!args.Contains("evidence-only")) {
foreach(var process in processes){
 Require(PmReportRepository.TryGetDefinition(process,out var definition),"Missing PM definition");
 var book=LegacyReportExport.Load(Path.Combine(root,"Templates",definition.TemplateFolder,definition.TemplateFile));
 book.Worksheets.ActiveWorksheet=book.Worksheets[definition.WorksheetName];
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
var memsProduct=Record("MEMS");memsProduct["ProdTreatLotNo"]="INSPECT-LOT";memsProduct["ProdTreatPackage"]="QFN-64";memsProduct["ProdTreatInspResult"]="PASS-INSPECTION";
memsProduct["ProdTreatUsageOfDummy"]="Historical dummy usage";memsProduct["ProdTreatDummyIn"]="11";memsProduct["ProdTreatDummyOut"]="12";
memsProduct["UseDummyFrames"]="";memsProduct["DummyIn"]="";memsProduct["DummyOut"]="";
var memsProductBook=Mems(memsProduct);
Require(memsProductBook.Worksheets[0].Cells["AA27"].StringValue=="INSPECT-LOT"&&memsProductBook.Worksheets[0].Cells["AK27"].StringValue=="QFN-64"&&memsProductBook.Worksheets[0].Cells["AC28"].StringValue=="PASS-INSPECTION"&&memsProductBook.Worksheets[0].Cells["AF27"].StringValue=="Package / Device",
    "MEMS product-treatment fields overwrite headers or each other");
Require(memsProductBook.Worksheets[0].Cells["AD33"].StringValue=="Historical dummy usage"&&memsProductBook.Worksheets[0].Cells["AH33"].StringValue=="11"&&memsProductBook.Worksheets[0].Cells["AN33"].StringValue=="12",
    "Blank new MEMS aliases erased historical dummy data");
var mpData=Record("MP");mpData["LotNoForInspection"]="INSPECT-MP";mpData["PmRelatedBM"]="Yes";mpData["Treatment9"]="Treatment nine";
var mpBook=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","MPTemplate.xlsx"));
new LegacyReportWriter(Images(false),Lookups("MP",mpData)).PopulatePm(mpBook,"MP",mpData,new List<IDictionary<string,object>>());
Require(mpBook.Worksheets[0].Cells["AL36"].StringValue=="Yes"&&mpBook.Worksheets[0].Cells["G55"].StringValue=="INSPECT-MP"&&mpBook.Worksheets[0].Cells["C50"].StringValue=="Treatment nine"&&mpBook.Worksheets[0].Cells["AB36"].StringValue=="Pm Related BM",
    "MP inspection lot or related-BM answer overwrote another section");
var edsOverflow=Record("EDS");edsOverflow["Treatment13"]="Must not disappear";
try {
 var book=LegacyReportExport.Load(Path.Combine(root,"Templates","PmReport","EDSTemplate.xlsx"));
 new LegacyReportWriter(Images(false),Lookups("EDS",edsOverflow)).PopulatePm(book,"EDS",edsOverflow,new List<IDictionary<string,object>>());
 throw new Exception("EDS treatment 13 silently overwrote the Turnover To section");
} catch(InvalidDataException ex)when(ex.Message.Contains("twelve treatment rows")){}
// Missing descriptive lookup IDs should leave a blank cell; valid zero values are preserved.
var wbRecord=Record("WB"); wbRecord["BMStatusID"]="14"; wbRecord["AcredPMCategoryID"]="99999";
var wbLookups=Lookups("WB",wbRecord);
wbLookups.Remove("PMCategory|99999|PMCategory");
var wbTemplate=Path.Combine(root,"Templates","EvidenceMaking","WBTemplate.xlsx");
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
foreach(var process in processes) {
 var data=Record(process);PmReportRepository.TryGetDefinition(process,out var definition);
 var book=LegacyReportExport.Load(Path.Combine(root,"Templates",definition.TemplateFolder,definition.TemplateFile));
 book.Worksheets.ActiveWorksheet=book.Worksheets[definition.WorksheetName];
 new LegacyReportWriter(Images(false),Lookups(process,data)).PopulatePm(book,process,data,new List<IDictionary<string,object>>());
 Require(!book.Worksheets.ActiveWorksheet.GetUsedCellRange(true)!.Any(cell=>cell.Value?.ToString()=="0.00"),process+" leaked an empty parts-row quantity");
}
var plData=Record("PL");plData["OtherUnitCheck"]="1,11,12,13|1,21,22,23|1,31,32,33";
plData["GLConfirmOPReqBy"]="GL-123";plData["ReqToOtherSec"]="Yes";
var pl=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","PLTemplate.xlsx"));
new LegacyReportWriter(Images(false),Lookups("PL",plData)).PopulatePm(pl,"PL",plData,new List<IDictionary<string,object>>());
Require(pl.Worksheets.ActiveWorksheet!.Cells["AX71"].StringValue=="32"&&pl.Worksheets.ActiveWorksheet.Cells["AZ71"].StringValue=="33",
    "PL row-three NG and Action Done values share a destination");
Require(pl.Worksheets[0].Cells["BD27"].StringValue=="GL-123"&&pl.Worksheets[0].Cells["AU56"].StringValue=="Yes",
    "PL other-section request overwrote GL Confirmation");
var partsData=Record("WB");partsData["ChangePartsName1"]="Drive belt";partsData["ChangePartsQty1"]="0";
var parts=LegacyReportExport.Load(wbTemplate);new LegacyReportWriter(Images(false),Lookups("WB",partsData)).PopulatePm(parts,"WB",partsData,new List<IDictionary<string,object>>());
Require(parts.Worksheets.ActiveWorksheet!.Cells["BB51"].StringValue=="0"&&parts.Worksheets.ActiveWorksheet.Cells["BB52"].StringValue=="",
    "Real zero quantity was removed or an empty parts row leaked a default");
Console.WriteLine("PASS: empty part-row defaults, real zero quantities and PL NG/Action Done separation.");
}
foreach(var process in new[]{"FL","FT","MEMS","EDS"}){
 var profile=process=="EDS"?"TE_EDS":"TE";var data=Record(profile);data["WD1"]="DIARY-FIRST";data["WD10"]="DIARY-LAST";data["TESign"]="TE-SIGN";data["PDSign1"]="PD-SIGN";
 var book=LegacyReportExport.Load(Path.Combine(root,"Templates","BmReport",process=="EDS"?"EDS-CSPTemplate.xlsx":"TETemplate.xlsx"));
 book.Worksheets.ActiveWorksheet=book.Worksheets[1]; // Writer must select the main sheet regardless of the saved tab.
 new LegacyReportWriter(Images(true),Lookups(profile,data),new[]{"ONE","TWO","THREE"}).PopulateTe(book,process,data);
 if(process=="EDS")Require(book.Worksheets[0].Cells["BK59"].Value is null&&book.Worksheets[0].Cells["BL59"].Value is null,
     "EDS developer row indices/code leaked into the report");
 Save(book,"TE-"+process,true);
 Require(book.Worksheets[1].Cells["B2"].Value?.ToString()==data["WD1"].ToString(),"Work diary sheet mapping changed");
 if(process=="FT") Require(book.Worksheets.ActiveWorksheet!.Cells["BE52"].StringValue=="TE-SIGN"&&book.Worksheets.ActiveWorksheet.Cells["BE53"].StringValue=="PD-SIGN","TE approval mapping changed");
}
var photoSources=new[]{"A.png","B.png","C.png","PBox2.png","FBox2.png","PFNBox2.png","ABox.png","BBox.png","CBox.png","NBox.png","68e9d436-c7ac-4152-b771-1e2c2bc005d2.jpg","ROHM_logo.jpg"};
var photoNumber=0;
EvidenceImageRow[] Group(int count)=>Enumerable.Range(0,count).Select(i=>{
 var number=photoNumber++;var file=number+Path.GetExtension(photoSources[number]);
 File.Copy(Path.Combine(Images(false),photoSources[number]),Path.Combine(pmUpload,file));
 return new EvidenceImageRow{Description="Evidence slot "+number+": alignment and inspection",ImageFile=@"\Upload\2026\"+file};
}).ToArray();
var groups=new EvidenceImageGroups{ProblemPoint=Group(3),ActionTaken=Group(2),PartsReplaced=Group(2),DisAssembly=Group(2),QualityConfirm=Group(3)};
using var pmPhotos=await LegacyReportExport.StagedPhotos.LoadAsync(new[]{groups.ProblemPoint,groups.ActionTaken,groups.PartsReplaced,groups.DisAssembly,groups.QualityConfirm}.SelectMany(group=>group).Select(row=>row.ImageFile),pmLegacyShare,Images(false),false,sharedArchive);
var header=new EvidenceMachineHeader{McNo="FIXTURE-MC",OccurrenceDate="10/01/2026",PmConfirmBy="FIXTURE"};
foreach(var process in processes){
 var config=ProcessTemplateMap.Get(process)!;var book=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking",config.TemplateFile));
 EvidenceMakingService.PopulateWorkbook(book,process,header,new TscConfirmation{Tsc1="No",Tsc6="N/A",RouteNo="SHOULD-NOT-PRINT",VersionNo="SHOULD-NOT-PRINT"},groups,Images(false),pmLegacyShare,sharedArchive,pmPhotos.Paths);
 var anchors=process=="SING"?new[]{"C11","O11","Z11","C27","L27","V27","AD27","C47","T47","C69","O69","AA69"}:new[]{"C11","P11","AC11","C26","L26","V26","AD26","C46","T46","C68","O68","AA68"};
 var rowImages=new[]{groups.ProblemPoint,groups.ActionTaken,groups.PartsReplaced,groups.DisAssembly,groups.QualityConfirm}
  .SelectMany(group=>group).ToArray();
 for(var slot=0;slot<anchors.Length;slot++)PhotoAt(book,anchors[slot],LegacyReportExport.ResolveEvidenceImage(rowImages[slot].ImageFile,pmLegacyShare,Images(false),sharedEvidenceRoot:sharedArchive));
 Require(book.Worksheets[0].Cells[process=="SING"?"Z19":"AD21"].StringValue==groups.ProblemPoint[2].Description,
     "Third problem/safety photograph lost its description");
 Require(book.Worksheets.ActiveWorksheet!.Cells[process=="SING"?"AF63":"AF62"].Value?.ToString()!="SHOULD-NOT-PRINT","Unconfirmed route printed");
 Save(book,"E-"+process,process=="SING");
}
var confirmed=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","EvidenceMaking_DC.xlsx"));
EvidenceMakingService.PopulateWorkbook(confirmed,"DC",header,new TscConfirmation{Tsc1="Yes",Tsc6="Yes",RouteNo="ROUTE",VersionNo="VERSION"},groups,Images(false),Images(false));
Require(confirmed.Worksheets.ActiveWorksheet!.Cells["S60"].StringValue=="\u2714"&&confirmed.Worksheets.ActiveWorksheet.Cells["AF62"].StringValue=="ROUTE"&&confirmed.Worksheets.ActiveWorksheet.Cells["AF41"].StringValue=="VERSION","Confirmed route/version or TSC mark lost");
groups.ProblemPoint[0].ImageFile=@"\Upload\2026\photo.png";
groups.ActionTaken[0].ImageFile=archivedImage;
var uploadedEvidence=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","EvidenceMaking_DC.xlsx"));
EvidenceMakingService.PopulateWorkbook(uploadedEvidence,"DC",header,new(),groups,Images(false),pmLegacyShare,sharedArchive);
Require(ContainsPhoto(uploadedEvidence,pmImage)&&ContainsPhoto(uploadedEvidence,archivedImage),
    "Form4 workbook did not embed the actual PM and historical archive photos");
Save(uploadedEvidence,"E-UPLOADED-PHOTOS");
var cwc=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","CWC.xlsx"));
Require(cwc.Worksheets.Select(sheet=>sheet.Name).SequenceEqual(new[]{"FORMAT","TESTBOARD","DATASHEET"}),"The wrong CWC workbook version is selected");
var teHeader=new TeEvidenceHeader{MCNo="FIXTURE-MC",TesterNo="FIXTURE-TE",TestNoNG="TESTER PROBLEM",StartOfRepair="10/01/2026 10:20:00",EndOfRepair="10/01/2026 10:50:00",TEAssign="TE1",Ideas="IDEA",TelekoCheckingData="1,2,3,4|5,6,7,8|9,10,11,12|13,14,15,16|17,18,19,20"};
var teCategories=new[]{"PHENOMENON","MACHINE COUNTER AND VISUAL BIN","PROGRAM LOADED","TEST CABLE","OTHER EVIDENCE"};
var tePhotos=new List<TeEvidenceImage>();
for(var category=0;category<teCategories.Length;category++)for(var condition=0;condition<2;condition++) {
 var file=$"category-{category}-{condition}.png";
 File.Copy(Path.Combine(Images(false),photoSources[category*2+condition]),Path.Combine(teUpload,file));
 for(var slot=0;slot<3;slot++)tePhotos.Add(new TeEvidenceImage{Category=teCategories[category],Condition=condition==0?"BEFORE":"AFTER",ImgFile=@"~\Upload\2026\"+file});
}
tePhotos.AddRange(new[]{new TeEvidenceImage{Category="TEST BOARD",Condition="BEFORE",CN="CN3",ImgFile=@"~\Upload\2026\photo.png"},new TeEvidenceImage{Category="TEST BOARD",Condition="AFTER",CN="CN1",ImgFile=@"~\Upload\2026\photo.png"}});
tePhotos.Add(new TeEvidenceImage{Category="TEST BOARD",Condition="BEFORE",CN="CN3",ImgFile=@"~\Upload\Evidence\2020\photo.png"});
tePhotos=((IEnumerable<TeEvidenceImage>)typeof(TeEvidenceMakingService).GetMethod("VisibleImages",
    System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,new object[]{tePhotos})!).ToList();
Require(tePhotos.Count(row=>row.Category=="TEST BOARD")==2 &&
    tePhotos.Single(row=>row.Category=="TEST BOARD"&&row.Condition=="BEFORE").ImgFile.Contains("Evidence"),
    "TE staging must omit overwritten connector photos while retaining the last visible row");
var telekoPhotos=Enumerable.Range(0,4).Select(_=>new TelekoEvidenceImage{TelekoEvidence=@"~/Upload/Evidence/2020/photo.png"}).ToList();
using var teStagedPhotos=await LegacyReportExport.StagedPhotos.LoadAsync(tePhotos.Select(row=>row.ImgFile).Concat(telekoPhotos.Select(row=>row.TelekoEvidence)),teLegacyShare,Images(true),true,sharedArchive);
TeEvidenceMakingService.PopulateWorkbook(cwc,teHeader,new(),tePhotos,telekoPhotos,new(),Images(true),teLegacyShare,sharedEvidenceRoot:sharedArchive,imagePaths:teStagedPhotos.Paths);
Require(cwc.Worksheets["FORMAT"].Cells["B5"].StringValue=="TESTER PROBLEM","CWC Request Content differs from Form10 TestNoNG");
var blankRule=cwc.Worksheets["FORMAT"].ConditionalFormatting.Single();
Require(blankRule.AppliesTo.Contains("M6:Q6") && blankRule.AppliesTo.Contains("M8:Q8") &&
 blankRule.AppliesTo.Contains("M10:P11") && !blankRule.AppliesTo.Contains("M10:Q11") &&
 !blankRule.AppliesTo.Contains("M14:Q15") && !blankRule.AppliesTo.Contains("M6:Q8"),"Optional CWC fields still inherit pink blank-field formatting");
Require(cwc.Worksheets["FORMAT"].Cells["M7"].StringValue=="" && cwc.Worksheets["FORMAT"].Cells["M14"].StringValue=="","Empty optional CWC fields changed");
using(var serialized=new MemoryStream(LegacyReportExport.Save(cwc,"xlsx"))) {
 var reloaded=ExcelFile.Load(serialized);
 Require(reloaded.Worksheets["FORMAT"].ConditionalFormatting.Single().AppliesTo==blankRule.AppliesTo,"CWC optional-field formatting did not survive XLSX serialization");
}
var teRows=new[]{20,31,36,54,62};
for(var category=0;category<teRows.Length;category++)for(var condition=0;condition<2;condition++)
 foreach(var column in condition==0?new[]{"B","D","F"}:new[]{"J","L","N"})PhotoAt(cwc,column+teRows[category],Path.Combine(teUpload,$"category-{category}-{condition}.png"));
foreach(var column in new[]{"H","J","M","O"})PhotoAt(cwc,column+"70",archivedImage);
PhotoAt(cwc,"B49",archivedImage);
Require(ContainsPhoto(cwc,teImage)&&ContainsPhoto(cwc,archivedImage),
    "Form10 workbook did not embed the actual TE and shared Teleko photos");
Require(cwc.Worksheets[0].Cells["G76"].StringValue=="20","TE QC array mapping failed");
Require(cwc.Worksheets[0].Cells["M11"].StringValue=="10:20:00 -AM","TE repair time format differs from legacy");
var pictures=cwc.Worksheets[0].Pictures;
Require(pictures.Any(p=>p.Position!.From!.Row.Index==48&&p.Position!.From!.Column.Index==1)&&pictures.Any(p=>p.Position!.From!.Row.Index==44&&p.Position!.From!.Column.Index==9),"CN3-before/CN1-after was mapped by list position instead of connector code");
foreach(var column in new[]{7,9,12,14}) Require(pictures.Any(p=>p.Position!.From!.Row.Index==69&&p.Position!.From!.Column.Index==column&&p.Position.GetWidth(LengthUnit.Inch)<=1.54+0.001&&p.Position.GetHeight(LengthUnit.Inch)<=1.4+0.001&&p.Position.Mode==PositioningMode.MoveAndSize),"Teleko anchor/containment changed");
Save(cwc,"TE-EVIDENCE",true);
var incomplete=new ExcelFile(); incomplete.Worksheets.AddCopy("FORMAT",cwc.Worksheets["FORMAT"]);
try{TeEvidenceMakingService.PopulateWorkbook(incomplete,teHeader,new(),new(){new(){Category="TEST BOARD",CN="CN5"}},new(),new(),Images(true),Images(true));throw new Exception("Missing connector sheet was ignored");}
catch(InvalidDataException ex)when(ex.Message.Contains("TESTBOARD")){}
try{TeEvidenceMakingService.PopulateWorkbook(incomplete,teHeader,new(),new(),new(),new(){new(){Category="BEFORE QUALITY CONDITION"}},Images(true),Images(true));throw new Exception("Missing quality sheet was ignored");}
catch(InvalidDataException ex)when(ex.Message.Contains("DATASHEET")){}
// Exercise the supplied connector and quality sheets, deliberately reordered.
var qualityBook=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","CWC.xlsx"));
var reordered=new ExcelFile();
foreach(var name in new[]{"DATASHEET","FORMAT","TESTBOARD"})reordered.Worksheets.AddCopy(name,qualityBook.Worksheets[name]);
qualityBook=reordered;
var connectors=new List<TeEvidenceImage>();
foreach(var condition in new[]{"BEFORE","AFTER"})for(var number=5;number<=8;number++)
 connectors.Add(new(){Category="TEST BOARD",Condition=condition,CN="CN"+number,ImgFile=@"~\Upload\Evidence\2020\photo.png"});
var qualityCategories=new[]{"BEFORE QUALITY CONDITION","AFTER QUALITY CONDITION","IDEAL QUALITY CONDITION"};
var qualityDescriptions=qualityCategories.Select((category,index)=>new TeEvidenceCategoryDesc{Category=category,Condition=index==0?"BEFORE":"AFTER",Desc="QUALITY-"+index}).ToList();
// A Windows temporary directory may contain a literal ~ (including short user paths).
// Only stored Form10 paths are normalized; resolved staging paths must stay intact.
var tildeDirectory=Path.Combine(output,"photo~staging");Directory.CreateDirectory(tildeDirectory);
var tildePhoto=Path.Combine(tildeDirectory,"quality.png");File.Copy(archivedImage,tildePhoto);
var tildePaths=new Dictionary<string,string>{[@"~\Upload\Evidence\2020\photo.png"]=tildePhoto};
TeEvidenceMakingService.PopulateWorkbook(qualityBook,teHeader,qualityDescriptions,connectors,new(),
    qualityCategories.Select(category=>new QualityConditionImage{Category=category,ImgFile=@"~\Upload\Evidence\2020\photo.png"}).ToList(),
    Images(true),teLegacyShare,sharedEvidenceRoot:sharedArchive,imagePaths:tildePaths);
var qualityPictures=new ExcelFile(); qualityPictures.Worksheets.AddCopy("Quality",qualityBook.Worksheets["DATASHEET"]);
Require(ContainsPhoto(qualityPictures,archivedImage),"Quality-condition sheet did not embed the shared evidence photo");
var boardPictures=new ExcelFile(); boardPictures.Worksheets.AddCopy("Board",qualityBook.Worksheets["TESTBOARD"]);
foreach(var cell in new[]{"A3","C3","A7","C7","G3","I3","G7","I7"})PhotoAt(boardPictures,cell,archivedImage);
foreach(var cell in new[]{"F3","K3","P3"})PhotoAt(qualityPictures,cell,archivedImage);
foreach(var pair in new[]{("E7","QUALITY-0"),("J7","QUALITY-1"),("O7","QUALITY-2")})
 Require(qualityBook.Worksheets["DATASHEET"].Cells[pair.Item1].StringValue==pair.Item2,"Quality description mapped to the wrong sheet/cell");
Save(qualityBook,"TE-EVIDENCE-QUALITY");
Console.WriteLine("PASS: actual CWC CN5-CN8/quality sheets, named routing after sheet reordering, QC/time, Teleko and explicit missing-sheet errors.");
if(args.Contains("photo-benchmark")) {
 // Phone-sized photographs exercise the expensive path that small static marks cannot.
 var largePhotos=new List<string>();
 for(var number=0;number<3;number++) {
  using var bitmap=new SkiaSharp.SKBitmap(3000,2000);
  var pixels=new int[bitmap.Width*bitmap.Height];var random=new Random(number);
  for(var index=0;index<pixels.Length;index++)pixels[index]=unchecked((int)0xff000000)|random.Next(0x1000000);
  System.Runtime.InteropServices.Marshal.Copy(pixels,0,bitmap.GetPixels(),pixels.Length);
  using var image=SkiaSharp.SKImage.FromBitmap(bitmap);using var encoded=image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg,90);
  var path=Path.Combine(output,$"phone-{number}.jpg");File.WriteAllBytes(path,encoded.ToArray());largePhotos.Add(path);
 }
 var localPaths=largePhotos.ToDictionary(path=>path,path=>path);
 var slot=0;
 EvidenceImageRow[] LargeGroup(int count)=>Enumerable.Range(0,count).Select(_=>new EvidenceImageRow{ImageFile=largePhotos[slot++%3]}).ToArray();
 var largeGroups=new EvidenceImageGroups{ProblemPoint=LargeGroup(3),ActionTaken=LargeGroup(2),PartsReplaced=LargeGroup(2),DisAssembly=LargeGroup(2),QualityConfirm=LargeGroup(3)};
 var pm=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","EvidenceMaking_DC.xlsx"));
 var preparation=System.Diagnostics.Stopwatch.StartNew();
 EvidenceMakingService.PopulateWorkbook(pm,"DC",header,new(),largeGroups,Images(false),pmLegacyShare,imagePaths:localPaths);
 var pmPreparationMs=preparation.Elapsed.TotalMilliseconds;
 var te=LegacyReportExport.Load(Path.Combine(root,"Templates","EvidenceMaking","CWC.xlsx"));
 var largeRows=teCategories.SelectMany(category=>Enumerable.Range(0,6).Select(index=>new TeEvidenceImage{
  Category=category,Condition=index<3?"BEFORE":"AFTER",ImgFile=largePhotos[index%3]})).ToList();
 preparation.Restart();
 TeEvidenceMakingService.PopulateWorkbook(te,teHeader,new(),largeRows,new(),new(),Images(true),teLegacyShare,imagePaths:localPaths);
 var tePreparationMs=preparation.Elapsed.TotalMilliseconds;
 foreach(var fixture in new[]{(Name:"PM evidence",Book:pm,PreparationMs:pmPreparationMs),(Name:"TE evidence",Book:te,PreparationMs:tePreparationMs)}) {
  // Recreate the previous full-resolution embeddings at identical anchors and sizes.
  var baseline=fixture.Book.Clone();var sourceIndex=0;
  foreach(var sheet in baseline.Worksheets) foreach(var picture in sheet.Pictures.ToArray()) {
   if(picture.PictureFormat!=ExcelPictureFormat.Jpeg)continue;
   var position=picture.Position!;var from=position.From!;
   var anchor=sheet.Cells[from.Row.Index,from.Column.Index].Name;
   var width=position.Width;var height=position.Height;
   sheet.Pictures.Remove(picture);
   sheet.Pictures.Add(largePhotos[sourceIndex++%3],anchor,width,height,LengthUnit.Point).Position!.Mode=PositioningMode.MoveAndSize;
  }
  foreach(var format in new[]{"xlsx","pdf"}) {
  var clock=System.Diagnostics.Stopwatch.StartNew();
  using var original=new MemoryStream();
  baseline.Save(original,format=="pdf"?new PdfSaveOptions{SelectionType=SelectionType.EntireFile}:SaveOptions.XlsxDefault);
  var oldMs=clock.Elapsed.TotalMilliseconds;clock.Restart();
  var optimized=LegacyReportExport.Save(fixture.Book,format);var newMs=clock.Elapsed.TotalMilliseconds;
  Require(optimized.Length<original.Length/2,fixture.Name+" oversized photographs were not reduced");
  if(format=="xlsx") {
   var roundTrip=ExcelFile.Load(new MemoryStream(optimized));
   Require(roundTrip.Worksheets.Sum(sheet=>sheet.Pictures.Count)==fixture.Book.Worksheets.Sum(sheet=>sheet.Pictures.Count),"Image optimization dropped photographs");
   using var bitmap=SkiaSharp.SKBitmap.Decode(roundTrip.Worksheets[0].Pictures[0].PictureStream);
   Require(bitmap.Width<3000 && bitmap.Height<2000,"XLSX still embeds phone-sized originals");
  }
  File.WriteAllBytes(Path.Combine(output,fixture.Name.Replace(' ','-')+"-optimized."+format),optimized);
  Console.WriteLine($"MEASURE: {fixture.Name} {format}: previous save={oldMs:F1}ms/{original.Length:N0} bytes, optimized save={newMs:F1}ms/{optimized.Length:N0} bytes, optimized population/preparation={fixture.PreparationMs:F1}ms (6MP synthetic photos; excludes SQL/share).");
  }
 }
}
Console.WriteLine("Report artifacts: "+output);
sealed class ReportResponseProbe : MachineKarte.Web.Controllers.WorkflowController {
 public Microsoft.AspNetCore.Mvc.FileContentResult Generate(byte[] bytes,string name)=>ReportFile(bytes,name);
}
'@ | Set-Content -Encoding UTF8 (Join-Path $directory 'Program.cs')
# Keep the RID on the runner so project references build/copy the same portable app DLL.
& $Dotnet build (Join-Path $directory 'Checks.csproj') --nologo -v minimal --self-contained false --property:UseAppHost=false -m:1 --nodeReuse:false --property:NuGetAudit=false --property:UseSharedCompilation=false
if($LASTEXITCODE -ne 0){throw 'Report fixture build failed.'}
$downloadMode=if($PerformanceOnly){'performance-only'}elseif($CwcOnly){'cwc-only'}elseif($DownloadOnly){'download-only'}elseif($EvidenceOnly){'evidence-only'}else{'all'}
$benchmarkMode=if($PhotoBenchmark){'photo-benchmark'}else{'no-benchmark'}
& $Dotnet (Join-Path $directory 'bin/Debug/net8.0/win-x64/Checks.dll') $repo $downloadMode $benchmarkMode
if($LASTEXITCODE -ne 0){throw 'Report generation checks failed.'}
