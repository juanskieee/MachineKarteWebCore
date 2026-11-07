param([string]$Dotnet = 'dotnet', [switch]$Sql, [string]$LocalDbInstance = 'MSSQLLocalDB')
# Only Monitoring order/date fixtures. Default is offline; -Sql adds synthetic LocalDB rows.
$ErrorActionPreference = 'Stop'
if ($LocalDbInstance -notmatch '^[A-Za-z0-9_-]+$') { throw 'Expected a LocalDB instance name.' }
$repo = Split-Path $PSScriptRoot -Parent
$repository = [IO.File]::ReadAllText((Join-Path $repo 'Repositories/MonitoringRepository.cs'))
$service = [IO.File]::ReadAllText((Join-Path $repo 'Services/MonitoringService.cs'))
$legacy = [IO.File]::ReadAllText((Join-Path $repo 'Legacy/Form1.vb.txt'))
$legacyCard = [IO.File]::ReadAllText((Join-Path $repo 'Legacy/uc_Test.vb.txt'))
if ($legacy -notmatch 'ORDER BY MACHINENAME desc,MCNO asc' -or
    $legacy -notmatch 'ORDER BY machinename desc,a.MCType asc' -or
    $legacy -notmatch 'UCNum.Left = uCWidth \* x' -or
    $legacyCard -notmatch 'CreatePanel.Dock = DockStyle.Bottom') {
    throw 'The original descending count order, left-to-right placement, or bottom docking changed.'
}
$dateMatches = [regex]::Matches($repository,
    'WHERE\s+(a\.TimeOfRequest\s+>=\s+@Range1\s+AND\s+a\.TimeOfRequest\s+<\s+DATEADD\(DAY,1,@Range2\))')
if ($dateMatches.Count -ne 3) { throw 'PM/TE header counts and card reads must share the same full-To-day range.' }
$datePredicate = $dateMatches[0].Groups[1].Value
$orders = [regex]::Matches($repository, '"(MACHINENAME DESC,[^"]+)"')
if ($orders.Count -ne 2) { throw 'Could not locate the PM/TE column orders.' }
$machineOrder = $orders[0].Groups[1].Value
$testerOrder = $orders[1].Groups[1].Value
$sort = [regex]::Match($service,
    'summaries = summaries\.OrderByDescending\(summary =>\s*columnRows\[\(summary\.MachineNo, summary\.MachineType\)\]\.Count\(\)\)\.ToList\(\);')
if (!$sort.Success -or $sort.Index -gt $service.IndexOf('summaries.Skip(')) {
    throw 'The complete list must be stably ordered by displayed card count before paging.'
}
Write-Output 'PASS: legacy descending count order, left-to-right placement, bottom docking, and server ordering before pagination'
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-Staircase-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
[IO.File]::WriteAllText((Join-Path $checkDirectory 'Checks.csproj'), @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><UseAppHost>false</UseAppHost><NuGetAudit>false</NuGetAudit><EnableNETAnalyzers>false</EnableNETAnalyzers></PropertyGroup>
</Project>
'@)
# Execute the production sort statement verbatim; no app build or database is needed.
[IO.File]::WriteAllText((Join-Path $checkDirectory 'Program.cs'), @"
void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
var summaries = new[] {
    new { MachineNo = "MC-A", MachineType = "TYPE" },
    new { MachineNo = "MC-B", MachineType = "TYPE" },
    new { MachineNo = "MC-C", MachineType = "TYPE" }
}.ToList();
var columnRows = summaries.SelectMany((summary,index) =>
    Enumerable.Repeat(summary, new[] {2,5,2}[index]))
    .ToLookup(row => (row.MachineNo,row.MachineType));
$($sort.Value)
Require(string.Join(",",summaries.Select(s=>s.MachineNo))=="MC-B,MC-A,MC-C",
    "Displayed card counts did not override header order or ties were unstable.");
summaries = Enumerable.Range(0,23).Select(i=>new { MachineNo="MC-"+i.ToString("D2"), MachineType="TYPE" }).ToList();
columnRows = summaries.SelectMany((s,index)=>Enumerable.Repeat(s,index+1))
    .ToLookup(row=>(row.MachineNo,row.MachineType));
$($sort.Value)
Require(summaries.Select(s=>columnRows[(s.MachineNo,s.MachineType)].Count())
    .SequenceEqual(Enumerable.Range(1,23).Reverse()),"Global staircase is not descending.");
Require(summaries.Take(10).Count()==10 && summaries.Skip(10).Take(10).Count()==10 &&
    summaries.Skip(20).Count()==3 && summaries.Skip(10).First().MachineNo=="MC-12",
    "Ordering happened after pagination.");
var from = new DateTime(2026,11,1); var to = new DateTime(2026,11,7);
var times = new[] { from.AddMilliseconds(-3), from, to, to.AddHours(12),
    to.AddDays(1).AddMilliseconds(-3), to.AddDays(1) };
Require(times.Count(time=>time>=from && time<to.AddDays(1))==4,
    "The full To day or exclusion of the next day changed.");
Console.WriteLine("PASS: production sort gives 5/2/2 stacks, stable ties, 23 columns across three pages, and full-To-day boundaries");
"@)
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --verbosity quiet --property:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw 'The focused offline staircase check failed.' }
if (!$Sql) { return }
$sql = @"
SET NOCOUNT ON;
DECLARE @Range1 datetime = '20261101', @Range2 datetime = '20261107';
DECLARE @BM TABLE (MCNO varchar(10), MCType varchar(10), TesterNo varchar(10), actionline int, TimeOfRequest datetime);
INSERT @BM VALUES
('MC-A','Z','TE-A',4,'2026-11-01'),('MC-A','Z','TE-A',4,'2026-11-02'),
('MC-B','B','TE-B',4,'2026-11-03'),('MC-B','B','TE-B',4,'2026-11-07T09:00:00'),
('MC-B','B','TE-B',4,'2026-11-07T12:00:00'),('MC-B','B','TE-B',4,'2026-11-07T18:00:00'),
('MC-B','B','TE-B',4,'2026-11-07T23:59:59.997'),
('MC-C','A','TE-C',4,'2026-11-07'),('MC-C','A','TE-C',4,'2026-11-07T15:00:00'),
('OUT','A','OUT',4,'2026-10-31T23:59:59.997'),('OUT','A','OUT',4,'2026-11-08');
SELECT COUNT_BIG(*) AS MACHINENAME, MCNO, MCType, TesterNo, actionline
INTO #Counts FROM @BM a WHERE $datePredicate
GROUP BY MCNO, MCType, TesterNo, actionline;
IF (SELECT SUM(MACHINENAME) FROM #Counts) <> 9 OR (SELECT COUNT(*) FROM #Counts) <> 3
    THROW 51000, 'Date range omitted To-day cards or included outside dates.', 1;
IF (SELECT MACHINENAME FROM #Counts WHERE MCNO='MC-B') <> 5
    THROW 51000, 'The tallest column lost its To-day cards.', 1;
DECLARE @machineOrder varchar(100), @testerOrder varchar(100);
SELECT @machineOrder = STRING_AGG(MCNO, ',') WITHIN GROUP (ORDER BY $machineOrder) FROM #Counts;
SELECT @testerOrder = STRING_AGG(TesterNo, ',') WITHIN GROUP (ORDER BY $testerOrder) FROM #Counts;
IF @machineOrder <> 'MC-B,MC-A,MC-C' THROW 51000, 'PM column count/tie order differs from legacy.', 1;
IF @testerOrder <> 'TE-B,TE-C,TE-A' THROW 51000, 'TE column count/type order differs from legacy.', 1;
IF (SELECT COUNT(*) FROM @BM WHERE MCNO='MC-B' AND TimeOfRequest BETWEEN @Range1 AND @Range2) <> 1
    THROW 51000, 'The fixture does not reproduce the former midnight cutoff.', 1;
PRINT 'PASS: descending 5/2/2 card stacks, PM/TE tie rules, full To day, and exclusion of next-day midnight';
DROP TABLE #Counts;
"@
$sqlcmd = (Get-Command sqlcmd -ErrorAction Stop).Source
& $sqlcmd -S "(localdb)\$LocalDbInstance" -d master -E -l 5 -b -Q $sql
if ($LASTEXITCODE -ne 0) { throw 'The focused staircase SQL check failed.' }
