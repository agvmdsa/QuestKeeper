# Local feed (for other programs)

Off by default. **Settings, "Let other programs on this PC read my characters"** turns on a read-only feed that other programs on the same PC can use, for example a remote viewer. It listens on `127.0.0.1:47821` only, never on the network, and it never sends anything to the game.

- **Secret:** a program must send `Authorization: Bearer <secret>`. The secret is the text in `%LOCALAPPDATA%\AqwCompanion\feed.token`, which is created when you turn the feed on; **New secret** in Settings replaces it. A request with an `Origin` header (a web page) or another `Host` is refused.
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
