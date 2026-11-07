using Microsoft.Data.SqlClient;

namespace MachineKarte.Web.Services;

// Singleton: owns connection selection, never shares an open connection between callers.
public sealed class DatabaseConnectionResolver
{
    private const string MachineName = "MachineDBConnection";
    private const string IntranetName = "IntranetDBConnection";
    private readonly string? _machineConnectionString;
    private readonly string? _intranetConnectionString;
    private readonly ILogger<DatabaseConnectionResolver> _logger;
    private readonly Func<string, CancellationToken, Task<SqlConnection>> _openValidatedConnection;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _validationTimeout;
    private readonly TimeSpan _primaryRetryInterval;
    private readonly SemaphoreSlim _recoveryProbe = new(1, 1);
    private long _retryPrimaryAt;
    private string? _activeConnectionName;

    public DatabaseConnectionResolver(IConfiguration configuration, ILogger<DatabaseConnectionResolver> logger)
        : this(configuration, logger, OpenAndValidateAsync, TimeProvider.System) { }

    // The internal seam lets the focused offline check exercise selection without a live database.
    internal DatabaseConnectionResolver(IConfiguration configuration, ILogger<DatabaseConnectionResolver> logger,
        Func<string, CancellationToken, Task<SqlConnection>> openValidatedConnection, TimeProvider timeProvider)
    {
        // Keep old-only deployments working; the new primary setting always takes precedence.
        _machineConnectionString = configuration.GetConnectionString(MachineName)
            ?? configuration.GetConnectionString("DBConnection");
        _intranetConnectionString = configuration.GetConnectionString(IntranetName);
        _logger = logger;
        _openValidatedConnection = openValidatedConnection;
        _timeProvider = timeProvider;
        var timeout = configuration.GetValue("DatabaseConnection:ValidationTimeoutSeconds", 2);
        var retry = configuration.GetValue("DatabaseConnection:PrimaryRetrySeconds", 30);
        if (timeout <= 0 || retry <= 0)
            throw new InvalidOperationException("DatabaseConnection timeouts must be positive seconds.");
        _validationTimeout = TimeSpan.FromSeconds(timeout);
        _primaryRetryInterval = TimeSpan.FromSeconds(retry);
    }

    public async Task<string> GetConnectionStringAsync(CancellationToken cancellationToken = default)
    {
        var resolved = await ResolveAsync(cancellationToken);
        await using var connection = resolved.Connection;
        return resolved.ConnectionString;
    }

    // Repositories reuse the validated connection so there is no second login or validation/open gap.
    public async Task<SqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        (await ResolveAsync(cancellationToken)).Connection;

    private async Task<(SqlConnection Connection, string ConnectionString)> ResolveAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retryAt = Interlocked.Read(ref _retryPrimaryAt);
        var recoveryProbe = retryAt != 0 && _timeProvider.GetUtcNow().Ticks >= retryAt
            && await _recoveryProbe.WaitAsync(0, cancellationToken);
        try
        {
            if (retryAt == 0 || recoveryProbe)
            {
                var machine = await TryOpenAsync(MachineName, _machineConnectionString, cancellationToken);
                if (machine is not null)
                {
                    Interlocked.Exchange(ref _retryPrimaryAt, 0);
                    LogSelection(MachineName);
                    return (machine, _machineConnectionString!);
                }

                Interlocked.Exchange(ref _retryPrimaryAt,
                    (_timeProvider.GetUtcNow() + _primaryRetryInterval).Ticks);
            }
        }
        finally
        {
            if (recoveryProbe) _recoveryProbe.Release();
        }

        var intranet = await TryOpenAsync(IntranetName, _intranetConnectionString, cancellationToken);
        if (intranet is not null)
        {
            LogSelection(IntranetName);
            return (intranet, _intranetConnectionString!);
        }

        _logger.LogError("No database connection passed connectivity validation. Both connection settings are unavailable.");
        throw new InvalidOperationException("No database connection is available. See database connection diagnostics in the application log.");
    }

    private async Task<SqlConnection?> TryOpenAsync(string name, string? connectionString, CancellationToken cancellationToken)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(_validationTimeout);
        try
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("Connection setting is missing or empty.");
            return await _openValidatedConnection(connectionString, budget.Token);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is SqlException or TimeoutException or OperationCanceledException or ArgumentException or InvalidOperationException)
        {
            // Do not log exception messages or connection strings: malformed settings can contain credentials.
            var sql = exception as SqlException;
            _logger.LogWarning("Database connection {ConnectionName} failed validation: {FailureType}; SQL number {SqlNumber}, state {SqlState}, class {SqlClass}. Validation budget {TimeoutSeconds}s. Check the setting, network, SQL availability, login and database permissions.",
                name, exception.GetType().Name, sql?.Number, sql?.State, sql?.Class, _validationTimeout.TotalSeconds);
            return null;
        }
    }

    private static async Task<SqlConnection> OpenAndValidateAsync(string connectionString, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("SELECT 1", connection);
            if (!Equals(await command.ExecuteScalarAsync(cancellationToken), 1))
                throw new InvalidOperationException("Database connectivity validation returned an unexpected result.");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private void LogSelection(string name)
    {
        if (Interlocked.Exchange(ref _activeConnectionName, name) == name) return;
        if (name == MachineName)
            _logger.LogInformation("Selected MachineDBConnection as the active database connection.");
        else
            _logger.LogWarning("Falling back to IntranetDBConnection. MachineDBConnection will be retried after {RetrySeconds}s.",
                _primaryRetryInterval.TotalSeconds);
    }
}
