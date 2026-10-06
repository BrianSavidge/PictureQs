param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$DeviceSerial
)

$installScript = Join-Path $PSScriptRoot "Install-Android.ps1"
& $installScript -Configuration $Configuration -DeviceSerial $DeviceSerial
