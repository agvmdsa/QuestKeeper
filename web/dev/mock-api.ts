import type { Plugin } from 'vite';
import type { RemoteState, SkuaInstance } from '../src/types';

function state(): RemoteState {
  const now = new Date().toISOString();
  return {
    snapshot: {
      contract: 1,
      capture: { state: 'Listening. 2 characters captured so far.', listening: true },
      selected: 'Shake the babe',
      characters: [
        {
          name: 'Shake the babe', level: 82, live: true, savedAtUtc: now, lastLiveUpdateUtc: now, liveUpdates: 58,
          figures: { gold: 93650135, exp: 1609986, expToLevel: 5590000, ac: 200, classPoints: 302500, daysPlayed: 8 }, lastKill: { gold: 625, exp: 1250 },
        },
        {
          name: 'Second Hero', level: 60, live: false, savedAtUtc: new Date(Date.now() - 3 * 3600_000).toISOString(), lastLiveUpdateUtc: null, liveUpdates: 0,
          figures: { gold: 1200, exp: 340, expToLevel: 5000, ac: 777, classPoints: 99, daysPlayed: 300 }, lastKill: null,
        },
      ],
      factions: [
        { name: 'Yokai', rank: 8, rep: 192600, toNext: 9900, fraction: 0.864, max: false },
        { name: 'Good', rank: 10, rep: 302500, toNext: 0, fraction: 1, max: true },
      ],
      hardFarms: { owned: 1, total: 138, missing: Array.from({ length: 40 }, (_, i) => `Hard Farm item ${i + 1}`) },
    },
    notices: [{ id: 'rank:Shake the babe:Yokai:8', kind: 'RankUp', character: 'Shake the babe', title: 'Rank up', body: 'Yokai reached rank 8.', atUtc: now }],
    // MOCK=skua adds one connected Skua playing the selected character, so the Skua tab and its reputation link can be seen.
    ...(process.env.MOCK === 'skua' ? { skuaInstances: [skua(now)] } : {}),
  };
}

function skua(now: string): SkuaInstance {
  return {
    instanceId: 'mock-1', label: 'Shake the babe', disconnected: false, connection: 'Connected', hello: { pluginVersion: '0.5.0', skuaVersion: '1.4.5.0' },
    status: {
      loggedIn: true, character: 'Shake the babe', server: 'Artix', map: 'battleon', script: 'Farm.cs', scriptRunning: true, runningSince: now, control: 'running',
      quests: [{ id: 1, name: 'Slay the Beast', ready: true }],
      factions: [{ id: 1, name: 'Yokai', rank: 8, totalRep: 192600, rep: 30000, requiredRep: 39900 }, { id: 2, name: 'Good', rank: 10, totalRep: 302500, rep: 0, requiredRep: 0 }],
    },
    log: [{ at: now, kind: 'Sold', text: 'Sold Blood Orb x50 for 2,500 gold' }],
  };
}

export function mockApi(): Plugin {
  return {
    name: 'qkp-mock-api',
    apply: 'serve',
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const path = new URL(req.url ?? '/', 'http://localhost').pathname;
        if (!path.startsWith('/api/')) return next();
        if (process.env.MOCK === 'unpaired') {
          res.statusCode = 401;
          res.setHeader('content-type', 'application/json');
          return res.end('{"error":"not paired"}');
        }
        if (path === '/api/state') {
          res.setHeader('content-type', 'application/json');
          return res.end(JSON.stringify(state()));
        }
        if (path === '/api/events') {
          res.writeHead(200, { 'content-type': 'text/event-stream', 'cache-control': 'no-cache' });
          res.write(`event: state\ndata: ${JSON.stringify(state())}\n\n`);
          const timer = setInterval(() => res.write('event: ping\ndata: {}\n\n'), 25_000);
          req.on('close', () => clearInterval(timer));
          return undefined;
        }
        res.statusCode = path === '/api/pair' ? 204 : 404;
        return res.end();
      });
    },
  };
}
