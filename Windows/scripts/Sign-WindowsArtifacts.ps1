[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string[]] $Path,

    [Parameter()]
    [string] $CertificateBase64 = $env:WINDOWS_SIGNING_CERTIFICATE_BASE64,

    [Parameter()]
    [string] $CertificatePassword = $env:WINDOWS_SIGNING_CERTIFICATE_PASSWORD,

    [Parameter()]
    [string] $TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($CertificateBase64) -or [string]::IsNullOrWhiteSpace($CertificatePassword)) {
    throw "Windows Authenticode certificate secrets are missing. Both certificate and password are required."
}

$signtool = Get-ChildItem -Path "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Filter "signtool.exe" -File -Recurse |
    Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
    Sort-Object FullName -Descending |
    Select-Object -First 1

if (-not $signtool) {
    throw "signtool.exe was not found in the Windows SDK."
}

$temporaryDirectory = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    [IO.Path]::GetTempPath()
}
else {
    $env:RUNNER_TEMP
}
$certificatePath = Join-Path $temporaryDirectory "wifi-health-console-signing.pfx"

try {
    [IO.File]::WriteAllBytes($certificatePath, [Convert]::FromBase64String($CertificateBase64))

    foreach ($item in $Path) {
        $resolvedPath = (Resolve-Path -LiteralPath $item).Path
        Write-Host "Authenticode signing $(Split-Path -Leaf $resolvedPath)"

        & $signtool.FullName sign /fd SHA256 /td SHA256 /tr $TimestampUrl /f $certificatePath /p $CertificatePassword $resolvedPath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool sign failed for '$resolvedPath' with exit code $LASTEXITCODE."
        }

        & $signtool.FullName verify /pa /v $resolvedPath
        if ($LASTEXITCODE -ne 0) {
            throw "signtool verify failed for '$resolvedPath' with exit code $LASTEXITCODE."
        }
    }
}
finally {
    Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
}
