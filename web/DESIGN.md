---
name: Questkeeper phone page
description: A calm parchment-and-plum glance page that says whether the characters are fine.
colors:
  parchment: "#FEF0C2"
  paper: "#FEF7E1"
  band: "#F3E1AB"
  gold-edge: "#9A7410"
  ink: "#16151A"
  muted-brown: "#5C4A1F"
  link-red: "#800000"
  danger-red: "#9B1C14"
  accent-orange: "#F69A07"
  on-accent: "#2A1208"
  ok-green: "#197419"
  on-ok: "#C7F6B6"
  bar-red: "#800000"
  top-plum: "#3B0C30"
  on-top: "#FFFDF7"
  night-bg: "#1A0B17"
  night-surface: "#2A1124"
  night-raised: "#3B1A33"
  night-border: "#94741A"
  night-text: "#FEF0C2"
  night-muted: "#D9C99A"
  night-link: "#FAB84F"
  night-danger: "#FF9A8F"
  night-bar: "#F69A07"
typography:
  headline:
    fontFamily: "Georgia, 'Times New Roman', serif"
    fontSize: "1.25rem"
    lineHeight: 1.2
  title:
    fontFamily: "Georgia, 'Times New Roman', serif"
    fontSize: "1.125rem"
    lineHeight: 1.2
  body:
    fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.5
  label:
    fontFamily: "system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif"
    fontSize: "0.9375rem"
    fontWeight: 400
    lineHeight: 1.5
rounded:
  bar: "5px"
  button: "8px"
  card: "12px"
spacing:
  xs: "0.25rem"
  sm: "0.5rem"
  md: "0.75rem"
  lg: "1rem"
  xl: "2rem"
components:
  card:
    backgroundColor: "{colors.paper}"
    textColor: "{colors.ink}"
    rounded: "{rounded.card}"
    padding: "1rem"
  button:
    backgroundColor: "{colors.accent-orange}"
    textColor: "{colors.on-accent}"
    rounded: "{rounded.button}"
    padding: "0 1rem"
    height: "3rem"
  banner:
    backgroundColor: "{colors.band}"
    textColor: "{colors.ink}"
    padding: "0.75rem 1rem"
  top-bar:
    backgroundColor: "{colors.top-plum}"
    textColor: "{colors.on-top}"
    padding: "0.75rem 1rem"
  progress-bar:
    backgroundColor: "{colors.band}"
    rounded: "{rounded.bar}"
    height: "0.625rem"
---

# Design System: Questkeeper phone page

Derived from the shipped page (`web/src/styles/tokens.css`, `web/src/styles/app.css`, `web/src/views.ts`, `web/index.html`). It describes what is built; nothing here is planned. The page is served by Questkeeper itself on the player's PC (Settings, Phone access) and reaches the phone through Tailscale.

## Overview

**Creative North Star: "The Parchment Field Report"**

A short report on paper, read in a few seconds on a phone: a plum title strip, then cream cards with a gold edge, ink-coloured text and one orange action. It uses the AQW palette of the Questkeeper desktop app so the page and the app read as one product. It is warm, calm and plain: no gradients, no shadows, no web fonts, no framework.

The page is built for glances. A character is one row (name, level, gold, a status line); everything deeper sits one tap away. The system is flat, and depth comes only from tone (page, paper card, darker band) and a 1px gold edge. A dark variant uses the same hues on a plum-black ground and follows the system setting.

**Key Characteristics:**
- One column, at most 34rem wide, centred.
- Tonal layering and a gold hairline instead of shadows.
- Status is a dot shape plus words, never colour alone.
- Tap targets are at least 3rem (48px) tall.
- System fonts only; a serif for headings, a sans for everything else.

## Colors

A cream-and-plum parchment palette sampled from the AQW site, with a dark variant built from the same hues. Contrast is enforced by `web/test/tokens.test.ts`.

### Primary
- **Link Red** (#800000, `--link`; #FAB84F in dark): links and the focus ring.
- **Accent Orange** (#F69A07, `--accent`): fill of buttons and the "idle" status dot, and text selection. Only ever a fill with On Accent text (#2A1208); orange text on cream is 1.9:1.

### Secondary
- **Top Plum** (#3B0C30, `--top`) with **On Top** (#FFFDF7, `--on-top`): the title strip. Same in both schemes.
- **OK Green** (#197419, `--ok`) with On OK (#C7F6B6, `--on-ok`): the "live" status dot fill.

### Neutral
- **Parchment** (#FEF0C2, `--bg`; #1A0B17 in dark): page background.
- **Paper** (#FEF7E1, `--surface`; #2A1124 dark): card background.
- **Band** (#F3E1AB, `--raised`; #3B1A33 dark): banner, progress track, row separators.
- **Gold Edge** (#9A7410, `--border`; #94741A dark): every border.
- **Ink** (#16151A, `--text`; #FEF0C2 dark): body text.
- **Muted Brown** (#5C4A1F, `--muted`; #D9C99A dark): secondary text.
- **Danger Red** (#9B1C14, `--danger`; #FF9A8F dark): kept in the tokens with its contrast test; no component uses it at present.
- **Bar Red** (#800000, `--bar`; #F69A07 dark): progress fill.

### Named Rules
**The Fill-Only Orange Rule.** Accent orange is a background with dark text on it, never a text or icon colour on cream.
**The Words-Too Rule.** A colour never carries a state alone; every state has text, and the dots differ by fill (solid green, solid orange, hollow).

## Typography

**Display/Heading Font:** Georgia (with Times New Roman, serif)
**Body Font:** system-ui (with -apple-system, Segoe UI, Roboto, sans-serif)

**Character:** A bookish serif for the few headings over a plain native sans, so the page loads nothing and respects the phone's own text size (sizes are in rem).

### Hierarchy
- **Headline** (serif, 1.25rem, 1.2): the app name in the top strip (`h1`).
- **Title** (serif, 1.125rem, 1.2): card headings (`h2`: Characters, Recent notices, Factions, Hard Farms), wrap anywhere.
- **Body** (sans, 400, 1rem, 1.5): default text.
- **Label** (sans, 0.9375rem): muted lines, level, status, notes. Weight 600 marks names, notice titles, the banner, button text and `summary`.
- Numbers use tabular figures (`gold`, the figures list).

### Named Rules
**The Native Type Rule.** No web fonts; headings are Georgia, all else is the system sans.

## Layout

A single centred column: `max-width: 34rem`, 1rem side padding, 1rem gap between cards (`main > div` is a grid). Bottom padding adds the device safe-area inset, and the top strip adds the top inset. The banner aligns its text with the column edge. Character rows are a two-column grid (name and level on the left, gold on the right) with a full-width status line below. The character view lists figures as a label/value grid (value right-aligned). Spacing runs on 0.25 / 0.5 / 0.75 / 1 / 2 rem. There are no breakpoints; the column simply stops growing.

## Elevation & Depth

Flat. There are no shadows anywhere. Depth is tone: Parchment page, Paper cards, Band for the banner and the progress track, plus a 1px Gold Edge around cards, buttons and the track.

### Named Rules
**The Flat-By-Default Rule.** Add no box-shadow. Separation comes from tone and the gold hairline.

## Shapes

Soft but modest: cards 12px (0.75rem), buttons 8px (0.5rem), the progress bar 5px, status dots fully round (0.75rem circles with a 2px gold ring). Row and notice separators are 1px Band lines, not boxes.

## Components

### Top strip
Plum band with the serif app name in On Top, inner column 34rem, top padding includes the safe-area inset.

### Banner
Full-width Band strip, 600 weight, 1px gold bottom border, `role="status"`. It has one tone (`data-tone="warn"`) and appears only while the live event stream to the PC is closed: "Old data - last update 3 min ago. Reconnecting..." (the age from the time the last state arrived, which is kept with the stored state, so it survives a reload), or "Old data - reconnecting..." when that time is not known. It is hidden while the stream is open and on the "Not paired" card.

### Fixed header
The title strip and the banner sit together in one `<div class="chrome">` that is `position: sticky; top: 0` with a z-index above the content, so they stay in view while scrolling. It is deliberately not `position: fixed` with a body offset: the banner has a variable height and appears and disappears, and sticky keeps the layout correct with no arithmetic. A hidden banner is `display: none`, so it leaves no gap or border. The strip keeps the `env(safe-area-inset-top)` padding for the iPhone notch, and `html` has a `scroll-padding-top` of the header's measured height plus 0.5rem, so a focused or anchored element is not left under the header. `main.ts` measures `.chrome` (at startup and through a `ResizeObserver`, so it follows the banner appearing) and publishes it as `--chrome-h`; before the first measurement, or without `ResizeObserver`, the padding falls back to the strip alone (3rem plus the safe-area inset).

### Appearance switch
A footer on every view (the list, a character, Skua and the "Not paired" card; only the draw-failed card has none) with the label "Appearance" and three toggle buttons, System / Light / Dark. Each is at least 3rem tall, has `aria-pressed`, `data-action="theme"` and `data-value`; the pressed one is Accent Orange with a thicker border (state is never colour alone). The choice is kept in `localStorage` under `qkp-theme-v1` (reads and writes never throw, since storage can be blocked in private mode) and applied as `data-theme="light"|"dark"` on `<html>`; System removes the attribute so the phone's setting decides. `main.ts` applies the stored choice before anything else.

The dark values are written twice in `tokens.css`: in `@media (prefers-color-scheme: dark) { :root:not([data-theme='light']) {...} }` and in `:root[data-theme='dark'] {...}`. The CSS `light-dark()` function would need only one copy but is missing on older iPhones, so it is not used; `tokens.test.ts` keeps the two copies identical and checks every contrast pairing in light, system dark, forced dark and forced light on a dark phone. `color-scheme` is `light dark` for System and `light` or `dark` when forced. The `theme-color` meta stays the plum strip in both.

**Known caveat: a brief flash.** The Content Security Policy forbids inline script, so the stored choice can only be applied once the module has loaded. A phone set to dark with Light chosen (or the reverse) can show the system theme for a moment on load.

### Cards
- **Corner:** 12px. **Background:** Paper. **Border:** 1px Gold Edge. **Padding:** 1rem. **Shadow:** none.
- Cards hold a serif heading and either a list, a notice list, a figures grid or one muted sentence.

### Character row
A link-styled grid row, at least 3rem tall, ink text, no underline at rest. The name is 600; the level and gold are muted/tabular. On hover-capable devices the name underlines. The last row has no separator.

### Status line
A 0.75rem dot plus words. `live` is solid green, `idle` is solid orange, `saved` is hollow; the dot is `aria-hidden`, the text carries the meaning.

### Progress bar
A 0.625rem track (Band, gold border, 5px corners) with a fill of Bar Red whose width follows `--fraction`, animated 250ms with `cubic-bezier(0.25, 1, 0.5, 1)`. It is a `progressbar` with a label and value. Used for experience to the next level and Hard Farms owned.

### Figures
A two-column `dl`: muted labels left, tabular values right.

### Faction row
Name left, muted `Rank n` or `Max rank` right, with a muted "to next rank" line and a bar below when not at max.

### Notices
A list divided by 1px Band rules; a 600 title, then a muted line with the character and time.

### Button
Accent Orange fill, On Accent text, 600 weight, 1px gold border, 8px corners, 3rem tall, 1rem side padding. Used for "Show fewer"-style toggles. `summary` rows (the missing Hard Farms list) are also 3rem tall and 600.

### Back link
Inline-flex, 3rem tall, "Back to characters".

### States
- **Empty:** a muted sentence inside the card (for example "No characters yet..."). 
- **Not paired:** a card titled "Not paired", a 600 reason line when a pairing link failed, and the instruction: On your PC, open Questkeeper, then Settings, Phone access, "Pair a phone...", and scan the code or open the link here.
- **Install hint:** on an iPhone or iPad that is not running the page from the Home Screen, a muted line "Install: tap Share, then Add to Home Screen." ends the character list (as a note under the cards) and the "Not paired" card. Other browsers and the installed page do not show it.
- **Error:** a card titled "Something went wrong showing this data" with the muted line "The page will try again when new data arrives."
- **Focus:** `:focus-visible` 3px Link Red outline, 2px offset.
- **Reduced motion:** the bar fill transition is removed.

## Do's and Don'ts

### Do:
- **Do** take every colour from the CSS variables in `tokens.css`; add a dark value when you add a token.
- **Do** keep text and states paired: every dot and colour has words.
- **Do** keep tap targets at least 3rem tall and use rem for type.
- **Do** keep it one column, 34rem wide, safe-area aware.
- **Do** put new data in cards on Paper with the 1px Gold Edge.

### Don't:
- **Don't** use accent orange as text on cream.
- **Don't** add shadows, gradients, web fonts, icon fonts or images from the internet.
- **Don't** convey state by colour alone.
- **Don't** add inline styles or scripts (the page runs under a strict CSP).
- **Don't** make it a dashboard of everything; the first screen answers "is everything fine?".
