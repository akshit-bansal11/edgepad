# The quality gate, for every app in the repo, in one place.
#
#   pwsh scripts/check.ps1            writes: formats Kotlin, C# and Swift in place, then checks
#   pwsh scripts/check.ps1 -Ci        non-mutating twin: fails on unformatted code instead of fixing it
#   pwsh scripts/check.ps1 -Only android|core|windows|linux|macos
#
# The Mac app's gate needs Swift, so it runs on a Mac only: a plain run on Windows or Linux leaves it to CI.
#
# CI runs this same script with -Ci, so the local gate and CI cannot disagree.

param(
    [switch]$Ci,
    [ValidateSet('all', 'android', 'core', 'windows', 'linux', 'macos')]
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
        Invoke-Step "android: $format, lint, unit tests" { & $gradle --no-daemon --continue $format lint testDebugUnitTest }
    }
    finally { Pop-Location }
}

if ($Only -in 'all', 'core') {
    Push-Location (Join-Path $root 'shared')
    try {
        if ($Ci) {
            Invoke-Step 'core: format (verify)' { dotnet format Edgepad.Core.slnx --verify-no-changes }
        }
        else {
            Invoke-Step 'core: format' { dotnet format Edgepad.Core.slnx }
        }
        Invoke-Step 'core: build' { dotnet build Edgepad.Core.slnx -c Release -warnaserror }
        Invoke-Step 'core: test' { dotnet test --solution Edgepad.Core.slnx -c Release --no-build }
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

if ($Only -in 'all', 'linux') {
    Push-Location (Join-Path $root 'linux')
    try {
        if ($Ci) {
            Invoke-Step 'linux: format (verify)' { dotnet format Edgepad.Linux.slnx --verify-no-changes }
        }
        else {
            Invoke-Step 'linux: format' { dotnet format Edgepad.Linux.slnx }
        }
        Invoke-Step 'linux: build' { dotnet build Edgepad.Linux.slnx -c Release -warnaserror }
        Invoke-Step 'linux: test' { dotnet test --solution Edgepad.Linux.slnx -c Release --no-build }
    }
    finally { Pop-Location }
}

if ($Only -eq 'macos' -and -not $IsMacOS) {
    throw 'The macos gate needs a Mac: it builds against AppKit and IOBluetooth. CI runs it on every push.'
}

if ($Only -eq 'macos' -or ($Only -eq 'all' -and $IsMacOS)) {
    Push-Location (Join-Path $root 'macos')
    try {
        $sources = 'Sources', 'Tests', 'Package.swift'
        if (-not $Ci) {
            Invoke-Step 'macos: format' { swift format format --in-place --recursive @sources }
        }
        Invoke-Step 'macos: lint' { swift format lint --strict --recursive @sources }
        Invoke-Step 'macos: build' { swift build -Xswiftc -warnings-as-errors }
        Invoke-Step 'macos: test' { swift test -Xswiftc -warnings-as-errors }
    }
    finally { Pop-Location }
}

Write-Host 'check: clean'
