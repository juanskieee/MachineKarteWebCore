param([string]$Dotnet = 'dotnet')
# Focused resolver checks. No application settings or live databases are loaded.
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$checkDirectory = Join-Path $env:TEMP ('MachineKarte-Connections-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $checkDirectory | Out-Null
[xml]$appProject = Get-Content -Raw -LiteralPath (Join-Path $repo 'MachineKarteWebCore.csproj')
$sqlClientVersion = ($appProject.Project.ItemGroup.PackageReference | Where-Object { $_.Include -eq 'Microsoft.Data.SqlClient' }).Version
@"
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup>
  <ItemGroup><PackageReference Include="Microsoft.Data.SqlClient" Version="$sqlClientVersion" /></ItemGroup>
</Project>
"@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Checks.csproj')
# Compile the actual resolver in the runner so its internal connectivity seam is accessible.
$source = [IO.File]::ReadAllText((Join-Path $repo 'Services/DatabaseConnectionResolver.cs'))
$source.Replace('namespace MachineKarte.Web.Services;', 'namespace ConnectionChecks;') |
    Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'DatabaseConnectionResolver.cs')
@'
using ConnectionChecks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

const string primary = "Server=machine;Database=fixture;User ID=test;Password=secret;TrustServerCertificate=True";
const string fallback = "Server=intranet;Database=fixture;User ID=test;Password=secret;TrustServerCertificate=True";
void Require(bool value, string message) { if (!value) throw new Exception(message); }
IConfiguration Config(string? machine = primary, string? intranet = fallback, string? legacy = null) =>
    new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
        ["ConnectionStrings:MachineDBConnection"] = machine,
        ["ConnectionStrings:IntranetDBConnection"] = intranet,
        ["ConnectionStrings:DBConnection"] = legacy,
        ["DatabaseConnection:ValidationTimeoutSeconds"] = "1"
    }).Build();
Task<SqlConnection> Success(string value, CancellationToken token) => Task.FromResult(new SqlConnection(value));
var clock = new Clock();
var log = new TestLogger();
var attempts = new List<string>();
bool machineAvailable = true;
var resolver = new DatabaseConnectionResolver(Config(), log, (value, token) => {
    attempts.Add(value);
    if (value == primary && !machineAvailable) throw new TimeoutException("secret");
    return Success(value, token);
}, clock);
Require(await resolver.GetConnectionStringAsync() == primary, "Healthy primary must win.");
Require(attempts.SequenceEqual(new[] { primary }), "Healthy primary must not contact fallback.");
machineAvailable = false;
Require(await resolver.GetConnectionStringAsync() == fallback, "Primary failure must fall back.");
Require(await resolver.GetConnectionStringAsync() == fallback, "Fallback must remain usable during cooldown.");
Require(attempts.Count(value => value == primary) == 2, "Unavailable primary was repeatedly probed during cooldown.");
machineAvailable = true;
clock.Advance();
Require(await resolver.GetConnectionStringAsync() == primary, "Recovered primary must be preferred after cooldown.");
Require(log.Messages.Any(m => m.Contains("Selected MachineDBConnection")) &&
    log.Messages.Any(m => m.Contains("Falling back to IntranetDBConnection")), "Missing selection diagnostics.");
Require(!log.Messages.Any(m => m.Contains("secret")), "Logs exposed a credential or exception message.");

foreach (var failure in new Exception[] { new ArgumentException(), new InvalidOperationException(), new TimeoutException() }) {
    var check = new DatabaseConnectionResolver(Config(), new TestLogger(), (value, token) =>
        value == primary ? Task.FromException<SqlConnection>(failure) : Success(value, token), clock);
    Require(await check.GetConnectionStringAsync() == fallback, "Connectivity/configuration failure did not fall back.");
}
foreach (var missing in new string?[] { null, "", " " }) {
    var check = new DatabaseConnectionResolver(Config(missing), new TestLogger(), Success, clock);
    Require(await check.GetConnectionStringAsync() == fallback, "Missing primary must use fallback.");
}
var legacyOnly = new DatabaseConnectionResolver(Config(null, null, primary), new TestLogger(), Success, clock);
Require(await legacyOnly.GetConnectionStringAsync() == primary, "Old-only configuration stopped working.");
var precedence = new DatabaseConnectionResolver(Config(legacy: fallback), new TestLogger(), Success, clock);
Require(await precedence.GetConnectionStringAsync() == primary, "Legacy setting displaced the new primary setting.");

var timeouts = new List<string>();
var timeoutCheck = new DatabaseConnectionResolver(Config(), new TestLogger(), async (value, token) => {
    timeouts.Add(value);
    if (value == primary) await Task.Delay(Timeout.Infinite, token);
    return new SqlConnection(value);
}, clock);
Require(await timeoutCheck.GetConnectionStringAsync() == fallback && timeouts.Count == 2,
    "Validation budget cancellation did not trigger fallback.");
var cancelledAttempts = new List<string>();
using var cancellation = new CancellationTokenSource();
var cancellationCheck = new DatabaseConnectionResolver(Config(), new TestLogger(), (value, token) => {
    cancelledAttempts.Add(value);
    cancellation.Cancel();
    return Task.FromCanceled<SqlConnection>(token);
}, clock);
try { await cancellationCheck.GetConnectionStringAsync(cancellation.Token); throw new Exception("Cancellation was ignored."); }
catch (OperationCanceledException) { }
Require(cancelledAttempts.SequenceEqual(new[] { primary }), "Caller cancellation attempted fallback.");

int primaryProbes = 0;
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var concurrent = new DatabaseConnectionResolver(Config(), new TestLogger(), async (value, token) => {
    if (value == primary) {
        if (Interlocked.Increment(ref primaryProbes) == 1) throw new TimeoutException();
        await release.Task.WaitAsync(token);
    }
    return new SqlConnection(value);
}, clock);
Require(await concurrent.GetConnectionStringAsync() == fallback, "Initial fallback failed.");
clock.Advance();
var recovery = concurrent.GetConnectionStringAsync();
var followers = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => concurrent.GetConnectionStringAsync()));
Require(followers.All(value => value == fallback) && primaryProbes == 2, "Concurrent recovery duplicated primary probes or blocked fallback.");
release.SetResult();
Require(await recovery == primary, "Recovery probe did not restore primary.");

// Exercise the real SqlClient path against invalid settings, without any network access.
var failedLog = new TestLogger();
var unavailable = new DatabaseConnectionResolver(Config("BadKeyword=secret", "BadKeyword=secret"), failedLog);
try { await unavailable.OpenConnectionAsync(); throw new Exception("Both failed connections were accepted."); }
catch (InvalidOperationException ex) { Require(ex.Message.Contains("No database connection is available"), "Missing all-connections-failed diagnostic."); }
Require(failedLog.Messages.Count(m => m.Contains("failed validation")) == 2 &&
    !failedLog.Messages.Any(m => m.Contains("secret")), "Failure diagnostics are missing or reveal credentials.");
Console.WriteLine("PASS: primary preference, fallback, missing/malformed configuration, legacy compatibility, validation timeout, cancellation, cooldown, concurrent recovery and safe diagnostics. No live database contacted.");

sealed class Clock : TimeProvider {
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance() => _now += TimeSpan.FromSeconds(31);
}
sealed class TestLogger : ILogger<DatabaseConnectionResolver> {
    public List<string> Messages { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Add(formatter(state, exception));
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Database connection checks failed.' }
