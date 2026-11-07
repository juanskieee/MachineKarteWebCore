using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Services;

// Hosted singleton: discovery and health checks run independently of page requests.
// Individual repositories still own and dispose their SQL connections.
public sealed class DatabaseConnectionResolver : BackgroundService
{
    private const string MachineName = "MachineDBConnection";
    private const string IntranetName = "IntranetDBConnection";
    private sealed record Target(string Name, string? ConnectionString)
    {
        public Task<string> ResolvedString { get; } = Task.FromResult(ConnectionString!);
    }
    private readonly Target[] _targets;
    private readonly ILogger<DatabaseConnectionResolver> _logger;
    private readonly Func<string, bool, CancellationToken, Task<SqlConnection>> _connect;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _validationTimeout;
    private readonly TimeSpan _healthInterval;
    private readonly SemaphoreSlim _selectionGate = new(1, 1);
    private readonly object _initializationLock = new();
    private Task? _initialization;
    private Target? _active;
    private CancellationToken _stoppingToken;

    public DatabaseConnectionResolver(IConfiguration configuration, ILogger<DatabaseConnectionResolver> logger)
        : this(configuration, logger, ConnectAsync, TimeProvider.System) { }

    // The focused offline runner injects connectivity without a live database.
    internal DatabaseConnectionResolver(IConfiguration configuration, ILogger<DatabaseConnectionResolver> logger,
        Func<string, bool, CancellationToken, Task<SqlConnection>> connect, TimeProvider timeProvider)
    {
        _targets = [
            new(MachineName, configuration.GetConnectionString(MachineName)
                ?? configuration.GetConnectionString("DBConnection")),
            new(IntranetName, configuration.GetConnectionString(IntranetName))
        ];
        _logger = logger;
        _connect = connect;
        _timeProvider = timeProvider;
        var timeout = configuration.GetValue("DatabaseConnection:ValidationTimeoutSeconds", 2);
        var interval = configuration.GetValue("DatabaseConnection:PrimaryRetrySeconds", 30);
        if (timeout <= 0 || interval <= 0)
            throw new InvalidOperationException("DatabaseConnection timeouts must be positive seconds.");
        _validationTimeout = TimeSpan.FromSeconds(timeout);
        _healthInterval = TimeSpan.FromSeconds(interval);
    }

    public string? ActiveConnectionName => Volatile.Read(ref _active)?.Name;

    // Cold callers share discovery. A request cancels only its own wait.
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task initialization;
        lock (_initializationLock)
            initialization = _initialization ??= RefreshAsync(_stoppingToken);
        return initialization.WaitAsync(cancellationToken);
    }

    public Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var active = Volatile.Read(ref _active);
        return active is not null ? active.ResolvedString : ResolveStringAsync(cancellationToken);
    }

    private async Task<string> ResolveStringAsync(CancellationToken cancellationToken) =>
        (await GetTargetAsync(cancellationToken)).ConnectionString!;

    private async Task<Target> GetTargetAsync(CancellationToken cancellationToken)
    {
        var active = Volatile.Read(ref _active);
        if (active is not null) return active;
        await InitializeAsync(cancellationToken);
        return Volatile.Read(ref _active) ?? throw Unavailable();
    }

    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = await GetTargetAsync(cancellationToken);
        var connection = await TryConnectAsync(target, validate: false, cancellationToken);
        if (connection is not null) return connection;

        // Fail over only before repository SQL; never replay a query or write.
        await _selectionGate.WaitAsync(cancellationToken);
        try
        {
            var current = Volatile.Read(ref _active) ?? throw Unavailable();
            var failed = new HashSet<string> { target.Name };
            if (current != target)
            {
                connection = await TryConnectAsync(current, validate: false, cancellationToken);
                if (connection is not null) return connection;
                failed.Add(current.Name);
            }
            return await SelectAsync(cancellationToken, failed) ?? throw Unavailable();
        }
        finally { _selectionGate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stoppingToken = stoppingToken;
        // The host and HTML rendering never wait for the startup probe.
        await Task.Yield();
        try
        {
            await InitializeAsync(stoppingToken);
            using var timer = new PeriodicTimer(_healthInterval, _timeProvider);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RefreshAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    internal async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _selectionGate.WaitAsync(cancellationToken);
        try
        {
            await using var probe = await SelectAsync(cancellationToken);
        }
        finally { _selectionGate.Release(); }
    }

    private async Task<SqlConnection?> SelectAsync(CancellationToken cancellationToken, HashSet<string>? failed = null)
    {
        foreach (var target in _targets)
        {
            if (failed?.Contains(target.Name) == true) continue;
            var connection = await TryConnectAsync(target, validate: true, cancellationToken);
            if (connection is null) continue;
            Publish(target);
            return connection;
        }
        Publish(null);
        return null;
    }

    private async Task<SqlConnection?> TryConnectAsync(Target target, bool validate, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_validationTimeout);
        try
        {
            if (string.IsNullOrWhiteSpace(target.ConnectionString))
                throw new InvalidOperationException("Connection setting is missing or empty.");
            return await _connect(target.ConnectionString, validate, budget.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is SqlException or TimeoutException or OperationCanceledException or ArgumentException or InvalidOperationException)
        {
            // Raw exception messages and connection strings may contain credentials.
            var sql = exception as SqlException;
            _logger.LogWarning("Database connection {ConnectionName} failed {Operation}: {FailureType}; SQL number {SqlNumber}, state {SqlState}, class {SqlClass}. Connection budget {TimeoutSeconds}s. Check network, SQL availability, login and database permissions.",
                target.Name, validate ? "validation" : "opening", exception.GetType().Name,
                sql?.Number, sql?.State, sql?.Class, _validationTimeout.TotalSeconds);
            return null;
        }
    }

    private static async Task<SqlConnection> ConnectAsync(string connectionString, bool validate, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            if (validate)
            {
                await using var command = new SqlCommand("SELECT 1", connection);
                if (!Equals(await command.ExecuteScalarAsync(cancellationToken), 1))
                    throw new InvalidOperationException("Database connectivity validation returned an unexpected result.");
            }
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private void Publish(Target? target)
    {
        var previous = Interlocked.Exchange(ref _active, target);
        if (target is null)
            _logger.LogError("No database connection passed connectivity validation. Background discovery will retry in {RetrySeconds}s.", _healthInterval.TotalSeconds);
        else if (previous?.Name != target.Name)
        {
            if (target.Name == MachineName)
                _logger.LogInformation("Selected MachineDBConnection as the active database connection.");
            else
                _logger.LogWarning("Falling back to IntranetDBConnection. Background primary checks run every {RetrySeconds}s.", _healthInterval.TotalSeconds);
        }
    }

    private static InvalidOperationException Unavailable() =>
        new("No database connection is available. See database connection diagnostics in the application log.");
}
