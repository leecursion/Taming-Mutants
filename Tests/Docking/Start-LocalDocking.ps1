param([switch]$Stop)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$checks = Join-Path $workspace 'Temp/DockingChecks'
$environment = Join-Path $workspace 'Temp/DockingEnv'
$pythonExe = Join-Path $environment 'Scripts/python.exe'
$proxyScript = Join-Path $workspace 'server/local-docking.mjs'
$stateFile = Join-Path $checks 'processes.json'
New-Item -ItemType Directory -Force $checks | Out-Null
if (Test-Path -LiteralPath $stateFile) {
    $state = Get-Content -Raw -LiteralPath $stateFile | ConvertFrom-Json
    foreach ($entry in $state) {
        $process = Get-CimInstance Win32_Process -Filter "ProcessId=$($entry.id)" -ErrorAction SilentlyContinue
        if ($process -and $process.CommandLine.Contains($entry.marker)) {
            if (-not $Stop) { throw 'Local docking is already running. Use -Stop before restarting.' }
            # Only the recorded process whose command line still matches this workspace.
            & taskkill /PID $entry.id /T /F | Out-Null
        }
    }
    Remove-Item -LiteralPath $stateFile
}
if ($Stop) {
    $config = Join-Path $checks 'editor-service.json'
    if (Test-Path -LiteralPath $config) { Remove-Item -LiteralPath $config }
    Write-Output 'Stopped local docking and restored the configured remote resolver.'
    exit
}
if (-not (Test-Path -LiteralPath $pythonExe)) { python -m venv $environment; if ($LASTEXITCODE -ne 0) { throw 'Python environment creation failed.' } }
$requirements = Join-Path $workspace 'server/docking/requirements.txt'
& $pythonExe -m pip install -r $requirements
if ($LASTEXITCODE -ne 0) { throw 'Docking dependencies could not be installed.' }
$vina = Join-Path $checks 'vina.exe'
if (-not (Test-Path -LiteralPath $vina)) {
    Invoke-WebRequest -Uri 'https://github.com/ccsb-scripps/AutoDock-Vina/releases/download/v1.2.7/vina_1.2.7_win.exe' -OutFile $vina
}
$version = & $vina --version
if ($version -notmatch 'v1\.2\.7') { throw 'AutoDock Vina 1.2.7 is required.' }
$env:VINA_EXECUTABLE = $vina
$env:PYTHONUTF8 = '1'
$cpu = Start-Process -FilePath $pythonExe -ArgumentList '-m uvicorn app:app --app-dir server/docking --host 127.0.0.1 --port 8000 --workers 1' -WorkingDirectory $workspace -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $checks 'cpu.log') -RedirectStandardError (Join-Path $checks 'cpu-errors.log')
$node = (Get-Command node).Source
$proxy = Start-Process -FilePath $node -ArgumentList ('"' + $proxyScript + '"') -WorkingDirectory $workspace -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $checks 'proxy.log') -RedirectStandardError (Join-Path $checks 'proxy-errors.log')
@(@{ id=$cpu.Id; marker='-m uvicorn app:app --app-dir server/docking' },@{ id=$proxy.Id; marker=$proxyScript }) | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
Start-Sleep -Seconds 2
$cpu.Refresh(); $proxy.Refresh()
if ($cpu.HasExited -or $proxy.HasExited) { throw 'A local service could not start. Check Temp/DockingChecks/*-errors.log and run this script with -Stop.' }
Write-Output 'Local docking started. Unity Editor uses Temp/DockingChecks/editor-service.json; builds keep their configured server. Use this script with -Stop to stop.'
