param([string]$OutputDirectory = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw '.NET Framework 4.x compiler was not found.'
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$outputPath = Join-Path $OutputDirectory 'MonitorGate.exe'
$sourcePath = Join-Path $PSScriptRoot 'MonitorGate.cs'
$settingsPath = Join-Path $PSScriptRoot 'StartupSettings.cs'
$dialogPath = Join-Path $PSScriptRoot 'SettingsDialog.cs'
$manifestPath = Join-Path $PSScriptRoot 'MonitorGate.manifest'
& $compilerPath /nologo /target:winexe /platform:anycpu /optimize+ "/out:$outputPath" "/win32manifest:$manifestPath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $sourcePath $settingsPath $dialogPath
if ($LASTEXITCODE -ne 0) { throw "Build failed: $LASTEXITCODE" }
Write-Output "Built: $outputPath"
