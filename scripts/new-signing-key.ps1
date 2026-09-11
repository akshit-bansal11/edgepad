# Creates the Android release signing key, once, and hands it to GitHub Actions as repository secrets.
#
#   pwsh scripts/new-signing-key.ps1
#
# The key never enters the repository and its password is never printed. A copy stays in -KeyDir:
# keep it. Every later release must be signed with this same key, or the installed app refuses the
# update and has to be uninstalled first. Needs the GitHub CLI, signed in with access to the repo.

param(
    [string]$KeyDir = (Join-Path $HOME '.edgepad-signing'),
    [string]$Repo = 'akshit-bansal11/edgepad'
)

$ErrorActionPreference = 'Stop'
$keyPath = Join-Path $KeyDir 'edgepad-release.p12'

if (Test-Path $keyPath) {
    throw "A key already exists at $keyPath. Refusing to replace it: a new key breaks updates of the installed app."
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
Set-Content -Path (Join-Path $KeyDir 'password.txt') -Value $password -NoNewline

[Convert]::ToBase64String([IO.File]::ReadAllBytes($keyPath)) | gh secret set EDGEPAD_KEYSTORE_BASE64 --repo $Repo
if ($LASTEXITCODE -ne 0) { throw 'Setting EDGEPAD_KEYSTORE_BASE64 failed' }
$password | gh secret set EDGEPAD_KEYSTORE_PASSWORD --repo $Repo
if ($LASTEXITCODE -ne 0) { throw 'Setting EDGEPAD_KEYSTORE_PASSWORD failed' }

Write-Host "Key saved in $KeyDir. Secrets EDGEPAD_KEYSTORE_BASE64 and EDGEPAD_KEYSTORE_PASSWORD are set on $Repo."
