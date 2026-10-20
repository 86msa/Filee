[CmdletBinding()]
param([Parameter(Mandatory)][string]$Version, [string]$Packages = (Join-Path $PSScriptRoot '../Releases'))
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'Test the Windows installer on Windows.' }
$work = Join-Path $env:RUNNER_TEMP ('filee-install-' + [Guid]::NewGuid().ToString('N'))
$install = Join-Path $work 'تثبيت Filee'
New-Item -ItemType Directory -Force $work | Out-Null
$env:FILEE_DATA_DIR = Join-Path $work 'state'
$env:PYTHONUTF8 = '1'
function Invoke-Exe([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$File exited with $($process.ExitCode)" }
}
$setup = (Resolve-Path (Join-Path $Packages "Filee-$Version-win-Setup.exe")).Path
Invoke-Exe $setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NoStart=1', '/TASKS="contextmenu,contextmenu\top"',
    "/DIR=`"$install`"", "/LOG=`"$(Join-Path $work 'setup.log')`"")
$app = Join-Path $install 'Filee.exe'
$cli = Join-Path $install 'filee-cli.exe'
try {
    if (-not (Test-Path $app) -or -not (Test-Path $cli)) { throw 'The installer did not install both executables.' }
    $report = Join-Path $PSScriptRoot '../TestResults/native-installers/win-x64'
    Invoke-Exe "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" @('-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
        '-File', "`"$(Join-Path $PSScriptRoot 'test-explorer-menu-upgrade.ps1')`"", '-Setup', "`"$setup`"", '-Install', "`"$install`"", '-Report', "`"$report`"")
    python (Join-Path $PSScriptRoot 'smoke-arabic-pdf.py') --cli $cli --work (Join-Path $work 'installed-arabic')
    if ($LASTEXITCODE -ne 0) { throw 'Installed CLI Arabic PDF validation failed.' }
    $desktop = Start-Process -FilePath $app -ArgumentList @('--settings', '--install-engines=', '--context-menu=off', '--start-with-system=off') -PassThru
    try {
        Start-Sleep -Seconds 8
        if ($desktop.HasExited) { throw 'Installed desktop exited before the launch check.' }
        Invoke-Exe $app @('--quit')
        if (-not $desktop.WaitForExit(30000) -or $desktop.ExitCode -ne 0) { throw 'Installed desktop did not quit cleanly.' }
    }
    finally { if (-not $desktop.HasExited) { Stop-Process -Id $desktop.Id -Force } }
}
finally {
    $uninstaller = Join-Path $install 'unins000.exe'
    if (Test-Path $uninstaller) { Invoke-Exe $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') }
}
if ((Test-Path $app) -or (Test-Path $cli)) { throw 'The uninstaller left the application executables installed.' }
$portable = Join-Path $work 'portable'
Expand-Archive -LiteralPath (Join-Path $Packages "Filee-$Version-win-Portable.zip") -DestinationPath $portable
python (Join-Path $PSScriptRoot 'smoke-arabic-pdf.py') --cli (Join-Path $portable 'filee-cli.exe') --work (Join-Path $work 'portable-arabic')
if ($LASTEXITCODE -ne 0) { throw 'Portable ZIP Arabic PDF validation failed.' }
Write-Host 'Windows EXE install/launch/quit/uninstall and portable ZIP Arabic PDF validation passed.'
$report = Join-Path $PSScriptRoot '../TestResults/native-installers/win-x64'
New-Item -ItemType Directory -Force $report | Out-Null
@{ runtime = 'win-x64'; version = $Version; install = 'passed'; arabic_pdf = 'passed'; desktop = 'passed'; uninstall = 'passed'; portable_zip = 'passed' } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $report 'installation.json') -Encoding utf8NoBOM
