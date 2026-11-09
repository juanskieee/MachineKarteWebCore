param([string]$Dotnet = 'dotnet', [string]$LocalDbInstance = 'MSSQLLocalDB')
# Compare actual repository/service results with the preserved Form1/uc_Test SQL.
# Uses synthetic rows in a disposable LocalDB catalog, never application settings.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Test-MonitoringParity.ps1') -Dotnet $Dotnet -LocalDbInstance $LocalDbInstance -StaircaseOnly
