$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '.NET Framework compiler was not found.' }
$testExecutable = Join-Path ([System.IO.Path]::GetTempPath()) ('MonitorGate.Tests.' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & $compilerPath /nologo /target:exe /main:MonitorGate.EngineTests "/out:$testExecutable" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'MonitorGate.cs') (Join-Path $PSScriptRoot 'StartupSettings.cs') (Join-Path $PSScriptRoot 'SettingsDialog.cs') (Join-Path $PSScriptRoot 'OverlayAppearance.cs') (Join-Path $PSScriptRoot 'Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    & $testExecutable
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally {
    if (Test-Path -LiteralPath $testExecutable) { Remove-Item -LiteralPath $testExecutable }
}
