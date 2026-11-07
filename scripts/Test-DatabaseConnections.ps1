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
using System.Collections.Concurrent;
using System.Diagnostics;
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
Task<SqlConnection> Success(string value, bool validate, CancellationToken token) => Task.FromResult(new SqlConnection(value));
async Task Unavailable(DatabaseConnectionResolver resolver) {
    try { await resolver.GetConnectionStringAsync(); throw new Exception("Unavailable endpoints were accepted."); }
    catch (InvalidOperationException ex) { Require(ex.Message.Contains("No database connection is available"), "Missing outage diagnostic."); }
}

var log = new TestLogger();
var attempts = new ConcurrentQueue<(string Value, bool Validate)>();
bool machineAvailable = true, intranetAvailable = true;
TaskCompletionSource? recoveryRelease = null;
using var resolver = new DatabaseConnectionResolver(Config(), log, async (value, validate, token) => {
    attempts.Enqueue((value, validate));
    if (value == primary && !machineAvailable || value == fallback && !intranetAvailable)
        throw new TimeoutException("secret");
    if (value == primary && validate && recoveryRelease is not null)
        await recoveryRelease.Task.WaitAsync(token);
    return new SqlConnection(value);
}, TimeProvider.System);
await resolver.InitializeAsync();
Require(resolver.ActiveConnectionName == "MachineDBConnection" && attempts.Count == 1, "Startup must prefer and validate only primary.");
var cachedTask = resolver.GetConnectionStringAsync();
Require(ReferenceEquals(cachedTask, resolver.GetConnectionStringAsync()), "Cached lookup allocated a new resolution task.");
var benchmark = Stopwatch.StartNew();
for (var i = 0; i < 100_000; i++)
    Require(await resolver.GetConnectionStringAsync() == primary, "Cached selection changed.");
benchmark.Stop();
Require(attempts.Count == 1, "Cached string lookup performed network operations.");
Console.WriteLine($"MEASURE: 100,000 cached resolver lookups={benchmark.Elapsed.TotalMilliseconds:F2}ms; {benchmark.Elapsed.TotalMilliseconds * 10:F3}ns/lookup; zero extra connections/probes (offline).");
await using (var connection = await resolver.OpenConnectionAsync())
    Require(connection.ConnectionString == primary, "Normal acquisition changed primary.");
Require(attempts.Last() == (primary, false), "Normal repository acquisition ran SELECT 1.");

machineAvailable = false;
await using (var connection = await resolver.OpenConnectionAsync())
    Require(connection.ConnectionString == fallback, "Failed primary open must transparently fall back before SQL.");
Require(resolver.ActiveConnectionName == "IntranetDBConnection", "Fallback was not cached.");
var failedPrimaryCount = attempts.Count(value => value.Value == primary);
await using (var connection = await resolver.OpenConnectionAsync())
    Require(connection.ConnectionString == fallback, "Cached fallback was lost.");
Require(attempts.Last() == (fallback, false) && attempts.Count(value => value.Value == primary) == failedPrimaryCount,
    "Normal fallback use re-probed primary or ran SELECT 1.");

// A slow background recovery probe must not delay reads or opens on the usable fallback.
machineAvailable = true;
recoveryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
var recovery = resolver.RefreshAsync();
Require(!recovery.IsCompleted, "Recovery probe did not wait for its simulated network.");
var followers = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => resolver.GetConnectionStringAsync()));
Require(followers.All(value => value == fallback), "Recovery blocked cached fallback lookups.");
await using (var connection = await resolver.OpenConnectionAsync())
    Require(connection.ConnectionString == fallback, "Recovery blocked normal fallback acquisition.");
recoveryRelease.SetResult();
await recovery;
recoveryRelease = null;
Require(await resolver.GetConnectionStringAsync() == primary, "Background recovery did not restore primary.");

machineAvailable = intranetAvailable = false;
await resolver.RefreshAsync();
var failedCount = attempts.Count;
await Unavailable(resolver); await Unavailable(resolver);
Require(attempts.Count == failedCount, "Known total outage repeated discovery for each request.");
intranetAvailable = true;
await resolver.RefreshAsync();
Require(await resolver.GetConnectionStringAsync() == fallback, "Background refresh did not recover from total outage.");
Require(log.Messages.Any(m => m.Contains("Selected MachineDBConnection")) &&
    log.Messages.Any(m => m.Contains("Falling back to IntranetDBConnection")) &&
    !log.Messages.Any(m => m.Contains("secret")), "Selection diagnostics are missing or expose credentials.");

// Concurrent cold callers share discovery; cancelling one waiter cannot cancel it for others.
int coldProbes = 0;
var coldRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var cold = new DatabaseConnectionResolver(Config(), new TestLogger(), async (value, validate, token) => {
    Interlocked.Increment(ref coldProbes);
    await coldRelease.Task.WaitAsync(token);
    return new SqlConnection(value);
}, TimeProvider.System);
using var cancelled = new CancellationTokenSource();
var cancelledWait = cold.GetConnectionStringAsync(cancelled.Token);
var coldWaiters = Enumerable.Range(0, 16).Select(_ => cold.GetConnectionStringAsync()).ToArray();
cancelled.Cancel();
try { await cancelledWait; throw new Exception("Cancelled discovery waiter completed."); } catch (OperationCanceledException) { }
Require(coldProbes == 1, "Concurrent cold calls duplicated connectivity validation.");
coldRelease.SetResult();
Require((await Task.WhenAll(coldWaiters)).All(value => value == primary), "Cancelling one request cancelled shared discovery.");
try { await cold.OpenConnectionAsync(cancelled.Token); throw new Exception("Warm open ignored cancellation."); } catch (OperationCanceledException) { }
Require(coldProbes == 1, "Cancelled warm open made a connection.");

// The hosted service starts discovery itself and yields before network completion.
var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using var hosted = new DatabaseConnectionResolver(Config(), new TestLogger(), async (value, validate, token) => {
    started.TrySetResult();
    await Task.Delay(Timeout.Infinite, token);
    return new SqlConnection(value);
}, TimeProvider.System);
await hosted.StartAsync(CancellationToken.None);
await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
Require(hosted.ActiveConnectionName is null, "Host startup waited for a database probe.");
await hosted.StopAsync(CancellationToken.None);

foreach (var missing in new string?[] { null, "", " " }) {
    using var check = new DatabaseConnectionResolver(Config(missing), new TestLogger(), Success, TimeProvider.System);
    Require(await check.GetConnectionStringAsync() == fallback, "Missing primary must use fallback.");
}
using var legacyOnly = new DatabaseConnectionResolver(Config(null, null, primary), new TestLogger(), Success, TimeProvider.System);
Require(await legacyOnly.GetConnectionStringAsync() == primary, "Old-only configuration stopped working.");
using var precedence = new DatabaseConnectionResolver(Config(legacy: fallback), new TestLogger(), Success, TimeProvider.System);
Require(await precedence.GetConnectionStringAsync() == primary, "Legacy setting displaced the new primary.");
using var timeoutCheck = new DatabaseConnectionResolver(Config(), new TestLogger(), async (value, validate, token) => {
    if (value == primary) await Task.Delay(Timeout.Infinite, token);
    return new SqlConnection(value);
}, TimeProvider.System);
Require(await timeoutCheck.GetConnectionStringAsync() == fallback, "Validation timeout did not fall back.");

// Exercise actual SqlClient parsing without a network connection.
var failedLog = new TestLogger();
using var malformed = new DatabaseConnectionResolver(Config("BadKeyword=secret", "BadKeyword=secret"), failedLog);
await Unavailable(malformed);
Require(failedLog.Messages.Count(m => m.Contains("failed validation")) == 2 &&
    !failedLog.Messages.Any(m => m.Contains("secret")), "Malformed settings were not safely diagnosed.");
Console.WriteLine("PASS: startup discovery, cached lookup, probe-free acquisition, open failure fallback, background recovery, single-flight discovery, total outage caching, cancellation, legacy settings and safe logs. No live database contacted.");

sealed class TestLogger : ILogger<DatabaseConnectionResolver> {
    public ConcurrentQueue<string> Messages { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Messages.Enqueue(formatter(state, exception));
}
'@ | Set-Content -Encoding UTF8 (Join-Path $checkDirectory 'Program.cs')
& $Dotnet run --project (Join-Path $checkDirectory 'Checks.csproj') --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Database connection checks failed.' }
