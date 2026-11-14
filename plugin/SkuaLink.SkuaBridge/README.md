# SkuaLink.SkuaBridge

A Skua plugin. It runs inside Skua 1.4.5.0, reports Skua's state and a short event log to SkuaLink over a local named
pipe (`SkuaLink.SkuaBridge.v1`, current Windows user only, two-way since AQW-0088), and accepts a fixed set of commands
from SkuaLink. Status: **connect and status verified by the owner on Windows on 2026-11-11 with an earlier build; the
quest reading, the re-attach across script runs, the commands and the rest are not yet verified** (only the owner's
Windows can prove it).

## What the plugin can do

Exactly this, nothing else:

1. **Report** (as before): `hello`, `status` and `log` lines, see "What it sends".
2. **List scripts** (`listScripts`): the `.cs` files under Skua's scripts folder (`ClientFileSources.SkuaScriptsDIR`,
   `%AppData%\Skua\Scripts`), except Skua's compile cache `Cached-Scripts`, hidden/system files and links, sorted by
   folder then name, at most `SkuaProtocol.MaxScripts` (the answer says `truncated` when cut). Each script travels as
   `{id, name, folder}`: `id` is the first 8 bytes of the SHA-256 of the path relative to the scripts folder, lowercase
   hex; no full path leaves the plugin.
3. **Start a listed script** (`start {id}`): only an id from the plugin's own listing (an id it has not listed yet gets one
   fresh scan). The path is checked again (inside the scripts folder plus a separator, ends in `.cs`, not under
   `Cached-Scripts`, neither the file nor a folder on the way a link) and must still exist. Refused while a script runs
   (`already running`) or while `stopping` / `settling`; only then `SetLoadedScript(path)` and `await StartScript()`. The
   error Skua returns (compile error, version error) becomes the answer's `error`, absolute paths reduced to file names,
   cut to 300 chars.
4. **Stop the running script** (`stop`): answers at once, then calls Skua's `StopScript()` on a pool thread (never on the
   script thread), once at a time. The status shows `stopping`, then `settling`, then `idle`.
5. **Read a script's options** (`getOptions {id}`): the definitions the script declares and the values saved in its
   options file (see "Script options").
6. **Write a script's options** (`setOptions {id, values}`): only names the script declares, only values that fit their
   type, only into that script's options file inside Skua's options folder, only while no script runs.
7. **Close a started script's options dialog** on the PC (part of `start`), so Skua saves the values already in the file.

It never accepts a path, code or a script name from SkuaLink, and option values only for options the script itself
declared; it never sends anything to the game.

## Corrections to the plan (verified against the 1.4.5.0 DLLs and the 1.4.5.0 tag source)

Every member below was compiled against `lib/skua/Skua.Core.Interfaces.dll` and `Skua.Core.Models.dll` (1.4.5.0) in a
throwaway project and read in the tag source.

1. **`bot.Manager` is not `INotifyPropertyChanged`.** `IScriptInterface.Manager` is typed `IScriptStatus`, which has no
   change notification (assigning it to `INotifyPropertyChanged` does not compile). `IScriptManager : IScriptStatus,
   INotifyPropertyChanged` is the notifying type. The plugin asks the provider for `IScriptManager` (Skua registers
   `IScriptManager` and `IScriptInterface` as singletons in `Skua.Core/AppStartup/Services.cs`, and `ScriptInterface`
   exposes that same instance as `Manager`), with `bot.Manager as INotifyPropertyChanged` as a fallback.
   `ScriptManager` raises `PropertyChanged` for `ScriptRunning` and `LoadedScript` through `[ObservableProperty]`, and
   raises `ScriptRunning` a second time at the end of `StopScript`, so the plugin only acts on real transitions.
2. **`ItemBase` has no `CharItemID`.** It is on `InventoryItem : ItemBase`. The inventory is enumerated with
   `IScriptInventory.Items` (`List<InventoryItem>`, inherited from `IItemContainer<InventoryItem>` through
   `ICanEquip -> ICheckEquipped -> ICanBank -> ICheckInventory<InventoryItem>`). `ID`, `Name`, `Quantity` are on `ItemBase`.
   The snapshot is keyed by `CharItemID`, because `ItemBought` and `ItemSold` carry the CharItemID.
3. **Skua drops plugin handlers on every script stop.** `ScriptEvent` handles `ScriptStoppedMessage` with
   `ClearHandlers()`, which sets `PlayerDeath`, `QuestAccepted`, `QuestTurnedIn`, `MapChanged`, `ReloginTriggered`,
   `ItemDropped` and `ScriptStopping` (and others) to null. The plugin therefore re-attaches its handlers on each
   `ScriptRunning` notification (remove, then add, under a lock, so there is exactly one copy; done on every
   notification, not only on detected transitions). The stop message is sent before `ScriptRunning = false` on the same
   thread, so the re-attach runs after the clear. A script that itself calls `Bot.Events.ClearHandlers()` silences the
   plugin's log lines until the next script start or stop.
4. **Game events only arrive while a script runs.** `ScriptEvent` registers for the game messages on
   `ScriptStartedMessage` and unregisters on `ScriptStoppedMessage`. A login, logout or map change by hand raises no
   event, so the status is re-read every 3 s on the plugin's own background task and sent only when it changed. The log
   events (bought, sold, dropped, map, died, logged out, relogin) are therefore only reported during scripts.
5. **`ScriptStopping` return value.** The handler returns `bool`. Skua invokes the multicast delegate inside
   `Task.Run` and the last handler's value goes back to `ScriptManager`, which uses it only to choose a `Trace.WriteLine`
   ("Script finished successfully." / "Script finished early or with errors."); nothing else reads it. The plugin
   returns `true` (the delegate's documented "the script should be stopped", which is what happens anyway). Because the
   plugin re-attaches when `ScriptRunning` turns true, before `ScriptMain` runs, a script's own handler is added after
   the plugin's and its value is the one Skua sees.
6. **Script started/stopped come from `ScriptRunning` changes**, not from Skua's `ScriptStartedMessage` /
   `ScriptStoppedMessage` as the spec suggested, so `Skua.Core.dll` is not referenced.
7. **Active quests (AQW-0076):** `IScriptInterface.Quests` -> `IScriptQuest.Active` (`List<Quest>`, never null in
   1.4.5.0: it is `Tree.FindAll(x => x.Active)`, `Quest.Active` is `Status != null`, and the `world.questTree` binding
   answers an empty dictionary when the read fails), with `Quest.ID`, `Quest.Name` and `Quest.Status`. "Ready" is
   `Quest.Status == "c"` on the quests that one read returned: Skua's own `CanComplete(id)` is
   `Completed.Contains(q => q.ID == id)` with `Completed = Tree.FindAll(x => x.Status == "c")`, so this is the same
   check without a second tree read (Skua's remark on it: "can not always be right"). `CanComplete` is not called. The
   `QuestAccepted` and `QuestTurnedIn` subscriptions and the `IScriptQuest.Tree` name lookup were removed: the active
   list in the status replaces those two log lines.
8. **Faction reputation (AQW-0081):** `IScriptInterface.Reputation` -> `IScriptFaction.FactionList` (`List<Faction>`).
   In 1.4.5.0 it is generated from `[ObjectBinding("world.myAvatar.factions", Default = "new()", HasSetter = false)]`:
   one `Flash.GetGameObject<List<Faction>>("world.myAvatar.factions")` call (`getGameObject`, one Flash call), a fresh
   deserialized list per read, `new()` when the answer is null or anything throws (so it never throws and never answers
   null; logged out it answers an empty list). It touches no Skua cache. `Faction.ID` (FactionID), `Name` (sName),
   `Rank` (iRank), `TotalRep` (iRep), `Rep` (iSpillRep, earned inside the current rank) and `RequiredRep` (iRepToRank,
   the size of the current rank step, 0 at rank 10) are read; `RemainingRep` is not.
9. `IScriptInterface.Version` exists (Skua's version from its settings) but is not used: `hello` reports
   `Assembly.GetEntryAssembly()?.GetName().Version` as decided.

## What it sends

- `hello` on every (re)connect: plugin version (informational version without build metadata), Skua's version, and
  `instanceId`, a random GUID made once per plugin load (SkuaLink tells several Skua apart by it).
- `status` on every (re)connect and whenever it changes: logged in; character, server, map (only while logged in);
  script file name without folder or extension (no local path leaves the plugin); running; running since; the active
  quests (`quests`: id, name, ready to turn in; at most 30, in Skua's order, a blank name sent as `#id`; empty while
  logged out; re-read at most every 15 s, see below); the faction reputation (`factions`: id, name, rank, total rep,
  rep inside the rank, size of the rank step; at most 60, in Skua's order, a blank name sent as `#id`; empty while
  logged out; read on every status, so about every 3 s). A change in either list, a ready flag or a reputation number
  sends a new status; nothing changed sends nothing. Also `control` (`idle`, `running`, `stopping`, `settling`,
  `awaitingOptions`) with `settleSecondsLeft` while settling and `optionsStuck` (see "Commands and control state").
- `result` once per command (see below).
- `log`: bought (quantity = now minus snapshot when positive, else none), sold (name from the snapshot taken before the
  sale), dropped (`Drop: X` for an item offered in the drop list, Skua's `dropItem`, the usual farming case; `Received: X`
  for an item put straight into the inventory, Skua's `addItems`, such as a reward; accepting a drop-list item raises no
  further event, so each drop is one line; a drop whose name is known also carries `item` and `count` 1, and SkuaLink
  merges repeated drops of one item into one line, `Drop: X x13`, while each comes within 60 s of the last), map changed, died, logged out, relogin, script started, script stopped,
  script error (the exception's message with every absolute path reduced to its file name, so no local folder and its
  Windows user name leaves the plugin). Quest accepted / turned in are no longer sent (the active quests travel in the status instead); the two
  kinds stay in the protocol so logs kept by an older version still load.
- Never: gold, inventory contents, monster kills (`MonsterKilled` is not subscribed).

## Commands and control state (AQW-0088)

- The pipe is opened `InOut`. The plugin reads only through the shared bounded reader (`SkuaCommandReader` over
  `BoundedLineReader` in `src/AqwCompanion.Skua`): a line over 4 KB (`SkuaProtocol.MaxCommandLineBytes`) is dropped, as
  is an unknown `type`, a line of another version, anything that is not a command, and anything malformed; unknown fields
  are ignored. The reader never throws; when SkuaLink closes the pipe it ends and the connection is made again like
  after a failed write. An older SkuaLink serves an inbound-only pipe, which Windows refuses to a client that asks to read
  (access denied): the plugin then connects outbound only, as before, and reads nothing.
- Each command goes to one command worker (queue of 16; one more is answered `busy` at once) and is answered exactly once
  with a `result {reqId, ok, error?, scripts?, optionDefs?, options?, hasOptions?, firstRun?, truncated?}`. Every failure is `ok: false` with a short error:
  `unknown script id`, `not found`, `already running`, `stopping`, `settling`, `not running`, `could not list scripts`,
  `busy`, `options not known yet`, `options file not allowed`, `could not read options`, `could not write options`, a
  validation text (`unknown option X`, `not a valid int for X`, ...), or Skua's own start error. Nothing reaches Skua as an exception.
- The decisions live in `src/AqwCompanion.Skua/SkuaCommandHandler.cs` (unit-tested on the Mac with a fake Skua); the
  Skua-bound part is `ScriptControl.cs`. Commands run one at a time, so a start's check and its acceptance cannot
  interleave with another start or stop.
- `SetLoadedScript` is never called while a script runs (Skua restarts the loaded path by itself on restart and relogin).
- After a successful start, `hasOptions` = the script declares at least one option (`Config.Options` /
  `Config.MultipleOptions`, read after the start); the state becomes `awaitingOptions` with options, `running` without
  (see "Script options" for how `awaitingOptions` ends). A start Skua has not reported running after 10 s (its running
  report may come and go before the start is accepted) settles, so the state never sticks.
- A stop accepted before Skua reported the start (state `running` or `awaitingOptions`, Skua not yet running) moves to
  `stopping` (a poll may settle it, since nothing runs yet); the stop is remembered until that start's 10 s wait ends, and
  when Skua reports the script running (its running change, or the 3 s poll) the plugin calls `StopScript()` once and the
  state goes back to `stopping`, so a script never runs on after an accepted stop. The same holds for a discovery start's
  own stop. A script started by hand in Skua after that wait is not stopped.
- `ScriptRunning` is read inside the control state's lock both by the running event and by the poll, so an older read
  can never overwrite a newer one.
- Settling (`SkuaControlState`): when the script stops running the state is `settling` until the map has not changed for
  10 s, then `idle`. Skua raises `MapChanged` only while a script runs, so during the settle the map is compared on the
  3 s status poll (each change restarts the 10 s). `ScriptRunning` changes and the poll both feed the state; the poll
  (`SkuaCommandHandler.Tick`) also runs while SkuaLink is away (it reads only `ScriptRunning`, no Flash).

## Script options (AQW-0089)

From the Skua 1.4.5.0 source: a script declares its options in fields (`Options`, `MultiOptions`, `OptionsStorage`,
`DontPreconfigure`). On every start `ScriptManager.LoadScriptConfig` puts a new `Config` (`IScriptOptionContainer`) in
place with those options, loads the values from the options file `Config.OptionsFile` =
`%AppData%\Skua\options\{OptionsStorage}.cfg` (`ClientFileSources.SkuaOptionsDIR`; `default.cfg` without
`OptionsStorage`), and, for a script with `Options` and no `DontPreconfigure = true`, calls `Config.Configure()` on a pool
task once the script thread runs: it loads the file again and shows the modal options dialog (`HostDialog`, `DataContext`
= `OptionContainerViewModel`) with `ShowDialog` on Skua's UI thread. When that dialog closes in any way, Skua saves the
values it shows. The script thread already runs meanwhile, with the values loaded from the file. File format: one line
per option, `Options:Name=Value` for `Options` and `Category:Name=Value` for each `MultiOptions` list (the category is
the field name); enum values with spaces for underscores (Skua turns them back when reading).

How the plugin uses that (decisions in `src/AqwCompanion.Skua/SkuaCommandHandler.cs` and `SkuaScriptOptions.cs`,
unit-tested; the Skua-bound part is `ScriptOptions.cs`):

- **Definitions are learnt at a start.** Right after the plugin's own `StartScript()` returns, `Config` describes that
  script (only kept when `LoadedScript` is still the path just started). The plugin keeps, per script id and for this
  plugin load, the definitions (category, name, display name, description, type mapped to
  `bool|int|double|string|enum|other`, default, enum members) and `Config.OptionsFile`. They are not kept across a Skua
  restart or a plugin reload (SkuaLink keeps its own copy of the definitions).
- **Options file without learnt definitions (fallback):** the plugin finds the script's options file from its source:
  a plain `public string OptionsStorage = "Name";` line (the public field Skua reads; uncommented, a literal, one value,
  a safe file name) gives `%AppData%\Skua\options\Name.cfg`. The source is only read (at most 4 MB), never run. A script
  whose storage name cannot be read this way has no fallback (Skua's `default.cfg` is shared by every script without
  `OptionsStorage`, so it is never assumed).
- **`getOptions`:** learnt: `optionDefs` (empty for a script without options) and the saved values from the options file
  (only options the script declares, in its order; no file is no values). Not learnt but the options file exists: `ok`
  with no `optionDefs` and the file's values as they are written (no type is known, so enum underscores and the PC's
  decimal comma are not converted). Neither: `ok` with no `optionDefs` and no values, which means "options not known
  yet": the next start is a discovery and the phone says the first start uses the default options.
- **`setOptions`:** refused while a script runs, starts or stops (Skua reads the file at the start, and an open dialog
  would overwrite it when it closes); refused before the definitions are learnt (`options not known yet`); every value is
  checked with `SkuaOptionsFile.Validate` (known name in its category, at most 200 chars, no line break, no trailing
  space, fits the type: `True`/`False`, invariant int, invariant double, an enum member). The file is written with the
  saved values and these applied, only lines for declared options, in Skua's format (`SkuaOptionsFile.Format`).
  Without learnt definitions but with the options file (fallback above): only names already in the file are accepted,
  with the rules that need no type (at most 200 chars, no line break, no trailing space); SkuaLink checks the types from
  its cached definitions. The file keeps its names and order. With neither: `options not known yet`. Only a
  `.cfg` file inside Skua's options folder is ever read or written (never through a link); anything else is refused.
- **First start of a script with options (discovery)**, only for a script with no learnt definitions AND no options file
  (a file from an earlier session, or one that exists but cannot be read, means a normal start): the plugin waits for the options dialog (up to 5 s), closes it,
  then stops the script at once and answers `ok, firstRun, optionDefs` (plus the saved values). The phone shows the form;
  the owner fills it in, `setOptions`, then starts again. A script without options just runs. If no dialog appears within
  5 s (the script preconfigures itself) the script is stopped the same way. If the dialog is still open after 8 s
  (`FirstRunOptionsStuckAfter`, so the answer comes well within SkuaLink's 15 s), the script is NOT stopped: the answer
  is `ok, hasOptions` with the definitions, the state stays `awaitingOptions` with `optionsStuck`, and the phone shows the
  loud "Waiting for you on the PC" state.
- **Later starts:** the answer comes at once (`ok, hasOptions, optionDefs`), state `awaitingOptions`; in the background
  the plugin looks for the dialog every 250 ms and closes it. `awaitingOptions` ends: dialog gone after it was seen ->
  `running`; no dialog found within 5 s (Skua shows none for a script that preconfigures itself) -> `running`; dialog
  still open (or the windows could not be looked at) 10 s after the start -> `optionsStuck: true`, no more closing, and
  `running` once the owner closes it on the PC. A stop, or a start never seen running, ends it too. Windows that cannot be
  looked at are never taken for "no dialog".
- **Window glue:** framework WPF only (`UseWPF` in the project: `PresentationFramework`, no Skua.Core or Skua.WPF). On
  Skua's UI thread through Skua's `IDispatcherService.Invoke` (resolved from the service provider): scan
  `Application.Current.Windows` for a `Window` whose `DataContext` type is named `OptionContainerViewModel` and whose
  `Container` property (read by reflection) is the started script's `Config` (a plugin's options window, which uses the
  same view model with another container, is left alone); close it with `Window.Close()`. Each window call is waited for
  at most 2 s and only one is in flight at a time (a blocked UI thread counts as "cannot look"). The code that runs on
  Skua's UI thread catches everything itself (`Close()` runs Skua's own close handlers), so nothing reaches Skua's
  dispatcher; a failed look is "cannot look", a failed close is seen on the next look, then `optionsStuck`.
- Definitions are learnt only when `LoadedScript` is the script just started and, when its source declares a storage
  name, `Config.OptionsFile` is that name's file (otherwise `Config` may be another script's). `setOptions` for a learnt
  script without options is refused (`options not known yet`) rather than writing an empty file, which could be the
  `default.cfg` other scripts share.

## Threading and safety

- Every handler goes through `Safe(Action)`; `Load`, `Unload` and every public `PipeClient` member catch everything.
  A failed `Load` removes whatever it attached. Skua never sees an exception from the plugin.
- Handlers run on Skua's threads (the UI thread for game packets, the script thread, a `Task.Run` for
  `ScriptStopping`) and only build a message and queue it. Every read that goes through Flash (inventory, quests,
  logged in, map, user name, active quests, factions) runs on one background task, never on Skua's threads.
- Queues are bounded (256, oldest dropped): the pipe client's line queue and the worker's queue. A status is never
  queued itself: one marker means "write the latest status here", and at most one marker is pending, so status changes
  during a long disconnect cannot push log lines out or queue repeated writes.
- Every string sent loses its lone surrogates (an unpaired UTF-16 half that Skua or the game may hand over), because
  the JSON writer refuses them and the whole message would be lost.
- While SkuaLink is not connected the plugin does no Flash reads at all: no status poll, no inventory snapshot, no
  quest or faction read (log lines still queue, unnamed where a read would be needed). When the pipe connects, `hello` and
  the last known status are written, then one status read and one snapshot are requested.
- Pipe client: one connect attempt each time (timeout 0: on Windows a longer connect timeout spins a pool thread for the
  whole timeout while the pipe does not exist), then waits 1 s doubling to 30 s, retrying for the plugin's whole life; on
  each connect it writes `hello`, the latest status, then the queue; a write error drops the connection and reconnects;
  an empty line every 10 s while idle finds a closed SkuaLink (SkuaLink's reader skips empty lines). `Dispose`
  cancels, waits up to 2 s, then disposes its token source. `Unload` first waits up to 1 s for the background task to
  finish the read it may be in (bounded, since `Unload` may run on Skua's UI thread), then disposes the pipe client.
- No snapshot on a map change (it does not change the inventory).

## Cost of the quest reads (unverified on a real Skua)

`Quests.Active` deserializes the whole `world.questTree` (every quest loaded this session, not only the active ones) in
one Flash call, and every read rebuilds Skua's own shared tree cache (see the audit below). The plugin therefore reads it
at most once every 15 s, and at once after the pipe connects and after a login or relogin. The 3 s poll and the
event-driven status refreshes (map change, logout, script start or stop) never read it by themselves: in between, the
status carries the last list read. One read gives both the list and the ready flags, so there is no per-quest read.
Because the 15 s check runs on the 3 s poll, a quest accepted, turned in or made ready shows up to about 18 s late. If a
read fails, the last reported list stays (nothing is reported when there never was one); while logged out the list is
empty and the next login reads at once. **Reading `Active` is unverified on a real Skua**: neither the cost, nor whether
`Active` is meaningful outside a running script, nor whether "ready" matches the game has been checked inside Skua. The
decision rules live in `src/AqwCompanion.Skua/SkuaQuestRefresh.cs` and are unit-tested without Skua.

## Audit: every Skua member the plugin touches

Generated from the compiled DLL's member references (System.Reflection.Metadata) and checked against the source.

| Skua member | Kind | Where it is used |
|---|---|---|
| `ISkuaPlugin` (implemented: `Name`, `Author`, `Description`, `Options`, `Load`, `Unload`) | implemented by the plugin | plugin entry point |
| `IServiceProvider.GetService(typeof(IScriptInterface))`, `GetService(typeof(IScriptManager))` | read (service lookup) | `Load`; the `IScriptManager` (or `bot.Manager`) drives the commands |
| `IScriptInterface.Events` | read | subscribe / re-attach / unsubscribe |
| `IScriptEvent.ItemBought` | event subscription | log "Bought" |
| `IScriptEvent.ItemSold` | event subscription | log "Sold" |
| `IScriptEvent.ItemDropped` | event subscription | log "Drop" |
| `IScriptEvent.MapChanged` | event subscription | log "Map", status refresh |
| `IScriptEvent.PlayerDeath` | event subscription | log "Died" |
| `IScriptEvent.Logout` | event subscription | log "Logged out", status refresh |
| `IScriptEvent.ReloginTriggered` | event subscription | log "Relogin", status refresh |
| `IScriptEvent.ScriptStopping` | event subscription (returns `true`, see correction 5) | log "Script error" |
| `INotifyPropertyChanged.PropertyChanged` on Skua's `ScriptManager` | event subscription | `ScriptRunning` / `LoadedScript` changes |
| `IScriptInterface.Manager` | read | |
| `IScriptStatus.ScriptRunning` | read | running state |
| `IScriptStatus.LoadedScript` | read | script name |
| `IScriptInterface.Player` | read | |
| `IScriptPlayer.LoggedIn` | read | status, gate for other reads |
| `IScriptPlayer.Username` | read | status character |
| `IScriptInterface.Servers` | read | |
| `IScriptServers.LastName` | read (getter only; the setter is never used) | status server |
| `IScriptInterface.Map` | read | |
| `IScriptMap.Name` | read | status map |
| `IScriptInterface.Inventory` | read | |
| `IItemContainer<InventoryItem>.Items` | read | name snapshot, bought quantity |
| `InventoryItem.CharItemID` | read | snapshot key |
| `ItemBase.Name` | read | item names |
| `ItemBase.Quantity` | read | bought quantity |
| `IScriptInterface.Quests` | read | |
| `IScriptQuest.Active` | read (writes Skua's tree cache, see below; at most every 15 s) | status quests |
| `Quest.ID`, `Quest.Name`, `Quest.Status` | read | status quests, ready flag (`Status == "c"`) |
| `IScriptInterface.Reputation` | read | |
| `IScriptFaction.FactionList` | read (one Flash read of `world.myAvatar.factions`, no cache; on every status) | status factions |
| `Faction.ID`, `Faction.Name`, `Faction.Rank`, `Faction.TotalRep`, `Faction.Rep`, `Faction.RequiredRep` | read | status factions |
| `ClientFileSources.SkuaScriptsDIR` (Skua.Core.Models) | read | the scripts folder listed by `listScripts` |
| `IScriptStatus.SetLoadedScript(string)` | **changes Skua state** | `start`, only when no script runs |
| `IScriptStatus.StartScript()` | **changes Skua state** | `start` |
| `IScriptStatus.Config` -> `IScriptOptionContainer.Options`, `.MultipleOptions`, `.OptionsFile` | read | option definitions and the options file, after the plugin's own start; `Config` also identifies the dialog |
| `IOption.Name`, `.DisplayName`, `.Description`, `.Type`, `.DefaultValue` | read | option definitions |
| `ClientFileSources.SkuaOptionsDIR` (Skua.Core.Models) | read | the only folder options files are read from or written to |
| `IServiceProvider.GetService(typeof(IDispatcherService))`, `IDispatcherService.Invoke(Action)` | read / runs plugin code on Skua's UI thread | find and close the options dialog |
| the options file `%AppData%\Skua\options\{Storage}.cfg` | **written** (file, not a Skua member) | `setOptions`, only while no script runs |
| a listed script's `.cs` file | read as text (file, not a Skua member; never compiled or run by the plugin) | its `OptionsStorage` name, when the definitions are not learnt |
| `IScriptStatus.StopScript()` | **changes Skua state** | `stop`, on a pool thread; a first start with options (discovery) |

Framework WPF members (not Skua's): `Application.Current`, `Application.Windows`, `FrameworkElement.DataContext`, and
`Window.Close()` on the one window matched as the started script's options dialog (which makes Skua save the values it
shows).

Removed in AQW-0076: the `IScriptEvent.QuestAccepted` and `IScriptEvent.QuestTurnedIn` subscriptions and the
`IScriptQuest.Tree` read (it only named those two log lines). No other Skua member is referenced. In particular
nothing calls `Send`, `Flash`, `Shops`, `Options`, `RestartScriptAsync`, `Compile`, `CanComplete`, `Inventory.Sell` or
any setter; `SetLoadedScript`, `StartScript` and `StopScript` are called only by the `start` and `stop` commands above
(a discovery start stops its script itself). `Config.Set`, `Save`, `Load` and `Configure` are not called: the options file
is written directly, in Skua's format. Some getters above are implemented by Skua as Flash reads (`isLoggedIn`,
`world.strMapName`, `world.myAvatar.items`, `world.myAvatar.factions`, `world.questTree`, `loginInfo.strUsername`); they read game objects and
change nothing.

Apart from the commands (load, start and stop a script, on SkuaLink's request), the plugin never calls anything that
sends a packet or changes game or script state. One side effect inside Skua remains: `IScriptQuest.Active` goes through `IScriptQuest.Tree`, which
is not a pure getter. It writes Skua's own shared 100 ms cache of the quest tree it just read (`_cachedTree`,
`_lastTreeUpdate`, no lock), and Skua clears that cache itself after accepting or turning in a quest. So a plugin read
racing a script's own quest actions can, at most, make the script's immediately following tree read (for example its
`CanComplete` check) be served from a cache filled a moment earlier, bounded by the 100 ms cache life. This is why the
plugin reads `Active` at most every 15 s.

## Build

The Skua DLLs are not committed. Put `Skua.Core.Interfaces.dll` and `Skua.Core.Models.dll` from the Skua 1.4.5.0
release in `lib/skua/` (or pass `-p:SkuaLibDir=...`), then:

```
dotnet build plugin/SkuaLink.SkuaBridge -c Release
```

The output is `SkuaLink.SkuaBridge.dll` (plus `.pdb` and `.deps.json`); no Skua assembly is copied. The protocol
files from `src/AqwCompanion.Skua` are compiled in as source, so the plugin is one DLL whose only non-framework
references are `Skua.Core.Interfaces` and `Skua.Core.Models`. Since AQW-0089 the project sets `UseWPF` (the Windows
Desktop framework reference, no package) for the options dialog: the DLL then also references the framework's
`PresentationFramework`, which Skua (a WPF app on `net10.0-windows`) already loads.

## Not verified (owner's Windows checklist)

- The plugin loads in Skua 1.4.5.0 and connects to SkuaLink (including an elevated SkuaLink and a non-elevated Skua).
- Whether the game has applied a purchase to the inventory when `buyItem` arrives; the plugin reads it 300 ms later.
  If not, "Bought" shows without a quantity and possibly as `item #id`.
- Bought quantity can be wrong or missing for back-to-back buys of the same item: the "before" comes from the last
  snapshot, which may already include an earlier buy, or not yet include it.
- That the re-attach after `ClearHandlers` keeps the log working across several script runs.
- Reading `Quests.Active` at most every 15 s: the cost (see above) and whether the list is right with no script running.
- A script that registers quests (for example a `Bot.RegisterQuests` loop) keeps working normally with the plugin
  connected.
- The quest card updates within about 18 s of accepting or turning in a quest.
- The ready flag appears when the game shows the quest as completable.
- The faction list read every 3 s: its cost with ~50 factions, that it matches the game's Reputation panel, and that
  `Rep` / `RequiredRep` are the in-rank progress and the rank step (0 at rank 10) as the source says.
- Several Skua at once (AQW-0090): SkuaLink now keeps up to 8 connections on the same pipe name (9 pipe instances with
  the user-only descriptor: 8 connections and one waiting spare). Not seen on Windows: that 8 plugins connect at once
  and each one's commands reach only that Skua; that a ninth Skua, closed at once by SkuaLink, keeps retrying every
  second without errors in Skua; that a restarted Skua (new instance id) comes back as the same entry once its character
  logs in; that a Skua whose plugin fell back to the send-only pipe still reports while its commands time out ("Skua did
  not answer"); that the per-character log files (`skua-log.<character>.json`) appear in SkuaLink's data folder and the
  old `skua-log.json` is folded into the first character that logs in.
- Commands (AQW-0088):
  - The two-way pipe connects to the new SkuaLink, and a SkuaLink without commands (inbound-only pipe) still gets the
    reports: that Windows refuses the `InOut` client with access denied (`UnauthorizedAccessException`) is from the
    Win32 rules, not seen.
  - Listing: the folder, the names and folders shown, `Cached-Scripts` left out, the time a large scripts folder takes.
  - Start: the script really starts from the plugin (no Skua UI involved), Skua's loader label (it is not updated by
    `SetLoadedScript`), and the text of a compile error or version error as it reaches the phone.
  - `hasOptions` read from `Config` right after `StartScript` describes the script just started (not the previous one).
  - Stop duration: how long Skua takes from `stop` until `ScriptRunning` turns false, and a script that ignores the
    stop (Skua gives up after about 40 s and the state stays `stopping` until it really ends; a new `stop` tries again).
  - Settling: that 10 s without a map change (seen on the 3 s poll while no script runs) covers Skua's cleanup after a
    stop (for example going to the house), and that a script that stops by itself also settles.
  - A start accepted but never reported running settles after 10 s.
- Script options (AQW-0089):
  - The options file path and format on a real Skua: `%AppData%\Skua\options\{OptionsStorage}.cfg`, the lines Skua
    writes, enum values with spaces, and that a file written by the plugin is read back by Skua unchanged.
  - That `Config` read right after `StartScript()` returns is the script just started (definitions, `OptionsFile`).
  - The window match (`DataContext` type name `OptionContainerViewModel` and its `Container` being `Config`) and the
    close with `Window.Close()` through `IDispatcherService.Invoke`, while Skua's dialog runs in `ShowDialog`; and what
    the phone shows when either fails (`optionsStuck`).
  - That the script reads the saved values after the dialog closes (Skua saves what the dialog showed, which it loaded
    from the file just written), including a script that reads its options before the dialog closes.
  - First start (discovery): that stopping the script right after closing its dialog leaves Skua clean (settling
    applies), and that a dialog appearing later than 5 s after the start (slow PC) does not stay on the PC.
  - A second start of the same script in one Skua session: whether Skua shows the dialog again (the source suggests it
    does for every start of a script without `DontPreconfigure`), and that with no dialog the state goes to `running`
    after 5 s.
  - That a script with `DontPreconfigure = true` shows no dialog and starts with the saved values.
  - The fallback after a Skua restart: that real scripts declare `public string OptionsStorage = "...";` on one line
    (scripts that build the name another way get a discovery run instead), and that the file found this way is the one
    Skua uses.
  - A PC whose culture writes decimals with a comma: the phone sends doubles in invariant culture (`1.5`) and the plugin
    writes them in the PC's culture (`1,5`), as Skua reads them (`Convert.ChangeType` with the current culture), and
    reads them back the same way; that the plugin's thread culture is the one Skua's script thread uses is not seen.
  - After a Skua restart (the plugin has no definitions), SkuaLink converts doubles itself with SkuaLink's own culture
    (the same Windows user, so the same culture as Skua is assumed) and sends enum members with spaces; that Skua reads
    both back correctly from a file written this way (AQW-0091).
