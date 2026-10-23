<#
.SYNOPSIS
Builds a signed Android App Bundle for Google Play.

.DESCRIPTION
Uses the Android upload keystore to sign a Release AAB. Set the signing
passwords in ANDROID_SIGNING_STORE_PASS and ANDROID_SIGNING_KEY_PASS from a
secret manager before running. The keystore and key passwords are validated
before the Android build starts. The resulting bundle is written under
artifacts\android. By default, the version code is the number of UTC seconds
since 2000-01-01.

.EXAMPLE
.\Build-AndroidAab.ps1 -KeystorePath .\upload-key.jks -KeyAlias pictureqs -VersionCode 2

.EXAMPLE
.\Build-AndroidAab.ps1 -KeystorePath .\upload-key.jks -KeyAlias pictureqs
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$KeystorePath = "C:\Users\a_sna\pictureqs-upload-key.jks",

    [Parameter(Mandatory = $false)]
    [string]$KeyAlias = "PictureQs",

    [ValidateRange(1, 2147483647)]
    [int]$VersionCode = [int]([DateTime]::UtcNow - [DateTime]::new(2000, 1, 1)).TotalSeconds
)

$ErrorActionPreference = "Stop"
$projectPath = Join-Path $PSScriptRoot "PictureQsMaui.csproj"
$outputDirectory = Join-Path $PSScriptRoot "..\artifacts\android"
$publishDirectory = Join-Path $outputDirectory ".build-$VersionCode-$([guid]::NewGuid().ToString('N'))"
$artifactPath = Join-Path $outputDirectory "PictureQsMaui-v$VersionCode.aab"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK was not found on PATH."
}

if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
    throw "MAUI project not found at '$projectPath'."
}

$resolvedKeystorePath = Resolve-Path -LiteralPath $KeystorePath -ErrorAction Stop
if (-not (Test-Path -LiteralPath $resolvedKeystorePath -PathType Leaf)) {
    throw "Android upload keystore not found at '$KeystorePath'."
}

if ([string]::IsNullOrWhiteSpace($KeyAlias)) {
    throw "KeyAlias must not be empty."
}

$storePassword = $env:ANDROID_SIGNING_STORE_PASS
$keyPassword = $env:ANDROID_SIGNING_KEY_PASS

function Read-SigningPassword {
    param([Parameter(Mandatory = $true)][string]$Prompt)

    $securePassword = Read-Host -Prompt $Prompt -AsSecureString
    if ($securePassword.Length -eq 0) {
        $securePassword.Dispose()
        throw "A signing password cannot be empty."
    }

    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        $securePassword.Dispose()
    }
}

if ([string]::IsNullOrWhiteSpace($storePassword)) {
    $storePassword = Read-SigningPassword "Android upload keystore password"
}
if ([string]::IsNullOrWhiteSpace($keyPassword)) {
    $keyPassword = Read-SigningPassword "Android upload key password"
}

$previousStorePassword = [Environment]::GetEnvironmentVariable("AndroidSigningStorePass", "Process")
$previousKeyPassword = [Environment]::GetEnvironmentVariable("AndroidSigningKeyPass", "Process")
$hadPreviousStorePassword = $null -ne $previousStorePassword
$hadPreviousKeyPassword = $null -ne $previousKeyPassword
$signingCheckDirectory = Join-Path ([IO.Path]::GetTempPath()) "pictureqs-signing-check-$([guid]::NewGuid().ToString('N'))"

try {
    $jarsigner = Get-Command jarsigner -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $jarsigner) {
        throw "jarsigner was not found on PATH; it is required to validate the Android signing credentials."
    }

    $env:AndroidSigningStorePass = $storePassword
    $env:AndroidSigningKeyPass = $keyPassword

    New-Item -ItemType Directory -Path $signingCheckDirectory -Force | Out-Null
    $unsignedJarPath = Join-Path $signingCheckDirectory "signing-check.jar"
    $signedJarPath = Join-Path $signingCheckDirectory "signing-check-signed.jar"
    Add-Type -AssemblyName System.IO.Compression
    $archive = [IO.Compression.ZipFile]::Open($unsignedJarPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $manifest = $archive.CreateEntry("META-INF/MANIFEST.MF")
        $manifestWriter = [IO.StreamWriter]::new($manifest.Open(), [Text.UTF8Encoding]::new($false))
        try {
            $manifestWriter.Write("Manifest-Version: 1.0`r`n`r`n")
        }
        finally {
            $manifestWriter.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $jarsignerArguments = @(
        "-keystore", $resolvedKeystorePath.Path
        "-storepass:env", "AndroidSigningStorePass"
        "-keypass:env", "AndroidSigningKeyPass"
        "-signedjar", $signedJarPath
        $unsignedJarPath
        $KeyAlias
    )
    $validationOutput = @(& $jarsigner.Source @jarsignerArguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $details = ($validationOutput | ForEach-Object { "$_" }) -join [Environment]::NewLine
        throw "Android signing credentials could not be validated. Check the keystore password, key alias, and key password. jarsigner reported: $details"
    }

    if (-not (Test-Path -LiteralPath $signedJarPath -PathType Leaf)) {
        throw "jarsigner did not produce the temporary signed archive required to validate the Android signing credentials."
    }

    Remove-Item -LiteralPath $signingCheckDirectory -Recurse -Force

    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null

    Write-Host "Publishing signed Android App Bundle (version code $VersionCode)..."
    & dotnet publish $projectPath `
        --framework net10.0-android `
        --configuration Release `
        "-p:AndroidPackageFormats=aab" `
        "-p:AndroidKeyStore=true" `
        "-p:AndroidSigningKeyStore=$($resolvedKeystorePath.Path)" `
        "-p:AndroidSigningKeyAlias=$KeyAlias" `
        "-p:ApplicationVersion=$VersionCode" `
        "-p:PublishDir=$publishDirectory\"
    if ($LASTEXITCODE -ne 0) {
        throw "Android App Bundle publish failed with exit code $LASTEXITCODE."
    }

    $bundles = @(Get-ChildItem -LiteralPath $publishDirectory -Filter "*-Signed.aab" -File)
    if ($bundles.Count -ne 1) {
        throw "Expected exactly one signed AAB in '$publishDirectory', but found $($bundles.Count)."
    }
    if ($bundles[0].Length -eq 0) {
        throw "The generated signed Android App Bundle is empty."
    }

    $jarsigner = Get-Command jarsigner -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $jarsigner) {
        throw "jarsigner was not found on PATH; it is required to verify the generated Android App Bundle."
    }

    $verificationOutput = @(& $jarsigner.Source -verify $bundles[0].FullName 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $details = ($verificationOutput | ForEach-Object { "$_" }) -join [Environment]::NewLine
        throw "The generated Android App Bundle failed signature verification. jarsigner reported: $details"
    }

    Copy-Item -LiteralPath $bundles[0].FullName -Destination $artifactPath -Force
    Write-Host "Google Play bundle created: $artifactPath"
    Write-Host "Use a version code greater than the one currently published on Google Play."
}
finally {
    if ($hadPreviousStorePassword) {
        $env:AndroidSigningStorePass = $previousStorePassword
    }
    else {
        Remove-Item Env:\AndroidSigningStorePass -ErrorAction SilentlyContinue
    }

    if ($hadPreviousKeyPassword) {
        $env:AndroidSigningKeyPass = $previousKeyPassword
    }
    else {
        Remove-Item Env:\AndroidSigningKeyPass -ErrorAction SilentlyContinue
    }

    if (Test-Path -LiteralPath $publishDirectory) {
        Remove-Item -LiteralPath $publishDirectory -Recurse -Force
    }

    if (Test-Path -LiteralPath $signingCheckDirectory) {
        Remove-Item -LiteralPath $signingCheckDirectory -Recurse -Force
    }
}


# To generate a keystore for signing, run the following command and keep the keystore and passwords safe:
# keytool -genkeypair -v -keystore upload-key.jks -keyalg RSA -keysize 2048 -validity 10000 -alias pictureqs
