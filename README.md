<p align="center">
  <img src="docs/logo.svg" width="128" height="128" alt="Questkeeper logo">
</p>

<h1 align="center">Questkeeper</h1>

<p align="center">
  An unofficial Windows companion for AdventureQuest Worlds players. It reads the game's own network traffic on your
  machine while you play and shows your character, reputation and inventory in one place — read-only,
  and it never sends anything to the game.
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

## What it does

Questkeeper listens to the data the game already sends to your own computer and shows it in one screen while you play, so you do not have to click through the game and the Wiki.

- **One screen:** your character, your factions (closest to the next rank first) and the Hard Farms list, with what you own marked from your captured inventory.
- **Read-only:** it never sends anything to the game and never acts for you.
- **Local:** everything stays on your computer. No telemetry. **Forget my data** (in **Settings**) removes it. Nothing leaves the PC except the Wiki request you start and, if you turn them on, [Phone access](#watch-from-your-phone) and the [Skua plugin](#skua-plugin).

## Install

You need 64-bit Windows and [Npcap](https://npcap.com/), which you install yourself from its own site (its license does not allow other programs to bundle it; it is free for personal use on up to five computers).

1. Download `Questkeeper-Setup-vX.Y.Z.exe` from the [latest release](https://github.com/agvmdsa/QuestKeeper/releases/latest) and run it. It tells you whether Npcap is installed and opens the official download page if it is not.
2. Start Questkeeper from the Start menu. Windows asks for administrator rights each time, because the capture driver needs them.
3. The builds are not code-signed yet, so SmartScreen may warn you: choose **More info**, then **Run anyway**, if you trust the file. You can compare the download with `SHA256SUMS.txt` (`Get-FileHash .\Questkeeper-Setup-vX.Y.Z.exe -Algorithm SHA256` in PowerShell).

No installer? Download the `.zip` instead, extract it anywhere and run `Questkeeper.exe`. Your data, logs and crash reports live in `%LOCALAPPDATA%\AqwCompanion`; delete that folder to remove everything the app stored.

## Using it

Open the app, then open the game launcher and log in. The capture starts by itself and has to be listening **before** the login, because the game sends your data only once, during it. Your character appears on its own.

- **Character chip** (top bar): switch between characters when several are logged in.
- **Hard Farms:** press **Update from the Wiki** in its card; the list is imported on your click and never bundled (CC BY-SA 3.0).
- **Settings** (the gear): start with Windows, the privacy text, **Forget my data**, credits.
- **Tray icon:** closing the window hides it and the capture keeps listening. **Exit** in the tray menu really closes the app.
- **Nothing shows up?** Open the debug mode (Settings, or Ctrl+Shift+D) for the capture controls and the diagnostics export.

If you open the app while already logged in to the game, it shows the data saved from your last session and says so; switching server or logging in again sends the login again.

## What it reads, and what it does not

From the server's messages only: character name and level, experience, gold, AC, class points, days played, membership, slots, boosts, faction reputation, and each owned item. It never reads or keeps your e-mail, IP address, password, user or character ids, or anything about other players. It listens only to the game's TCP ports 5588 to 5594 and drops the client-to-server side, where the login travels, before looking at it.

After the login it follows a few of your own events (gold and experience gained, items picked up or sold, class points). **Not tracked yet:** buying, quest rewards, reputation gains, level-ups, trades and bank moves, so the numbers can drift from the game until you log in again. The bank, quests and achievements are not read.

## Watch from your phone

Off by default. **Settings, "Watch my characters from my phone"** serves a small page with your characters' numbers, factions and Hard Farms, updating live, on a phone you pair. It only watches. It works as an installed web app, with a light, dark or system theme.

**You need:** [Tailscale](https://tailscale.com/) on this PC and on your phone (same account), **MagicDNS** and **HTTPS certificates** turned on once in the Tailscale admin console (the app tells you which step is missing), and Questkeeper running on the PC. The phone reaches the page only through your own tailnet; nothing is opened to the internet.

**Steps:** turn on the switch, press **Pair a phone...**, scan the QR code with the phone camera, then add the page to the home screen (on an iPhone: Share, then Add to Home Screen). A pairing link works once and for 5 minutes. Each paired phone can be revoked in the same window.

- **Installed to the Home Screen first?** An iPhone Home Screen app keeps its own storage, so pairing in Safari does not pair it. Open the installed app, use the **Pairing link** field on its "Not paired" card and paste the link there.
- **PC off?** The page opens from its saved copy within a few seconds and says it cannot reach Questkeeper, with how old the last data is.
- Anyone on your tailnet can open the page, but only a paired phone sees any data.
- Push notifications (a notice while the page is closed) do not exist yet.

## Skua plugin

An optional, separate download for people who play with [Skua](https://github.com/auqw/Skua) (a third-party game client). `QuestKeeper.SkuaBridge.dll` runs inside Skua and shows its state in the **Skua** tab of the phone page: what the character is doing (server, map, script running), the **active quests** (the ones ready to turn in marked) and a short **log** with filters (buys, sells, drops, map changes, script start and stop, deaths). It is read-only: it cannot start, stop or change anything in Skua or the game.

1. Download `QuestKeeper.SkuaBridge.dll` from the [latest release](https://github.com/agvmdsa/QuestKeeper/releases/latest) (check it against `SHA256SUMS.txt`).
2. Copy it into `%AppData%\Skua\plugins` (the **Skua** block in **Settings** opens that folder) and reopen Skua. With Questkeeper running, that block reads "Skua connected".
3. To remove it, close Skua and delete the file.

Tested with Skua 1.4.5.0. One Skua at a time; log lines appear while a script is running; Skua started as administrator is not supported. Skua publishes no license: the plugin only references its interfaces and copies none of its code.

## For developers

- [Local feed](docs/local-feed.md): a read-only feed for other programs on the same PC.
- [Build from source and releases](docs/building.md).

## Not affiliated

Questkeeper is an unofficial fan project. It is not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds and its content belong to their owners.
