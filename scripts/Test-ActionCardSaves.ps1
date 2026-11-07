param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB')
# Runs real repository/service saves only in a newly created, disposable LocalDB database.
# Never reads appsettings or starts the web app. Drops only its own generated test database.
$ErrorActionPreference = 'Stop'
if ($LocalDbInstance -notmatch '^[A-Za-z0-9_-]+$') { throw 'Expected a local SQL Server LocalDB instance name.' }
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-ActionCardSaves-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
$projectPath = [Security.SecurityElement]::Escape((Join-Path $repo 'MachineKarteWebCore.csproj'))
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><ProjectReference Include="$projectPath" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
@'
using MachineKarte.Web.Models;
using MachineKarte.Web.Repositories;
using MachineKarte.Web.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
Require(args.Length == 1 && System.Text.RegularExpressions.Regex.IsMatch(args[0], @"^[A-Za-z0-9_-]+$"), "LocalDB instance required");
var database = "MachineKarte_ActionCardTest_" + Guid.NewGuid().ToString("N");
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
    ActionCardService Service() => new(new ActionCardRepository(connections), NullLogger<ActionCardService>.Instance);
    var repo = new ActionCardRepository(connections);
    async Task<object?> Sql(string sql) {
        await using var conn = new SqlConnection(connection.ConnectionString); await conn.OpenAsync();
        await using var command = new SqlCommand(sql, conn); return await command.ExecuteScalarAsync();
    }
    var columns = new[] { "ProblemContent", "Incharge", "DateCard", "InvestigationDone", "Action", "MCStatus",
        "Comment", "Date1", "Incharge1", "Comment2", "Date2", "Incharge2", "Comment3", "Date3", "Incharge3",
        "PMReply1", "PMReply2", "PMReply3", "PMIncharge1", "PMIncharge2", "PMIncharge3", "PMDate1", "PMDate2", "PMDate3" };
    var users = new[] { "PM", "TE", "CC", "EEE", "VIS" };
    foreach (var user in users) {
        var table = ActionCardTableMap.GetTable(user);
        await Sql($"CREATE TABLE [dbo].[{table}] (Seqno int IDENTITY PRIMARY KEY, BMID int, MachineNo varchar(50), " +
            string.Join(", ", columns.Select(column => $"[{column}] varchar(max) NULL")) + ")");
    }
    ActionCardData Card(int id, string user, string machine = "MC-A", string content = "original") => new() {
        BmId = id.ToString(), UserType = user, MachineNo = machine, ProblemContent = content,
        DateCard = "11/02/2026 10:56:11", McStatus = "RUNNING (ONGOING INVESTIGATION/MONITORING)"
    };
    ActionCardSaveRequest New(ActionCardData card) => new() { Card = card, StatusId = "New" };
    int checks = 0;
    foreach (var user in users) {
        var table = ActionCardTableMap.GetTable(user);
        // Independent services/connections simulate concurrent requests across web workers.
        var cards = Enumerable.Range(0, 8).Select(index => Card(100, user,
            user == "TE" ? $"TESTER-{index}" : "MC-A", $"winner-{index}")).ToArray();
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = cards.Select(async card => { await start.Task; return await Service().SaveAsync(New(card)); }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(pending);
        Require(results.Count(result => result.Success) == 1, $"{user}: expected exactly one concurrent insert");
        Require(results.Where(result => !result.Success).All(result => result.Message.Contains("already exists")), $"{user}: unexpected save error");
        Require(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE BMID=100")) == 1, $"{user}: duplicate persisted");
        var winner = cards[Array.FindIndex(results, result => result.Success)];
        var loaded = await repo.LoadAsync("100", winner.MachineNo, user);
        Require(loaded is not null && loaded.Seqno > 0 && loaded.ProblemContent == winner.ProblemContent, $"{user}: load lost saved data/identity");
        var stale = await Service().SaveAsync(New(Card(100, user, winner.MachineNo, "stale overwrite")));
        Require(!stale.Success && stale.Message.Contains("already exists"), $"{user}: stale New form was accepted");
        Require((await repo.LoadAsync("100", winner.MachineNo, user))!.ProblemContent == winner.ProblemContent, $"{user}: rejected insert overwrote the existing card");
        var otherMachine = await Service().SaveAsync(New(Card(100, user, "OTHER-MC")));
        Require(otherMachine.Success == (user != "TE"), $"{user}: incorrect legacy lookup key");

        // Seed the existing incident shape; neither load nor save may clean it up implicitly.
        await Sql($"INSERT [dbo].[{table}] (BMID,MachineNo,ProblemContent,DateCard) VALUES " +
            "(256152,'SW-18','older comments','first'),(256152,'SW-18','latest comments','second')");
        var latest = (await repo.LoadAsync("256152", "SW-18", user))!;
        Require(latest.ProblemContent == "latest comments", $"{user}: load did not select the highest Seqno");
        latest.ProblemContent = "edited latest"; latest.McStatus = "RUNNING (LOT EVALUATION)";
        var edit = await Service().SaveAsync(new() { Card = latest, StatusId = "Existing" });
        Require(edit.Success, $"{user}: existing edit failed: {edit.Message}");
        Require(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE BMID=256152")) == 2, $"{user}: an existing duplicate was deleted/inserted");
        Require(Convert.ToInt32(await Sql($"SELECT COUNT(*) FROM [dbo].[{table}] WHERE BMID=256152 AND ProblemContent='older comments' AND DateCard='first'")) == 1, $"{user}: sibling duplicate was overwritten");
        Require((await repo.LoadAsync("256152", "SW-18", user))!.ProblemContent == "edited latest", $"{user}: selected row was not updated");
        latest.BmId = "999";
        Require(!(await Service().SaveAsync(new() { Card = latest, StatusId = "Existing" })).Success, $"{user}: mismatched BM identity was accepted");
        latest.BmId = "256152"; latest.MachineNo = "WRONG-MACHINE";
        Require(!(await Service().SaveAsync(new() { Card = latest, StatusId = "Existing" })).Success, $"{user}: mismatched machine identity was accepted");
        latest.MachineNo = "SW-18"; latest.Seqno = 0;
        Require(!(await Service().SaveAsync(new() { Card = latest, StatusId = "Existing" })).Success, $"{user}: existing edit without Seqno was accepted");
        Require(!(await Service().SaveAsync(new() { Card = Card(200, user), StatusId = "Invalid" })).Success, $"{user}: unknown save mode was accepted");
        Require(!(await Service().SaveAsync(New(Card(0, user)))).Success, $"{user}: invalid BM ID was accepted");
        Require(!(await Service().SaveAsync(New(Card(200, user, "")))).Success, $"{user}: empty machine was accepted");
        checks++;
    }
    Console.WriteLine($"PASS: {checks} user modes; 40 concurrent saves, stale New conflicts, TE BM-only uniqueness, deterministic loads, single-Seqno edits, preserved existing duplicates, identity/mode validation. LocalDB only.");
}
finally {
    SqlConnection.ClearAllPools();
    Require(database.StartsWith("MachineKarte_ActionCardTest_") &&
        Guid.TryParseExact(database["MachineKarte_ActionCardTest_".Length..], "N", out _), "Unsafe test database cleanup target");
    await Admin($"DROP DATABASE [{database}]");
    Console.WriteLine("Removed the disposable LocalDB test database.");
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet restore (Join-Path $checkDirectory 'Checks.csproj') --ignore-failed-sources --disable-parallel -m:1 --property:NuGetAudit=false
if ($LASTEXITCODE -ne 0) { throw 'Action Card regression runner restore failed.' }
& $Dotnet build (Join-Path $checkDirectory 'Checks.csproj') --no-restore --nologo -m:1 --property:UseSharedCompilation=false --output (Join-Path $checkDirectory 'bin') -v minimal
if ($LASTEXITCODE -ne 0) { throw 'Action Card regression runner build failed.' }
& $Dotnet (Join-Path $checkDirectory 'bin/Checks.dll') $LocalDbInstance
if ($LASTEXITCODE -ne 0) { throw 'Action Card save regression checks failed.' }
