param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB')
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
foreach ($name in @('RawDataRepository','EvidenceRepository','TeEvidenceRepository','BmReportRepository','McdMonitoringRepository','PmReportRepository')) {
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
using RetrievalChecks.Repositories;

void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
Require(args.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(args[0], @"^[A-Za-z0-9_-]+$"), "LocalDB instance required");
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
        ["ConnectionStrings:DBConnection"] = connection.ConnectionString
    }).Build();
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
    var raw = new RawDataRepository(config);
    var frequency = await raw.GetFrequencyRawRowsAsync("DCBMMaintenance", "LSI1-1F", "2026-10-01", "2026-10-31");
    Require(frequency.Rows.Count == 3 && frequency.Columns.Contains("BMCase"), "Raw frequency skipped its current result set");
    Require(frequency.Rows.Cast<DataRow>().All(row => row["mcno"].ToString() == "MC-Z"), "Raw frequency changed filters");
    var monthly = await raw.GetBmCaseMonthlyRowsAsync("DCBMMaintenance", "LSI1-1F", "2026-10-01", "2026-10-31");
    Require(monthly.Rows.Count == 1 && (int)monthly.Rows[0]["BMCount"] == 3, "Raw monthly results/count changed");
    Require(frequency.Columns["DateNum"]!.DataType == typeof(int), "Raw reader changed column types");
    try { await raw.GetFrequencyRawRowsAsync("DCBMMaintenance];--", "", "", ""); throw new Exception("Raw identifier accepted"); }
    catch (ArgumentException) { }
    var mcd = new McdMonitoringRepository(config);
    var from = new DateTime(2026,10,1); var to = new DateTime(2026,10,31);
    Require((await mcd.GetMachinesAsync("DC","LSI1-1F",from,to,false)).SequenceEqual(new[] {"MC-Z"}), "MCD DISTINCT/order/filter failed");
    Require((await mcd.GetMachinesAsync("FL","LSI1-1F",from,to,true)).SequenceEqual(new[] {"TE-Z"}), "TE MCD DISTINCT/order/filter failed");
    var pm = await new PmReportRepository(config).GetReportDataAsync("DC", 5, "10/05/2026 00:00:00", "MC-Z");
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
    var bm = new BmReportRepository(config);
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
        var resolved = await new PmReportRepository(config).GetLookupValuesAsync(lookupData);
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
    var evidence = new EvidenceRepository(config);
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
    var te = new TeEvidenceRepository(config);
    var teImages = await te.GetCategoryImagesAsync("1");
    foreach (var category in teCategories) {
        var top = category == "TEST BOARD" ? 20 : 6;
        Require(teImages[category+"_BEFORE"].SequenceEqual(Enumerable.Range(1,top).Where(i => i%2==1).Select(i => $"/IMG-{i}")) &&
            teImages[category+"_AFTER"].SequenceEqual(Enumerable.Range(1,top).Where(i => i%2==0).Select(i => $"/IMG-{i}")), "TE TOP/order/condition split changed");
    }
    Require((await te.GetCategoryImagesAsync("999")).Values.All(list => list.Count==0), "TE empty category alignment failed");
    Require((await te.GetCategoryImageRowsAsync("1")).Where(row=>row.Category=="TEST BOARD").All(row=>row.CN=="CN4"),"Test-board connector identifiers were lost");
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

    using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
    foreach (var read in new Func<Task>[] {
        async () => { await raw.GetFrequencyRawRowsAsync("DCBMMaintenance","LSI1-1F","2026-10-01","2026-10-31",cancelled.Token); },
        async () => { await evidence.GetEvidenceImagesAsync(1,"DC",cancelled.Token); },
        async () => { await te.GetCategoryImagesAsync("1",cancelled.Token); },
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
& $Dotnet (Join-Path $checkDirectory 'bin/Debug/net8.0/win-x64/Checks.dll') $LocalDbInstance
if ($LASTEXITCODE -ne 0) { throw 'Data retrieval regression checks failed.' }
