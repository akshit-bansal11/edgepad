# winget

The manifest that puts the laptop app in the Windows Package Manager as `AkshitBansal.Edgepad`. It is a `portable` package: winget downloads the release's exe as it is, puts an `edgepad` command on the PATH, and replaces the file on `winget upgrade`.

The files here are a record of what was submitted. winget does not read them from this repository; a package exists once a pull request adding them is merged into [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Submitting is the maintainer's to do, because the pull request is opened from his account.

## Submitting

```bash
winget validate --manifest packaging/winget/3.2.0
bash packaging/winget/submit.sh 3.2.0
```

`submit.sh` forks `winget-pkgs` if there is no fork yet, writes the three files to a branch of it through the GitHub API, and opens the pull request with `gh`. Nothing is cloned. Microsoft's `wingetcreate` does the same job and is not used here, because it needs the .NET runtime.

On the pull request, a bot asks for Microsoft's Contributor License Agreement the first time; it is signed by replying there. Microsoft's checks then download and scan the exe, and a person approves a new package.

## Each later release

Copy the newest folder to one named for the new version, and in its three files change `PackageVersion`, the version in `InstallerUrl` and `ReleaseNotesUrl`, `ReleaseDate`, and `InstallerSha256`, which is the exe's line in the release's `SHA256SUMS.txt`, in capitals. Then validate and submit as above.

## Not yet known

None of this has been through Microsoft's checks, so these are open until the first submission:

- **Whether an unsigned exe passes.** `Edgepad.exe` is not code-signed, and the validation run includes a malware scan and a SmartScreen check.
- **Whether Start with Windows survives an upgrade.** The app writes its own path into the Run key. If winget keeps the versioned file name, the path changes with each release and the switch needs turning on again.
- **Whether `winget upgrade` can replace the exe while the tray app is running.** Quit Edgepad first if it cannot.
