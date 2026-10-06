param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [string]$DeviceSerial
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "PictureQsMaui.csproj"
$applicationId = "com.companyname.pictureqsmaui"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on PATH."
}

$sdkDirectory = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($sdkDirectory)) {
    $sdkDirectory = $env:ANDROID_SDK_ROOT
}
if ([string]::IsNullOrWhiteSpace($sdkDirectory)) {
    $localSdkDirectory = Join-Path $env:LOCALAPPDATA "Android\Sdk"
    if (Test-Path $localSdkDirectory) {
        $sdkDirectory = $localSdkDirectory
        $env:ANDROID_HOME = $sdkDirectory
        $env:ANDROID_SDK_ROOT = $sdkDirectory
    }
}

if ([string]::IsNullOrWhiteSpace($sdkDirectory) -or -not (Test-Path $sdkDirectory)) {
    throw "Android SDK not found. Set ANDROID_HOME or ANDROID_SDK_ROOT to its directory."
}

$adbPath = Join-Path $sdkDirectory "platform-tools\adb.exe"
if (-not (Test-Path $adbPath)) {
    throw "Android Debug Bridge was not found at '$adbPath'. Install Android platform-tools."
}

$deviceLines = @(& $adbPath devices)
if ($LASTEXITCODE -ne 0) {
    throw "Unable to query Android devices using adb."
}

$devices = @(
    $deviceLines | Select-Object -Skip 1 | ForEach-Object {
        $parts = $_.Trim() -split "\s+"
        if ($parts.Count -ge 2) {
            [pscustomobject]@{
                Serial = $parts[0]
                State  = $parts[1]
            }
        }
    }
)

$readyDevices = @($devices | Where-Object State -eq "device")
if ($DeviceSerial) {
    $selectedDevice = $readyDevices | Where-Object Serial -eq $DeviceSerial | Select-Object -First 1
    if (-not $selectedDevice) {
        $states = ($devices | ForEach-Object { "$($_.Serial) ($($_.State))" }) -join ", "
        throw "Device '$DeviceSerial' is not available to adb. Devices: $states"
    }
}
elseif ($readyDevices.Count -eq 1) {
    $selectedDevice = $readyDevices[0]
}
elseif ($readyDevices.Count -eq 0) {
    throw "No Android device is ready. Connect a device and enable USB debugging."
}
else {
    $serials = ($readyDevices | ForEach-Object Serial) -join ", "
    throw "More than one Android device is connected. Specify one with -DeviceSerial. Devices: $serials"
}

$apkPath = Join-Path $PSScriptRoot "bin\$Configuration\net10.0-android\$applicationId-Signed.apk"
Write-Host "Building Android app ($Configuration)..."
& dotnet build $projectPath --framework net10.0-android --configuration $Configuration -p:EmbedAssembliesIntoApk=true
if ($LASTEXITCODE -ne 0) {
    throw "Android build failed with exit code $LASTEXITCODE."
}
if (-not (Test-Path $apkPath)) {
    throw "The build succeeded, but the expected APK was not found at '$apkPath'."
}

Write-Host "Installing on Android device $($selectedDevice.Serial)..."
& $adbPath -s $selectedDevice.Serial install -r $apkPath
if ($LASTEXITCODE -ne 0) {
    throw "APK installation failed with exit code $LASTEXITCODE."
}

& $adbPath -s $selectedDevice.Serial shell am force-stop $applicationId
$syncScript = Join-Path $PSScriptRoot "Sync-AndroidAppData.ps1"
& $syncScript -Mode Restore -DeviceSerial $selectedDevice.Serial

$activityComponent = & $adbPath -s $selectedDevice.Serial shell cmd package resolve-activity --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER -p $applicationId |
    Where-Object { $_ -match "/" } |
    Select-Object -Last 1
if (-not $activityComponent) {
    throw "Could not find a launcher activity for $applicationId on device $($selectedDevice.Serial)."
}

& $adbPath -s $selectedDevice.Serial shell am start -n $activityComponent.Trim()
if ($LASTEXITCODE -ne 0) {
    throw "The app was installed, but launching it failed with exit code $LASTEXITCODE."
}

Start-Sleep -Seconds 5
$processId = & $adbPath -s $selectedDevice.Serial shell pidof $applicationId
if (-not $processId) {
    Write-Host "The app process exited shortly after launch. Recent Android errors:"
    & $adbPath -s $selectedDevice.Serial logcat -d -t 300 | Select-String -Pattern "$applicationId|FATAL EXCEPTION|FATAL|AndroidRuntime|monodroid"
    throw "The app was installed but did not stay running."
}

Write-Host "Installed and launched $applicationId on $($selectedDevice.Serial)."
