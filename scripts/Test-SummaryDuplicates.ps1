param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB')
# Execute the real summary SQL against session-local #tables in LocalDB tempdb.
# No application connection string, production database, or persistent test data is used.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-SummaryDuplicates-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
$projectPath = [System.Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
@'
using System.Data;
using System.Reflection;
using System.Text.RegularExpressions;
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
using MachineKarte.Web.Services;
using Microsoft.Data.SqlClient;

var flags = BindingFlags.Static | BindingFlags.NonPublic;
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
string AlertSql(string process, string user) {
    object Call(string method, params object[] values) => typeof(AlertBoardRepository).GetMethod(method, flags)!.Invoke(null, values)!;
    return (string)(user == "TE" ? Call("BuildQueryTE", process)
        : Call("BuildQueryNormal", process, Call("BuildAddQuery", user), Call("BuildActionTable", user)));
}
string AnalyticsSql(string process, bool te) => string.Format((string)typeof(BmAnalyticsRepository)
    .GetField(te ? "TeSql" : "EeeSql", flags)!.GetRawConstantValue()!, process);
var processes = new[] { "DC", "DB", "WB", "MP", "PL", "ATC", "FL", "SING", "FT", "TP", "EDS", "MEMS" };
var testers = new[] { "FL", "FT", "TP", "EDS", "MEMS" };
var actionTables = new[] { "MachineKarte_ActionCard", "MachineKarte_ActionCard_CC", "MachineKarte_ActionCard_EEE", "MachineKarte_ActionCard_VIS", "MachineKarte_ActionCard_TE" };
var fixtureTables = processes.Select(process => process + "BMMaintenance")
    .Concat(actionTables).Concat(new[] { "TEBMMaintenance", "BMCase", "BMTEUnit", "BMContent", "MachineKarte_MCD" }).ToHashSet(StringComparer.OrdinalIgnoreCase);
string FixtureSql(string sql) {
    sql = Regex.Replace(sql, @"\b(FROM|JOIN)\s+(?:\[eMaintenanceSystemDB\]\.\[dbo\]\.)?\[?(\w+)\]?", match => {
        var table = match.Groups[2].Value;
        Require(fixtureTables.Contains(table), $"Unexpected table: {table}");
        return match.Groups[1].Value + " #" + table;
    }, RegexOptions.IgnoreCase);
    Require(!sql.Contains("eMaintenanceSystemDB", StringComparison.OrdinalIgnoreCase), "Application database reference escaped fixture rewriting");
    return sql;
}
var connection = new SqlConnectionStringBuilder {
    DataSource = "(localdb)\\" + args[0], InitialCatalog = "tempdb", IntegratedSecurity = true,
    Encrypt = SqlConnectionEncryptOption.Optional, TrustServerCertificate = true, ConnectTimeout = 10
};
await using var conn = new SqlConnection(connection.ConnectionString);
await conn.OpenAsync();
async Task Execute(string sql) { await using var command = new SqlCommand(sql, conn); await command.ExecuteNonQueryAsync(); }
await Execute(@"
CREATE TABLE #BMCase (BMCaseID int PRIMARY KEY, BMCase varchar(100));
INSERT #BMCase VALUES (1,'CASE');
CREATE TABLE #BMTEUnit (BMTEUnitID int PRIMARY KEY, BMTEUnit varchar(100));
INSERT #BMTEUnit VALUES (1,'UNIT');
CREATE TABLE #BMContent (BMContentID int PRIMARY KEY, Process varchar(10), BMContent varchar(100));
INSERT #BMContent VALUES (48,'DC','CONTENT');
CREATE TABLE #MachineKarte_MCD (Seqno int PRIMARY KEY, DeviceName varchar(100), IsActive bit);
INSERT #MachineKarte_MCD VALUES (1,'DEVICE A',1),(2,'DEVICEA',1),(3,'INACTIVE',0);
CREATE TABLE #Rows (
    Process varchar(10), Location varchar(30), Mcno varchar(50), TesterNo varchar(50), MCType varchar(50),
    TimeOfRequest datetime, BMID int, BMCaseID int, BMUnitID int, BMContentID int, AcredBMContentID int,
    Devicename varchar(100), PMCategoryID int, BMStatusID int, Turnover varchar(10), Priority varchar(30));
INSERT #Rows VALUES
('DC','LSI2-3F','MC-A','MC-A','TYPE','2026-11-01',1,1,1,48,0,'DEV ICEA',1,6,'CC','QUALITY BM'),
('DC','LSI2-3F','MC-A','MC-A','TYPE','2026-10-31',2,1,1,48,0,'DEV ICEA',1,6,'PE','QUALITY BM'),
('DC','LSI2-3F','MC-B','MC-B','TYPE','2026-10-30',3,1,1,48,0,'DEV ICEA',1,6,'VIS','QUALITY BM'),
('DC','LSI2-3F','MC-C','MC-C','TYPE','2026-10-29',4,1,1,48,0,'INACTIVE',1,6,'OTHER','QUALITY BM'),
('DC','LSI2-3F','MC-D','MC-D','TYPE','2026-10-28',5,1,1,48,0,'UNREGISTERED',1,6,'OTHER','QUALITY BM'),
('DC','LSI2-3F','MC-E','MC-E','TYPE','2026-10-27',6,1,1,48,0,NULL,1,6,'OTHER','QUALITY BM'),
('DC','OTHER','MC-X','MC-X','TYPE','2026-11-01',7,1,1,48,0,'DEVICEA',1,6,'CC','QUALITY BM'),
('DC','LSI2-3F','MC-X','MC-X','TYPE','2025-11-01',8,1,1,48,0,'DEVICEA',1,6,'CC','QUALITY BM'),
('DC','LSI2-3F','MC-X','MC-X','TYPE','2026-11-01',9,1,1,48,0,'DEVICEA',1,30,'CC','QUALITY BM');
");
foreach (var table in actionTables) {
    await Execute($@"CREATE TABLE #{table} (Seqno int PRIMARY KEY, BMID int, MachineNo varchar(50), MCStatus varchar(50));
INSERT #{table} VALUES (1,1,'MC-A','OLD'),(2,1,'MC-A','LATEST'),(3,2,'OTHER-MC','OTHER'),(4,3,'MC-B','SINGLE');");
}
foreach (var process in processes) await Execute($"SELECT * INTO #{process}BMMaintenance FROM #Rows; UPDATE #{process}BMMaintenance SET Process = '{process}';");
await Execute("SELECT TOP (0) * INTO #TEBMMaintenance FROM #Rows;");
foreach (var process in testers) await Execute($"INSERT #TEBMMaintenance SELECT * FROM #Rows; UPDATE #TEBMMaintenance SET Process = '{process}' WHERE Process = 'DC';");
int reproduced = 0;
foreach (var sql in new[] {
    "SELECT ISNULL((SELECT 'Yes' FROM #MachineKarte_ActionCard WHERE bmid=1 AND machineno='MC-A'),'No')",
    "SELECT ISNULL((SELECT 'Yes' FROM #MachineKarte_MCD WHERE REPLACE(DeviceName,' ','')='DEVICEA' AND IsActive=1),'No')"
}) {
    try { await Execute(sql); throw new Exception("Old scalar lookup unexpectedly accepted duplicates"); }
    catch (SqlException error) when (error.Number == 512) { reproduced++; }
}
async Task<DataTable> Read(string sql, string process) {
    await using var command = new SqlCommand(FixtureSql(sql), conn);
    command.Parameters.AddWithValue("@Range1", new DateTime(2026,10,3));
    command.Parameters.AddWithValue("@Range2", new DateTime(2026,11,3));
    command.Parameters.AddWithValue("@Location", "LSI2-3F");
    command.Parameters.AddWithValue("@Process", process);
    await using var reader = await command.ExecuteReaderAsync();
    var result = new DataTable(); result.Load(reader); return result;
}
int checks = 0;
foreach (var user in new[] { "PM", "CC", "EEE", "VIS", "TE" })
foreach (var process in user == "TE" ? testers : processes) {
    var rows = await Read(AlertSql(process, user), process);
    var expectedIds = user switch { "CC" => new[] {1}, "EEE" => new[] {2}, "VIS" => new[] {3}, _ => new[] {1,2,3,4,5,6} };
    Require(rows.Rows.Cast<DataRow>().Select(row => (int)row["bmid"]).Order().SequenceEqual(expectedIds), $"Alert/{user}/{process}: duplicate rows or filters changed");
    foreach (DataRow row in rows.Rows) {
        int id = (int)row["bmid"];
        bool expected = id == 1 || id == 3 || (user == "TE" && id == 2);
        Require((string)row["HaveAction"] == (expected ? "Yes" : "No"), $"Alert/{user}/{process}/{id}: wrong machine/action correlation");
        if (id == 1) Require((string)row["MCStatus"] == "LATEST", "Latest action status not selected");
    }
    checks++;
}
foreach (var te in new[] { false, true })
foreach (var process in te ? testers : processes) {
    var rows = await Read(AnalyticsSql(process, te), process);
    Require(rows.Rows.Count == 6 && rows.Rows.Cast<DataRow>().Select(row => (int)row["bmid"]).Distinct().Count() == 6, $"Analytics/{te}/{process}: duplicate lookup rows multiplied BM counts");
    foreach (DataRow row in rows.Rows) {
        int id = (int)row["bmid"];
        Require((string)row["HaveAction"] == (id is 1 or 3 ? "Yes" : "No"), $"Analytics/{te}/{process}/{id}: machine correlation changed");
        Require((string)row["isMCD"] == (id <= 3 ? "Yes" : "No"), $"Analytics/{te}/{process}/{id}: normalized/active/null MCD match changed");
        if (id == 1) Require((string)row["MCStatus"] == "LATEST", "Analytics did not select the latest status");
    }
    if (process == "DC" || (te && process == "FL")) {
        var data = rows.Rows.Cast<DataRow>().Select(row => new BmAnalyticsRow {
            Process=process, Location="LSI2-3F", Mcno=(string)row["Mcno"],
            HaveAction=(string)row["HaveAction"], IsMCD=(string)row["isMCD"] }).ToList();
        foreach (var profile in new[] { "8", "12" }) {
            var summary = new BmAnalyticsResult();
            typeof(BmAnalyticsService).GetMethod("ApplyNewLoadColor", flags)!.Invoke(null, new object[] { data, summary, profile });
            Require(summary.Cells.Values.Sum(cell => cell.Count) == 2, $"Analytics profile {profile}: expected two qualifying BM rows");
        }
    }
    checks++;
}
Console.WriteLine($"PASS: reproduced {reproduced} SQL error 512 cases; {checks} actual query variants execute against duplicate action/MCD rows without errors or count multiplication. User/machine/date/status filters, TE matching differences, latest status and Form8/12 qualification are retained.");
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet restore (Join-Path $checkDirectory 'Checks.csproj') --ignore-failed-sources --disable-parallel -m:1 --property:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Duplicate-summary runner restore failed.' }
& $Dotnet build (Join-Path $checkDirectory 'Checks.csproj') --no-restore --nologo -m:1 --property:UseSharedCompilation=false --output (Join-Path $checkDirectory 'bin') -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Duplicate-summary runner build failed.' }
& $Dotnet (Join-Path $checkDirectory 'bin/Checks.dll') $LocalDbInstance
if ($LASTEXITCODE -ne 0) { throw 'Duplicate-summary SQL regression checks failed.' }
