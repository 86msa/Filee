# Run in Windows PowerShell 5.1: Appx cmdlets are not available in every PowerShell 7 installation.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Setup, [Parameter(Mandatory)][string]$Install,
    [Parameter(Mandatory)][string]$Report)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force $Report | Out-Null
$package = Get-AppxPackage -Name Filee.ExplorerMenu
if (-not $package) { throw 'The fresh installer did not register the Explorer package.' }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class FileeMenuCom {
    [DllImport("ole32.dll", PreserveSig = false)]
    public static extern void CoCreateInstance(ref Guid clsid, IntPtr outer, uint context, ref Guid iid, out IntPtr command);
}
'@
$clsid = [Guid]'6DB0E670-807F-4D22-BE3F-20492D0F4AB1'
$iid = [Guid]'a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9'
$command = [IntPtr]::Zero
try {
    # Activate the registered COM surrogate, as the Windows 11 menu does. Keep it alive during the upgrade.
    [FileeMenuCom]::CoCreateInstance([ref]$clsid, [IntPtr]::Zero, 4, [ref]$iid, [ref]$command)
    $reproduced = $false
    try {
        Add-AppxPackage -Path (Join-Path $Install 'FileeExplorerMenu.msix') -ExternalLocation $Install -AllowUnsigned -ForceUpdateFromAnyVersion
    }
    catch {
        if ($_ -notmatch '0x80073D02') { throw }
        $reproduced = $true
        $_ | Out-String | Set-Content (Join-Path $Report 'original-error.txt') -Encoding UTF8
    }
    if (-not $reproduced) { throw 'The test did not reproduce the original packages-in-use deployment failure.' }
    $log = Join-Path $Report 'upgrade.log'
    $process = Start-Process -FilePath $Setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/NoStart=1',
        "/DIR=`"$Install`"", "/LOG=`"$log`"") -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Upgrade failed: $($process.ExitCode)" }
    $text = Get-Content -LiteralPath $log -Raw
    if ($text -notmatch 'Explorer menu registration already current; keeping the active package\.' -or $text -match 'PowerShell failed:') {
        throw "Upgrade did not preserve the active registration: $text"
    }
    $after = Get-AppxPackage -Name Filee.ExplorerMenu
    if ($after.PackageFullName -ne $package.PackageFullName) { throw 'Upgrade lost the package registration.' }
    @{ original_error = '0x80073D02'; loaded_com_upgrade = 'passed'; package = $after.PackageFullName } |
        ConvertTo-Json | Set-Content (Join-Path $Report 'explorer-upgrade.json') -Encoding UTF8
}
finally {
    if ($command -ne [IntPtr]::Zero) { [void][Runtime.InteropServices.Marshal]::Release($command) }
}
