<#
.SYNOPSIS
Downloads or restores the PictureQs app's pictures and JSON data on Android.

.DESCRIPTION
Download saves a timestamped snapshot under the current user's PictureQs\AndroidAppDataBackups folder.
Restore copies the newest snapshot back to the app's private files directory on the selected Android device.

.EXAMPLE
.\Sync-AndroidAppData.ps1

.EXAMPLE
.\Sync-AndroidAppData.ps1 -Mode Restore -DeviceSerial DEVICE_SERIAL
#>
param(
    [ValidateSet("Download", "Restore")]
    [string]$Mode = "Download",

    [string]$DeviceSerial,

    [string]$BackupDirectory = (Join-Path $env:USERPROFILE "PictureQs\AndroidAppDataBackups")
)

$ErrorActionPreference = "Stop"
$applicationId = "com.companyname.pictureqsmaui"

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

$tarCommand = Get-Command tar.exe -ErrorAction SilentlyContinue
if (-not $tarCommand) {
    throw "tar.exe was not found on PATH. Install Windows tar or add it to PATH."
}
$tarPath = $tarCommand.Source

function Get-ConnectedDevice {
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
        return $selectedDevice
    }

    if ($readyDevices.Count -eq 1) {
        return $readyDevices[0]
    }
    if ($readyDevices.Count -eq 0) {
        throw "No Android device is ready. Connect a device and enable USB debugging."
    }

    $serials = ($readyDevices | ForEach-Object Serial) -join ", "
    throw "More than one Android device is connected. Specify one with -DeviceSerial. Devices: $serials"
}

function ConvertTo-ProcessArgument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-AdbFileTransfer {
    param(
        [string[]]$Arguments,
        [ValidateSet("Download", "Upload")]
        [string]$Transfer,
        [string]$FilePath
    )

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $adbPath
    $startInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-ProcessArgument $_ }) -join " ")
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.RedirectStandardOutput = ($Transfer -eq "Download")
    $startInfo.RedirectStandardInput = ($Transfer -eq "Upload")

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) {
        throw "Unable to start adb for app-data transfer."
    }

    $errorTask = $process.StandardError.ReadToEndAsync()
    try {
        if ($Transfer -eq "Download") {
            $outputStream = [System.IO.File]::Create($FilePath)
            try {
                $process.StandardOutput.BaseStream.CopyTo($outputStream)
            }
            finally {
                $outputStream.Dispose()
            }
        }
        else {
            $inputStream = [System.IO.File]::OpenRead($FilePath)
            try {
                $inputStream.CopyTo($process.StandardInput.BaseStream)
            }
            finally {
                $inputStream.Dispose()
                $process.StandardInput.Close()
            }
        }

        $process.WaitForExit()
        $errorOutput = $errorTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "adb app-data transfer failed with exit code $($process.ExitCode): $errorOutput"
        }
    }
    finally {
        $process.Dispose()
    }
}

$device = Get-ConnectedDevice
$temporaryArchive = Join-Path $env:TEMP ("pictureqs-app-data-" + [guid]::NewGuid().ToString("N") + ".tar")

try {
    if ($Mode -eq "Download") {
        New-Item -ItemType Directory -Path $BackupDirectory -Force | Out-Null
        $snapshotName = "snapshot-" + (Get-Date -Format "yyyyMMdd-HHmmssfff")
        $snapshotPath = Join-Path $BackupDirectory $snapshotName

        Write-Host "Downloading app data from Android device $($device.Serial)..."
        $remoteShellScript = 'cd files || exit 1; set --; for file in * .[!.]*; do [ -f "$file" ] || continue; case "$file" in *.png|*.PNG|*.jpg|*.JPG|*.jpeg|*.JPEG|*.gif|*.GIF|*.bmp|*.BMP|*.webp|*.WEBP|*.json|*.JSON) set -- "$@" "$file";; esac; done; [ "$#" -gt 0 ] || exit 3; tar -cf - -- "$@"'
        $remoteCommand = "run-as $applicationId sh -c '$remoteShellScript'"
        Invoke-AdbFileTransfer `
            -Arguments @("-s", $device.Serial, "exec-out", $remoteCommand) `
            -Transfer Download `
            -FilePath $temporaryArchive

        if ((Get-Item -LiteralPath $temporaryArchive).Length -eq 0) {
            throw "The Android app-data archive was empty."
        }

        $archiveEntries = @(& $tarPath -tf $temporaryArchive)
        if ($LASTEXITCODE -ne 0) {
            throw "The downloaded app-data archive is invalid."
        }
        $fileEntries = @($archiveEntries | Where-Object { $_ -and $_.TrimEnd("/") -ne "." })
        if ($fileEntries.Count -eq 0) {
            throw "No app data files were found to download."
        }

        New-Item -ItemType Directory -Path $snapshotPath -Force | Out-Null
        try {
            & $tarPath -xf $temporaryArchive -C $snapshotPath
            if ($LASTEXITCODE -ne 0) {
                throw "Unable to extract the downloaded app data into '$snapshotPath'."
            }
        }
        catch {
            Remove-Item -LiteralPath $snapshotPath -Recurse -Force
            throw
        }

        Write-Host "Downloaded app data to '$snapshotPath'."
    }
    else {
        if (-not (Test-Path -LiteralPath $BackupDirectory)) {
            Write-Host "No app-data backup exists yet; skipping restore."
            return
        }

        $latestSnapshot = Get-ChildItem -LiteralPath $BackupDirectory -Directory |
            Where-Object { $_.Name -match '^snapshot-\d{8}-\d{9}$' } |
            Sort-Object Name -Descending |
            Select-Object -First 1
        if (-not $latestSnapshot) {
            Write-Host "No app-data backup exists yet; skipping restore."
            return
        }

        if (-not (Get-ChildItem -LiteralPath $latestSnapshot.FullName -File -Recurse | Select-Object -First 1)) {
            throw "The latest app-data backup '$($latestSnapshot.FullName)' contains no files."
        }

        & $tarPath -cf $temporaryArchive -C $latestSnapshot.FullName .
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to archive the app-data backup at '$($latestSnapshot.FullName)'."
        }

        & $adbPath -s $device.Serial shell am force-stop $applicationId
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to stop the app on Android device $($device.Serial) before restoring data."
        }

        Write-Host "Restoring app data to Android device $($device.Serial)..."
        Invoke-AdbFileTransfer `
            -Arguments @("-s", $device.Serial, "exec-in", "run-as", $applicationId, "tar", "-C", "files", "-xf", "-") `
            -Transfer Upload `
            -FilePath $temporaryArchive

        Write-Host "Restored app data from '$($latestSnapshot.FullName)'."
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryArchive) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
}
