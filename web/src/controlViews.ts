import { controlCardModel, scriptGroups, startBlocked, type Control, type OptionDef, type Sheet } from './control';
import { t } from './i18n';
import type { SkuaInstance } from './types';
import { bar, h, type VNode } from './views';

// The Control card and the sheets, loaded with the Skua tab: the characters view does not need them.

const flag = (on: boolean): Record<string, string> => (on ? { disabled: '', 'aria-busy': 'true' } : {});

/** A switch: a checkbox announced as a switch, inside its label. */
function toggle(label: string, attrs: Record<string, string>, on: boolean): VNode {
  return h('label', { className: 'field-switch' }, h('input', { attrs: { type: 'checkbox', role: 'switch', ...attrs, ...(on ? { checked: '' } : {}) } }), h('span', { text: label }));
}

const alert = (text: string | null): VNode | null => (text ? h('p', { className: 'control-error', text, attrs: { role: 'alert' } }) : null);

/** The Control card: what the selected Skua does, its one button, and the error of the last command. */
export function controlCard(i: SkuaInstance, control: Control, nowMs: number): VNode | null {
  const c = controlCardModel(i, control, nowMs);
  if (!c) return null;
  const button = c.action === 'stop'
    ? h('button', { className: 'btn-danger', text: t('control.stop'), attrs: { type: 'button', 'data-action': 'control-stop' } })
    : c.action === 'start'
      ? h('button', { text: t('control.start'), attrs: { type: 'button', 'data-action': 'control-start' } })
      : null;
  if (button && c.disabled) Object.assign(button.attrs!, { ...flag(c.pending), disabled: '', 'aria-describedby': 'control-reason' });
  return h(
    'section',
    { className: c.waiting ? 'control-waiting' : 'card', attrs: { 'aria-labelledby': 'control-title' } },
    h('h2', { text: t('control.title'), attrs: { id: 'control-title', tabindex: '-1' } }),
    h(
      'p',
      { className: 'status' },
      h('span', { className: 'dot', attrs: { 'data-kind': c.tone === 'run' ? 'live' : c.tone === 'warn' ? 'idle' : 'saved', 'aria-hidden': 'true' } }),
      h('span', { text: c.state }),
      c.since ? h('span', { className: 'muted', text: c.since }) : null,
    ),
    c.script ? h('p', { className: 'skua-line', text: c.script }) : null,
    c.strong ? h('p', { className: 'control-strong' }, h('strong', { text: c.strong })) : null,
    c.settle === null ? null : bar(t('control.settleBar'), c.settle),
    c.hint ? h('p', { className: 'muted hint', text: c.hint }) : null,
    c.disabled && button ? h('p', { className: 'muted hint', text: c.disabled, attrs: { id: 'control-reason' } }) : null,
    button,
    alert(c.error),
    c.ask ? toggle(t('control.askOptions'), { 'data-action': 'ask-options', 'data-value': c.ask.id, ...(c.pending ? { disabled: '' } : {}) }, c.ask.checked) : null,
  );
}

const title = (text: string, focus = false): VNode => h('h2', { text, attrs: { id: 'sheet-title', ...(focus ? { tabindex: '-1', autofocus: '' } : {}) } });
const quiet = (text: string, focus = false): VNode => h('button', { className: 'btn-quiet', text, attrs: { type: 'button', 'data-action': 'sheet-close', ...(focus ? { autofocus: '' } : {}) } });

/** One field of the options step, by type: a switch, a number or text field, a choice, or a read-only value. */
function optionField(d: OptionDef, value: string, index: number, editable: boolean): VNode {
  const id = 'opt-' + index;
  const help = d.description ? h('p', { className: 'muted', text: d.description, attrs: { id: id + '-help' } }) : null;
  const label = d.displayName || d.name;
  const attrs: Record<string, string> = { id, 'data-action': 'opt', 'data-value': String(index), ...(help ? { 'aria-describedby': id + '-help' } : {}) };
  if (!editable || d.type === 'other') {
    return h('li', {}, h('p', { className: 'field-label', text: label }), h('p', { className: 'field-value', text: value }), h('p', { className: 'muted', text: t('control.readOnly') }), help);
  }
  if (d.type === 'bool') return h('li', {}, toggle(label, attrs, value.toLowerCase() === 'true'), help);
  const labelNode = h('label', { className: 'field-label', text: label, attrs: { for: id } });
  if (d.type === 'enum') {
    const choices = d.choices?.includes(value) || !value ? (d.choices ?? []) : [value, ...(d.choices ?? [])];
    const select = h('select', { className: 'field-input', attrs }, ...choices.map((c) => h('option', { text: c, attrs: { value: c, ...(c === value ? { selected: '' } : {}) } })));
    return h('li', {}, labelNode, select, help);
  }
  const mode: Record<string, string> = d.type === 'int' ? { inputmode: 'numeric' } : d.type === 'double' ? { inputmode: 'decimal' } : {};
  return h('li', {}, labelNode, h('input', { className: 'field-input', attrs: { ...attrs, type: 'text', value, maxlength: '200', autocomplete: 'off', ...mode } }), help);
}

function stopSheet(sheet: Extract<Sheet, { kind: 'stop' }>, pending: boolean): VNode[] {
  return [
    title(t('control.stopTitle', { script: sheet.script, character: sheet.character })),
    h('p', { className: 'muted', text: t('control.stopBody') }),
    alert(sheet.error),
    h('button', { className: 'btn-danger', text: t('control.stop'), attrs: { type: 'button', 'data-action': 'stop-confirm', ...flag(pending) } }),
    quiet(t('control.cancel'), true),
  ].filter((n): n is VNode => n !== null);
}

function scriptsSheet(sheet: Extract<Sheet, { kind: 'scripts' }>, pending: boolean): VNode[] {
  const groups = scriptGroups(sheet.scripts, sheet.query);
  // Up to 2000 rows: the words and the flag are made once, not per row.
  const startItem = t('control.startItem');
  const busy = flag(pending);
  const status = sheet.loading
    ? t('control.loadingScripts')
    : sheet.error ? null : sheet.scripts.length === 0 ? t('control.noScripts') : groups.length === 0 ? t('control.noMatch') : null;
  return [
    title(t('control.scriptsTitle', { character: sheet.character })),
    h('input', {
      className: 'field-input',
      attrs: {
        id: 'script-search', type: 'search', 'data-action': 'script-search', value: sheet.query, autocomplete: 'off', autofocus: '', enterkeyhint: 'search',
        'aria-label': t('control.search'), placeholder: t('control.search'),
      },
    }),
    status ? h('p', { className: 'muted', text: status, attrs: { role: 'status' } }) : null,
    ...groups.map((g) =>
      h(
        'section',
        {},
        h('h3', { text: g.folder || t('control.rootFolder') }),
        h(
          'ul',
          { className: 'script-list' },
          ...g.scripts.map((s) =>
            h(
              'li',
              {},
              h(
                'button',
                { className: 'script-btn', attrs: { type: 'button', 'data-action': 'script-pick', 'data-value': s.id, ...busy } },
                h(
                  'span',
                  { className: 'script-text' },
                  h('span', { className: 'script-name', text: s.title || s.name }),
                  s.description ? h('span', { className: 'script-desc', text: s.description }) : null,
                  s.title && s.title.toLowerCase() !== s.name.toLowerCase() ? h('span', { className: 'script-file', text: t('control.fileHint', { file: s.name }) }) : null,
                ),
                h('span', { className: 'muted', text: sheet.pick === s.id && sheet.pickText ? t(sheet.pickText) : startItem }),
              ),
            ),
          ),
        ),
      ),
    ),
    sheet.truncated ? h('p', { className: 'muted', text: t('control.truncated') }) : null,
    h('p', { className: 'muted', text: t('control.listHint') }),
    alert(sheet.error),
    quiet(t('control.close')),
  ].filter((n): n is VNode => n !== null);
}

function optionsSheet(sheet: Extract<Sheet, { kind: 'options' }>, pending: boolean, blocked: string | null): VNode[] {
  const start = h('button', { text: t('control.startWithOptions'), attrs: { type: 'button', 'data-action': 'options-start', ...flag(pending) } });
  if (blocked) Object.assign(start.attrs!, { disabled: '', 'aria-describedby': 'options-reason' });
  return [
    title(t('control.optionsTitle', { script: sheet.script.title || sheet.script.name }), true),
    h('p', { className: 'muted', text: t('control.optionsOn', { character: sheet.character }) }),
    sheet.firstRun ? h('p', { className: 'note', text: t('control.firstRun') }) : null,
    sheet.editable ? null : h('p', { className: 'note', text: t('control.unknownDefs') }),
    h('ul', { className: 'fields' }, ...sheet.defs.map((d, i) => optionField(d, sheet.values[i] ?? '', i, sheet.editable))),
    sheet.editable ? h('p', { className: 'muted', text: t('control.optionsHint') }) : null,
    toggle(t('control.dontAsk'), { 'data-action': 'dont-ask' }, sheet.dontAsk),
    alert(sheet.error),
    blocked ? h('p', { className: 'muted', text: blocked, attrs: { id: 'options-reason' } }) : null,
    start,
    quiet(t('control.cancel')),
  ].filter((n): n is VNode => n !== null);
}

/**
 * The open sheet (stop confirmation, script list or options step), drawn by the page into its dialog; null when none is open.
 * `live` is the sheet's Skua as the last state shows it: while it stops or settles, the options step cannot start.
 */
export function sheetView(control: Control, live?: SkuaInstance | null): VNode | null {
  const sheet = control.sheet;
  if (!sheet) return null;
  const pending = control.isPending(sheet.instanceId);
  const children = sheet.kind === 'stop' ? stopSheet(sheet, pending) : sheet.kind === 'scripts' ? scriptsSheet(sheet, pending) : optionsSheet(sheet, pending, startBlocked(live));
  return h('div', { className: 'sheet-body' }, ...children);
}

export { Control, controlBanner } from './control';
