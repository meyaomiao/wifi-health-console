[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("x64", "arm64")]
    [string[]] $Architecture = @("x64", "arm64"),

    [Parameter()]
    [string] $Version,

    [Parameter()]
    [string] $ArtifactsDirectory,

    [Parameter()]
    [string] $MakeNsisPath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$windowsRoot = Join-Path $repositoryRoot "Windows"
$versionFile = Join-Path $repositoryRoot "VERSION"
$installerScript = Join-Path $windowsRoot "installer\WiFiHealthConsole.nsi"
$iconPath = Join-Path $windowsRoot "src\WiFiHealthConsole.App\Assets\AppIcon.ico"

$strictUtf8 = [System.Text.UTF8Encoding]::new($false, $true)
try {
    $installerSource = [System.IO.File]::ReadAllText($installerScript, $strictUtf8)
}
catch {
    throw "NSIS source '$installerScript' must be valid UTF-8. $($_.Exception.Message)"
}

foreach ($requiredText in @("Wi-Fi 体检台", "安装程序", "桌面快捷方式")) {
    if ($installerSource.IndexOf($requiredText, [System.StringComparison]::Ordinal) -lt 0) {
        throw "NSIS source '$installerScript' is missing the expected UTF-8 text '$requiredText'."
    }
}

if ([string]::IsNullOrWhiteSpace($ArtifactsDirectory)) {
    $ArtifactsDirectory = Join-Path $windowsRoot "artifacts"
}

if (-not (Test-Path -LiteralPath $versionFile)) {
    throw "Repository version file not found at '$versionFile'."
}

$repositoryVersion = (Get-Content -LiteralPath $versionFile -Raw).Trim()
if ($repositoryVersion -notmatch "^\d+\.\d+\.\d+$") {
    throw "Repository version '$repositoryVersion' in '$versionFile' must use MAJOR.MINOR.PATCH format."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $repositoryVersion
}
elseif ($Version -ne $repositoryVersion) {
    throw "Requested installer version '$Version' does not match repository version '$repositoryVersion'."
}

$numericVersion = ($Version -split "[-+]")[0]
$versionParts = @($numericVersion -split "\.")
if ($versionParts.Count -gt 4 -or ($versionParts | Where-Object { $_ -notmatch "^\d+$" })) {
    throw "Version '$Version' cannot be converted to an NSIS four-part numeric version."
}

while ($versionParts.Count -lt 4) {
    $versionParts += "0"
}
$fileVersion = $versionParts -join "."

if ([string]::IsNullOrWhiteSpace($MakeNsisPath)) {
    $candidatePaths = @(
        (Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"),
        (Join-Path $env:ProgramFiles "NSIS\makensis.exe"),
        (Get-Command makensis.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue)
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    $MakeNsisPath = $candidatePaths | Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($MakeNsisPath) -or -not (Test-Path -LiteralPath $MakeNsisPath)) {
    throw "makensis.exe was not found. Install NSIS first or provide -MakeNsisPath."
}

$installerDirectory = Join-Path $ArtifactsDirectory "installers"
New-Item -ItemType Directory -Path $installerDirectory -Force | Out-Null

$outputs = foreach ($arch in $Architecture) {
    $runtimeIdentifier = "win-$arch"
    $publishDirectory = Join-Path $ArtifactsDirectory "publish\$runtimeIdentifier"
    $applicationPath = Join-Path $publishDirectory "WiFiHealthConsole.exe"

    if (-not (Test-Path -LiteralPath $applicationPath)) {
        throw "Published application not found at '$applicationPath'. Run dotnet publish for $runtimeIdentifier first."
    }

    $outputPath = Join-Path $installerDirectory "WiFi-Health-Console-Setup-$arch.exe"
    $arguments = @(
        "/V3",
        "/INPUTCHARSET",
        "UTF8",
        "/DAPP_ARCH=$arch",
        "/DPRODUCT_VERSION=$Version",
        "/DFILE_VERSION=$fileVersion",
        "/DPUBLISH_DIR=$publishDirectory",
        "/DOUTPUT_FILE=$outputPath",
        "/DAPP_ICON=$iconPath",
        $installerScript
    )

    Write-Host "Building $arch installer from $publishDirectory"
    & $MakeNsisPath @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "makensis.exe failed for $arch with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "NSIS completed without producing '$outputPath'."
    }

    Get-Item -LiteralPath $outputPath
}

$outputs | Select-Object Name, Length, FullName | Format-Table -AutoSize
