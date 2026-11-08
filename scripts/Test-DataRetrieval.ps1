param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB', [switch]$Benchmark)
# Real read paths, isolated in a uniquely named disposable LocalDB database.
# No appsettings, application hosting, or production connection is used.
$ErrorActionPreference = 'Stop'
if ($LocalDbInstance -notmatch '^[A-Za-z0-9_-]+$') { throw 'Expected a LocalDB instance name.' }
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-Retrieval-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
$projectPath = [Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><RuntimeIdentifier>win-x64</RuntimeIdentifier><UseAppHost>false</UseAppHost><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
# Test copies keep the real repository bodies, redirecting fully qualified reads
# into the disposable catalog. Reject any surviving production database name.
foreach ($name in @('RepositorySupport','RawDataRepository','EvidenceRepository','TeEvidenceRepository','BmReportRepository','McdMonitoringRepository','PmReportRepository','AlertBoardRepository','BmAnalyticsRepository','MachineSummaryRepository','OcapViewerRepository')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo "Repositories/$name.cs"))
    $source = $source.Replace('MachineKarte.Web.Repositories', 'RetrievalChecks.Repositories').Replace('[eMaintenanceSystemDB].[dbo].', '[dbo].')
    if ($source -match 'eMaintenanceSystemDB') { throw "Unredirected database reference in $name" }
    [IO.File]::WriteAllText((Join-Path $checkDirectory "$name.cs"), $source)
}
@'
using System.Data;
using System.Reflection;
using MachineKarte.Web.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MachineKarte.Web.Services;
using RetrievalChecks.Repositories;

void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
Require(args.Length >= 2 && System.Text.RegularExpressions.Regex.IsMatch(args[0], @"^[A-Za-z0-9_-]+$"), "LocalDB instance and repository required");
var database = "MachineKarte_RetrievalTest_" + Guid.NewGuid().ToString("N");
var connection = new SqlConnectionStringBuilder {
    DataSource = @"(localdb)\" + args[0], InitialCatalog = "master", IntegratedSecurity = true,
    Encrypt = SqlConnectionEncryptOption.Optional, TrustServerCertificate = true, ConnectTimeout = 10
};
await using var admin = new SqlConnection(connection.ConnectionString);
await admin.OpenAsync();
async Task Admin(string sql) { await using var command = new SqlCommand(sql, admin); await command.ExecuteNonQueryAsync(); }
await Admin($"CREATE DATABASE [{database}]");
try {
    connection.InitialCatalog = database;
    var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:MachineDBConnection"] = connection.ConnectionString
    }).Build();
    var connections = new DatabaseConnectionResolver(config, NullLogger<DatabaseConnectionResolver>.Instance);
    await using var fixture = new SqlConnection(connection.ConnectionString);
    await fixture.OpenAsync();
    async Task Sql(string sql) { await using var command = new SqlCommand(sql, fixture); await command.ExecuteNonQueryAsync(); }
    await Sql(@"
CREATE TABLE BMCase (BMCaseID int PRIMARY KEY, BMCase nvarchar(100));
INSERT BMCase VALUES (1,'CASE A'),(2,'CASE B');
CREATE TABLE BMTEUnit (BMTEUnitID int PRIMARY KEY, BMTEUnit nvarchar(100));
INSERT BMTEUnit VALUES (1,'UNIT');
CREATE TABLE DCBMMaintenance (BMID int, MCNo nvarchar(50), Process nvarchar(10), Location nvarchar(30),
    OccurrenceDate datetime, TimeOfRequest datetime, BMCaseID int, Devicename nvarchar(50), PMCategoryID int, BMStatusID int);
INSERT DCBMMaintenance VALUES
(1,'MC-Z','DC','LSI1-1F','2026-10-01','2026-10-01',1,'DEV ICE',1,6),
(2,'MC-Z','DC','LSI1-1F','2026-10-02','2026-10-02',1,'DEVICE',1,6),
(3,'MC-Z','DC','LSI1-1F','2026-10-03','2026-10-03',1,'DEVICE',1,6),
(4,'MC-A','DC','LSI1-1F','2026-10-01','2026-10-01',2,'DEVICE',1,6),
(5,'MC-X','DC','OTHER','2026-10-01','2026-10-01',2,'DEVICE',1,6),
(6,'MC-Y','DC','LSI1-1F','2026-10-01','2026-10-01',2,'INACTIVE',1,6);
CREATE TABLE TEBMMaintenance (BMID int, TesterNo nvarchar(50), Process nvarchar(10), Location nvarchar(30),
    TimeOfRequest datetime, BMUnitID int, Devicename nvarchar(50), PMCategoryID int, BMStatusID int);
INSERT TEBMMaintenance VALUES (1,'TE-Z','FL','LSI1-1F','2026-10-01',1,'DEVICE',1,6),
    (2,'TE-Z','FL','LSI1-1F','2026-10-02',1,'DEVICE',4,6),
    (3,'TE-A','FL','LSI1-1F','2026-10-03',1,'DEVICE',1,6),
    (4,'TE-X','FT','LSI1-1F','2026-10-03',1,'DEVICE',1,6);
CREATE TABLE MachineKarte_MCD (Devicename nvarchar(50), IsActive bit);
INSERT MachineKarte_MCD VALUES ('DEV ICE',1),('DEVICE',1),('INACTIVE',0);
CREATE TABLE MachineKarte_ActionCard (BMID int, MachineNo nvarchar(50));
CREATE TABLE MachineKarte_ActionCard_TE (BMID int, MachineNo nvarchar(50));
INSERT MachineKarte_ActionCard VALUES (4,'MC-A');
INSERT MachineKarte_ActionCard_TE VALUES (3,'TE-A');
CREATE TABLE VRNumber (BMID int, VRNumber1 nvarchar(20), VRNumber2 nvarchar(20), VRNumber3 nvarchar(20));
INSERT VRNumber VALUES (1,'VR-A',NULL,'VR-C');
CREATE TABLE gridEvidenceMaking_Ver3 (BMEvidenceID int, BMID int, Process nvarchar(10),
    BMEvidence_Category nvarchar(100), BMEvidence_Desc nvarchar(100), BMEvidence_ImgFile nvarchar(100));
CREATE TABLE TEBMEvidenceMaking (BMEvidenceID int, BMID int, BMEvidence_Category nvarchar(100),
    Condition nvarchar(20), BMEvidence_Desc nvarchar(100), BMEvidence_ImgFile nvarchar(100), TelekoEvidence nvarchar(100), CN nvarchar(10));
CREATE TABLE ViewDCData_Ver4 (BMID int, MCNo nvarchar(50), TimeOfRequest nvarchar(50), Marker nvarchar(50));
INSERT ViewDCData_Ver4 VALUES (5,'MC-Z','10/05/2026 00:00:00','CURRENT'),
    (4,'MC-Z','10/04/2026 00:00:00','PREVIOUS'),(3,'MC-Z','10/03/2026 00:00:00',NULL),
    (2,'MC-Z','10/02/2026 00:00:00','OLDER'),(1,'OTHER','10/01/2026 00:00:00','OTHER');
");
    var raw = new RawDataRepository(connections);
    var frequency = await raw.GetFrequencyRawRowsAsync("DCBMMaintenance", "LSI1-1F", "2026-10-01", "2026-10-31");
    Require(frequency.Rows.Count == 3 && frequency.Columns.Contains("BMCase"), "Raw frequency skipped its current result set");
    Require(frequency.Rows.Cast<DataRow>().All(row => row["mcno"].ToString() == "MC-Z"), "Raw frequency changed filters");
    var monthly = await raw.GetBmCaseMonthlyRowsAsync("DCBMMaintenance", "LSI1-1F", "2026-10-01", "2026-10-31");
    Require(monthly.Rows.Count == 1 && (int)monthly.Rows[0]["BMCount"] == 3, "Raw monthly results/count changed");
    Require(frequency.Columns["DateNum"]!.DataType == typeof(int), "Raw reader changed column types");
    try { await raw.GetFrequencyRawRowsAsync("DCBMMaintenance];--", "", "", ""); throw new Exception("Raw identifier accepted"); }
    catch (ArgumentException) { }
    var mcd = new McdMonitoringRepository(connections);
    var from = new DateTime(2026,10,1); var to = new DateTime(2026,10,31);
    Require((await mcd.GetMachinesAsync("DC","LSI1-1F",from,to,false)).SequenceEqual(new[] {"MC-Z"}), "MCD DISTINCT/order/filter failed");
    Require((await mcd.GetMachinesAsync("FL","LSI1-1F",from,to,true)).SequenceEqual(new[] {"TE-Z"}), "TE MCD DISTINCT/order/filter failed");
    var machineSummary=new MachineSummaryRepository(connections);
    Require((await machineSummary.GetMachinesAsync("DC","LSI1-1F",from.AddMilliseconds(900),to,false)).SequenceEqual(new[]{"MC-Z","MC-Z","MC-Z"}),"Machine Summary lost row-count semantics or whole-second date boundaries");
    Require((await machineSummary.GetMachinesAsync("FL","LSI1-1F",from,to,true)).SequenceEqual(new[]{"TE-Z","TE-Z"}),"TE Machine Summary deduplicated BM rows");
    var pm = await new PmReportRepository(connections).GetReportDataAsync("DC", 5, "10/05/2026 00:00:00", "MC-Z");
    Require(pm!.Current.Fields["Marker"] == "CURRENT" && pm.History.Count == 2 &&
        pm.History[0].Fields["Marker"] == "PREVIOUS" && pm.History[1].Fields["Marker"] == "", "PM history limit/order/null mapping changed");
    Require(pm.Current.Values["BMID"] is int && pm.History[1].Values["Marker"] is DBNull,"Legacy report values lost SQL types/DBNull");
    Console.WriteLine("PASS: real Raw Data results/types, MCD standard/TE deduplication and filters, PM current/history order/nulls, and identifier rejection.");

    var lookups = new[] { ("PMCategory","PMCategoryID","PMCategory"), ("BMContent","BMContentID","BMContent"),
        ("TEContent","BMContentID","BMContent"), ("TECase","BMCaseID","BMCase"),
        ("TEPoint","BMPointID","BMPoint"), ("TEWorkContent","BMWorkContentID","BMWorkContent"),
        ("TECause","BMCauseID","BMCause"), ("TEPREVMeasure","PREVMeasureID","PREVMeasure") };
    foreach (var (table,key,value) in lookups) {
        await Sql($"CREATE TABLE [{table}] ([{key}] int, [{value}] nvarchar(100)); INSERT [{table}] VALUES (1,'{table}'),(2,NULL);");
    }
    var bm = new BmReportRepository(connections);
    var report = new BmReportData { BmId=1, PMCategoryID="1", BMContentID="1", BMCaseID="1", BMUnitID="1",
        BMPointID="1", BMWorkContentID="1", BMCauseID="1", PrevMeasureID="1" };
    await bm.LoadSupplementalDataAsync(report);
    Require(report.VRNumber1 == "VR-A" && report.VRNumber2 == "" && report.VRNumber3 == "VR-C" &&
        report.PMCategory == "PMCategory" && report.BMContent == "BMContent" && report.TEContent == "TEContent" &&
        report.BMCase == "TECase" && report.BMTEUnit == "UNIT" && report.BMPoint == "TEPoint" &&
        report.BMWorkContent == "TEWorkContent" && report.BMCause == "TECause" && report.PREVMeasure == "TEPREVMeasure", "Batched report result sets misaligned");
    var emptyReport = new BmReportData { BmId=999, PMCategoryID="0", BMContentID="", BMCaseID="999", BMUnitID="0",
        BMPointID="2", BMWorkContentID=" ", BMCauseID="0", PrevMeasureID="2" };
    await bm.LoadSupplementalDataAsync(emptyReport);
    Require(emptyReport.VRNumber1 == "" && emptyReport.PMCategory == "" && emptyReport.BMCase == "" &&
        emptyReport.BMPoint == "" && emptyReport.PREVMeasure == "", "Empty/zero/missing/null report lookup changed");
    Require(emptyReport.LookupValues.Values.All(value => value != "0"),
        "Missing or null TE lookups were incorrectly rendered as literal zeroes");
    Console.WriteLine("PASS: batched VR and all nine report lookups, including empty/zero/missing/DBNull results.");
    await Sql(@"
CREATE TABLE MCType (MCTypeID int, MCType nvarchar(100)); INSERT MCType VALUES (1,'TYPE');
CREATE TABLE BMMCUnit (BMMCUnitID int, MCTypeID int, BMMCUnit nvarchar(100)); INSERT BMMCUnit VALUES (1,1,'PM UNIT'),(2,1,NULL);
CREATE TABLE BMPoint (BMPointID int, BMPoint nvarchar(100)); INSERT BMPoint VALUES (1,'PM POINT');
CREATE TABLE BMWorkContent (BMWorkContentID int, BMWorkContent nvarchar(100)); INSERT BMWorkContent VALUES (1,'PM WORK');
CREATE TABLE BMWhy (BMWhyID int, BM4M nvarchar(100), BMWhy nvarchar(100)); INSERT BMWhy VALUES (1,'METHOD','CAUSE');
");
    foreach(var process in new[]{"MEMS","WB","DB","DC","MP","PL","FT","SING","FL","TP","EDS"}) {
        var lookupData = new PmReportData { Process=process,Current=new PmReportRecord() };
        var specs = MachineKarte.Reporting.LegacyReportWriter.LookupSpecs(process).Select(value=>value.Split('|')).ToArray();
        foreach(var spec in specs) lookupData.Current.Fields[spec[0]]=spec[0]=="OtherUnitCheck"?"1,A|2,B":"1";
        var resolved = await new PmReportRepository(connections).GetLookupValuesAsync(lookupData);
        foreach(var spec in specs) Require(resolved.ContainsKey($"{spec[1]}|1|{spec[3]}"),"Missing PM lookup: "+process+"/"+string.Join('|',spec));
        foreach(var spec in specs.Where(spec=>spec[1]=="BMWhy")) Require(resolved[$"BMWhy|1|{spec[3]}"]==(spec[3]=="BM4M"?"METHOD":"CAUSE"),"BMWhy columns crossed");
        if(specs.Any(spec=>spec[1]=="BMMCUnitWithType")) Require(resolved["BMMCUnitWithType|1|BMMCUnit"]=="PM UNIT","Machine unit/type lookup changed");
        if(specs.Any(spec=>spec[0]=="OtherUnitCheck")) Require(resolved["BMMCUnit|2|BMMCUnit"]=="","Array unit lookup lost DBNull behavior");
    }
    Console.WriteLine("PASS: all 11 PM profiles' real parameterized lookup batches, BMWhy columns, joined machine units and array unit lookups.");

    var categories = new[] { "PROBLEM POINT / PHENOMENON", "ACTION TAKEN / EVIDENCE AFTER ADJUSTMENT",
        "PARTS REPLACED BEFORE / PARTS REPLACED AFTER", "DETAILS OF DIS-ASSEMBLY  DETAILS OF RE-ASSEMBLY",
        "BEFORE AND AFTER QUALITY CONFIRMATION" };
    foreach (var category in categories)
    for (var i = 4; i >= 1; i--)
        await Sql($"INSERT gridEvidenceMaking_Ver3 VALUES ({i},1,'DC','{category}','DESC-{i}','IMG-{i}');");
    var evidence = new EvidenceRepository(connections);
    var images = await evidence.GetEvidenceImagesAsync(1,"DC");
    foreach (var group in new[] {images.ProblemPoint, images.ActionTaken, images.PartsReplaced, images.DisAssembly, images.QualityConfirm})
        Require(group.Select(row => row.ImageFile).SequenceEqual(Enumerable.Range(1,group.Length).Select(i => $"IMG-{i}")), "PM evidence TOP/order/group changed");
    var emptyImages = await evidence.GetEvidenceImagesAsync(999,"DC");
    Require(emptyImages.ProblemPoint.All(row => row.ImageFile == ""), "PM empty slots changed");

    var teCategories = new[] {"PHENOMENON","MACHINE COUNTER AND VISUAL BIN","PROGRAM LOADED","TEST BOARD","TEST CABLE","OTHER EVIDENCE"};
    foreach (var category in teCategories)
    for (var i=22; i>=1; i--)
        await Sql($"INSERT TEBMEvidenceMaking VALUES ({i},1,'{category}','{(i%2==1 ? "BEFORE" : "AFTER")}','DESC-{i}','~/IMG-{i}',NULL,'CN4');");
    var qualityCategories = new[] {"BEFORE QUALITY CONDITION","AFTER QUALITY CONDITION","IDEAL QUALITY CONDITION"};
    foreach (var category in qualityCategories)
        await Sql($"INSERT TEBMEvidenceMaking VALUES (2,1,'{category}','AFTER','','~/SECOND',NULL,NULL),(1,1,'{category}','BEFORE','','~/FIRST',NULL,NULL);");
    var te = new TeEvidenceRepository(connections);
    var teImages = await te.GetCategoryImageRowsAsync("1");
    foreach (var category in teCategories) {
        var top = category == "TEST BOARD" ? 20 : 6;
        Require(teImages.Where(row=>row.Category==category && row.Condition=="BEFORE").Select(row=>row.ImgFile).SequenceEqual(Enumerable.Range(1,top).Where(i => i%2==1).Select(i => $"/IMG-{i}")) &&
            teImages.Where(row=>row.Category==category && row.Condition=="AFTER").Select(row=>row.ImgFile).SequenceEqual(Enumerable.Range(1,top).Where(i => i%2==0).Select(i => $"/IMG-{i}")), "TE TOP/order/condition split changed");
    }
    Require((await te.GetCategoryImageRowsAsync("999")).Count==0, "TE empty category alignment failed");
    Require(teImages.Where(row=>row.Category=="TEST BOARD").All(row=>row.CN=="CN4"),"Test-board connector identifiers were lost");
    var quality = await te.GetQualityImagesAsync("1");
    Require(quality.Select(row => row.Category).SequenceEqual(qualityCategories) && quality.All(row => row.ImgFile=="/FIRST"), "TE quality TOP/order changed");
    Require((await te.GetQualityImagesAsync("999")).Count==0, "Empty quality results changed");
    Console.WriteLine("PASS: PM five-category limits/order/empty slots; TE six-category combined TOP limits, BEFORE/AFTER order, path normalization, quality categories and empty result sets.");
    await Sql("ALTER TABLE TEBMEvidenceMaking ADD Ideas nvarchar(100), WaferNo nvarchar(100), Remarks nvarchar(100), TelekoCheckingData nvarchar(100);");
    await Sql("UPDATE TEBMEvidenceMaking SET Ideas='IDEA',WaferNo='WAFER',Remarks='REMARK',TelekoCheckingData='QC' WHERE BMEvidenceID=1;");
    var misc = await te.GetMiscRowsAsync("1");
    Require(misc.Any(row=>row["Ideas"].ToString()=="IDEA"&&row["TelekoCheckingData"].ToString()=="QC") && misc.Any(row=>row["Ideas"].ToString()==""),"Distinct TE misc rows were collapsed or optional nulls changed");
    Require((await te.GetMiscRowsAsync("999")).Count==0,"Empty TE misc results changed");
    Console.WriteLine("PASS: original TE distinct-data iteration, optional columns, connector identifiers and raw report SQL types.");

    // Execute Form5's original SQL as a separate oracle for the shared recurrence CTE.
    await Sql(@"
INSERT BMCase VALUES (3,'Corrective maintenance'),(4,'Preventive maintenance'),(5,NULL);
INSERT DCBMMaintenance VALUES
(10,'BOUNDARY','DC','LSI1-1F','2026-09-30','2026-09-30',1,'DEVICE',1,6),
(11,'BOUNDARY','DC','LSI1-1F','2026-10-01','2026-10-01',1,'DEVICE',1,6),
(12,'MIX','DC','LSI1-1F','2026-10-01','2026-10-01',1,'DEVICE',1,6),
(13,'MIX','DC','LSI1-1F','2026-10-02','2026-10-02',2,'DEVICE',1,6),
(14,'MIX','DC','LSI1-1F','2026-10-03','2026-10-03',1,'DEVICE',1,6),
(15,'NULL-DATE','DC','LSI1-1F',NULL,'2026-10-01',1,'DEVICE',1,6),
(16,'CORRECTIVE','DC','LSI1-1F','2026-10-01','2026-10-01',3,'DEVICE',1,6),
(17,'CORRECTIVE','DC','LSI1-1F','2026-10-02','2026-10-02',3,'DEVICE',1,6),
(18,'PREVENTIVE','DC','LSI1-1F','2026-10-01','2026-10-01',4,'DEVICE',1,6),
(19,'PREVENTIVE','DC','LSI1-1F','2026-10-02','2026-10-02',4,'DEVICE',1,6),
(20,'NULL-CASE','DC','LSI1-1F','2026-10-01','2026-10-01',5,'DEVICE',1,6);
");
    var legacyRaw=System.IO.File.ReadAllText(System.IO.Path.Combine(args[1],"Legacy","Form5.vb.txt"));
    string OriginalRaw(string variable) {
        var sql=System.Text.RegularExpressions.Regex.Match(legacyRaw,variable+" = \"(SELECT.*?Drop Table #temptable)\"",
            System.Text.RegularExpressions.RegexOptions.Singleline|System.Text.RegularExpressions.RegexOptions.IgnoreCase).Groups[1].Value;
        Require(sql.Length>100,"Original Form5 query missing");
        sql=sql.Replace("\" & GetTablename(Process) & \"","[dbo].[DCBMMaintenance]")
            .Replace("'\" & Location & \"'","@Location").Replace("'\" & DateFrom & \"'","@DateFrom").Replace("'\" & DateTo & \"'","@DateTo")
            .Replace("[eMaintenanceSystemDB].[dbo].","[dbo].");
        Require(!sql.Contains("eMaintenanceSystemDB",StringComparison.OrdinalIgnoreCase)&&!sql.Contains(" & "),"Original raw query was not redirected");
        return sql;
    }
    async Task<DataTable> ReadOriginalRaw(string sql) {
        await using var command=new SqlCommand(sql,fixture);
        command.Parameters.AddWithValue("@Location","LSI1-1F");
        command.Parameters.AddWithValue("@DateFrom",new DateTime(2026,10,1)); command.Parameters.AddWithValue("@DateTo",new DateTime(2026,10,31));
        await using var reader=await command.ExecuteReaderAsync(); var table=new DataTable(); table.Load(reader); return table;
    }
    void SameTable(DataTable original,DataTable current,string label) {
        Require(original.Columns.Cast<DataColumn>().Select(c=>(c.ColumnName,c.DataType)).SequenceEqual(current.Columns.Cast<DataColumn>().Select(c=>(c.ColumnName,c.DataType))),label+" column names/types changed");
        string[] Rows(DataTable table)=>table.Rows.Cast<DataRow>().Select(row=>string.Join("|",row.ItemArray.Select(value=>value?.ToString()))).ToArray();
        Require(Rows(original).SequenceEqual(Rows(current)),label+" rows/order/values changed");
    }
    var originalFrequency=OriginalRaw("strQuery"); var originalMonthly=OriginalRaw("strQuery2");
    var newFrequency=await raw.GetFrequencyRawRowsAsync("DCBMMaintenance","LSI1-1F","2026-10-01","2026-10-31");
    SameTable(await ReadOriginalRaw(originalFrequency),newFrequency,"Frequency CTE");
    SameTable(await ReadOriginalRaw(originalMonthly),await raw.GetBmCaseMonthlyRowsAsync("DCBMMaintenance","LSI1-1F","2026-10-01","2026-10-31"),"Monthly CTE");
    Require(!newFrequency.Rows.Cast<DataRow>().Any(row=>row["mcno"].ToString()=="BOUNDARY"),"Frequency included a neighbor outside its date window");
    Console.WriteLine("PASS: both recurrence CTEs match original Form5 SQL, including date-filter placement, alternating cases, machine boundaries, NULLs, excluded maintenance and SQL types/order.");

    await Sql(@"
CREATE TABLE ViewTEData_Test (bmID int, TesterNo varchar(50), MCNo varchar(50), OccurrenceDate datetime);
INSERT ViewTEData_Test VALUES (1,'TE-A',NULL,'2026-10-01');
CREATE TABLE ViewTEData (bmID int, TesterNo varchar(50), MCNo varchar(50), PMConfirmGLOPReqBy varchar(50), RepairedByEnd varchar(50));
INSERT ViewTEData VALUES (1,'TE-A',NULL,'REQUESTER','REPAIRER');
");
    var sparse=await bm.GetReportDataAsync(1);
    Require(sparse!.TesterNo=="TE-A" && sparse.MCNo=="" && sparse.DeviceName=="" && sparse.Values["OccurrenceDate"] is DateTime,"Sparse BM view lost optional/raw fields");
    var sparseTeHeader = (await te.GetHeaderAsync("1"))!;
    Require(sparseTeHeader.TesterNo=="TE-A" && sparseTeHeader.WaferNo=="" && sparseTeHeader.TEAssign=="REQUESTER, REPAIRER","Sparse TE header/assignment changed");
    foreach(var invalid in new Func<Task>[] {
        async()=>{await new OcapViewerRepository(connections).GetOcapDataAsync(1,"DCBMMaintenance];--");},
        async()=>{await evidence.GetTscConfirmationAsync(1,"DC","viewDCEMaking_Ver3;--");},
        async()=>{await evidence.GetMachineHeaderAsync(1,"ViewDCData_Ver4;--");}
    }) { try { await invalid(); throw new Exception("Untrusted identifier accepted"); } catch(ArgumentException) { } }
    Console.WriteLine("PASS: sparse BM/TE view metadata, raw report dates/DBNull, and repository-boundary OCAP/evidence identifier validation.");

    // Exercise complete AlertBoard reads against the same old per-floor processing order.
    await Sql("ALTER TABLE DCBMMaintenance ADD MCType varchar(50), BMContentID int, AcredBMContentID int, Turnover varchar(10);");
    await Sql("ALTER TABLE TEBMMaintenance ADD BMCaseID int, MCType varchar(50), MCNo varchar(50), BMContentID int, AcredBMContentID int, Priority varchar(30);");
    await Sql("ALTER TABLE BMContent ADD Process varchar(20);");
    await Sql("ALTER TABLE MachineKarte_ActionCard ADD Seqno int, MCStatus varchar(100);");
    await Sql("ALTER TABLE MachineKarte_ActionCard_TE ADD Seqno int, MCStatus varchar(100);");
    await Sql(@"
UPDATE DCBMMaintenance SET MCType='TYPE',BMContentID=48,AcredBMContentID=0,Turnover=CASE BMID%3 WHEN 0 THEN 'VIS' WHEN 1 THEN 'CC' ELSE 'PE' END;
INSERT DCBMMaintenance VALUES (100,'MC-R','DC','LSI3-1F','2026-10-30','2026-10-30',1,'DEVICE',1,6,'TYPE',48,0,'CC'),
 (101,'MC-R','DC','LSI3-1F','2026-10-31','2026-10-31',1,'DEVICE',1,6,'TYPE',48,0,'CC');
UPDATE TEBMMaintenance SET BMCaseID=1,MCType='TYPE',MCNo='MC-TE',BMContentID=48,AcredBMContentID=0,Priority='QUALITY BM';
INSERT TEBMMaintenance VALUES (100,'TE-R','FT','LSI2-3F','2026-10-30',1,'DEVICE',1,6,1,'TYPE','MC-TE',48,0,'QUALITY BM'),
 (101,'TE-R','FT','LSI2-3F','2026-10-31',1,'DEVICE',4,6,1,'TYPE','MC-TE',48,0,'QUALITY BM');
INSERT BMContent VALUES (48,'CONTENT','DC');
UPDATE MachineKarte_ActionCard SET Seqno=1,MCStatus='NO RE-OCCURRENCE AFTER 7 DAYS';
UPDATE MachineKarte_ActionCard_TE SET Seqno=1,MCStatus='NO RE-OCCURRENCE AFTER 7 DAYS';
");
    foreach(var user in new[]{"CC","EEE","VIS"}) await Sql($"SELECT * INTO MachineKarte_ActionCard_{user} FROM MachineKarte_ActionCard;");
    foreach(var process in new[]{"DB","WB","MP","PL","FL","ATC","SING","FT","TP","EDS","MEMS"})
        await Sql($"SELECT * INTO {process}BMMaintenance FROM DCBMMaintenance; UPDATE {process}BMMaintenance SET Process='{process}';");
    var alert=new AlertBoardRepository(connections);
    var flags=BindingFlags.Static|BindingFlags.NonPublic;
    object Call(string name,params object[] parameters)=>typeof(AlertBoardRepository).GetMethod(name,flags)!.Invoke(null,parameters)!;
    var fetch=typeof(AlertBoardRepository).GetMethod("FetchRowsAsync",BindingFlags.Instance|BindingFlags.NonPublic)!;
    async Task<AlertBoardData> PerFloorBoard(AlertBoardRequest request,bool bounded=false) {
        var board=new AlertBoardData();
        var processes=(string[])typeof(AlertBoardRepository).GetField(request.User=="TE"?"ProcessesTE":"ProcessesNormal",flags)!.GetValue(null)!;
        var locations=(string[])typeof(AlertBoardRepository).GetField("Locations",flags)!.GetValue(null)!;
        var queries=processes.SelectMany(process=>locations.Select(location=>(Process:process,Location:location)));
        foreach(var batch in queries.Chunk(bounded?4:1)) {
            var results=await Task.WhenAll(batch.Select(query=> {
                var sql=(string)(request.User=="TE"?Call("BuildQueryTE",query.Process):Call("BuildQueryNormal",query.Process,Call("BuildAddQuery",request.User),Call("BuildActionTable",request.User)));
                return (Task<List<AlertBoardRow>>)fetch.Invoke(alert,new object[]{sql,request.DateFrom,request.DateTo,query.Location,CancellationToken.None})!;
            }));
            foreach(var rows in results) {
                if(rows.Count==0) continue;
                var solved=(List<AlertBoardRow>)Call("ApplySolveLogic",rows);
                Call(request.User=="TE"?"ApplyColorTE":"ApplyColorNormal",solved,board);
            }
        }
        Call("BuildTotals",board); return board;
    }
    string BoardState(AlertBoardData board)=>System.Text.Json.JsonSerializer.Serialize(new {
        Cells=board.Cells.OrderBy(cell=>cell.Key).Select(cell=>new{cell.Key,cell.Value.Count,cell.Value.Machines}),
        Totals=board.LocationTotals.OrderBy(total=>total.Key),board.GrandTotal });
    foreach(var user in new[]{"","CC","EEE","VIS","TE"}) {
        var request=new AlertBoardRequest{User=user,DateFrom="10/01/2026",DateTo="11/02/2026"};
        var batched=await alert.LoadBoardAsync(request);
        Require(BoardState(batched)==BoardState(await PerFloorBoard(request)),"AlertBoard process batching changed "+user+" counts, list order, solved colors or floor isolation");
        Require(BoardState(batched)==BoardState(await PerFloorBoard(request,true)),"AlertBoard differs from prior bounded per-floor reads in "+user);
        if(user=="") Require(batched.Cells["ATC_65"].Count==2,"Repeated LSI3-1F pass was lost");
    }
    Console.WriteLine("PASS: complete AlertBoard results in all five modes match 120/50 per-floor reads, including repeated floors, independent color state, totals and machine-list order; batching uses 12/5 process reads.");
    var analyticsRows=await new BmAnalyticsRepository(connections).GetRowsAsync("DC","LSI1-1F","EEE",from.AddMilliseconds(900),new DateTime(2026,11,2));
    Require(analyticsRows.Any(row=>row.BmId==1 && row.Timeofrequest=="10/01/2026 00:00:00"),"Analytics native date conversion or whole-second range differs");

    if(args.Contains("benchmark")) {
        var request=new AlertBoardRequest{User="",DateFrom="10/01/2026",DateTo="11/02/2026"};
        var previous=new List<double>(); var boundedPrevious=new List<double>(); var improved=new List<double>();
        for(var run=0;run<7;run++) foreach(var version in Enumerable.Range(0,3).Select(index=>(index+run)%3)) {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var board=version==0?await PerFloorBoard(request):version==1?await PerFloorBoard(request,true):await alert.LoadBoardAsync(request);
            watch.Stop();
            Require(board.GrandTotal>0,"Benchmark fixture is empty");
            (version==0?previous:version==1?boundedPrevious:improved).Add(watch.Elapsed.TotalMilliseconds);
        }
        var before=previous.Order().ElementAt(3); var boundedBefore=boundedPrevious.Order().ElementAt(3); var after=improved.Order().ElementAt(3);
        Console.WriteLine($"SYNTHETIC LocalDB AlertBoard median (7 rotating runs, same SQL/data, real reads/colors): 120 sequential reads {before:F1} ms; 120 bounded reads {boundedBefore:F1} ms; 12 bounded process reads {after:F1} ms. {before/after:F2}x vs sequential; {boundedBefore/after:F2}x vs previous web scheduling. Not production timings.");
    }

    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    foreach (var read in new Func<Task>[] {
        async () => { await raw.GetFrequencyRawRowsAsync("DCBMMaintenance","LSI1-1F","2026-10-01","2026-10-31",cancelled.Token); },
        async () => { await evidence.GetEvidenceImagesAsync(1,"DC",cancelled.Token); },
        async () => { await te.GetCategoryImageRowsAsync("1",cancelled.Token); },
        async () => { await te.GetQualityImagesAsync("1",cancelled.Token); },
        async () => { await bm.LoadSupplementalDataAsync(report,cancelled.Token); },
        async () => { await mcd.GetMachinesAsync("DC","LSI1-1F",from,to,false,cancelled.Token); }
    }) {
        try { await read(); throw new Exception("Cancelled repository read completed"); }
        catch (OperationCanceledException) { }
    }
    Console.WriteLine("PASS: cancellation propagates through all newly batched/materialized SQL paths.");
} finally {
    SqlConnection.ClearAllPools();
    // The database name is generated above; never accept an external cleanup target.
    Require(database.StartsWith("MachineKarte_RetrievalTest_") && database.Length == "MachineKarte_RetrievalTest_".Length+32, "Invalid cleanup target");
    await Admin($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet build (Join-Path $checkDirectory 'Checks.csproj') --nologo -v minimal -r win-x64 --self-contained false --property:UseAppHost=false -m:1 --nodeReuse:false --property:NuGetAudit=false --property:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'Data retrieval fixture build failed.' }
$runArguments = @((Join-Path $checkDirectory 'bin/Debug/net8.0/win-x64/Checks.dll'), $LocalDbInstance, $repo)
if ($Benchmark) { $runArguments += 'benchmark' }
& $Dotnet @runArguments
if ($LASTEXITCODE -ne 0) { throw 'Data retrieval regression checks failed.' }
