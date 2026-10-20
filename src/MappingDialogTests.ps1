$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 8) {
    & "$env:WINDIR\sysnative\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath
    if ($LASTEXITCODE -ne 0) { throw '64-bit mapping dialog tests failed.' }
    return
}
Add-Type -AssemblyName System.Windows.Forms
[void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'MiniCockpitLab.exe'))
$assembly = [MiniCockpitLab.InputFrame].Assembly
$dialogType = $assembly.GetType('MiniCockpitLab.MappingDialog')
$flags = [Reflection.BindingFlags]'Instance,NonPublic'
foreach ($specificValue in @($false, $true)) {
    $frame = [MiniCockpitLab.InputFrame]::Parse('17,12300')
    $dialog = [Activator]::CreateInstance($dialogType, @($frame, 'FCU', $null))
    try {
        $dialogType.GetField('control', $flags).GetValue($dialog).Text = 'Altitude knob'
        $dialogType.GetField('action', $flags).GetValue($dialog).Text = 'Clockwise'
        $dialogType.GetField('exact', $flags).GetValue($dialog).Checked = $specificValue
        $dialog.Show()
        [Windows.Forms.Application]::DoEvents()
        $dialog.AcceptButton.PerformClick()
        [Windows.Forms.Application]::DoEvents()
        $expectedKey = if ($specificValue) { '17,12300' } else { '17' }
        if ($dialog.MappingKey -ne $expectedKey -or $dialog.MappingLabel -ne 'FCU / Altitude knob / Clockwise' -or $dialog.DialogResult -ne [Windows.Forms.DialogResult]::OK) {
            throw "Naming dialog did not save the expected mapping: $expectedKey"
        }
    } finally {
        $dialog.Dispose()
    }
}
Write-Output 'Touch-first dialog tests passed: generic control and exact switch-value naming.'
