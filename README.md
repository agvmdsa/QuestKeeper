<p align="center">
  <img src="docs/logo.svg" width="128" height="128" alt="Questkeeper logo">
</p>

<h1 align="center">Questkeeper</h1>

<p align="center">
  An unofficial Windows companion for AdventureQuest Worlds players. It reads the game's own network traffic on your
  machine while you play and shows your character, reputation and inventory in one place — read-only,
  nothing sent anywhere.
</p>

<p align="center">
  <a href="https://github.com/agvmdsa/QuestKeeper/releases/latest"><img src="docs/download-windows.svg" width="368" alt="Download the latest Questkeeper for Windows"></a>
</p>

<p align="center">
  <sub>Self-contained single file · SHA-256 checksum next to every release · <a href="#install">how to install</a></sub>
</p>

<p align="center">
  <a href="https://github.com/agvmdsa/QuestKeeper/actions/workflows/ci.yml"><img src="https://github.com/agvmdsa/QuestKeeper/actions/workflows/ci.yml/badge.svg?branch=main" alt="CI status of the main branch"></a>
</p>

## Why

Seeing what you own, your faction ranks and what is missing for the next rank means clicking through the game and the Wiki. Questkeeper listens to the data the game already sends to your own computer and shows it in one screen while you play.

- **Read-only.** It never sends anything to the game and never acts for you.
- **One screen.** Your character, your factions (closest to the next rank first) and the Hard Farms list, with what you own marked from your captured inventory.
- **Local.** Everything is saved on your computer. **Forget my data** (in **Settings**) removes it. No telemetry.

## Install

You need 64-bit Windows and [Npcap](https://npcap.com/), which you install yourself from its own site: its license does not allow other programs to bundle it. Npcap is free for personal use on up to five computers.

1. Download `Questkeeper-Setup-vX.Y.Z.exe` from the [latest release](https://github.com/agvmdsa/QuestKeeper/releases/latest) and run it. It has a page that tells you whether Npcap is installed and opens the official download page if it is not.
2. Start Questkeeper from the Start menu. Windows asks for administrator rights each time, because the capture driver needs them.
3. The builds are not code-signed yet, so SmartScreen may warn you: choose **More info**, then **Run anyway**, if you trust the file.
4. Optional: compare the download with `SHA256SUMS.txt` (`Get-FileHash .\Questkeeper-Setup-vX.Y.Z.exe -Algorithm SHA256` in PowerShell).

No installer? Download `Questkeeper-vX.Y.Z-win-x64.zip` instead, extract it anywhere and run `Questkeeper.exe`. Nothing else is needed but Npcap.

Your data, logs and crash reports live in `%LOCALAPPDATA%\AqwCompanion`; delete that folder to remove everything the app stored. Uninstalling asks whether to remove it too.

## Using it

Open the app, then open the game launcher and log in. The capture starts by itself and has to be listening before the login, because the game sends your data only once, during it. Your character appears on its own.

- **Character chip** (top bar): switch between characters when several are logged in, for example from another launcher or client.
- **Hard Farms:** press **Update from the Wiki** in its card; the list is imported on your click and never bundled (CC BY-SA 3.0).
- **Settings** (the gear): **Start with Windows** (opens the app in the notification area when you sign in), debug mode, the privacy text, **Forget my data**, credits.
- **Debug mode** (Settings, Ctrl+Shift+D or the tray menu): a second window with the capture controls, the capture numbers and the diagnostics export. Start there if nothing appears.
- **Tray icon:** closing the window hides it and the capture keeps listening. **Exit** in the tray menu really closes the app.

If you open the app while already logged in to the game, it shows the data saved from your last session and says so; switching server or logging in again sends the login again.

## What it reads

From the server's messages only: the character name and level, experience, gold, AC, class points, days played, membership days, slots, boosts, faction reputation, and each owned item (name, type, quantity, level, flags). It never reads or keeps your e-mail, IP address, password, user or character ids, or anything about other players. It listens only to the game's TCP ports 5588 to 5594, and drops the client-to-server side, where the login travels, before looking at it.

After the login it follows a few of your own plain events: gold and experience gained, items picked up or sold, and class points. The last kill is shown on the character strip.

- Not tracked yet: buying, quest rewards, reputation gains, level-ups, trades and bank moves. The numbers can drift from the game until you log in again.
- The app assumes the gold and experience events carry the amount gained. Kill a monster and compare **Last kill** with what the game showed; if they differ, do not trust the numbers.
- The bank, quests and achievements are not read. Class points of owned classes are not captured, so Hard Farms matches classes by name.
- Tested on a real Windows machine by the author only. The tray icon, Start with Windows and other setups may behave differently.

## Local feed (for other programs)

Off by default. **Settings, "Let other programs on this PC read my characters"** turns on a read-only feed that other programs on the same PC can use, for example a remote viewer. It listens on `127.0.0.1:47821` only, never on the network, and it never sends anything to the game.

- **Secret:** a program must send `Authorization: Bearer <secret>`. The secret is the text in `%LOCALAPPDATA%\AqwCompanion\feed.token`; **New secret** in Settings replaces it. A request with an `Origin` header (a web page) or another `Host` is refused.
- **`GET /v1/ping`** returns `{"contract":1,"app":"Questkeeper","version":"..."}`.
- **`GET /v1/state`** returns the snapshot below.
- **`GET /v1/events`** is a Server-Sent Events stream: a `state` event with the full snapshot at once and whenever it changed (at most once a second), and a `ping` event every 25 seconds.

```json
{
  "contract": 1,
  "capture": { "state": "Waiting for a login. Open the game launcher now.", "listening": true },
  "selected": "Alpha Hero",
  "characters": [
    {
      "name": "Alpha Hero", "level": 60, "live": true,
      "savedAtUtc": "2026-11-09T12:00:00+00:00", "lastLiveUpdateUtc": "2026-11-09T12:05:00+00:00", "liveUpdates": 3,
      "figures": { "gold": 1200, "exp": 340, "expToLevel": 5000, "ac": 777, "classPoints": 99, "daysPlayed": 300 },
      "lastKill": { "gold": 56, "exp": 0 }
    }
  ],
  "factions": [ { "name": "Nation", "rank": 3, "rep": 5000, "toNext": 5000, "fraction": 0.21875, "max": false } ],
  "hardFarms": { "owned": 4, "total": 8, "missing": ["Cape of Chaos", "Nation Hoodie"] }
}
```

Every character logged in is its own entry, identified by its name. `factions` and `hardFarms` describe the `selected` character (`hardFarms` is `null` while the inventory is unknown, and `missing` is cut to 200 names). The session keeps a single last kill, so `lastKill` is on the character updated last and `null` on the others. Fields are only ever added within contract 1; a breaking change raises the number. There are no ids, e-mail, IP addresses or other players in it.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) (the exact version is in `global.json`).

```sh
dotnet build AqwCompanion.slnx -c Release   # build everything
dotnet test AqwCompanion.slnx -c Release    # unit tests, the architecture tests and the headless UI tests
dotnet run --project src/AqwCompanion.App   # run the app (the capture needs Windows, Npcap and an elevated terminal)
```

A `vX.Y.Z` tag runs `.github/workflows/release.yml`: tests, a self-contained single-file `win-x64` publish, and a zip with its `SHA256SUMS.txt`. The version comes from the tag (MinVer).

## Not affiliated

Questkeeper is an unofficial fan project. It is not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds and its content belong to their owners.
