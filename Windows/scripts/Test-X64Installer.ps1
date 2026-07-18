[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $InstallerPath,

    [Parameter(Mandatory)]
    [string] $ExpectedVersion
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$expectedProductName = "Wi-Fi 体检台"
$installDirectory = Join-Path $env:LOCALAPPDATA "Programs\WiFiHealthConsole"
$applicationPath = Join-Path $installDirectory "WiFiHealthConsole.exe"
$uninstallerPath = Join-Path $installDirectory "Uninstall.exe"
$currentProgramsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
$commonProgramsDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonPrograms)
$shortcutCandidates = @(
    (Join-Path $currentProgramsDirectory "Wi-Fi 体检台\Wi-Fi 体检台.lnk"),
    (Join-Path $commonProgramsDirectory "Wi-Fi 体检台\Wi-Fi 体检台.lnk"),
    (Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Wi-Fi 体检台\Wi-Fi 体检台.lnk")
) | Select-Object -Unique
$startMenuShortcut = $null
$applicationRegistryPath = "HKCU:\Software\WiFiHealthConsole"
$uninstallRegistryPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\WiFiHealthConsole"
$historyDirectory = Join-Path $env:LOCALAPPDATA "WiFiHealthConsole"
$smokeHistoryDirectory = Join-Path $historyDirectory "installer-smoke-test"
$historySentinel = Join-Path $smokeHistoryDirectory "history.keep"
$requiredRepositoryLegalFiles = @(
    "LICENSE",
    "THIRD-PARTY-NOTICES.md",
    "CODE_SIGNING_POLICY.md"
)
$repositoryLicensesDirectory = Join-Path $repositoryRoot "licenses"

foreach ($relativePath in $requiredRepositoryLegalFiles) {
    $sourcePath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Required repository legal file '$sourcePath' was not found."
    }
}

if (-not (Test-Path -LiteralPath $repositoryLicensesDirectory -PathType Container)) {
    throw "Required repository license directory '$repositoryLicensesDirectory' was not found."
}

$repositoryLicenseFiles = @(Get-ChildItem -LiteralPath $repositoryLicensesDirectory -File -Recurse)
if ($repositoryLicenseFiles.Count -eq 0) {
    throw "Required repository license directory '$repositoryLicensesDirectory' does not contain any files."
}

function Invoke-AndRequireSuccess {
    param(
        [Parameter(Mandatory)] [string] $FilePath,
        [Parameter()] [string[]] $ArgumentList = @()
    )

    $process = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList -PassThru -Wait
    if ($process.ExitCode -ne 0) {
        throw "'$FilePath' exited with code $($process.ExitCode)."
    }
}

try {
    $installerVersionInfo = (Get-Item -LiteralPath $installer).VersionInfo
    if ($installerVersionInfo.ProductName -ne $expectedProductName) {
        throw "Installer product name '$($installerVersionInfo.ProductName)' does not match '$expectedProductName'. The NSIS source may have been compiled with the wrong input charset."
    }
    if ($installerVersionInfo.FileDescription -notlike "$expectedProductName*安装程序*") {
        throw "Installer file description '$($installerVersionInfo.FileDescription)' does not contain the expected Chinese text."
    }

    Write-Host "Silently installing $(Split-Path -Leaf $installer)"
    Invoke-AndRequireSuccess -FilePath $installer -ArgumentList "/S"

    foreach ($requiredPath in @($applicationPath, $uninstallerPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath)) {
            throw "Installer smoke test expected '$requiredPath' to exist."
        }
    }

    foreach ($relativePath in $requiredRepositoryLegalFiles) {
        $installedPath = Join-Path $installDirectory $relativePath
        if (-not (Test-Path -LiteralPath $installedPath -PathType Leaf)) {
            throw "Installer smoke test expected legal file '$installedPath' to exist."
        }
    }

    foreach ($sourceFile in $repositoryLicenseFiles) {
        $relativePath = [System.IO.Path]::GetRelativePath($repositoryLicensesDirectory, $sourceFile.FullName)
        $installedPath = Join-Path (Join-Path $installDirectory "licenses") $relativePath
        if (-not (Test-Path -LiteralPath $installedPath -PathType Leaf)) {
            throw "Installer smoke test expected third-party license '$installedPath' to exist."
        }
    }

    foreach ($candidate in $shortcutCandidates) {
        if (Test-Path -LiteralPath $candidate) {
            $startMenuShortcut = $candidate
            break
        }
    }
    if ($null -eq $startMenuShortcut) {
        Write-Warning "The headless runner did not expose the Start menu shortcut through current/common Programs paths. Core install, registry, launch, upgrade, and uninstall checks will continue."
        Write-Host "Current Programs directory: $currentProgramsDirectory"
        Write-Host "Common Programs directory: $commonProgramsDirectory"
    }

    foreach ($registryPath in @($applicationRegistryPath, $uninstallRegistryPath)) {
        if (-not (Test-Path -LiteralPath $registryPath)) {
            throw "Installer smoke test expected registry key '$registryPath' to exist."
        }
    }

    $applicationVersion = (Get-Item -LiteralPath $applicationPath).VersionInfo.ProductVersion
    if ($applicationVersion -ne $ExpectedVersion) {
        throw "Installed application version '$applicationVersion' does not match '$ExpectedVersion'."
    }

    $registeredVersion = (Get-ItemProperty -LiteralPath $applicationRegistryPath -Name Version).Version
    $uninstallRegistration = Get-ItemProperty -LiteralPath $uninstallRegistryPath
    $displayVersion = $uninstallRegistration.DisplayVersion
    if ($uninstallRegistration.DisplayName -ne $expectedProductName) {
        throw "Installed display name '$($uninstallRegistration.DisplayName)' does not match '$expectedProductName'."
    }
    foreach ($actualVersion in @($registeredVersion, $displayVersion)) {
        if ($actualVersion -ne $ExpectedVersion) {
            throw "Installed registry version '$actualVersion' does not match '$ExpectedVersion'."
        }
    }

    New-Item -ItemType Directory -Path $smokeHistoryDirectory -Force | Out-Null
    Set-Content -LiteralPath $historySentinel -Value "This file must survive upgrade and uninstall." -Encoding utf8

    Write-Host "Re-running the installer to exercise in-place upgrade"
    Invoke-AndRequireSuccess -FilePath $installer -ArgumentList "/S"
    if (-not (Test-Path -LiteralPath $historySentinel)) {
        throw "In-place upgrade removed the LocalAppData history sentinel."
    }

    Write-Host "Launching the installed application"
    $application = Start-Process -FilePath $applicationPath -PassThru
    Start-Sleep -Seconds 8
    $application.Refresh()

    if ($application.HasExited) {
        throw "The installed application exited during the startup smoke test with code $($application.ExitCode)."
    }

    Stop-Process -Id $application.Id -Force
    $application.WaitForExit()

    Write-Host "Silently uninstalling the application"
    Invoke-AndRequireSuccess -FilePath $uninstallerPath -ArgumentList "/S"

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    while ((Test-Path -LiteralPath $applicationPath) -and [DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }

    if (Test-Path -LiteralPath $applicationPath) {
        throw "The application executable still exists after uninstall."
    }

    if ($null -ne $startMenuShortcut -and (Test-Path -LiteralPath $startMenuShortcut)) {
        throw "The Start menu shortcut still exists after uninstall."
    }

    foreach ($registryPath in @($applicationRegistryPath, $uninstallRegistryPath)) {
        if (Test-Path -LiteralPath $registryPath) {
            throw "Installer registry key '$registryPath' still exists after uninstall."
        }
    }

    if (-not (Test-Path -LiteralPath $historySentinel)) {
        throw "Uninstall removed the LocalAppData history sentinel; user history must be retained by default."
    }

    Write-Host "x64 install, legal-file, in-place upgrade, launch, uninstall, and history-retention smoke tests passed."
}
finally {
    Get-Process -Name "WiFiHealthConsole" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

    if (Test-Path -LiteralPath $uninstallerPath) {
        Start-Process -FilePath $uninstallerPath -ArgumentList "/S" -Wait -ErrorAction SilentlyContinue | Out-Null
    }

    Remove-Item -LiteralPath $historySentinel -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $smokeHistoryDirectory -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $historyDirectory -Force -ErrorAction SilentlyContinue
}
