export interface Figures {
  gold: number;
  exp: number;
  expToLevel: number;
  ac: number;
  classPoints: number;
  daysPlayed: number;
}

export interface Kill {
  gold: number;
  exp: number;
}

export interface Character {
  name: string;
  level: number;
  live: boolean;
  savedAtUtc: string;
  lastLiveUpdateUtc: string | null;
  liveUpdates: number;
  figures: Figures;
  lastKill: Kill | null;
}

export interface Faction {
  name: string;
  rank: number;
  rep: number;
  toNext: number;
  fraction: number;
  max: boolean;
}

export interface HardFarms {
  owned: number;
  total: number;
  missing: string[];
}

export interface Snapshot {
  contract: number;
  capture: { state: string; listening: boolean };
  selected: string | null;
  characters: Character[];
  factions: Faction[];
  hardFarms: HardFarms | null;
}

export type NoticeKind = 'RankUp' | 'HardFarmDrop' | 'ActivityStopped' | 'CaptureLost' | 'CaptureBack' | 'NewLogin';

export interface Notice {
  id: string;
  kind: NoticeKind;
  character: string;
  title: string;
  body: string;
  atUtc: string;
}

export type SkuaConnection = 'NotConnected' | 'Connected' | 'VersionMismatch';

export interface SkuaStatus {
  loggedIn: boolean;
  character: string | null;
  server: string | null;
  map: string | null;
  script: string | null;
  scriptRunning: boolean;
  runningSince: string | null;
}

export interface SkuaLogEntry {
  at: string;
  kind: string;
  text: string;
}

export interface SkuaState {
  connection: SkuaConnection;
  hello: { pluginVersion: string; skuaVersion: string } | null;
  status: SkuaStatus | null;
  log: SkuaLogEntry[];
}

/** What /api/state and the "state" event carry: the last snapshot of the PC and its recent notices. */
export interface RemoteState {
  snapshot: Snapshot | null;
  notices: Notice[];
  /** The Skua part; an older app sends none, which means null. */
  skua?: SkuaState | null;
}
