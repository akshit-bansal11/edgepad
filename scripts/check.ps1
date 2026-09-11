# The quality gate, for both halves of the repo, in one place.
#
#   pwsh scripts/check.ps1            writes: formats Kotlin and C# in place, then checks
#   pwsh scripts/check.ps1 -Ci        non-mutating twin: fails on unformatted code instead of fixing it
#   pwsh scripts/check.ps1 -Only android|windows
#
# CI runs this same script with -Ci, so the local gate and CI cannot disagree.

param(
    [switch]$Ci,
    [ValidateSet('all', 'android', 'windows')]
    [string]$Only = 'all'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Invoke-Step([string]$Name, [scriptblock]$Command) {
    Write-Host "==> $Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Name failed with exit code $LASTEXITCODE"
    }
}

if ($Only -in 'all', 'android') {
    Push-Location (Join-Path $root 'android')
    try {
        $gradle = if ($IsWindows) { '.\gradlew.bat' } else { './gradlew' }
        $format = if ($Ci) { 'ktlintCheck' } else { 'ktlintFormat' }
        Invoke-Step "android: $format, lint, unit tests" { & $gradle --no-daemon $format lint testDebugUnitTest }
    }
    finally { Pop-Location }
}

if ($Only -in 'all', 'windows') {
    Push-Location (Join-Path $root 'windows')
    try {
        if ($Ci) {
            Invoke-Step 'windows: format (verify)' { dotnet format Edgepad.slnx --verify-no-changes }
        }
        else {
            Invoke-Step 'windows: format' { dotnet format Edgepad.slnx }
        }
        Invoke-Step 'windows: build' { dotnet build Edgepad.slnx -c Release -warnaserror }
        Invoke-Step 'windows: test' { dotnet test --solution Edgepad.slnx -c Release --no-build }
    }
    finally { Pop-Location }
}

Write-Host 'check: clean'
