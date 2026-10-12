# Design system

The visual system of the web app (`web/app`). It is plain CSS in `web/app/src/app/globals.css`, with no CSS
framework and no web fonts (the content security policy allows `font-src 'self'` only). This document is in English;
every label in the product is Polish.

The aim is a calm, civic look a parent can trust: one ink colour for data, warm paper background, visible caveats,
and no visual device that turns a score into a verdict.

## Rules that carry product meaning

These are design rules because breaking them breaks a product or legal rule (see `docs/ux/UI-UX.md`).

| Rule | How the design enforces it |
|---|---|
| A score is never shown without its interval, cohort size and methodology version | `ScoreBadge` renders the number, `± half-width`, the 95% interval, `Kohorta: N uczniów`, `metodologia X` and the source and as-of date in one `.score` block. There is no "number only" variant: `variant` (`default`, `compact`, `hero`) changes density, never content. |
| A score is not a verdict | Every published score uses the same neutral ink (`--color-score`). No red/amber/green, no stars, no medals, no "top" badges. Rank numbers in the ranking are plain grey discs. |
| `insufficient_cohort` is never 0 or blank | `.score--suppressed`: dashed outline, a hollow-circle marker and the explicit message "Brak wyniku: zbyt mała grupa". No number is drawn, also not in the trend chart. |
| Every value keeps source, as-of and method | Provenance and preschool fact tables keep all columns; on phones they stack into labelled rows instead of hiding columns. The score block shows "Źródło … · stan na …". |
| Demo data is never read as real | Any source containing `DEMO SEED` is shown as a magenta `.badge--demo`; a profile with demo provenance gets a `.notice--demo` banner; demo provenance rows carry a magenta edge (`tr[data-demo]`). |
| AI output is never the score | AI output sits in a dashed violet `.ai-box` with its label first (the label gets an "AI" chip). AI panels carry a violet top rule (`.ai-panel`). The structured facts are rendered next to the AI text by the same `ScoreBadge`. |
| Sponsored content is separate | Ochre `sponsor` tone, its own card below scores and reviews, label pill "Treść sponsorowana". Never used in lists, ranking or comparison. |
| No ascending sort, no "worst" control | The ranking has no sort or direction control of any kind (no `select`, no header buttons). |
| Caveats are visible | Product caveats ("nie jest oficjalnym rankingiem", no catchment data, preschools have no quality score) use the amber `.notice--caution`. |

## Tokens

All tokens are custom properties on `:root`. Dark mode redefines the colour tokens under
`@media (prefers-color-scheme: dark)` (guarded by `:root:not([data-theme="light"])`) and again under
`:root[data-theme="dark"]`, so a future theme switch only has to set `data-theme`.

### Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-bg` | `#f6f5f1` | `#121417` | Page background (warm paper) |
| `--color-bg-band` | `#eeece5` | `#171a1e` | Hero and footer bands |
| `--color-surface` | `#ffffff` | `#1b1e23` | Cards, tables, inputs |
| `--color-surface-sunken` | `#f2f1ec` | `#16191d` | Score block, table headers |
| `--color-fg` | `#1a1c1f` | `#e9e7e2` | Body text |
| `--color-muted` | `#555a62` | `#aeb2b9` | Secondary text |
| `--color-border` / `-strong` | `#dcd9d0` / `#c4c0b4` | `#2f343b` / `#424851` | Hairlines |
| `--color-border-input` | `#847f72` | `#7a818b` | Form control boundary (≥ 3:1) |
| `--color-accent` (+ `-hover`, `-fg`, `-soft`) | `#1f4e8c` | `#8fb9f0` | Links, primary buttons, current nav item |
| `--color-focus` | `#0b63ce` | `#7cb4ff` | Focus ring |
| `--color-score`, `--color-score-rule` | `#1f3a5f`, `#1f4e8c` | `#dbe7f7`, `#8fb9f0` | Score number and its rule |

Semantic tones come as `bg` / `fg` / `border` triples: `info`, `warn`, `danger`, `success`, `ai`, `sponsor`, `demo`.

Measured contrast (WCAG 2.x, computed from the token values in `globals.css`), light / dark: body text 15.7 / 14.9;
muted on background 6.4 / 8.7; accent on surface 8.3 / 8.2; button text on accent 8.3 / 8.6; every tone's text on its
own background ≥ 6.88 / ≥ 9.6 (lowest: success in light, 6.88; AI in dark, 9.61); input border on surface 4.0 / 4.25
(non-text, needs 3); focus ring on background 5.2 / 8.6. All pass AA. Text pairs pass AAA except light-mode muted text
(6.4 on background, 6.9 on surface) and the success tone in light mode (6.88).

### Type

System font stack (`--font-sans`), mono stack for tokens. Scale: `--text-xs` 13px, `--text-sm` 14px, `--text-base`
16px, `--text-lg` 18px, `--text-xl` 20px, `--text-2xl` 21–24px, `--text-3xl` 26–36px, `--text-display` 30–46px (the
last three are fluid with `clamp`). Body line height 1.6; headings 1.2 with `text-wrap: balance`. Running text is
capped at `--measure` (70ch). Numbers in scores and tables use tabular figures. Inputs never go below 16px, so
iOS does not zoom on focus.

### Spacing, shape, depth

Spacing on a 4px base: `--space-1` 4px … `--space-8` 64px. Radii: `--radius-sm` 6px (controls, notices),
`--radius` 10px (cards, tables), `--radius-lg` 16px (hero, panels), `--radius-pill`. Shadows: `--shadow-sm`,
`--shadow`, `--shadow-lg` (flattened in dark mode). Layout: `--container` 70rem, `--gutter` 16px (24px from 40rem),
`--tap` 44px.

## Components (shared classes)

| Class | What it is |
|---|---|
| `.site-header`, `.site-nav`, `.site-footer` | Header with brand mark; main nav (`aria-label="Główna"`) marks the current section with `aria-current="page"` |
| `.page-header`, `.lead`, `.eyebrow`, `.meta-row` | Page title block, lead paragraph, small caps kicker, row of badges |
| `.card`, `.section` | Surface card; content section with a hairline above its heading |
| `.notice` | Callout. Tone comes from the ARIA role: default and `role="note"` = info, `role="alert"` = danger, `role="status"` = success. Modifiers: `--caution`, `--ai`, `--demo` |
| `.badge` (+ `--info`, `--demo`, `--ai`, `[data-confidence]`) | Pills for facts and states |
| `.button` (+ `--secondary`, `--block`), `.link-button` | 44px-high actions |
| `.field`, `.field--check`, `.check-label`, `.form-grid` (`--2`, `--address`, `--filters`), `.inline-form` | Forms; labels always visible above controls; checkbox rows are 44px targets |
| `.score` (+ `--suppressed`, `--compact`, `--hero`) | The score block (via `ScoreBadge` only) |
| `.school-list`, `.school-list__item` | Result cards: name and meta on the left, the complete score block on the right from 44rem |
| `.table-wrap`, `.table--stack`, `.table--sticky`, `.table--compare`, `.num` | Tables (see Responsive) |
| `.trend`, `.trend__scroll` | SVG trend chart (dot + 95% whisker per year; suppressed years labelled "brak wyniku") |
| `.ai-panel`, `.ai-box`, `.chips`, `.ai-chat` | AI surfaces |
| `.tabs`, `.tabs__tab` | Moderation tabs (`role="tab"`, `aria-selected`) |
| `.prose`, `.toc`, `.checklist--no` | Long-form pages, table of contents, "what we do not do" lists |
| `.sponsored`, `.pricing` | Monetisation |

## Responsive

Phone first; the reference width is 360px and no page scrolls sideways at that width.

- Header: brand on one row, the main nav as a single horizontally scrollable row under it (the current item is
  scrolled into view); inline with the brand from 62rem.
- Tables: `.table-wrap` scrolls inside itself, never the page. Below 40rem `.table--stack` turns each row into a
  labelled block (cells carry `data-label`), used for the trend table, provenance, preschool facts and the ranking.
  The comparison keeps its columns and pins the attribute column (`.table--sticky`) while the schools scroll.
- The trend chart keeps a 30rem minimum width inside a focusable scroll region so its labels stay readable.
- Touch targets: buttons, inputs, selects, nav and pagination links, tabs and checkbox rows are at least 44px high.
  Links inside running text are exempt (WCAG 2.5.8 inline exception).

## Accessibility

- Landmarks: one `header`, `nav` "Główna", `main#main`, `footer` with `nav` "Serwis" and "Informacje prawne".
- Skip link "Przejdź do treści" is the first focusable element and becomes visible on focus.
- Every control has a visible `label`. Status and error messages keep their `role="status"` / `role="alert"`.
- `:focus-visible` draws a 3px ring in `--color-focus`, offset 2px (inputs: the ring sits on the border, which also takes the focus colour).
- Decorative SVG icons are `aria-hidden`; the trend chart has `role="img"` with a title and description, and the
  same numbers are in the table below it.
- `prefers-reduced-motion: reduce` removes transitions and the button press offset. There are no animations.
- Print hides navigation, AI panels and pagination, and keeps score blocks unbroken.

## Contract with the end-to-end suite

`tests/e2e/tests/*.spec.ts` asserts on text, roles and some classes. When restyling, keep: the score number first
in a ranking score cell; number and `±` in one element; `N uczniów` and the methodology version visible in the score
block; no `±` anywhere on an `insufficient_cohort` profile; no `select` or sort button on the ranking; exactly one
`h1` per page; `section.ai-search`, `.ai-msg`, `.token`, `.pricing`; no extra `role="status"` on form pages; no
"Wygenerowano przez AI" text on a profile or the methodology page before an AI answer exists; no "wynik", "ocena",
"ranking" or star characters inside `main` on preschool pages.
