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

## Reputation proof of concept (branch `poc/reputation-discovery`)

The account website does not show faction reputation, so this branch tries a different source: a JSON file you export yourself from the game. The **Reputation** tab only imports and shows that file. It makes no network calls and never touches the game, a session or credentials. The app builds and runs on macOS and Windows; only collecting the data needs Windows.

> Collecting the data means running the game inside a third-party client ([Skua](https://github.com/auqw/Skua)), which is a different risk class from the rest of this app: it is probably not allowed by the game's terms and can get an account banned. Use a throwaway account, and do it knowingly. Skua has no license, so none of its code is in this repository and it is not distributed with the app.

On the Windows machine:

1. Clone and run this branch: `git clone -b poc/reputation-discovery https://github.com/agvmdsa/aqw-companion.git`, then `dotnet run --project src/AqwCompanion.App` (needs the .NET 10 SDK).
2. Install Skua yourself from its official releases and log in with the throwaway account. Skua needs a Flash runtime (its readme explains CleanFlash).
3. Save this as `ReputationExport.cs` in Skua's scripts folder (`%APPDATA%\Skua\Scripts`), then in Skua press **Load script** and **Start**, and leave it running while you play. Every 5 seconds it checks your factions and rewrites `%LOCALAPPDATA%\AqwCompanion\reputation\reputation.json` only when something changed (never with an empty list, and never half-written):

   ```csharp
   using System;
   using System.Collections.Generic;
   using System.IO;
   using Newtonsoft.Json;
   using Skua.Core.Interfaces;

   public class Script
   {
       public void ScriptMain(IScriptInterface Bot)
       {
           var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AqwCompanion", "reputation");
           Directory.CreateDirectory(dir);
           var file = Path.Combine(dir, "reputation.json");
           var tmp = file + ".tmp";
           var last = "";
           Bot.Log("Reputation export running, writing " + file);
           while (!Bot.ShouldExit)
           {
               try
               {
                   var rows = new List<object>();
                   foreach (var f in Bot.Reputation.FactionList)
                       rows.Add(new { factionId = f.ID, name = f.Name, rank = f.Rank, rep = f.TotalRep, repToRank = f.RequiredRep, spillRep = f.Rep });
                   var key = JsonConvert.SerializeObject(rows);
                   if (rows.Count > 0 && key != last)
                   {
                       var json = JsonConvert.SerializeObject(new { schema = 1, exportedAt = DateTime.UtcNow.ToString("o"), factions = rows }, Formatting.Indented);
                       File.WriteAllText(tmp, json);
                       File.Move(tmp, file, true);
                       last = key;
                   }
               }
               catch (Exception e)
               {
                   Bot.Log("Reputation export: " + e.Message);
               }
               Bot.Sleep(5000);
           }
       }
   }
   ```

4. In AQW Companion open the **Reputation** tab. It reads the file by itself when the tab opens, and **Refresh** reads it again. Nothing to pick. The older **Import reputation file…** button still works for a file saved somewhere else.

What to check: does the list match the in-game reputation (rank, total rep, rep still missing for the next rank)? Skua only lists factions with at least 1 reputation point, and the tab only shows what the script last wrote, so the script must be running (the tab says when there is no file yet and shows the expected path).

The script was written from reading Skua's source and has not been run: the field names come from `Faction.cs` (`iRep` is the total, `iSpillRep` the progress in the current rank, `iRepToRank` the total needed for the rank), and `Bot.ShouldExit`, `Bot.Sleep` and `Bot.Log` from its script interface. If Skua reports a compile error when loading it, send the message back and the script gets fixed. The file format is `schema: 1`; files over 1 MiB, with more than 500 factions or with an unknown schema are rejected, and signing out removes the imported data and the file, with everything else.

## Game data capture (Windows, branch `poc/reputation-discovery`)

The **Game data** tab reads the login data of your own characters from the game's network traffic, on your own machine, while the game logs in. It shows the profile numbers, the faction standings (with rank and what is still missing for the next one) and the owned items, and **Hard Farms** uses the captured inventory first to mark what you own. It is read-only: the app never sends anything to the game and never acts for you.

> Reading the game's traffic is a different risk class from reading the website, which is what the rest of this app does. It is probably not allowed by the game's terms and can get an account banned. Use a throwaway account, and do it knowingly.

What you need:

- Windows, and [Npcap](https://npcap.com/) installed by you from its own site. The app never installs or bundles Npcap; it only checks that it is there.
- Administrator rights, because the capture driver needs them.

Steps:

1. Open the app. Windows asks for administrator rights every time it starts (the app asks for them in its manifest, because the capture driver needs them), and you accept. The capture starts by itself when the app opens, so there is no need to press **Start capture**, and it keeps listening until you press **Stop capture**. It still reads only the login data; live game events are a later step. Starting it with `dotnet run` from a normal terminal does not work for that reason: open the built `AqwCompanion.exe`, or run `dotnet run` from an elevated terminal.
2. Open the **Game data** tab. If it says Npcap is missing, install it and press **Start capture**. If it still says it needs administrator rights (for example when the app was started in a way that skipped the manifest), press **Restart as administrator** and accept the Windows prompt; the app restarts with the same data folder.
3. Open the game launcher and log in. The capture has to be listening before the login, because the data is sent once, during the login.
4. Wait until the tab shows your character. Press **Stop capture** when you are done; **Start capture** starts it again. If you log in with several characters while it runs, each one appears and a **Character** selector lets you switch between them.

The capture card shows numbers only, and **Refresh numbers** re-reads them on demand (nothing updates by itself): segments seen and dropped, messages read, characters captured, how many messages were unreadable, how many profile and inventory parts were seen, and, when there is something to say, one line about the capture driver (devices found and opened, packets received, with error type names but never a device name, address or packet content). Read them like this: 0 segments seen means nothing arrived from the driver; segments but no characters means the login was not read, usually because the capture started after the login; unreadable above 0 with no parts means the real message format differs from what was measured; messages read above 0 with 0 profile and inventory parts means the data did not pass during this capture, usually because the login happened before Start. A second line, "Unreadable: ...", appears when messages could not be read and says at which step each one failed (for example not base64, or no compression header), and a third, "Commands read: ...", counts the messages of each known game command, including how many initUserData messages were the own profile and how many were ignored for lacking gold or a name. Two more lines show how the connections went (how many were seen, how many with the start of their stream, how many gave a profile, an inventory or both) and, for the first unreadable messages, only their size, the reading that decoded them, the class of their first bytes (zlib header, gzip header, printable text, binary or empty) and an entropy figure, never any byte or text. All of these show numbers and fixed command names only; any other command is counted as "other". A last line, "Other commands seen: ...", lists up to 40 distinct names of commands the capture does not know yet, with a count each, but only names made of letters, digits and `_ + . -` (at most 40 characters); any other name is counted as "other" and never shown. It exists to find out how the game announces reputation gains, level ups and quest results.

What the app reads and keeps on this machine: from the server's login messages only, the character name and level, experience, gold, AC, class points, days played, membership days, bag, bank, house and loadout slots, active boosts, the faction reputation totals, and for each owned item its name, type, item id (and class id for classes), quantity, level and flags (AC, member, equipped, worn, quest, bank, house, temporary). Ranks are computed from the Wiki's reputation table. Data from the client to the server, where the login travels, is dropped before anything is looked at, and it only listens to TCP ports 5588 to 5594.

What is never kept: your e-mail address, IP address, user and character ids, password, the account creation date, and anything about other players.

If you open the app while you are already logged in to the game: the data saved from your last session is shown, labelled as saved from an earlier session. The app cannot read a login that was sent before it started listening, and it says so when it sees a game connection whose start it missed. Switching server or logging in again sends the login again and fixes it. Live updates while you play are a later step and do not exist yet.

**Sign out** on the Account tab removes the captured data too, together with everything else the app saved.

Limits and what is not verified:

- At most 16 characters are kept; the oldest is dropped first.
- The bank, quests and achievements are not read as data of their own. Whether the game sends your bank items in the login data is not verified, so do not rely on them. Class points of owned classes are not captured, so Hard Farms matches classes by name only.
- The Reputation tab and its Skua flow above are unchanged and separate from this.
- **The capture adapter (the part that talks to Npcap) has not been run on a real Windows machine yet.** It is tested here only with generated traffic on fake network sources. Expect that the first real run can fail, for example with "No network device could be opened." Nothing in this section is a promise that it works on your machine.

## Not affiliated

AQW Companion is an unofficial fan project. It is not made by, endorsed by or connected to Artix Entertainment. AdventureQuest Worlds and its content belong to their owners.
