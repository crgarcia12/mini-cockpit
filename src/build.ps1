param([string]$SimConnectDirectory = $env:MINICOCKPIT_SIMCONNECT)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $compiler)) {
    throw "The built-in .NET Framework C# compiler was not found: $compiler"
}
Push-Location $PSScriptRoot
try {
    if (!$SimConnectDirectory) { $SimConnectDirectory = Join-Path $PSScriptRoot '..\..\msfs-copilot' }
    foreach ($dependency in @('Microsoft.FlightSimulator.SimConnect.dll', 'SimConnect.dll')) {
        if (!(Test-Path (Join-Path $PSScriptRoot $dependency))) {
            $source = Join-Path $SimConnectDirectory $dependency
            if (!(Test-Path $source)) {
                throw "Missing $dependency. Set MINICOCKPIT_SIMCONNECT or -SimConnectDirectory to an installed x64 SimConnect SDK runtime directory."
            }
            Copy-Item $source (Join-Path $PSScriptRoot $dependency)
        }
    }
    & $compiler /nologo /platform:x64 /target:winexe /optimize+ /out:MiniCockpitLab.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:Microsoft.FlightSimulator.SimConnect.dll Protocol.cs CaravanProfile.cs SimulatorBridge.cs MappingDialog.cs LightMappingPanel.cs StartupPanel.cs HardwarePanel.cs Lab.cs
    if ($LASTEXITCODE -ne 0) { throw 'Lab build failed.' }
    & $compiler /nologo /target:exe /out:ProtocolTests.exe Protocol.cs CaravanProfile.cs ProtocolTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Protocol test build failed.' }
    & .\ProtocolTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Protocol tests failed.' }
} finally {
    Pop-Location
}
