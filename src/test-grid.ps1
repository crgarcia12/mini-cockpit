param([switch]$SweepOnly)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testDirectory = Join-Path $PSScriptRoot 'tests\grid'
New-Item -ItemType Directory -Path $testDirectory -Force | Out-Null
Push-Location $PSScriptRoot
try {
    if (!(Test-Path .\Microsoft.FlightSimulator.SimConnect.dll) -or !(Test-Path .\SimConnect.dll)) { & .\build.ps1 }
    Copy-Item .\Microsoft.FlightSimulator.SimConnect.dll, .\SimConnect.dll $testDirectory -Force
    $assetDirectory = Join-Path $testDirectory 'assets'
    New-Item -ItemType Directory -Path $assetDirectory -Force | Out-Null
    Copy-Item .\assets\*.png $assetDirectory -Force
    & $compiler /nologo /platform:x64 /target:exe /main:MiniCockpitLab.GridMappingTests "/out:$testDirectory\GridMappingTests.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.FlightSimulator.SimConnect.dll Protocol.cs CaravanProfile.cs SimulatorBridge.cs MappingDialog.cs LightMappingPanel.cs StartupPanel.cs HardwarePanel.cs Lab.cs GridMappingTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Grid mapping test build failed.' }
    if ($SweepOnly) { & "$testDirectory\GridMappingTests.exe" --sweep-only }
    else { & "$testDirectory\GridMappingTests.exe" }
    if ($LASTEXITCODE -ne 0) { throw 'Grid mapping tests failed.' }
} finally {
    Pop-Location
}
