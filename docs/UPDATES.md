# Updating in place

Edgepad checks GitHub once a day for a newer release. Where it can, it downloads the new file, checks it, and replaces itself. This page says what is checked, in what order, and what that does and does not protect against. It is not part of the wire protocol in [PROTOCOL.md](PROTOCOL.md): it runs between an app and GitHub, never over the phone link. Nothing here has been run on a device.

## The scheme

Every release carries `SHA256SUMS.txt`, a list of the SHA-256 of each file in the release, one `sha256sum` line each: 64 lowercase hex digits, two characters, the file name. Beside it is `SHA256SUMS.txt.sig`, an ECDSA signature over the exact bytes of that list.

- Algorithm: ECDSA on NIST P-256 with SHA-256.
- Signature encoding: ASN.1 DER (RFC 3279 `SEQUENCE` of two integers), which is what `openssl dgst -sha256 -sign` writes and what .NET reads as `DSASignatureFormat.Rfc3279DerSequence`.
- The public key is a base64 SubjectPublicKeyInfo, embedded in each app and kept in `protocol/update-key.txt`. A public key is not a secret.
- The private key exists only as the secret `EDGEPAD_UPDATE_KEY_PEM` of the repository's `release` environment, plus the copy the owner keeps. The owner creates it once with `scripts/new-update-key.ps1`. A session never makes it.
- The signing step is its own job, `sign-sums` in `.github/workflows/release.yml`. It needs the `release` environment, so it waits for the owner's approval like the publish step. It builds nothing, runs no action but checkout and the two artifact ones, and is the only job that reads the key. It checks the signature it made against the public half, and a tagged run fails if that public half differs from `protocol/update-key.txt`, so a release cannot be signed with a key no app knows.
- The Android app does not verify this signature. Android's installer refuses an APK that is not signed by the same key as the installed app, and that check is the one that counts there.

## The order of checks

The Windows, Mac and Linux apps follow this order. Each step stops everything after it, so any failure leaves nothing installed and no half file behind.

1. Find the newest version from the release redirect. Only this repository's own tag page with a plain three-part version is accepted.
2. Decide. A version that is not newer than the running one is ignored. A version the owner skipped is not offered again. With no key embedded the answer is to open the release page. A newer first number means the phone must be updated too, so the owner confirms first.
3. Fetch `SHA256SUMS.txt` and `SHA256SUMS.txt.sig` for the offered version. Downloads are https only, from GitHub's own hosts, each redirect followed by hand and checked, with a size cap enforced on the announced length and on the bytes read.
4. Verify the signature over the list with the embedded key. The key must be a P-256 key; anything else fails. An empty, malformed or wrong signature fails.
5. Find the hash listed for the exact asset name of the offered version (`Edgepad-<version>.exe`, `Edgepad-<version>-linux-x64.tar.gz`, `Edgepad-<version>-mac.zip`). A list with a line in any other shape, a name holding `/` or `\`, or the asked-for name listed twice is unusable as a whole.
6. Download the asset to a `.download` file, hashing as it arrives, within its size cap.
7. Compare the file's hash with the listed one, in constant time.
8. Only then move the file into place and start the platform's own swap.

## What it protects against

A takeover of the GitHub account or of a release. Someone who can edit the release can replace the files and the list, but cannot make a signature the embedded key accepts, so every app refuses the result and nothing runs. The same holds for a file swapped on the way: step 7 catches it.

Naming `Edgepad-<offered>.*` in step 5 means a genuine older signed release replayed as a newer one is not accepted: the version in the file name must be the version being offered. A replay is limited to releases that are genuine and newer than the one running.

## What it does not protect against

- A stolen signing key. Whoever holds the private key can sign any release, and every installed app will accept it. The key lives in a protected environment secret and in the owner's copy, and that is all that guards it.
- A bug or a malicious change in a genuine release. A signature says who published a file, not that the file is good.
- A compromised laptop or a compromised copy of the app. They check nothing of their own accord.
- Anything on the phone link. That has its own protections, described in [PROTOCOL.md](PROTOCOL.md), and none of this applies to it.

## When no key is set

An app built with no embedded key installs nothing. It opens the release page in the browser and the owner downloads by hand. This is the safe default, and it is also how a draft build behaves before the key exists: a hand-run draft without the secret ships without `SHA256SUMS.txt.sig`, and a tagged run without it fails.

## If the key is lost

There is no way to sign for the old public key without its private half, and no app can be told to trust a new key by a release the old key did not sign. Recovery is a manual release:

1. Run `scripts/new-update-key.ps1` to make a new key. It refuses to overwrite an existing one, so move the old folder aside first.
2. Put the public line it prints into `protocol/update-key.txt`, build and publish a release with it.
3. Say plainly on that release that installed copies must be downloaded and installed by hand once. Apps still holding the old key will refuse its signature and fall back to the release page.
4. From then on, every release is signed with the new key.

A leaked key is handled the same way, with one addition: until every user has installed the new build by hand, the old key can still sign something installed apps accept, so treat the window as open and shorten it.
