import { readFileSync } from 'node:fs';
import { describe, expect, it, vi } from 'vitest';
import type { CallResult, ControlApi } from '../src/api';
import {
  Control, controlBanner, controlCardModel, failureText, parseOptions, parseScripts, parseStart, scriptGroups, searchScripts, type ScriptItem,
} from '../src/control';
import { parseSkuaInstances } from '../src/skua';
import { controlCard, sheetView } from '../src/controlViews';
import { skuaView, type Model, type VNode } from '../src/views';
import type { SkuaInstance } from '../src/types';

const NOW = Date.parse('2026-11-14T12:00:00Z');
const ID_A = '0123456789abcdef';
const ID_B = 'fedcba9876543210';
const ID_C = 'aaaaaaaaaaaaaaaa';

const status = (over: object = {}) => ({
  loggedIn: true, character: 'Hero', server: 'Artix', map: 'battleon', script: null, scriptRunning: false, runningSince: null, quests: null, factions: null,
  control: 'idle', settleSecondsLeft: null, optionsStuck: false, ...over,
});
const inst = (st: object = {}, over: Record<string, unknown> = {}): SkuaInstance =>
  parseSkuaInstances([{ instanceId: 'i-1', label: 'Hero', connection: 'Connected', disconnected: false, hello: null, status: status(st), log: [], ...over }])![0]!;
const running = (over: object = {}) => inst({ control: 'running', scriptRunning: true, script: 'Farm', runningSince: '2026-11-14T10:00:00Z', ...over });

const all = (n: VNode | null): VNode[] => (n ? [n, ...n.children.flatMap(all)] : []);
const texts = (n: VNode | null) => all(n).map((x) => x.text).filter((t): t is string => t !== undefined);
const byAction = (n: VNode | null, action: string) => all(n).filter((x) => x.attrs?.['data-action'] === action);

const ok = (data: unknown, status = 200): CallResult => ({ status, data });
const err = (status: number, error?: string): CallResult => ({ status, data: error === undefined ? null : { error } });

const scriptsBody = (over: ScriptItem[] = []) => ({
  scripts: [
    { id: ID_A, name: 'Farm', folder: 'Farming', askOptions: true },
    { id: ID_B, name: 'Quests', folder: '', askOptions: false },
    { id: ID_C, name: 'Butler', folder: 'Farming', askOptions: true },
    ...over,
  ],
  truncated: false,
});
const def = (name: string, type: string, extra: object = {}) => ({ category: 'Options', name, displayName: `${name} label`, description: `${name} help`, type, default: 'd', choices: null, ...extra });
const optionsBody = (over: object = {}) => ({
  defs: [def('Sell', 'bool'), def('Loops', 'int'), def('Rate', 'double'), def('Player', 'string'), def('Mode', 'enum', { choices: ['Fast Farm', 'Slow'] }), def('Blob', 'other')],
  values: [
    { category: 'Options', name: 'Sell', value: 'True' },
    { category: 'Options', name: 'Loops', value: '200' },
    { category: 'Options', name: 'Rate', value: '1.5' },
    { category: 'Options', name: 'Player', value: 'Artix' },
    { category: 'Options', name: 'Mode', value: 'Slow' },
    { category: 'Options', name: 'Blob', value: 'x;y' },
  ],
  known: true, hasOptions: true, discovery: false, askOptions: true, ...over,
});

interface Deferred { resolve(r: CallResult): void }
/** A fake API whose answers the test hands out one by one; every call is recorded. */
function fakeApi() {
  const calls: { name: string; args: unknown[] }[] = [];
  const waiting: Deferred[] = [];
  const answers: CallResult[] = [];
  const call = (name: string) => (...args: unknown[]) => {
    calls.push({ name, args });
    const next = answers.shift();
    if (next) return Promise.resolve(next);
    return new Promise<CallResult>((resolve) => waiting.push({ resolve }));
  };
  const api = {
    listScripts: call('listScripts'), getOptions: call('getOptions'), setOptions: call('setOptions'), setPrefs: call('setPrefs'), start: call('start'), stop: call('stop'),
  } as unknown as ControlApi;
  return { api, calls, answers, waiting, names: () => calls.map((c) => c.name) };
}
const flush = async () => {
  for (let i = 0; i < 10; i += 1) await Promise.resolve();
};

function setup() {
  const fake = fakeApi();
  const changed = vi.fn();
  const unpaired = vi.fn();
  const control = new Control(fake.api, changed, unpaired, () => NOW);
  return { ...fake, control, changed, unpaired };
}

describe('parsing the answers', () => {
  it('reads the script list and drops broken entries', () => {
    const r = parseScripts({ scripts: [...scriptsBody().scripts, { id: 'BAD', name: 'x', folder: '' }, { id: ID_A.slice(1), name: 'x' }, { id: `${ID_A}\n`, name: 'x' }, 7, { id: ID_B, name: 3 }], truncated: true })!;
    expect(r.scripts.map((s) => s.id)).toEqual([ID_A, ID_B, ID_C]);
    expect(r.truncated).toBe(true);
    expect(r.scripts[1]).toEqual({ id: ID_B, name: 'Quests', folder: '', askOptions: false });
  });
  it('asks for options when askOptions is missing (the default)', () => expect(parseScripts({ scripts: [{ id: ID_A, name: 'F', folder: 'x' }] })!.scripts[0]!.askOptions).toBe(true));
  it('is null for a body that is not the shape', () => {
    expect(parseScripts(null)).toBeNull();
    expect(parseScripts({ scripts: 'x' })).toBeNull();
  });
  it('caps the list at 2000 like the PC, says it was cut, and cuts long texts at 300', () => {
    const many = Array.from({ length: 2001 }, (_, i) => ({ id: i.toString(16).padStart(16, '0'), name: 'n'.repeat(400), folder: 'f' }));
    const r = parseScripts({ scripts: many, truncated: false })!;
    expect(r.scripts).toHaveLength(2000);
    expect(r.truncated).toBe(true);
    expect(r.scripts[0]!.name.length).toBe(300);
  });
  it('keeps all 2000 scripts the PC may send without calling the list cut', () => {
    const many = Array.from({ length: 2000 }, (_, i) => ({ id: i.toString(16).padStart(16, '0'), name: `s${i}`, folder: 'f' }));
    const r = parseScripts({ scripts: many, truncated: false })!;
    expect(r.scripts).toHaveLength(2000);
    expect(r.truncated).toBe(false);
  });
  it('reads options with every type and keeps unknown types read-only', () => {
    const r = parseOptions(optionsBody({ defs: [def('A', 'bool'), def('B', 'weird'), { name: 'no category' }, def('', 'int')] }))!;
    expect(r.defs!.map((d) => [d.name, d.type])).toEqual([['A', 'bool'], ['B', 'other']]);
    expect(r.known).toBe(true);
  });
  it('keeps null definitions apart from none', () => {
    expect(parseOptions(optionsBody({ defs: null, known: false }))!.defs).toBeNull();
    expect(parseOptions(optionsBody({ defs: [] }))!.defs).toEqual([]);
  });
  it('reads the start answer', () => {
    expect(parseStart({ hasOptions: true, firstRun: true, defs: [def('P', 'string')], values: [] })).toMatchObject({ firstRun: true, hasOptions: true });
    expect(parseStart({ ok: true })).toEqual({ hasOptions: false, firstRun: false, defs: null, values: [] });
  });
});

describe('error texts', () => {
  it.each([
    [err(504, 'Skua did not answer'), 'start', 'Skua did not answer. Check that Skua is open and the plugin is loaded.'],
    [err(409, 'busy'), 'start', 'Another command is still running. Try again in a moment.'],
    [err(409, 'Skua is disconnected'), 'stop', 'This Skua is disconnected.'],
    [err(404, 'unknown Skua'), 'start', 'SkuaLink no longer knows this Skua.'],
    [err(0), 'start', 'Could not reach SkuaLink. Try again.'],
    [err(500), 'start', 'Something went wrong. Try again.'],
    [err(400, 'already running'), 'start', 'Could not start: a script is already running.'],
    [err(400, 'settling'), 'start', 'Could not start: Skua is still finishing the last script. Try again in a few seconds.'],
    [err(400, 'stopping'), 'start', 'Could not start: Skua is still finishing the last script. Try again in a few seconds.'],
    [err(400, 'unknown script id'), 'start', "Could not start: the script is no longer in Skua's scripts folder."],
    [err(400, 'Compile error (line 42)'), 'start', 'Could not start: Compile error (line 42)'],
    [err(400, 'value too long for Rate'), 'options', 'Options not saved: value too long for Rate'],
    [err(400, 'x'), 'stop', 'Could not stop: x'],
    [err(400, 'x'), 'list', 'Could not list the scripts: x'],
    [err(400, 'x'), 'load', 'Could not read the options: x'],
    [err(400, 'x'), 'prefs', 'Could not save the choice: x'],
    [err(400), 'start', 'Something went wrong. Try again.'],
  ] as const)('%j while %s says the right thing', (r, action, text) => expect(failureText(r, action)).toBe(text));
  it('cuts a long plugin text', () => expect(failureText(err(400, 'e'.repeat(1000)), 'start').length).toBeLessThan(340));
});

describe('the Control card model', () => {
  const card = (i: SkuaInstance, control?: Control) => controlCardModel(i, control ?? setup().control, NOW)!;
  it('offers Start on an idle Skua', () => {
    const c = card(inst());
    expect(c).toMatchObject({ state: 'Idle', action: 'start', disabled: null, waiting: false });
  });
  it('offers Stop while running, with how long and which script, and says values change only while stopped', () => {
    const c = card(running());
    expect(c).toMatchObject({ state: 'Running', action: 'stop', disabled: null, script: 'Farm' });
    expect(c.since).toBe('for 2 h');
    expect(c.hint).toBe('Option values can be changed only while the script is stopped.');
  });
  it('disables Start while stopping, with the reason', () => {
    const c = card(inst({ control: 'stopping', script: 'Farm' }));
    expect(c).toMatchObject({ state: 'Stopping', action: 'start' });
    expect(c.disabled).toBe('Wait until the script has stopped.');
  });
  it('disables Start while settling, with the seconds left and a bar', () => {
    const c = card(inst({ control: 'settling', settleSecondsLeft: 7 }));
    expect(c.state).toBe('Settling');
    expect(c.disabled).toBe('The script stopped. Waiting for the bot to finish going back (about 7 s) before another script can start.');
    expect(c.settle).toBeCloseTo(0.3);
    expect(card(inst({ control: 'settling' })).disabled).toBe('The script stopped. Waiting for the bot to finish going back before another script can start.');
  });
  it('says Applying options... while Skua closes the options window, and keeps Stop', () => {
    expect(card(inst({ control: 'awaitingOptions', script: 'Farm' }))).toMatchObject({ state: 'Applying options...', action: 'stop', waiting: false });
  });
  it('is loud when the options window could not be closed, and keeps Stop', () => {
    const c = card(inst({ control: 'awaitingOptions', optionsStuck: true, script: 'Farm' }));
    expect(c).toMatchObject({ state: 'Waiting for you on the PC', action: 'stop', waiting: true });
    expect(c.strong).toBe("Close the script's options window on the computer.");
    expect(c.hint).toBe('The script does not start until you do.');
  });
  it('is loud for optionsStuck whatever the control says', () => expect(card(inst({ control: 'running', optionsStuck: true })).waiting).toBe(true));
  it('says an older plugin cannot be controlled and offers no button', () => {
    const c = card(inst({ control: undefined, scriptRunning: true }));
    expect(c.action).toBeNull();
    expect(c.hint).toBe('This Skua plugin cannot start or stop scripts. Update it to the same release as SkuaLink.');
  });
  it('is null for a disconnected Skua, a version mismatch or no status', () => {
    const { control } = setup();
    expect(controlCardModel(inst({}, { disconnected: true }), control, NOW)).toBeNull();
    expect(controlCardModel(inst({}, { connection: 'VersionMismatch' }), control, NOW)).toBeNull();
    expect(controlCardModel(inst({}, { status: null }), control, NOW)).toBeNull();
  });
  it('says Not logged in but still offers Start', () => expect(card(inst({ loggedIn: false }))).toMatchObject({ state: 'Not logged in', action: 'start', disabled: null }));
});

describe('the script list sheet', () => {
  it('opens loading, then lists folders as headings with the target in the title', async () => {
    const s = setup();
    s.control.openStart(inst());
    let v = sheetView(s.control)!;
    expect(texts(v)).toContain('Start on Hero');
    expect(texts(v)).toContain('Loading scripts...');
    expect(s.names()).toEqual(['listScripts']);
    expect(s.calls[0]!.args).toEqual(['i-1']);
    s.waiting.shift()!.resolve(ok(scriptsBody()));
    await flush();
    v = sheetView(s.control)!;
    const headings = all(v).filter((n) => n.tag === 'h3').map((n) => n.text);
    expect(headings).toEqual(['Farming', 'Main scripts folder']);
    expect(byAction(v, 'script-pick').map((b) => b.attrs!['data-value'])).toEqual([ID_A, ID_C, ID_B]);
  });
  it('filters by the search text, ignoring case, and says when nothing matches', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()));
    s.control.openStart(inst());
    await flush();
    s.control.setQuery('BUT');
    expect(byAction(sheetView(s.control), 'script-pick').map((b) => b.attrs!['data-value'])).toEqual([ID_C]);
    s.control.setQuery('zzz');
    expect(texts(sheetView(s.control))).toContain('No script matches your search.');
  });
  it('groups and sorts like the server, keeping empty folders out', () => {
    const g = scriptGroups(scriptsBody().scripts, '');
    expect(g.map((x) => x.folder)).toEqual(['Farming', '']);
    expect(scriptGroups(scriptsBody().scripts, 'farm').map((x) => x.scripts.map((s) => s.name))).toEqual([['Farm', 'Butler']]);
  });
  it('searches folder and name, ignoring case, spaces and punctuation, every word required', () => {
    const list: ScriptItem[] = [
      { id: ID_A, name: '0RunAll', folder: 'Hollowborn/HollowbornChaosEnvoy', askOptions: false },
      { id: ID_B, name: 'HollowbornREP', folder: 'Farm/REP', askOptions: false },
      { id: ID_C, name: 'Run-All', folder: 'Other', askOptions: false },
    ];
    const names = (q: string) => scriptGroups(list, q).flatMap((g) => g.scripts.map((x) => x.id));
    for (const q of ['hollowborn envoy', 'chaos envoy', 'chaosenvoy', 'run all', 'HOLLOWBORN, Envoy!', '  chaos   envoy  ']) {
      expect(names(q), q).toContain(ID_A);
    }
    expect(names('hollowborn envoy')).toEqual([ID_A]);
    expect(names('envoy zzz')).toEqual([ID_A]); // partial: only one word matches anywhere
    expect(names('zzz qqq')).toEqual([]);
    expect(names('hollowborn')).toEqual([ID_A, ID_B]);
    expect(names('')).toEqual([ID_A, ID_B, ID_C]);
    expect(names('   ')).toEqual([ID_A, ID_B, ID_C]);
    expect(names('!!!')).toEqual([ID_A, ID_B, ID_C]);
    expect(names('zzz')).toEqual([]);
  });
  it('notes a cut list', async () => {
    const s = setup();
    s.answers.push(ok({ ...scriptsBody(), truncated: true }));
    s.control.openStart(inst());
    await flush();
    expect(texts(sheetView(s.control))).toContain('The list was cut, so some scripts are not shown.');
  });
  it('shows and filters a list of 2000 scripts', async () => {
    const s = setup();
    const many = Array.from({ length: 2000 }, (_, i) => ({ id: i.toString(16).padStart(16, '0'), name: `Script ${i}`, folder: `F${i % 20}`, askOptions: true }));
    s.answers.push(ok({ scripts: many, truncated: false }));
    s.control.openStart(inst());
    await flush();
    expect(byAction(sheetView(s.control), 'script-pick')).toHaveLength(2000);
    expect(texts(sheetView(s.control))).not.toContain('The list was cut, so some scripts are not shown.');
    s.control.setQuery('script 1999');
    expect(byAction(sheetView(s.control), 'script-pick').map((b) => b.attrs!['data-value'])).toEqual([(1999).toString(16).padStart(16, '0')]);
    s.control.setQuery('f19 1999');
    expect(byAction(sheetView(s.control), 'script-pick')).toHaveLength(1);
    s.control.setQuery('');
    expect(byAction(sheetView(s.control), 'script-pick')).toHaveLength(2000);
  });
  it('says when Skua has no scripts', async () => {
    const s = setup();
    s.answers.push(ok({ scripts: [], truncated: false }));
    s.control.openStart(inst());
    await flush();
    expect(texts(sheetView(s.control))).toContain("No scripts in Skua's scripts folder.");
  });
  it('shows a failed list near the Close button', async () => {
    const s = setup();
    s.answers.push(err(409, 'Skua is disconnected'));
    s.control.openStart(inst());
    await flush();
    const v = sheetView(s.control)!;
    expect(all(v).find((n) => n.attrs?.role === 'alert')!.text).toBe('This Skua is disconnected.');
  });
  it('starts at once, without asking for options, when the owner turned asking off', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_B);
    await flush();
    expect(s.names()).toEqual(['listScripts', 'start']);
    expect(s.calls[1]!.args).toEqual(['i-1', ID_B]);
    expect(s.control.sheet).toBeNull();
  });
  it('starts at once a script that has no options', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok(optionsBody({ defs: [], values: [], hasOptions: false })), ok({ hasOptions: false, firstRun: false, defs: [], values: [] }));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_A);
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'start']);
    expect(s.control.sheet).toBeNull();
  });
  it('ignores a second tap while the first is in flight (double-tap protection)', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_A);
    s.control.pick(ID_A);
    s.control.pick(ID_C);
    expect(s.names()).toEqual(['listScripts', 'getOptions']);
    const v = sheetView(s.control)!;
    expect(byAction(v, 'script-pick').every((b) => b.attrs!.disabled === '')).toBe(true);
    expect(texts(v)).toContain('Loading options...');
  });
  it('closes on close() and draws nothing', () => {
    const s = setup();
    s.control.openStart(inst());
    s.control.close();
    expect(sheetView(s.control)).toBeNull();
  });
});

describe('the options step', () => {
  async function openOptions(body: object = optionsBody()) {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok(body));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_A);
    await flush();
    return s;
  }
  const field = (v: VNode | null, i: number) => all(v).find((n) => n.attrs?.['data-action'] === 'opt' && n.attrs['data-value'] === String(i));

  it('opens the form for a script with known options, titled and pre-filled with the saved values', async () => {
    const s = await openOptions();
    const v = sheetView(s.control)!;
    expect(texts(v)).toContain('Farm options');
    expect(texts(v)).toContain('on Hero');
    expect(field(v, 0)!.attrs).toMatchObject({ type: 'checkbox', role: 'switch', checked: '' });
    expect(field(v, 1)!.attrs).toMatchObject({ type: 'text', inputmode: 'numeric', value: '200' });
    expect(field(v, 2)!.attrs).toMatchObject({ type: 'text', inputmode: 'decimal', value: '1.5' });
    expect(field(v, 3)!.attrs).toMatchObject({ type: 'text', value: 'Artix', maxlength: '200' });
    expect(field(v, 4)!.tag).toBe('select');
    const options = field(v, 4)!.children;
    expect(options.map((o) => o.text)).toEqual(['Fast Farm', 'Slow']);
    expect(options[1]!.attrs).toMatchObject({ value: 'Slow', selected: '' });
    expect(field(v, 5)).toBeUndefined();
    expect(texts(v)).toContain('x;y');
    expect(texts(v)).toContain('Blob label');
    expect(texts(v)).toContain('Sell help');
    expect(texts(v)).toContain('Start with these options');
    expect(texts(v)).toContain("Don't ask again for this script");
  });
  it('labels every field and ties its help to it', async () => {
    const v = sheetView((await openOptions()).control)!;
    for (const i of [0, 1, 2, 3, 4]) {
      const f = field(v, i)!;
      expect(f.attrs!.id).toBeTruthy();
      expect(all(v).some((n) => n.tag === 'label' && (n.attrs?.for === f.attrs!.id || n.children.includes(f)))).toBe(true);
      expect(f.attrs!['aria-describedby']).toBeTruthy();
    }
  });
  it('uses the default when a value is not saved', async () => {
    const s = await openOptions(optionsBody({ values: [] }));
    expect(field(sheetView(s.control), 1)!.attrs!.value).toBe('d');
  });
  it('a bool that is not True starts off', async () => {
    const s = await openOptions(optionsBody({ values: [{ category: 'Options', name: 'Sell', value: 'false' }] }));
    expect(field(sheetView(s.control), 0)!.attrs!.checked).toBeUndefined();
  });
  it('posts the edited options, then starts', async () => {
    const s = await openOptions();
    s.control.setValue(1, '300');
    s.control.setValue(0, 'false');
    s.control.setValue(2, '2,5');
    s.control.setValue(4, 'Fast Farm');
    s.answers.push(ok(optionsBody()), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'setOptions', 'start']);
    const [, id, values] = s.calls[2]!.args as [string, string, { name: string; value: string }[]];
    expect(id).toBe(ID_A);
    expect(Object.fromEntries(values.map((v) => [v.name, v.value]))).toEqual({ Sell: 'false', Loops: '300', Rate: '2.5', Player: 'Artix', Mode: 'Fast Farm', Blob: 'x;y' });
    expect(s.control.sheet).toBeNull();
  });
  it('refuses a number that is not one, near the button, and sends nothing', async () => {
    const s = await openOptions();
    s.control.setValue(1, '1.5');
    s.control.submitOptions();
    expect(s.names()).toEqual(['listScripts', 'getOptions']);
    expect(all(sheetView(s.control)).find((n) => n.attrs?.role === 'alert')!.text).toBe('Loops label: enter a whole number.');
    s.control.setValue(1, '7');
    s.control.setValue(2, 'abc');
    s.control.submitOptions();
    expect(all(sheetView(s.control)).find((n) => n.attrs?.role === 'alert')!.text).toBe('Rate label: enter a number such as 1.5.');
  });
  it("with Don't ask again on, saves the choice before the start", async () => {
    const s = await openOptions();
    s.control.setDontAsk(true);
    s.answers.push(ok(optionsBody()), ok({ id: ID_A, askOptions: false }), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'setOptions', 'setPrefs', 'start']);
    expect(s.calls[3]!.args).toEqual(['i-1', ID_A, false]);
  });
  it("then the next start of that script skips the form", async () => {
    const s = await openOptions();
    s.control.setDontAsk(true);
    s.answers.push(ok(optionsBody()), ok({ id: ID_A, askOptions: false }), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    const refreshed = { ...scriptsBody(), scripts: scriptsBody().scripts.map((x) => (x.id === ID_A ? { ...x, askOptions: false } : x)) };
    s.answers.push(ok(refreshed), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_A);
    await flush();
    expect(s.names().slice(5)).toEqual(['listScripts', 'start']);
  });
  it('leaves the choice alone when the switch did not change', async () => {
    const s = await openOptions(optionsBody({ askOptions: false }));
    expect(all(sheetView(s.control)).find((n) => n.attrs?.['data-action'] === 'dont-ask')!.attrs!.checked).toBe('');
    s.answers.push(ok(optionsBody()), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.names()).not.toContain('setPrefs');
  });
  it('turning the switch off again asks again', async () => {
    const s = await openOptions(optionsBody({ askOptions: false }));
    s.control.setDontAsk(false);
    s.answers.push(ok(optionsBody()), ok({ id: ID_A, askOptions: true }), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.calls[3]!.args).toEqual(['i-1', ID_A, true]);
  });
  it('keeps the form open with the plugin text when the options are refused', async () => {
    const s = await openOptions();
    s.answers.push(err(400, 'option Rate given twice'));
    s.control.submitOptions();
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'setOptions']);
    expect(s.control.sheet?.kind).toBe('options');
    expect(all(sheetView(s.control)).find((n) => n.attrs?.role === 'alert')!.text).toBe('Options not saved: option Rate given twice');
  });
  it('disables the button and ignores a second submit while the first is in flight', async () => {
    const s = await openOptions();
    s.control.submitOptions();
    s.control.submitOptions();
    expect(s.names().filter((n) => n === 'setOptions')).toHaveLength(1);
    const button = byAction(sheetView(s.control), 'options-start')[0]!;
    expect(button.attrs!.disabled).toBe('');
    expect(button.attrs!['aria-busy']).toBe('true');
  });
  it('shows saved values read-only when the definitions are not known yet, and starts without posting them', async () => {
    const s = await openOptions({ defs: null, values: [{ category: 'Options', name: 'Rate', value: '1,5' }], known: false, hasOptions: true, discovery: false, askOptions: true });
    const v = sheetView(s.control)!;
    expect(all(v).filter((n) => n.attrs?.['data-action'] === 'opt')).toHaveLength(0);
    expect(texts(v)).toContain('1,5');
    expect(texts(v)).toContain("SkuaLink does not know this script's options yet, so they cannot be changed here. They become editable after the next start.");
    s.answers.push(ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'start']);
  });
});

describe('the first run of a never-seen script', () => {
  it('starts as a discovery start, opens the form with the returned definitions, then starts normally', async () => {
    const s = setup();
    s.answers.push(
      ok(scriptsBody()),
      ok({ defs: null, values: [], known: false, hasOptions: false, discovery: true, askOptions: true }),
      ok({ hasOptions: true, firstRun: true, defs: [def('Player', 'string')], values: [] }),
    );
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_C);
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'start']);
    expect(s.control.sheet?.kind).toBe('options');
    const v = sheetView(s.control)!;
    expect(texts(v)).toContain('Butler options');
    expect(texts(v)).toContain('First start: Skua opened the script with its default options to learn them, then stopped it. Check them and start again.');
    s.control.setValue(0, 'Artix');
    s.answers.push(ok(optionsBody()), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'start', 'setOptions', 'start']);
    expect(s.calls[3]!.args[2]).toEqual([{ category: 'Options', name: 'Player', value: 'Artix' }]);
    expect(s.control.sheet).toBeNull();
  });
  it('also opens the form when a script started at once turns out to be a first run', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok({ hasOptions: true, firstRun: true, defs: [def('Player', 'string')], values: [] }));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_B);
    await flush();
    expect(s.control.sheet?.kind).toBe('options');
  });
});

describe('script titles, descriptions and tags', () => {
  const RUN_ALL = { id: ID_A, name: '0RunAll', folder: 'Hollowborn/HollowbornChaosEnvoy', title: 'Hollowborn Chaos Envy Run all', description: 'does all of hollowborn chaos envoy', tags: 'hollowborn chaos envoy, hollowborn, do all', askOptions: true };
  const PLAIN = { id: ID_B, name: 'Plain', folder: 'Hollowborn/HollowbornChaosEnvoy', askOptions: true };
  const SAME = { id: ID_C, name: 'Butler', folder: 'Other', title: 'butler', description: 'Waits.', askOptions: true };
  const open = async (scripts: unknown[]) => {
    const s = setup();
    s.answers.push(ok({ scripts, truncated: false }));
    s.control.openStart(inst());
    await flush();
    return s;
  };

  it('parses title, description and tags, cutting them like the PC and leaving null or non-strings out', () => {
    const r = parseScripts({
      scripts: [
        { id: ID_A, name: 'A', folder: '', title: 'T', description: 'D', tags: 'G' },
        { id: ID_B, name: 'B', folder: '', title: null, description: 5, tags: {} },
        { id: ID_C, name: 'C', folder: '', title: 't'.repeat(500), description: '', tags: '  ' },
      ],
    })!;
    expect(r.scripts).toHaveLength(3);
    expect(r.scripts[0]).toMatchObject({ title: 'T', description: 'D', tags: 'G' });
    expect(r.scripts[1]!.title).toBeUndefined();
    expect(r.scripts[1]!.description).toBeUndefined();
    expect(r.scripts[1]!.tags).toBeUndefined();
    expect(r.scripts[2]!.title!.length).toBe(300);
    expect(r.scripts[2]!.description).toBeUndefined();
    expect(r.scripts[2]!.tags).toBeUndefined();
  });

  it('shows the title as the main text, the description under it and the file name as a small hint', async () => {
    const s = await open([RUN_ALL]);
    const row = byAction(sheetView(s.control), 'script-pick')[0]!;
    const cls = (c: string) => all(row).filter((n) => n.className === c);
    expect(cls('script-name').map((n) => n.text)).toEqual(['Hollowborn Chaos Envy Run all']);
    expect(cls('script-desc').map((n) => n.text)).toEqual(['does all of hollowborn chaos envoy']);
    expect(cls('script-file').map((n) => n.text)).toEqual(['0RunAll.cs']);
  });

  it('shows the file name as the main text and no description or hint for a script without a header', async () => {
    const s = await open([PLAIN]);
    const row = byAction(sheetView(s.control), 'script-pick')[0]!;
    expect(all(row).filter((n) => n.className === 'script-name').map((n) => n.text)).toEqual(['Plain']);
    expect(all(row).some((n) => n.className === 'script-desc' || n.className === 'script-file')).toBe(false);
  });

  it('leaves out the file hint when the title is the file name (any case) and keeps a description without a title', async () => {
    const s = await open([SAME, { ...PLAIN, id: ID_A, description: 'Only text' }]);
    const rows = byAction(sheetView(s.control), 'script-pick');
    const butler = rows.find((r) => r.attrs!['data-value'] === ID_C)!;
    expect(all(butler).some((n) => n.className === 'script-file')).toBe(false);
    expect(all(butler).filter((n) => n.className === 'script-desc').map((n) => n.text)).toEqual(['Waits.']);
    const plain = rows.find((r) => r.attrs!['data-value'] === ID_A)!;
    expect(all(plain).filter((n) => n.className === 'script-name').map((n) => n.text)).toEqual(['Plain']);
    expect(all(plain).filter((n) => n.className === 'script-desc').map((n) => n.text)).toEqual(['Only text']);
  });

  it('keeps hostile text as plain text nodes, never markup', async () => {
    const evil = '<img src=x onerror=alert(1)>';
    const s = await open([{ ...PLAIN, title: evil, description: evil }]);
    const row = byAction(sheetView(s.control), 'script-pick')[0]!;
    expect(texts(row)).toContain(evil);
    expect(all(row).some((n) => n.tag === 'img')).toBe(false);
  });

  it('finds a script by title, description or tags as well as folder and name, every word required', () => {
    const list = [RUN_ALL, PLAIN, SAME] as ScriptItem[];
    const ids = (q: string) => scriptGroups(list, q).flatMap((g) => g.scripts.map((x) => x.id));
    expect(ids('run all')).toEqual([ID_A]);
    expect(ids('hollowborn envoy')).toEqual([ID_A, ID_B]);
    expect(ids('envy')).toEqual([ID_A]);
    expect(ids('does all')).toEqual([ID_A]);
    expect(ids('do all')).toEqual([ID_A]);
    expect(ids('waits')).toEqual([ID_C]);
    expect(ids('envy waits')).toEqual([ID_A, ID_C]); // no script has both: partial matches
    expect(ids('hollowborn plain')).toEqual([ID_B]);
  });

  describe('partial matches when no script has every word', () => {
    const ENVOY = RUN_ALL as ScriptItem;
    const rest: ScriptItem[] = [
      { id: ID_B, name: 'FarmGold', folder: 'Farm', title: 'Farm gold', askOptions: true },
      { id: ID_C, name: 'Butler', folder: 'Other', description: 'Waits for all', askOptions: true },
      { id: 'bbbbbbbbbbbbbbbb', name: 'FarmAllAround', folder: 'Zed', askOptions: true },
    ];
    const ids = (q: string, list: ScriptItem[] = [ENVOY, ...rest]) => searchScripts(list, q).groups.flatMap((g) => g.scripts.map((x) => x.id));
    it('finds the Envoy script for "farm all" and flags the result as partial', () => {
      const r = searchScripts([ENVOY, rest[0]!, rest[1]!], 'farm all');
      expect(r.partial).toBe(true);
      expect(r.groups.flatMap((g) => g.scripts.map((x) => x.id))).toContain(ID_A);
    });
    it('ranks by matched words, then keeps the list order', () => {
      expect(ids('farm gold all')).toEqual([ID_B, 'bbbbbbbbbbbbbbbb', ID_A, ID_C]);
    });
    it('lets an exact match suppress partial ones', () => {
      const r = searchScripts([ENVOY, ...rest], 'farm all');
      expect(r.partial).toBe(false);
      expect(r.groups.flatMap((g) => g.scripts.map((x) => x.id))).toEqual(['bbbbbbbbbbbbbbbb']);
    });
    it('leaves single words and empty searches as they were', () => {
      expect(searchScripts([ENVOY, ...rest], 'zzz')).toEqual({ groups: [], partial: false });
      expect(searchScripts([ENVOY, ...rest], 'farm').partial).toBe(false);
      expect(searchScripts([ENVOY, ...rest], '').partial).toBe(false);
      expect(ids('zzz qqq')).toEqual([]);
      expect(searchScripts([ENVOY, ...rest], 'zzz qqq').partial).toBe(false);
    });
    it('does not let a word match across two fields', () => {
      const one: ScriptItem[] = [{ id: ID_A, name: 'Alpha', folder: 'Beta', askOptions: true }];
      expect(ids('alphabeta', one)).toEqual([]);
    });
    it('counts a repeated word once', () => expect(ids('farm farm zzz')).not.toEqual([]));
    it('shows the note above the list only for partial matches', async () => {
      const s = await open([RUN_ALL, PLAIN, SAME]);
      s.control.setQuery('farm all');
      expect(texts(sheetView(s.control))).toContain('No script matches every word. Showing partial matches.');
      expect(byAction(sheetView(s.control), 'script-pick').map((b) => b.attrs!['data-value'])).toEqual([ID_A]);
      s.control.setQuery('run all');
      expect(texts(sheetView(s.control))).not.toContain('No script matches every word. Showing partial matches.');
      s.control.setQuery('zzz qqq');
      expect(texts(sheetView(s.control))).toContain('No script matches your search.');
    });
    it('keeps hostile text in the search as plain text', () => {
      expect(ids('<img onerror=alert(1)> all')).toEqual([ID_A, ID_C, 'bbbbbbbbbbbbbbbb']);
    });
    it('searches 2000 scripts quickly with the words made once per script', () => {
      const many = Array.from({ length: 2000 }, (_, i) => ({ id: i.toString(16).padStart(16, '0'), name: `Script${i}`, folder: `F${i % 20}`, title: `Title ${i}`, description: `Does thing ${i}`, tags: 'farm', askOptions: true }));
      const started = performance.now();
      for (let k = 0; k < 20; k++) expect(searchScripts(many, 'farm zzz').partial).toBe(true);
      expect(performance.now() - started).toBeLessThan(1500);
    });
  });

  it('shows and filters 2000 described scripts quickly', async () => {
    const many = Array.from({ length: 2000 }, (_, i) => ({
      id: i.toString(16).padStart(16, '0'), name: `Script${i}`, folder: `F${i % 20}`, title: `Title number ${i}`, description: `Farms thing ${i} and then sells it all`, tags: `tag${i}, farm`, askOptions: true,
    }));
    const s = await open(many);
    expect(byAction(sheetView(s.control), 'script-pick')).toHaveLength(2000);
    const started = performance.now();
    s.control.setQuery('thing 1999 sells');
    expect(byAction(sheetView(s.control), 'script-pick').map((b) => b.attrs!['data-value'])).toEqual([(1999).toString(16).padStart(16, '0')]);
    s.control.setQuery('farm');
    expect(byAction(sheetView(s.control), 'script-pick')).toHaveLength(2000);
    s.control.setQuery('');
    expect(performance.now() - started).toBeLessThan(1500);
  });

  it('names the script by its title in the options step and the stop confirmation, falling back to the file name', async () => {
    const s = await open([RUN_ALL, PLAIN]);
    s.answers.push(ok(optionsBody()));
    s.control.pick(ID_A);
    await flush();
    expect(texts(sheetView(s.control))).toContain('Hollowborn Chaos Envy Run all options');
  });

  it('names the running script by its title in the stop confirmation when the list is known', async () => {
    const s = setup();
    s.answers.push(ok({ scripts: [{ ...RUN_ALL, name: 'Farm' }], truncated: false }));
    const i = running();
    s.control.track(i);
    await flush();
    s.control.openStop(i);
    expect(texts(sheetView(s.control))).toContain('Stop Hollowborn Chaos Envy Run all on Hero?');
  });
});

describe('the stop confirmation', () => {
  it('names the character and the script, offers Stop script and Cancel', () => {
    const s = setup();
    s.control.openStop(running());
    const v = sheetView(s.control)!;
    expect(texts(v)).toContain('Stop Farm on Hero?');
    expect(texts(v)).toContain('The bot will run its own cleanup, which can take several seconds, and may travel to another map.');
    expect(byAction(v, 'stop-confirm')[0]!.text).toBe('Stop script');
    expect(byAction(v, 'sheet-close')[0]!.text).toBe('Cancel');
    expect(s.names()).toEqual([]);
  });
  it('stops on confirm and closes; a second tap is ignored', async () => {
    const s = setup();
    s.control.openStop(running());
    s.control.confirmStop();
    s.control.confirmStop();
    expect(s.names()).toEqual(['stop']);
    expect(byAction(sheetView(s.control), 'stop-confirm')[0]!.attrs!.disabled).toBe('');
    s.waiting.shift()!.resolve(ok({ ok: true }));
    await flush();
    expect(s.control.sheet).toBeNull();
  });
  it('keeps the sheet open with the error and lights the banner on 504', async () => {
    const s = setup();
    s.control.openStop(running());
    s.answers.push(err(504, 'Skua did not answer'));
    s.control.confirmStop();
    await flush();
    expect(all(sheetView(s.control)).find((n) => n.attrs?.role === 'alert')!.text).toBe('Skua did not answer. Check that Skua is open and the plugin is loaded.');
    expect(controlBanner(NOW, s.control.noAnswerUntil)).toEqual({ tone: 'warn', text: 'Skua did not answer. Check that Skua is open and the plugin is loaded.' });
    expect(controlBanner(NOW + 31_000, s.control.noAnswerUntil)).toBeNull();
  });
  it('after closing, the error stays near the card button until the next action', async () => {
    const s = setup();
    s.control.openStop(running());
    s.answers.push(err(409, 'busy'));
    s.control.confirmStop();
    await flush();
    s.control.close();
    expect(controlCardModel(running(), s.control, NOW)!.error).toBe('Another command is still running. Try again in a moment.');
    s.control.openStop(running());
    s.control.confirmStop();
    expect(controlCardModel(running(), s.control, NOW)!.error).toBeNull();
  });
  it('a 401 makes the page unpaired and closes the sheet', async () => {
    const s = setup();
    s.control.openStop(running());
    s.answers.push(err(401, 'not paired'));
    s.control.confirmStop();
    await flush();
    expect(s.unpaired).toHaveBeenCalledOnce();
    expect(s.control.sheet).toBeNull();
  });
  it('the card button is disabled with the reason while a command is in flight', () => {
    const s = setup();
    s.control.openStop(running());
    s.control.confirmStop();
    s.control.close();
    expect(controlCardModel(running(), s.control, NOW)).toMatchObject({ disabled: 'Waiting for Skua to answer...', pending: true });
  });
  it('does not open a sheet while a command for that Skua is in flight', () => {
    const s = setup();
    s.control.openStop(running());
    s.control.confirmStop();
    s.control.close();
    s.control.openStart(running());
    expect(s.control.sheet).toBeNull();
  });
});

describe('the ask-for-options switch on the running card', () => {
  it('appears once the running script is known (from the list) and posts the choice', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()));
    s.control.track(running());
    s.control.track(running());
    await flush();
    expect(s.names()).toEqual(['listScripts']);
    expect(controlCardModel(running(), s.control, NOW)!.ask).toEqual({ id: ID_A, checked: true });
    s.answers.push(ok({ id: ID_A, askOptions: false }));
    s.control.setAsk('i-1', ID_A, false);
    await flush();
    expect(s.calls[1]).toEqual({ name: 'setPrefs', args: ['i-1', ID_A, false] });
    expect(controlCardModel(running(), s.control, NOW)!.ask).toEqual({ id: ID_A, checked: false });
  });
  it('is hidden when the name is not unique in the list', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody([{ id: '1111111111111111', name: 'Farm', folder: 'Other', askOptions: true }])));
    s.control.track(running());
    await flush();
    expect(controlCardModel(running(), s.control, NOW)!.ask).toBeNull();
  });
  it('knows the script the phone started itself', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok({ hasOptions: false, firstRun: false, defs: null, values: [] }));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_B);
    await flush();
    expect(controlCardModel(running({ script: 'Quests' }), s.control, NOW)!.ask).toEqual({ id: ID_B, checked: false });
  });
  it('keeps the old value and shows the error when saving fails', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), err(504));
    s.control.track(running());
    await flush();
    s.control.setAsk('i-1', ID_A, false);
    await flush();
    const c = controlCardModel(running(), s.control, NOW)!;
    expect(c.ask!.checked).toBe(true);
    expect(c.error).toBe('Skua did not answer. Check that Skua is open and the plugin is loaded.');
  });
});

describe('the Control card in the Skua view', () => {
  const view = (i: SkuaInstance, control?: Control) => {
    const m: Model = { state: { snapshot: null, notices: [], skua: i, skuaInstances: [i] }, nowMs: NOW, showAll: false, hfOpen: false, installHint: null, controlCard: (x) => controlCard(x, control ?? setup().control, NOW) };
    return skuaView(m);
  };
  const card = (v: VNode) => all(v).find((n) => n.attrs?.['aria-labelledby'] === 'control-title')!;
  it('has a Start a script... button on an idle Skua', () => {
    const c = card(view(inst()));
    expect(byAction(c, 'control-start')[0]!.text).toBe('Start a script...');
    expect(byAction(c, 'control-start')[0]!.attrs!.disabled).toBeUndefined();
    expect(texts(c)).toContain('Control');
  });
  it('has a Stop script button and the switch while running', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()));
    s.control.track(running());
    await flush();
    const c = card(view(running(), s.control));
    expect(byAction(c, 'control-stop')[0]!.text).toBe('Stop script');
    const sw = byAction(c, 'ask-options')[0]!;
    expect(sw.attrs).toMatchObject({ type: 'checkbox', role: 'switch', checked: '', 'data-value': ID_A });
    expect(texts(c)).toContain('Ask for options when starting this script');
    expect(texts(c)).toContain('Option values can be changed only while the script is stopped.');
  });
  it('disables Start with the reason tied to the button while settling', () => {
    const c = card(view(inst({ control: 'settling', settleSecondsLeft: 3 })));
    const b = byAction(c, 'control-start')[0]!;
    expect(b.attrs!.disabled).toBe('');
    const reason = all(c).find((n) => n.attrs?.id === b.attrs!['aria-describedby'])!;
    expect(reason.text).toContain('about 3 s');
    expect(all(c).some((n) => n.attrs?.role === 'progressbar')).toBe(true);
  });
  it('marks the loud waiting state with a class and strong text', () => {
    const c = card(view(inst({ control: 'awaitingOptions', optionsStuck: true })));
    expect(c.className).toBe('control-waiting');
    expect(all(c).find((n) => n.tag === 'strong')!.text).toBe("Close the script's options window on the computer.");
    expect(byAction(c, 'control-stop')).toHaveLength(1);
  });
  it('shows the last error in an alert near the button', async () => {
    const s = setup();
    s.answers.push(err(400, 'already running'));
    s.control.openStop(running());
    s.control.confirmStop();
    await flush();
    s.control.close();
    const alert = all(card(view(running(), s.control))).find((n) => n.attrs?.role === 'alert')!;
    expect(alert.text).toBe('Could not stop: a script is already running.');
  });
  it('is not shown without a controller (an older page model) nor for an older app without instances', () => {
    const m: Model = { state: { snapshot: null, notices: [], skua: inst(), skuaInstances: [inst()] }, nowMs: NOW, showAll: false, hfOpen: false, installHint: null };
    expect(all(skuaView(m)).some((n) => n.attrs?.['aria-labelledby'] === 'control-title')).toBe(false);
    const old: Model = { ...m, state: { snapshot: null, notices: [], skua: inst() }, controlCard: (x) => controlCard(x, setup().control, NOW) };
    expect(all(skuaView(old)).some((n) => n.attrs?.['aria-labelledby'] === 'control-title')).toBe(false);
  });
});

describe('sheets for the keyboard', () => {
  it('every sheet has a title the dialog is labelled by, and one control to focus first', async () => {
    const s = setup();
    s.control.openStop(running());
    const stop = sheetView(s.control)!;
    expect(all(stop).find((n) => n.attrs?.id === 'sheet-title')).toBeTruthy();
    expect(all(stop).filter((n) => n.attrs?.autofocus === '').map((n) => n.attrs!['data-action'])).toEqual(['sheet-close']);
    s.control.close();
    s.answers.push(ok(scriptsBody()));
    s.control.openStart(inst());
    await flush();
    const list = sheetView(s.control)!;
    expect(all(list).filter((n) => n.attrs?.autofocus === '').map((n) => n.attrs!.id)).toEqual(['script-search']);
    const search = all(list).find((n) => n.attrs?.id === 'script-search')!;
    expect(search.attrs).toMatchObject({ type: 'search', 'aria-label': 'Search scripts', placeholder: 'Search scripts' });
  });
  it('the options form focuses its title, the list is made of real buttons', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok(optionsBody()));
    s.control.openStart(inst());
    await flush();
    expect(byAction(sheetView(s.control), 'script-pick').every((b) => b.tag === 'button' && b.attrs!.type === 'button')).toBe(true);
    s.control.pick(ID_A);
    await flush();
    const v = sheetView(s.control)!;
    const first = all(v).filter((n) => n.attrs?.autofocus === '');
    expect(first).toHaveLength(1);
    expect(first[0]!.attrs!.id).toBe('sheet-title');
    expect(first[0]!.attrs!.tabindex).toBe('-1');
  });
  it('Escape closes through close(), whatever the sheet', () => {
    const s = setup();
    s.control.openStop(running());
    s.control.close();
    expect(s.control.sheet).toBeNull();
    expect(s.changed).toHaveBeenCalled();
  });
});

describe('hostile text', () => {
  it('stays text in every sheet and on the card', async () => {
    const evil = '<img src=x onerror=alert(1)>';
    const s = setup();
    s.answers.push(ok({ scripts: [{ id: ID_A, name: evil, folder: evil, askOptions: true }] }), ok(optionsBody({ defs: [def(evil, 'enum', { displayName: evil, description: evil, choices: [evil] })], values: [{ category: 'Options', name: evil, value: evil }] })));
    const i = inst({ script: evil, character: evil }, { label: evil });
    s.control.openStart(i);
    await flush();
    const nodes = [...all(sheetView(s.control))];
    s.control.pick(ID_A);
    await flush();
    nodes.push(...all(sheetView(s.control)));
    s.control.close();
    s.control.openStop(i);
    nodes.push(...all(sheetView(s.control)));
    for (const n of nodes) {
      expect(['img', 'script']).not.toContain(n.tag);
      for (const k of Object.keys(n.attrs ?? {})) expect(k.startsWith('on') || k === 'style').toBe(false);
    }
    expect(nodes.some((n) => n.text === `Stop ${evil} on ${evil}?`)).toBe(true);
  });
  it('no source module uses innerHTML', () => {
    for (const f of ['control.ts', 'controlViews.ts', 'views.ts', 'main.ts', 'api.ts']) {
      expect(readFileSync(new URL(`../src/${f}`, import.meta.url), 'utf8')).not.toMatch(/innerHTML|outerHTML|insertAdjacentHTML/);
    }
  });
});

describe('control styles', () => {
  // The control rules live in control.css (loaded with the control chunk); the generic button rules stay in app.css.
  const css = ['app.css', 'control.css'].map((f) => readFileSync(new URL(`../src/styles/${f}`, import.meta.url), 'utf8')).join('\n');
  const rule = (sel: string) => css.match(new RegExp(`(?:^|\\n)${sel}\\s*\\{([^}]*)\\}`))?.[1] ?? '';
  it('gives the loud waiting card an accent border thicker than a card edge', () => expect(rule('\\.control-waiting')).toMatch(/border:\s*3px solid var\(--accent\)/));
  it('draws the danger button with its own text colour', () => {
    expect(rule('\\.btn-danger')).toMatch(/background:\s*var\(--danger\)/);
    expect(rule('\\.btn-danger')).toMatch(/color:\s*var\(--on-danger\)/);
  });
  it('shows disabled buttons without opacity, and keeps every new button 3rem tall', () => {
    expect(css).toMatch(/button:disabled\s*\{[^}]*cursor:\s*not-allowed/);
    expect(css).not.toMatch(/opacity/);
    for (const sel of ['\\.btn-danger', '\\.btn-quiet', '\\.script-btn']) expect(rule(sel)).not.toMatch(/min-height:\s*[^3]/);
  });
  it('keeps the sheet out of position: fixed (the native dialog places it) and animates it only without reduced motion', () => {
    expect(css).toMatch(/dialog\.sheet\s*\{/);
    expect(css).toMatch(/dialog\.sheet::backdrop\s*\{/);
    expect(css).not.toMatch(/position:\s*fixed/);
    const outside = css.replace(/@media \(prefers-reduced-motion: no-preference\)\s*\{[\s\S]*?\n\}/g, '');
    expect(outside).not.toMatch(/animation:/);
    expect(css).toMatch(/@media \(prefers-reduced-motion: no-preference\)\s*\{[\s\S]*animation:/);
  });
  it('wraps server text in the sheets and the card', () => {
    for (const sel of ['\\.script-name', '\\.control-error', '\\.field-label', '\\.sheet h3']) expect(rule(sel)).toMatch(/overflow-wrap:\s*anywhere/);
  });
});

describe('contrast of the control styles', () => {
  const tokens = readFileSync(new URL('../src/styles/tokens.css', import.meta.url), 'utf8');
  // The control rules live in control.css (loaded with the control chunk); the generic button rules stay in app.css.
  const css = ['app.css', 'control.css'].map((f) => readFileSync(new URL(`../src/styles/${f}`, import.meta.url), 'utf8')).join('\n');
  const block = (t: string) => Object.fromEntries([...t.matchAll(/--([a-z-]+):\s*(#[0-9A-Fa-f]{6})\s*;/g)].map((m) => [m[1]!, m[2]!]));
  const body = (re: RegExp) => tokens.match(re)![1]!;
  const light = block(body(/^:root\s*\{([^}]*)\}/m));
  const sysDark = { ...light, ...block(body(/@media \(prefers-color-scheme: dark\)\s*\{\s*:root:not\(\[data-theme='light'\]\)\s*\{([^}]*)\}/)) };
  const forcedDark = { ...light, ...block(body(/^:root\[data-theme='dark'\]\s*\{([^}]*)\}/m)) };
  const lum = (hex: string) => {
    const [r, g, b] = [1, 3, 5].map((i) => parseInt(hex.slice(i, i + 2), 16) / 255).map((v) => (v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4)) as [number, number, number];
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
  };
  const ratio = (a: string, b: string) => {
    const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x) as [number, number];
    return (hi + 0.05) / (lo + 0.05);
  };
  const decl = (sel: string, prop: string) => css.match(new RegExp(`(?:^|\\n)${sel}\\s*\\{[^}]*(?:^|[;{\\s])${prop}:\\s*var\\(--([a-z-]+)\\)`))?.[1];
  // [label, selector, minimum ratio, background when the rule sets none]
  const pairs: [string, string, number, string][] = [
    ['danger button', '\\.btn-danger', 4.5, ''],
    ['quiet button', '\\.btn-quiet', 7, ''],
    ['script button', '\\.script-btn', 7, ''],
    ['error text on a card', '\\.control-error', 4.5, 'surface'],
    ['sheet text', 'dialog\\.sheet', 7, ''],
    ['folder heading in the sheet', '\\.sheet h3', 4.5, 'bg'],
    ['field input', '\\.field-input', 7, ''],
  ];
  describe.each([['light', light], ['system dark', sysDark], ['forced dark', forcedDark], ['forced light on a dark system', light]] as const)('%s', (_n, t) => {
    it.each(pairs)('%s reads at its ratio', (_label, sel, min, fallback) => {
      const fg = decl(sel, 'color')!;
      expect(fg, `${sel} color`).toBeTruthy();
      const bg = fallback || decl(sel, 'background')!;
      expect(bg, `${sel} background`).toBeTruthy();
      expect(ratio(t[fg]!, t[bg]!)).toBeGreaterThanOrEqual(min);
    });
    it('the danger fill stands out from the card as a shape (3:1)', () => expect(ratio(t.danger!, t.surface!)).toBeGreaterThanOrEqual(3));
  });
});

describe('fix round 1', () => {
  const field = (v: VNode | null, i: number) => all(v).find((n) => n.attrs?.['data-action'] === 'opt' && n.attrs['data-value'] === String(i));
  it('an enum with no saved value and no default takes its first choice, shown and sent alike', async () => {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok(optionsBody({ defs: [def('Mode', 'enum', { default: '', choices: ['Fast Farm', 'Slow'] })], values: [] })));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_A);
    await flush();
    const select = field(sheetView(s.control), 0)!;
    expect(select.children.map((o) => [o.text, o.attrs!.selected])).toEqual([['Fast Farm', ''], ['Slow', undefined]]);
    s.answers.push(ok(optionsBody()), ok({ hasOptions: true, firstRun: false, defs: null, values: [] }));
    s.control.submitOptions();
    await flush();
    expect(s.calls[2]!.args[2]).toEqual([{ category: 'Options', name: 'Mode', value: 'Fast Farm' }]);
  });

  async function firstRunForm() {
    const s = setup();
    s.answers.push(ok(scriptsBody()), ok({ hasOptions: true, firstRun: true, defs: [def('Player', 'string')], values: [] }));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_B);
    await flush();
    return s;
  }
  it.each([
    [{ control: 'settling', settleSecondsLeft: 6 }, 'The script stopped. Waiting for the bot to finish going back (about 6 s) before another script can start.'],
    [{ control: 'settling' }, 'The script stopped. Waiting for the bot to finish going back before another script can start.'],
    [{ control: 'stopping' }, 'Wait until the script has stopped.'],
  ])('the first-run form disables Start with the reason while the Skua is %j', async (st, reason) => {
    const s = await firstRunForm();
    const v = sheetView(s.control, inst(st))!;
    const button = byAction(v, 'options-start')[0]!;
    expect(button.attrs!.disabled).toBe('');
    const why = all(v).find((n) => n.attrs?.id === button.attrs!['aria-describedby'])!;
    expect(why.text).toBe(reason);
  });
  it('the first-run form enables Start once the Skua is idle', async () => {
    const s = await firstRunForm();
    const button = byAction(sheetView(s.control, inst()), 'options-start')[0]!;
    expect(button.attrs!.disabled).toBeUndefined();
    expect(button.attrs!['aria-describedby']).toBeUndefined();
  });
  it('a start on one Skua does not touch the list sheet of another', async () => {
    const s = setup();
    const other = inst({}, { instanceId: 'i-2', label: 'Mule' });
    s.answers.push(ok(scriptsBody()));
    s.control.openStart(inst());
    await flush();
    s.control.pick(ID_A); // getOptions for i-1 is now waiting
    s.control.close();
    s.answers.push(ok(scriptsBody()));
    s.control.openStart(other);
    await flush();
    s.answers.push(ok({ hasOptions: false, firstRun: false, defs: [], values: [] }));
    s.waiting.shift()!.resolve(ok(optionsBody({ defs: [], values: [], hasOptions: false })));
    await flush();
    expect(s.names()).toEqual(['listScripts', 'getOptions', 'listScripts', 'start']);
    const sheet = s.control.sheet!;
    expect(sheet.kind === 'scripts' && sheet.instanceId === 'i-2' && sheet.pickText).toBe(null);
    expect(texts(sheetView(s.control))).not.toContain('Starting...');
  });
});
