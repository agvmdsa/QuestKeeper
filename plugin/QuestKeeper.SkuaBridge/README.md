# QuestKeeper.SkuaBridge

A read-only Skua plugin. It runs inside Skua 1.4.5.0 and reports Skua's state and a short event log to Questkeeper over a
local named pipe (`QuestKeeper.SkuaBridge.v1`, current Windows user only, outbound only). Status: **written and
statically reviewed, not verified inside Skua** (only the owner's Windows can prove it).

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
7. **Active quests (AQW-0076), read-only:** `IScriptInterface.Quests` -> `IScriptQuest.Active` (`List<Quest>`, never null
   in 1.4.5.0: it is `Tree.FindAll(x => x.Active)`, and the `world.questTree` binding answers an empty dictionary when the
   read fails) and `IScriptQuest.CanComplete(int)` (`Completed.Contains(q => q.ID == id)`, which reads `Tree` again and
   checks `Status == "c"`; Skua's own remark: "can not always be right"), with `Quest.ID`, `Quest.Name`. The
   `QuestAccepted` and `QuestTurnedIn` subscriptions and the `IScriptQuest.Tree` name lookup were removed: the active
   list in the status replaces those two log lines.
8. `IScriptInterface.Version` exists (Skua's version from its settings) but is not used: `hello` reports
   `Assembly.GetEntryAssembly()?.GetName().Version` as decided.

## What it sends

- `hello` on every (re)connect: plugin version (informational version without build metadata) and Skua's version.
- `status` on every (re)connect and whenever it changes: logged in; character, server, map (only while logged in);
  script file name without folder or extension (no local path leaves the plugin); running; running since; the active
  quests (`quests`: id, name, ready to turn in; at most 30, in Skua's order, a blank name sent as `#id`; empty while
  logged out). A change in the list or in a ready flag sends a new status; nothing changed sends nothing.
- `log`: bought (quantity = now minus snapshot when positive, else none), sold (name from the snapshot taken before the
  sale), dropped (`Drop: X` for an item offered in the drop list, Skua's `dropItem`, the usual farming case; `Received: X`
  for an item put straight into the inventory, Skua's `addItems`, such as a reward; accepting a drop-list item raises no
  further event, so each drop is one line), map changed, died, logged out, relogin, script started, script stopped,
  script error. Quest accepted / turned in are no longer sent (the active quests travel in the status instead); the two
  kinds stay in the protocol so logs kept by an older version still load.
- Never: gold, inventory contents, monster kills (`MonsterKilled` is not subscribed).

## Threading and safety

- Every handler goes through `Safe(Action)`; `Load`, `Unload` and every public `PipeClient` member catch everything.
  A failed `Load` removes whatever it attached. Skua never sees an exception from the plugin.
- Handlers run on Skua's threads (the UI thread for game packets, the script thread, a `Task.Run` for
  `ScriptStopping`) and only build a message and queue it. Every read that goes through Flash (inventory, quests,
  logged in, map, user name, active quests, ready flags) runs on one background task, never on Skua's threads.
- Queues are bounded (256, oldest dropped): the pipe client's line queue and the worker's queue.
- While Questkeeper is not connected the plugin does no Flash reads at all: no status poll, no inventory snapshot, no
  quest read (log lines still queue, unnamed where a read would be needed). When the pipe connects, `hello` and
  the last known status are written, then one status read and one snapshot are requested.
- Pipe client: one connect attempt each time (timeout 0: on Windows a longer connect timeout spins a pool thread for the
  whole timeout while the pipe does not exist), then waits 1 s doubling to 30 s, retrying for the plugin's whole life; on
  each connect it writes `hello`, the latest status, then the queue; a write error drops the connection and reconnects;
  an empty line every 10 s while idle finds a closed Questkeeper (Questkeeper's reader skips empty lines). `Dispose`
  cancels, waits up to 2 s, then disposes its token source.
- No snapshot on a map change (it does not change the inventory).

## Cost of the quest reads (unverified on a real Skua)

`Quests.Active` deserializes the whole `world.questTree` (every quest loaded this session, not only the active ones) in
one Flash call; it is read with every status read (the 3 s poll and the event-driven refreshes). `CanComplete(id)` reads
`Tree` again (Skua keeps a 100 ms cache, so a burst of calls mostly reuses one read) and is called once per active quest,
at most 30. To bound that cost the ready flags are read only when the set of active quest ids changed or 15 s have
passed since the last read, whichever comes first; between reads the last flags are reused, so a quest can show "ready"
up to 15 s late. If a list read fails, the last reported list stays; if one `CanComplete` fails, that quest keeps its last
flag. **Reading `Active` and `CanComplete` is unverified on a real Skua**: neither the cost, nor whether `Active` is
meaningful outside a running script, nor the accuracy of `CanComplete` has been checked inside Skua. The decision rules
live in `src/AqwCompanion.Skua/SkuaQuestRefresh.cs` and are unit-tested without Skua.

## Read-only audit: every Skua member the plugin touches

Generated from the compiled DLL's member references (System.Reflection.Metadata) and checked against the source.

| Skua member | Kind | Where it is used |
|---|---|---|
| `ISkuaPlugin` (implemented: `Name`, `Author`, `Description`, `Options`, `Load`, `Unload`) | implemented by the plugin | plugin entry point |
| `IServiceProvider.GetService(typeof(IScriptInterface))`, `GetService(typeof(IScriptManager))` | read (service lookup) | `Load` |
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
| `IScriptQuest.Active` | read | status quests |
| `IScriptQuest.CanComplete(int)` | read (a check, changes nothing) | status quest ready flag (at most every 15 s or on a change of the active set) |
| `Quest.ID`, `Quest.Name` | read | status quests |

Removed in AQW-0076: the `IScriptEvent.QuestAccepted` and `IScriptEvent.QuestTurnedIn` subscriptions and the
`IScriptQuest.Tree` read (it only named those two log lines). No other Skua member is referenced. In particular nothing calls `Send`, `Flash`, `Shops`, `Options`, `StartScript`,
`StopScript`, `SetLoadedScript`, `Inventory.Sell` or any setter. Some getters above are implemented by Skua as Flash
reads (`isLoggedIn`, `world.strMapName`, `world.myAvatar.items`, `world.questTree`, `loginInfo.strUsername`); they
read game objects and change nothing. `IScriptQuest.Active` and `CanComplete` go through `IScriptQuest.Tree` inside
Skua, which is not a pure getter: it writes Skua's own 100 ms cache of the quest tree it just read (`_cachedTree`,
`_lastTreeUpdate`); that is read-only in effect (no game or script state changes) but it is a write inside Skua.

## Build

The Skua DLLs are not committed. Put `Skua.Core.Interfaces.dll` and `Skua.Core.Models.dll` from the Skua 1.4.5.0
release in `lib/skua/` (or pass `-p:SkuaLibDir=...`), then:

```
dotnet build plugin/QuestKeeper.SkuaBridge -c Release
```

The output is `QuestKeeper.SkuaBridge.dll` (plus `.pdb` and `.deps.json`); no Skua assembly is copied. The protocol
files from `src/AqwCompanion.Skua` are compiled in as source, so the plugin is one DLL whose only non-framework
references are `Skua.Core.Interfaces` and `Skua.Core.Models`.

## Not verified (owner's Windows checklist)

- The plugin loads in Skua 1.4.5.0 and connects to Questkeeper (including an elevated Questkeeper and a non-elevated Skua).
- Whether the game has applied a purchase to the inventory when `buyItem` arrives; the plugin reads it 300 ms later.
  If not, "Bought" shows without a quantity and possibly as `item #id`.
- Bought quantity can be wrong or missing for back-to-back buys of the same item: the "before" comes from the last
  snapshot, which may already include an earlier buy, or not yet include it.
- That the re-attach after `ClearHandlers` keeps the log working across several script runs.
- Reading `Quests.Active` and `CanComplete` on the poll: the cost (see above), whether the list is right with no script
  running, and whether "ready" matches the game.
- Only one Skua instance is reported at a time (Questkeeper serves one connection); a second Skua keeps retrying quietly.
