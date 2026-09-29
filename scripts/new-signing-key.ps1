# Creates the Android release signing key, once, and hands it to GitHub Actions as secrets of the repository's
# `release` environment, the one .github/workflows/release.yml signs in. Create that environment first, with
# yourself as its required reviewer.
#
#   pwsh scripts/new-signing-key.ps1
#
# The key never enters the repository and its password is never printed. A copy stays in -KeyDir:
# keep it. Every later release must be signed with this same key, or the installed app refuses the
# update and has to be uninstalled first. Needs Windows, and the GitHub CLI signed in with access to the repo.
#
# The password is kept beside the key encrypted with DPAPI, not in plain text, so a copy of the folder is a
# locked keystore rather than a usable one. Only this Windows account on this machine can read it back:
#   [Net.NetworkCredential]::new('', (Get-Content password.dpapi | ConvertTo-SecureString)).Password
# Copy it into a password manager too; the DPAPI copy dies with this account.

param(
    [string]$KeyDir = (Join-Path $HOME '.edgepad-signing'),
    [string]$Repo = 'akshit-bansal11/edgepad'
)

$ErrorActionPreference = 'Stop'
$keyPath = Join-Path $KeyDir 'edgepad-release.p12'
$environment = 'release'

# Off Windows, ConvertFrom-SecureString has no DPAPI and would write the password out in the clear.
if (-not $IsWindows) {
    throw 'Run this on Windows: the password is stored with DPAPI.'
}

if (Test-Path $keyPath) {
    throw "A key already exists at $keyPath. Refusing to replace it: a new key breaks updates of the installed app."
}

# Checked before the key exists, so a missing environment never leaves a key that no secret holds.
gh api "repos/$Repo/environments/$environment" --silent
if ($LASTEXITCODE -ne 0) {
    throw "The repository has no '$environment' environment. Create it under Settings > Environments first."
}

New-Item -ItemType Directory -Force $KeyDir | Out-Null
$password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))

$rsa = [Security.Cryptography.RSA]::Create(3072)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    'CN=Edgepad', $rsa,
    [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$cert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddYears(30))
[IO.File]::WriteAllBytes($keyPath, $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $password))
ConvertTo-SecureString $password -AsPlainText -Force | ConvertFrom-SecureString |
    Set-Content -Path (Join-Path $KeyDir 'password.dpapi') -NoNewline

[Convert]::ToBase64String([IO.File]::ReadAllBytes($keyPath)) | gh secret set EDGEPAD_KEYSTORE_BASE64 --env $environment --repo $Repo
if ($LASTEXITCODE -ne 0) { throw 'Setting EDGEPAD_KEYSTORE_BASE64 failed' }
$password | gh secret set EDGEPAD_KEYSTORE_PASSWORD --env $environment --repo $Repo
if ($LASTEXITCODE -ne 0) { throw 'Setting EDGEPAD_KEYSTORE_PASSWORD failed' }

Write-Host "Key saved in $KeyDir. Secrets EDGEPAD_KEYSTORE_BASE64 and EDGEPAD_KEYSTORE_PASSWORD are set on the $environment environment of $Repo."
