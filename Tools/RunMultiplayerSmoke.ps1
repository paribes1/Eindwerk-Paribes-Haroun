# Start een host en client op deze pc en controleer hun testrapporten.
param([int]$Port = 17777)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$player = Join-Path $projectRoot 'Builds/MechanicsSmoke/MechanicsSmoke.exe'
if (!(Test-Path -LiteralPath $player)) {
    throw 'Maak eerst de multiplayer testbuild met MechanicsValidation.BuildSmokePlayer in Unity.'
}
$runId = Get-Date -Format 'yyyyMMdd-HHmmss'
$logDir = Join-Path $projectRoot 'Logs'
New-Item -ItemType Directory -Path $logDir -Force | Out-Null
$hostReport = Join-Path $logDir "smoke-host-$runId.json"
$clientReport = Join-Path $logDir "smoke-client-$runId.json"
$hostLog = Join-Path $logDir "smoke-host-$runId.log"
$clientLog = Join-Path $logDir "smoke-client-$runId.log"
$hostArgs = @('-batchmode', '-nographics', '-mechanicsSmoke', 'host', '-networkPort', $Port,
    '-smokeOutput', ('"' + $hostReport + '"'), '-logFile', ('"' + $hostLog + '"'))
$clientArgs = @('-batchmode', '-nographics', '-mechanicsSmoke', 'client', '-networkPort', $Port,
    '-networkAddress', '127.0.0.1', '-smokeOutput', ('"' + $clientReport + '"'),
    '-logFile', ('"' + $clientLog + '"'))
# Beide testprocessen draaien zonder zichtbaar venster.
$hostProcess = Start-Process -FilePath $player -ArgumentList $hostArgs -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
$clientProcess = $null
try {
    # Geef de host tijd om de poort te openen voordat de client verbindt.
    Start-Sleep -Seconds 1
    $clientProcess = Start-Process -FilePath $player -ArgumentList $clientArgs -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru
    if (!$hostProcess.WaitForExit(55000)) { throw "Host timeout. Zie $hostLog" }
    if (!$clientProcess.WaitForExit(5000)) { throw "Client timeout. Zie $clientLog" }
    # De test slaagt alleen als beide processen een geslaagd rapport schrijven.
    foreach ($reportPath in @($hostReport, $clientReport)) {
        if (!(Test-Path -LiteralPath $reportPath)) { throw "Testrapport ontbreekt: $reportPath" }
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        $report | ConvertTo-Json
        if (!$report.passed) { throw "Multiplayertest mislukt: $($report.message)" }
    }
    if ($hostProcess.ExitCode -ne 0 -or $clientProcess.ExitCode -ne 0) { throw 'Een testproces eindigde met een foutcode.' }
    Write-Output 'MULTIPLAYER_SMOKE_PASSED'
} finally {
    # Stop alleen de testprocessen die dit script zelf heeft gestart.
    foreach ($testProcess in @($hostProcess, $clientProcess)) {
        if ($null -ne $testProcess -and !$testProcess.HasExited) { $testProcess.Kill() }
    }
}
