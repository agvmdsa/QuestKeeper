<p align="center">
  <img src="docs/logo.svg" width="128" height="128" alt="AQW Companion logo">
</p>

<h1 align="center">AQW Companion</h1>

<p align="center">
  A Windows desktop companion for AdventureQuest Worlds. Sign in once on the official site and see your
  character's account, inventory, equipment and badges in one place — read-only, refreshed only when you
  ask, nothing sent anywhere.
</p>

<p align="center">
  <a href="https://github.com/agvmdsa/aqw-companion/releases/latest"><img src="docs/download-windows.svg" width="368" alt="Download the latest AQW Companion for Windows"></a>
</p>

<p align="center">
  <sub>Self-contained single file · SHA-256 checksum next to every release · <a href="#install">how to install</a></sub>
</p>

<p align="center">
  <a href="https://github.com/agvmdsa/aqw-companion/actions/workflows/ci.yml"><img src="https://github.com/agvmdsa/aqw-companion/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI status of the main branch"></a>
</p>

## Why

Checking what you own in AQW means clicking through several account pages, one at a time. AQW Companion reads them for you and keeps the latest copy on your machine, so you can look things up without going back to the browser.

- **You sign in, the app only reads** — a sign-in window opens `account.aq.com` and you log in yourself. The app never fills forms, clicks or navigates on its own, and pages outside `account.aq.com` are ignored entirely.
- **Refresh on demand** — nothing is fetched in the background. Press **Refresh** when you want fresh data; the last sync time is always shown.
- **Account, inventory, equipment and badges** — summary cards for each, with a details sidebar. Equipment, inventory and badges are read from your public character pages.
- **Saved locally** — the last good result is kept in a local database, so the app opens with your data even when you are offline. A suspicious read (a layout change on the site, an empty table) never overwrites data that was good.
- **Honest session state** — the app shows whether your session is still valid. **Sign out** ends it on the site and removes everything the app saved on your computer. The app never reads or stores your password.
- **No telemetry** — nothing leaves your machine. When something breaks you can export a diagnostics zip yourself, with cookies, tokens, e-mail and account name redacted, and send it only if you choose to.

## Install

1. Click **Download for Windows** above (or open the [latest release](https://github.com/agvmdsa/aqw-companion/releases/latest)) and download `AqwCompanion-vX.Y.Z-win-x64.zip`; the file name carries the version.
2. Extract the zip anywhere and run `AqwCompanion.exe`. There is no installer and no .NET to install, the runtime is bundled.
3. The first release builds are not code-signed yet, so Windows SmartScreen may warn you. Choose **More info**, then **Run anyway**, if you trust the file.
4. Optional: check the download against `SHA256SUMS.txt` from the same release, with `Get-FileHash .\AqwCompanion-vX.Y.Z-win-x64.zip -Algorithm SHA256` in PowerShell, or `shasum -a 256 -c SHA256SUMS.txt` on macOS or Linux.

AQW Companion needs 64-bit Windows. Your data, logs and crash reports live under `%LOCALAPPDATA%\AqwCompanion`. Delete that folder to remove everything the app stored.

## Basic usage

- **Connect** — open the **Account** tab, press **Sign in** and log in on the window that appears. It closes by itself once your character name has been read.
- **Refresh** — read the pages again. When the app is showing saved data, a notice says so, and the last sync time is shown.
- **Details** — click a card to open its sidebar, with the full list for inventory, equipment or badges.
- **Sign out** — asks for confirmation, ends the session on the site and removes what the app saved locally.
- **Diagnostics** — the **Diagnostics** tab shows where the logs are and exports the diagnostics zip.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the exact version is pinned in `global.json`).

```sh
dotnet build AqwCompanion.slnx -c Release   # build everything
dotnet test AqwCompanion.slnx -c Release    # unit tests, the architecture test and the headless UI smoke test
dotnet run --project src/AqwCompanion.App   # run the app
```

Releases are built by `.github/workflows/release.yml` when a `vX.Y.Z` tag is pushed: tests, a self-contained single-file `win-x64` publish, and a zip with its `SHA256SUMS.txt`. The version comes from the tag (MinVer).

## Not affiliated

AQW Companion is an unofficial fan project. It is not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds and its content belong to their owners.
