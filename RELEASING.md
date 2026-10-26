# Releasing AltPane

The version, company, product name, and copyright live in `Directory.Build.props`. A release is the commit that `vX.Y.Z` points at. GitHub Actions builds the official zip from that tag. Do not publish an executable built on a developer machine as the release asset.

## GitHub Release

Pushing a tag that matches `v*` runs `.github/workflows/release.yml` on `windows-latest`:

1. `dotnet test`
2. Framework-dependent single-file publish for `win-x64` (`--self-contained false`, `PublishSingleFile=true`)
3. Zip `AltPane-<version>-win-x64.zip` with `AltPane.exe` at the root of the archive
4. Write `AltPane-<version>-win-x64.zip.sha256` (`sha256sum` layout: hash, two spaces, file name)
5. Create or update the GitHub Release for that tag with `GITHUB_TOKEN` (`contents: write`) and upload both files

The existing `.github/workflows/build.yml` still runs tests and a self-contained publish on every push and pull request. That artifact is not the winget package.

The .NET 8 Desktop Runtime (x64) is required to run the framework-dependent zip:

- <https://dotnet.microsoft.com/download/dotnet/8.0>
- <https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe>

## winget

Manifests for 0.3.3 are in `packaging/winget/manifests/k/kurlez/AltPane/0.3.3/`.

`InstallerSha256` for 0.3.3 is 64 zeros until the CI asset exists. Copy the hash from `AltPane-0.3.3-win-x64.zip.sha256` on the GitHub Release. That file is the official zip. A locally packed `AltPane-v0.3.3-win-x64-fx.zip` is a different archive and will not match.

From a machine with [App Installer](https://learn.microsoft.com/windows/package-manager/winget/) (the `winget` client):

```powershell
winget validate --manifest packaging/winget/manifests/k/kurlez/AltPane/0.3.3
winget install --manifest packaging/winget/manifests/k/kurlez/AltPane/0.3.3
```

Validate after the placeholder hash is replaced. `winget install --manifest` downloads `InstallerUrl`, so the release asset has to be public first.

Package identifier: `kurlez.AltPane`. The installer is a portable zip (`NestedInstallerType: portable`, command alias `altpane`) and depends on `Microsoft.DotNet.DesktopRuntime.8`.

`.github/workflows/winget.yml` runs when a GitHub Release is published. It uses [vedantmgoyal9/winget-releaser](https://github.com/vedantmgoyal9/winget-releaser) for `kurlez.AltPane` and keeps three versions. The workflow needs a classic PAT in the repository secret `WINGET_TOKEN` with the `public_repo` scope, authorized to open a pull request on `microsoft/winget-pkgs`. If that secret is absent, the workflow logs that and skips the submission.

## Startup path

winget’s portable command is often a symlink or alias. AltPane resolves `Environment.ProcessPath` with `File.ResolveLinkTarget` before writing the HKCU Run value and before restarting as administrator, so the registered path is the real `AltPane.exe`.

## SignPath Foundation

AltPane is MIT licensed (OSI) and the public CI workflow above is the reproducible build. That is the usual bar for a free [SignPath Foundation](https://signpath.org/foundation) certificate for an open-source project.

1. Publish the repository at <https://github.com/kurlez/AltPane> and confirm a `v*` tag produces `AltPane-<version>-win-x64.zip` only from GitHub Actions.
2. Apply at <https://signpath.org/foundation> (the same program is described at <https://signpath.io/foundation>). The project needs an OSI license, a public repository, and a public CI build. Describe the unsigned zip and the workflow file.
3. After approval, add a SignPath signing step in `.github/workflows/release.yml` between the `build` job and the `release` job. The commented `sign` job in that file is the insertion point. The `release` job should upload the signed zip and a hash computed from that signed zip.
4. Put the new hash into the winget installer manifest before submitting the package.

Do not submit a developer-machine binary for signing or as the winget installer. The signed artifact has to be the one GitHub Actions built from the tag.
