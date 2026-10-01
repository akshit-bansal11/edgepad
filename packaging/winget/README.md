# winget

The manifest that puts the laptop app in the Windows Package Manager as `AkshitBansal.Edgepad`. It is a `portable` package: winget downloads the release's exe as it is, puts an `edgepad` command on the PATH, and replaces the file on `winget upgrade`.

The files here are a record of what was submitted. winget does not read them from this repository; a package exists once a pull request adding them is merged into [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs). Submitting is the maintainer's to do, because the pull request is opened from his account.

## First submission

```powershell
winget install Microsoft.WingetCreate
winget validate --manifest packaging/winget/3.2.0
wingetcreate submit packaging/winget/3.2.0
```

`wingetcreate submit` asks for a GitHub sign-in the first time, forks `winget-pkgs` and opens the pull request. Microsoft's checks then download and scan the exe; a person approves a new package.

## Each later release

```powershell
wingetcreate update AkshitBansal.Edgepad --version 3.3.0 `
  --urls https://github.com/akshit-bansal11/edgepad/releases/download/v3.3.0/Edgepad-3.3.0.exe --submit
```

It fetches the exe, works out the hash and opens the pull request. Add the manifest it wrote to this folder if the record is to be kept.

## Not yet known

None of this has been through Microsoft's checks, so these are open until the first submission:

- **Whether an unsigned exe passes.** `Edgepad.exe` is not code-signed, and the validation run includes a malware scan and a SmartScreen check.
- **Whether Start with Windows survives an upgrade.** The app writes its own path into the Run key. If winget keeps the versioned file name, the path changes with each release and the switch needs turning on again.
- **Whether `winget upgrade` can replace the exe while the tray app is running.** Quit Edgepad first if it cannot.
