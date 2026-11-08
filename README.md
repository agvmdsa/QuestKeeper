<p align="center">
  <img src="docs/logo.svg" width="128" height="128" alt="AQW Companion logo">
</p>

<h1 align="center">AQW Companion</h1>

<p align="center">
  A Windows desktop companion for AdventureQuest Worlds. It reads the game's own network traffic on your
  machine while you play and shows your character, reputation and inventory in one place — read-only,
  nothing sent anywhere.
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

Seeing what you own, your faction ranks and what is missing for the next rank means clicking through the game and the wiki. AQW Companion listens to the data the game already sends to your own computer and keeps the latest copy on your machine, so you can look things up while you play.

> **The app was re-architected.** Earlier versions were a companion where you signed in on `account.aq.com` and the app read the HTML of your account pages. That is gone: there is no sign-in, no embedded browser and no reading of the account website any more. The app now reads the game's network traffic (Windows, see [Game data capture](#game-data-capture-windows)), which is a different risk class: read the warning there before using it.

- **Read-only** — the app never sends anything to the game and never acts for you.
- **Game data** — profile numbers, faction standings with rank and what is missing for the next one, and your items, updated while you play.
- **Hard Farms** — a list imported from the Wiki on your click (CC BY-SA, never bundled) that marks what you own from the captured inventory.
- **Saved locally** — the last data is kept in a local database, so the app opens with it. **Forget my data** (in **Settings**) removes everything the app saved.
- **No telemetry** — nothing leaves your machine. When something breaks you can export a diagnostics zip yourself, with the logs redacted, and send it only if you choose to.

## Install

1. Click **Download for Windows** above (or open the [latest release](https://github.com/agvmdsa/aqw-companion/releases/latest)) and download `AqwCompanion-vX.Y.Z-win-x64.zip`; the file name carries the version.
2. Extract the zip anywhere and run `AqwCompanion.exe`. There is no installer and no .NET to install, the runtime is bundled.
3. The first release builds are not code-signed yet, so Windows SmartScreen may warn you. Choose **More info**, then **Run anyway**, if you trust the file.
4. Optional: check the download against `SHA256SUMS.txt` from the same release, with `Get-FileHash .\AqwCompanion-vX.Y.Z-win-x64.zip -Algorithm SHA256` in PowerShell, or `shasum -a 256 -c SHA256SUMS.txt` on macOS or Linux.

AQW Companion needs 64-bit Windows. Your data, logs and crash reports live under `%LOCALAPPDATA%\AqwCompanion`. Delete that folder to remove everything the app stored.

## Basic usage

The app is one screen, with no tabs: a strip with your character across the top (name, level, class, gold, experience, AC, class points, days played, and the gold and experience of your last kill), and below it **Reputation** (your factions, closest to the next rank first, with what is missing) and **Hard Farms** (the Wiki list, with what you own). The top bar has the character selector (when several were captured) and **Settings**; the footer says what the capture is doing.

- **Game data** — open the app (it asks for administrator rights), then log in to the game. Your character appears by itself. Details are in [Game data capture](#game-data-capture-windows).
- **Hard Farms** — press **Update from the Wiki** in its card; ownership comes from the captured inventory.
- **Settings** — the button in the top bar opens a dialog with the debug mode switch, the privacy text, **Forget my data** (asks for confirmation, then removes what the app saved locally), the credits and the version.
- **Debug mode** — turn it on in Settings, press Ctrl+Shift+D, or use the tray icon menu. It opens a second window with the capture controls and numbers (segments, decoding, commands seen, other commands) and the diagnostics export.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the exact version is pinned in `global.json`).

```sh
dotnet build AqwCompanion.slnx -c Release   # build everything
dotnet test AqwCompanion.slnx -c Release    # unit tests, the architecture test and the headless UI smoke test
dotnet run --project src/AqwCompanion.App   # run the app
```

Releases are built by `.github/workflows/release.yml` when a `vX.Y.Z` tag is pushed: tests, a self-contained single-file `win-x64` publish, and a zip with its `SHA256SUMS.txt`. The version comes from the tag (MinVer).

## Game data capture (Windows)

The dashboard reads the login data of your own characters from the game's network traffic, on your own machine, while the game logs in. It shows the profile numbers, the faction standings (with rank and what is still missing for the next one) and the owned items, and **Hard Farms** uses the captured inventory to mark what you own. It is read-only: the app never sends anything to the game and never acts for you. After the login it also follows your own gold, experience, items and class points while you play (see below), with no refresh button.

> Reading the game's traffic is a different risk class from reading a website. It is probably not allowed by the game's terms and can get an account banned. Use a throwaway account, and do it knowingly.

What you need:

- Windows, and [Npcap](https://npcap.com/) installed by you from its own site. The app never installs or bundles Npcap; it only checks that it is there.
- Administrator rights, because the capture driver needs them.

Steps:

1. Open the app. Windows asks for administrator rights every time it starts (the app asks for them in its manifest, because the capture driver needs them), and you accept. The capture starts by itself when the app opens, so there is no need to press **Start capture**, and it keeps listening until you press **Stop capture**. After the login it keeps reading a short list of your own game events (see **Live updates** below). Starting it with `dotnet run` from a normal terminal does not work for that reason: open the built `AqwCompanion.exe`, or run `dotnet run` from an elevated terminal.
2. Open **debug mode** (see Basic usage) if the footer says the capture cannot run. If it says Npcap is missing, install it and press **Start capture** there. If it still says it needs administrator rights (for example when the app was started in a way that skipped the manifest), press **Restart as administrator** and accept the Windows prompt; the app restarts with the same data folder.
3. Open the game launcher and log in. The capture has to be listening before the login, because the data is sent once, during the login.
4. Wait until the dashboard shows your character. In debug mode, **Stop capture** stops the capture and **Start capture** starts it again. If you log in with several characters while it runs, each one appears and the **Character** selector in the top bar lets you switch between them.

The debug window shows the capture state, the privacy note and the **Capture details** section with the numbers. The numbers update by themselves on every change, and **Refresh numbers** inside that section re-reads them on demand: segments seen and dropped, messages read, characters captured, how many messages were unreadable, how many profile and inventory parts were seen, and, when there is something to say, one line about the capture driver (devices found and opened, packets received, with error type names but never a device name, address or packet content). Read them like this: 0 segments seen means nothing arrived from the driver; segments but no characters means the login was not read, usually because the capture started after the login; unreadable above 0 with no parts means the real message format differs from what was measured; messages read above 0 with 0 profile and inventory parts means the data did not pass during this capture, usually because the login happened before Start. A second line, "Unreadable: ...", appears when messages could not be read and says at which step each one failed (for example not base64, or no compression header), and a third, "Commands read: ...", counts the messages of each known game command, including how many initUserData messages were the own profile and how many were ignored for lacking gold or a name. Two more lines show how the connections went (how many were seen, how many with the start of their stream, how many gave a profile, an inventory or both) and, for the first unreadable messages, only their size, the reading that decoded them, the class of their first bytes (zlib header, gzip header, printable text, binary or empty) and an entropy figure, never any byte or text. All of these show numbers and fixed command names only; any other command is counted as "other". A last line, "Other commands seen: ...", lists up to 40 distinct names of commands the capture does not know yet, with a count each, but only names made of letters, digits and `_ + . -` (at most 40 characters); any other name is counted as "other" and never shown. It exists to find out how the game announces reputation gains, level ups and quest results.

What the app reads and keeps on this machine: from the server's login messages only, the character name and level, experience, gold, AC, class points, days played, membership days, bag, bank, house and loadout slots, active boosts, the faction reputation totals, and for each owned item its name, type, item id (and class id for classes), quantity, level and flags (AC, member, equipped, worn, quest, bank, house, temporary). Ranks are computed from the Wiki's reputation table. Data from the client to the server, where the login travels, is dropped before anything is looked at, and it only listens to TCP ports 5588 to 5594.

**Live updates.** Once a character's login has been read, the app keeps that connection and applies a few of the game's own plain events to the saved snapshot, so the Game data tab changes by itself, without a refresh button:

- gold and experience gained (`addGoldExp`), items picked up or changed (`dropItem`, `getDrop`, `addItems`), items sold (`sellItem`: quantity, and gold or AC) and class points (`updateClass`).
- On that connection the app never decodes the compressed messages, where other players' data lives, and it parses a plain message only when a scan of its first 256 bytes finds one of those six event names. Every other message only adds its name to the numbers in **Capture details**. When the connection closes, or a new login starts on it, the live state is dropped.
- The profile card then says how many live updates the character received. Changes are saved a few seconds after the last one, and at once when you stop the capture.
- Not tracked yet: buying, quest rewards, reputation gains, level-ups, trades and bank moves. The snapshot can drift from the game until you log in again, and the tab says so.
- **How to check gold and experience:** the app assumes that the game's gold and experience events carry the amounts GAINED (not new totals). Kill one monster and compare the **Last kill: +gold, +exp** line with what the game showed. If they do not match, the assumption is wrong and the numbers cannot be trusted. This is not verified against the real game yet.

What is never kept: your e-mail address, IP address, user and character ids, password, the account creation date, and anything about other players.

If you open the app while you are already logged in to the game: the data saved from your last session is shown, labelled as saved from an earlier session. The app cannot read a login that was sent before it started listening, and it says so when it sees a game connection whose start it missed. Switching server or logging in again sends the login again and fixes it. Live updates (below) only start after a login the app saw.

**Forget my data** (in **Settings**) removes the captured data too, together with everything else the app saved. Data that older versions saved from the account website is removed by the app by itself; the folder an older version used for an exported Skua reputation file is removed by **Forget my data**.

Tray icon and a single instance:

- Closing the window does not close the app. It hides to the notification area (on Windows, often under the hidden icons arrow), and the capture keeps listening in the background. The icon's tooltip says what the capture is doing (waiting for a login, capturing, or why it cannot).
- Open the window again by clicking the icon, or from its right-click menu: **Open**, **Start capture** or **Stop capture**, **Debug mode**, and **Exit**. **Exit** is the only way to really close the app: it stops the capture, waits up to 3 seconds for the capture devices to close and ends the process.
- Starting the app again while it is already running does not open a second capture: the running window comes to the front and the new process ends. When the app restarts itself as administrator it waits up to 10 seconds for the old process to finish closing.
- Not verified: the tray icon, its menu and the single instance have not been run on a real Windows machine; they were only tested in this repository without a real tray. The app does not start with Windows (there is no auto-start). Mac and Linux were not checked either; on those systems a second launch may not be able to wake the running window.

Limits and what is not verified:

- At most 16 characters are kept; the oldest is dropped first. At most 16 connections are followed live; when more start, the longest idle live one is dropped.
- Live updates were only tested with generated traffic: the real event names and fields come from reading another tool's source and from structure-only measurements, not from a real session.
- The bank, quests and achievements are not read as data of their own. Whether the game sends your bank items in the login data is not verified, so do not rely on them. Class points of owned classes are not captured, so Hard Farms matches classes by name only.
- **The capture adapter (the part that talks to Npcap) has not been run on a real Windows machine yet.** It is tested here only with generated traffic on fake network sources. Expect that the first real run can fail, for example with "No network device could be opened." Nothing in this section is a promise that it works on your machine.

## Not affiliated

AQW Companion is an unofficial fan project. It is not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds and its content belong to their owners.
