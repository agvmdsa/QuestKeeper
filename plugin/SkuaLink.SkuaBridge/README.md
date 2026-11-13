# SkuaLink.SkuaBridge

A read-only Skua plugin. It runs inside Skua 1.4.5.0 and reports Skua's state and a short event log to SkuaLink over a
local named pipe (`SkuaLink.SkuaBridge.v1`, current Windows user only, outbound only). Status: **connect and status
verified by the owner on Windows on 2026-11-11 with an earlier build; the quest reading, the re-attach across script
runs and the rest are not yet verified** (only the owner's Windows can prove it).

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

- `hello` on every (re)connect: plugin version (informational version without build metadata) and Skua's version.
- `status` on every (re)connect and whenever it changes: logged in; character, server, map (only while logged in);
  script file name without folder or extension (no local path leaves the plugin); running; running since; the active
  quests (`quests`: id, name, ready to turn in; at most 30, in Skua's order, a blank name sent as `#id`; empty while
  logged out; re-read at most every 15 s, see below); the faction reputation (`factions`: id, name, rank, total rep,
  rep inside the rank, size of the rank step; at most 60, in Skua's order, a blank name sent as `#id`; empty while
  logged out; read on every status, so about every 3 s). A change in either list, a ready flag or a reputation number
  sends a new status; nothing changed sends nothing.
- `log`: bought (quantity = now minus snapshot when positive, else none), sold (name from the snapshot taken before the
  sale), dropped (`Drop: X` for an item offered in the drop list, Skua's `dropItem`, the usual farming case; `Received: X`
  for an item put straight into the inventory, Skua's `addItems`, such as a reward; accepting a drop-list item raises no
  further event, so each drop is one line; a drop whose name is known also carries `item` and `count` 1, and SkuaLink
  merges repeated drops of one item into one line, `Drop: X x13`, while each comes within 60 s of the last), map changed, died, logged out, relogin, script started, script stopped,
  script error (the exception's message with every absolute path reduced to its file name, so no local folder and its
  Windows user name leaves the plugin). Quest accepted / turned in are no longer sent (the active quests travel in the status instead); the two
  kinds stay in the protocol so logs kept by an older version still load.
- Never: gold, inventory contents, monster kills (`MonsterKilled` is not subscribed).

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
| `IScriptQuest.Active` | read (writes Skua's tree cache, see below; at most every 15 s) | status quests |
| `Quest.ID`, `Quest.Name`, `Quest.Status` | read | status quests, ready flag (`Status == "c"`) |
| `IScriptInterface.Reputation` | read | |
| `IScriptFaction.FactionList` | read (one Flash read of `world.myAvatar.factions`, no cache; on every status) | status factions |
| `Faction.ID`, `Faction.Name`, `Faction.Rank`, `Faction.TotalRep`, `Faction.Rep`, `Faction.RequiredRep` | read | status factions |

Removed in AQW-0076: the `IScriptEvent.QuestAccepted` and `IScriptEvent.QuestTurnedIn` subscriptions and the
`IScriptQuest.Tree` read (it only named those two log lines). No other Skua member is referenced. In particular
nothing calls `Send`, `Flash`, `Shops`, `Options`, `StartScript`, `StopScript`, `SetLoadedScript`, `CanComplete`,
`Inventory.Sell` or any setter. Some getters above are implemented by Skua as Flash reads (`isLoggedIn`,
`world.strMapName`, `world.myAvatar.items`, `world.myAvatar.factions`, `world.questTree`, `loginInfo.strUsername`); they read game objects and
change nothing.

The plugin is read-only with respect to game and script state: it never calls anything that sends a packet or changes
game or script state. One side effect inside Skua remains: `IScriptQuest.Active` goes through `IScriptQuest.Tree`, which
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
references are `Skua.Core.Interfaces` and `Skua.Core.Models`.

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
- Only one Skua instance is reported at a time (SkuaLink serves one connection); a second Skua keeps retrying quietly.
