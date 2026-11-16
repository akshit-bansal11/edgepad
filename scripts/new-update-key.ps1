# Creates the key that signs SHA256SUMS.txt for in-app updates, once, and hands it to GitHub Actions as the
# secret EDGEPAD_UPDATE_KEY_PEM of the repository's `release` environment, the one .github/workflows/release.yml
# signs in. Create that environment first, with yourself as its required reviewer.
#
#   pwsh scripts/new-update-key.ps1
#
# The private key never enters the repository and is never printed. A copy stays in -KeyDir: keep it. Every later
# release must be signed with this same key, or installed apps refuse the update. The script prints the PUBLIC
# key once; that line goes into protocol/update-key.txt. Needs Windows (the folder is locked to this account with
# an ACL) and the GitHub CLI signed in with access to the repo.

param(
    [string]$KeyDir = (Join-Path $HOME '.edgepad-update-signing'),
    [string]$Repo = 'akshit-bansal11/edgepad'
)

$ErrorActionPreference = 'Stop'
$keyPath = Join-Path $KeyDir 'update-key.pem'
$environment = 'release'

if (-not $IsWindows) {
    throw 'Run this on Windows: the key file is locked to the current account with an ACL.'
}

if (Test-Path $keyPath) {
    throw "A key already exists at $keyPath. Refusing to replace it: a new key makes every installed app refuse updates."
}

# Checked before the key exists, so a missing environment never leaves a key that no secret holds.
gh api "repos/$Repo/environments/$environment" --silent
if ($LASTEXITCODE -ne 0) {
    throw "The repository has no '$environment' environment. Create it under Settings > Environments first."
}

New-Item -ItemType Directory -Force $KeyDir | Out-Null

$ecdsa = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve]::NamedCurves.nistP256)
$pem = $ecdsa.ExportPkcs8PrivateKeyPem()
$publicKey = [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo())

# Self-test before anything is stored: sign and verify a fixed string.
$data = [Text.Encoding]::UTF8.GetBytes('edgepad update key self-test')
$sha256 = [Security.Cryptography.HashAlgorithmName]::SHA256
if (-not $ecdsa.VerifyData($data, $ecdsa.SignData($data, $sha256), $sha256)) {
    throw 'Self-test failed: the new key did not verify its own signature.'
}

# Only the current user may read the folder: drop inherited access, then grant this account alone.
$acl = Get-Acl $KeyDir
$acl.SetAccessRuleProtection($true, $false)
$me = [Security.Principal.WindowsIdentity]::GetCurrent().User
$acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    $me, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
Set-Acl -Path $KeyDir -AclObject $acl
[IO.File]::WriteAllText($keyPath, $pem)

$pem | gh secret set EDGEPAD_UPDATE_KEY_PEM --env $environment --repo $Repo
if ($LASTEXITCODE -ne 0) { throw 'Setting EDGEPAD_UPDATE_KEY_PEM failed' }

Write-Host "Key saved in $KeyDir. Secret EDGEPAD_UPDATE_KEY_PEM is set on the $environment environment of $Repo."
Write-Host "PUBLIC KEY (not secret): $publicKey"
