# Design system

The visual system of the web app (`web/app`). It is plain CSS in `web/app/src/app/globals.css` plus four page-scoped
files in `web/app/src/app/styles/` (`home.css`, `lists.css`, `profile.css`, `static.css`, `forms.css`), with no CSS
framework and no web fonts (the content security policy allows `font-src 'self'` only). This document is in English;
every label in the product is Polish.

## Concept: "Atlas"

A calm civic publication a worried parent can trust. Three ideas carry it:

- **Petrol ink on warm paper.** One deep petrol brand colour (links, buttons, rules, the brand mark) on a warm paper
  background. The only deep band on the site is the home hero and the footer; everything else is light (or dark, in
  dark mode) and quiet.
- **Two voices.** A serif display voice for page and section headings (system serif stack: Charter, Sitka, Iowan,
  Cambria, Georgia, Noto Serif, DejaVu Serif) and the system sans for interface and data. Scores use heavy, tabular,
  lining figures in neutral ink.
- **Address first.** The home hero is a deep band with a quiet street map (inline SVG: streets, a dashed boundary and
  a marigold address marker). The address form sits on a raised white panel over it and is the focal point of the
  site.

The marigold highlight (`--color-highlight`) is decoration only: the brand-mark dot, the hero marker, the hero
eyebrow and footer link underlines. It never marks a value.

## Rules that carry product meaning

These are design rules because breaking them breaks a product or legal rule (see `docs/ux/UI-UX.md`).

| Rule | How the design enforces it |
|---|---|
| A score is never shown without its interval, cohort size and methodology version | `ScoreBadge` renders the number, `± half-width`, the 95% interval, `Grupa porównawcza: N uczniów`, `Wersja metodologii X` and the source and as-of date in one `.score` block. There is no "number only" variant: `variant` (`default`, `compact`, `hero`) changes density, never content. The hero variant adds a decorative interval whisker (`.score__range`, `aria-hidden`) that repeats the two interval ends; it has no scale. |
| A score is not a verdict | Every published score uses the same neutral ink (`--color-score`). No red/amber/green, no stars, no medals, no "top" badges. Rank numbers in the ranking are plain muted serif numerals, the same for every row. The petrol rule beside a score is the brand colour, the same for every score. |
| `insufficient_cohort` and no data are never 0 or blank | `.score--suppressed`: sunken neutral surface, 1.5px dashed outline in `--color-state-line`, a hollow-circle marker and the explicit message ("Brak wyniku: …"). No number is drawn, also not in the trend chart. |
| `limited` (5 to 8 pupils, ADR-0009) shows no score | `.score--limited`: info tones, dashed on every side in the info ink, an "i" marker, the cohort line and the exact line "Dla tej grupy nie publikujemy wyniku ani miejsca w zestawieniu." in semibold. Distinct from suppressed (neutral, hollow circle). |
| Every value keeps source, as-of and method | Provenance and preschool fact tables keep all columns; on phones they stack into labelled rows instead of hiding columns. Score blocks end with "Źródło … · stan na …". |
| Demo data is never read as real | Any source containing `DEMO SEED` is a magenta `.badge--demo` with a flask icon; a profile with demo provenance gets a `.notice--demo` banner; demo provenance rows carry a magenta edge (`tr[data-demo]`). |
| AI output is never the score | AI output sits in a dashed violet `.ai-box` with its label first (the label gets an "AI" chip). AI panels carry a violet top rule (`.ai-panel`) and a sparkle `.badge--ai`. The structured facts are rendered next to the AI text by the same `ScoreBadge`. |
| Sponsored content is separate | Ochre `sponsor` tone, its own card below scores and reviews, label pill "Treść sponsorowana". Never used in lists, ranking or comparison. |
| No ascending sort, no "worst" control | The ranking has no sort or direction control of any kind (no `select`, no header buttons). The scope switch is links only. |
| Preschools have no quality score | Preschool cards and profiles show practical facts only (`.fact-summary`, `.school-list__facts`); no score block, no rank numeral, no stars. Decorative copy on preschool pages avoids "wynik", "ocena" and "ranking". |
| Caveats are visible | Product caveats ("nie jest oficjalnym rankingiem", no catchment data, preschools have no quality score) use the amber `.notice--caution` with a warning icon. |

## Tokens

All tokens are custom properties on `:root`. Dark mode redefines the colour tokens under
`@media (prefers-color-scheme: dark)` (guarded by `:root:not([data-theme="light"])`) and again under
`:root[data-theme="dark"]`, so a future theme switch only has to set `data-theme`.

### Colour

| Token | Light | Dark | Use |
|---|---|---|---|
| `--color-bg` | `#f6f4ef` | `#0e1316` | Page background (warm paper) |
| `--color-bg-band` | `#eeeae1` | `#121a1f` | Page masthead band (top of the gradient) |
| `--color-surface` | `#ffffff` | `#172026` | Cards, tables, inputs, score blocks |
| `--color-surface-sunken` | `#f3f0e9` | `#121a1f` | Suppressed score, table headers |
| `--color-fg` | `#15202b` | `#e8ecee` | Body text |
| `--color-muted` | `#525d68` | `#a7b3ba` | Secondary text |
| `--color-border` / `-strong` | `#e2dccf` / `#cbc3b3` | `#26323a` / `#364550` | Hairlines |
| `--color-border-input` | `#7f7a6f` | `#6f818c` | Form control boundary (≥ 3:1) |
| `--color-state-line` | `#737373` | `#6c7d88` | Dashed outline of a suppressed score (≥ 3:1 on its surface) |
| `--color-accent` (+ `-hover`, `-fg`, `-soft`) | `#0b5d6b` | `#7cc8d3` | Brand petrol: links, primary buttons, rules, current nav item |
| `--color-deep`, `--color-deep-2` | `#0c3d48`, `#0a3039` | `#10313a`, `#0c262d` | The deep band: home hero gradient and footer |
| `--color-deep-fg`, `--color-deep-muted` | `#ffffff`, `#c9dfe2` | `#ffffff`, `#c3d9dd` | Text on the deep band |
| `--color-highlight` | `#e9a93a` | `#e9a93a` | Marigold, decoration only |
| `--color-focus` | `#0b63ce` | `#7cb4ff` | Focus ring (`#9cc8ff` inside the footer) |
| `--color-score`, `--color-score-rule` | `#15202b`, `#0b5d6b` | `#f1f4f5`, `#7cc8d3` | Score number (neutral ink) and its rule |

Placeholder text has no token of its own: `::placeholder` uses `--color-muted` at full opacity. Hints in
`.field-hint` use the same muted token.

Semantic tones come as `bg` / `fg` / `border` triples: `info`, `warn`, `danger`, `success`, `ai`, `sponsor`, `demo`.
They are unchanged from the previous system except `--color-info-bg` light (`#eaf1f8`).

Measured contrast (WCAG 2.x, computed from the token values in `globals.css`), light / dark:

| Pair | Light | Dark |
|---|---|---|
| Body text on background / surface | 15.00 / 16.49 | 15.72 / 13.90 |
| Muted on background / surface / sunken | 6.11 / 6.72 / 5.91 | 8.72 / 7.71 / 8.21 |
| Muted on the masthead band | 5.60 | 8.29 |
| Accent (links) on surface / background | 7.52 / 6.84 | 8.70 / 9.84 |
| Button label on accent (white / `#062a31`) | 7.52 | 8.00 |
| Hero heading (white) on deep band | 11.82 (on `--color-deep-2`: 14.05) | 14.32 |
| Hero lead / footer text (`deep-muted`) on deep band | 8.52 (on `deep-2`: 10.13) | 9.39 (on `deep-2`: 10.74) |
| Marigold eyebrow on deep band | 5.74 (on `deep-2`: 6.82) | 6.70 (on `deep-2`: 7.67) |
| Tone text on its own background | ≥ 6.88 (success) | ≥ 9.61 (AI) |
| Input border on surface (non-text, needs 3) | 4.27 | 4.09 |
| Suppressed dashed line on sunken (non-text) | 4.17 | 4.13 |
| Focus ring on background / surface | 5.18 / 5.69 | 8.75 / 7.73 |
| Footer focus ring `#9cc8ff` on footer | 8.11 | 9.11 |

An axe WCAG 2.0/2.1 AA scan of every public route (26 routes, light and dark, 360 and 1366 px) reports no serious or
critical finding.

### Type

Sans (`--font-sans`, system stack), serif (`--font-serif`, system stack, headings `h1`/`h2`, the brand name, the
rank numerals and the suppressed/limited titles in the hero score), mono for tokens. Scale: `--text-xs` 13px,
`--text-sm` 14px, `--text-base` 16px, `--text-lg` 18px, `--text-xl` 20px, `--text-2xl` 22–28px, `--text-3xl`
30–44px, `--text-display` 34–58px (the last three fluid with `clamp`). Body line height 1.6; headings 1.15 with
`text-wrap: balance`. Eyebrows are 13px bold uppercase with 0.1em tracking and a short leading rule. Running text is
capped at `--measure` (68ch). Score numbers use weight 800, tabular lining figures and negative tracking. Inputs never
go below 16px, so iOS does not zoom on focus.

### Spacing, shape, depth

Spacing on a 4px base: `--space-1` 4px … `--space-8` 64px. Radii: `--radius-sm` 8px (controls, notices),
`--radius` 12px (cards, tables, score blocks), `--radius-lg` 20px (hero panel, chat form), `--radius-pill`.
Shadows: `--shadow-sm`, `--shadow` (cards on hover, result cards), `--shadow-lg` (the hero panel, the open phone menu).
Layout: `--container` 72rem, `--gutter` 16px (24px from 40rem, 32px from 75rem), `--tap` 44px, `--main-pad-top`.

### Icons

Decorative icons are CSS masks painted in `currentColor` (`--icon-info`, `--icon-caution`, `--icon-alert`,
`--icon-check`, `--icon-flask`, `--icon-sparkle`, `--icon-pin`, `--icon-users`, `--icon-interval`, `--icon-book`,
`--icon-doc`, `--icon-circle`, `--icon-chevron`, `--icon-cross`), as `data:` SVG URLs (allowed by `img-src 'self'
data:`). They are pseudo-elements, so they never enter the accessible name or the text a test reads. Inline SVG
icons in markup carry `aria-hidden="true"`.

## Components (shared classes)

| Class | What it is |
|---|---|
| `.site-header`, `.site-nav`, `.site-nav__toggle`, `.site-nav__list`, `.brand-mark` | Header with the brand mark (roof over an open book, marigold dot) and the serif name. Main nav (`aria-label="Główna"`) marks the current section with `aria-current="page"` (a short petrol bar under the label on desktop). Below 62rem a "Menu" button (`aria-expanded`, `aria-controls`, Escape closes and returns focus) opens the links as a panel over the page; without scripts (no `js` class on `<html>`) the links wrap under the brand instead. |
| `.site-footer` | The deep band: brand, data note, the "Nie jest oficjalnym rankingiem" claim, link columns "Serwis" and "Informacje prawne". It redefines `--color-fg`, `--color-muted` and `--color-focus` locally. |
| `.site-main`, `.site-container` | `main` is full width and clips sideways overflow (`overflow-x: clip`); `.site-container` centres the page. This lets full-bleed bands paint edge to edge without page scroll. |
| page masthead | The first `.page-header`, `.profile-header` or bare `h1` of a page sits on a full-bleed band (gradient plus dot texture, hairline below). |
| `.page-header`, `.lead`, `.eyebrow`, `.meta-row` | Title block, lead paragraph, petrol uppercase kicker with a rule, row of badges |
| `.card`, `.section`, `.empty-note` | Surface card; content section whose `h2` carries a hairline and a short petrol rule; dashed box for an empty list |
| `.notice` | Callout with a tone icon. Tone comes from the ARIA role: default and `role="note"` = info, `role="alert"` = danger, `role="status"` = success. Modifiers: `--caution`, `--ai`, `--demo` |
| `.badge` (+ `--info`, `--demo`, `--ai`, `[data-confidence]`) | Pills for facts and states |
| `.button` (+ `--secondary`, `--block`), `.link-button` | 44px-high actions; inline SVG icons allowed |
| `.field`, `.field--check`, `.check-label`, `.form-grid` (`--2`, `--address`, `--filters`), `.inline-form` | Forms; labels always visible above controls; text inputs 48px high; checkbox rows are 44px targets |
| `.hero`, `.hero__art`, `.hero__panel`, `.principles`, `.home-grid` | Home: deep band with the street-map art and the address panel; three promise cards; name search and AI search cards |
| `.score` (+ `--suppressed`, `--limited`, `--compact`, `--hero`) | The score block (via `ScoreBadge` only). Children: `.score__lead` (number, `±`, hero whisker) and `.score__facts` (interval, cohort, methodology, source lines with icons). Compact has no box for a published score, only a petrol rule. Hero lays number and facts side by side when its card is at least 46rem wide (container query on `.result-card`). |
| `.school-list`, `.school-list__item` | Result cards. Scored: name and place left, the complete score block right from 44rem. Preschool (`--facts`): name on top, three facts in a row below from 44rem. |
| `.catchment`, `.catchment__school` | Address result: petrol top rule, pinned address line, the catchment school card outlined in petrol |
| `.table-wrap`, `.table--stack`, `.table--sticky`, `.table--compare`, `.num` | Tables (see Responsive) |
| `.ranking-table`, `.rank` | Ranking rows: neutral serif position numeral, school and place, compact score block |
| `.trend`, `.trend__scroll` | SVG trend chart (ink dot + petrol 95% whisker per year; suppressed years labelled "brak wyniku") |
| `.ai-panel`, `.ai-box`, `.chips`, `.ai-chat` | AI surfaces |
| `.tabs`, `.tabs__tab` | Moderation tabs (`role="tab"`, `aria-selected`) |
| `.prose`, `.toc`, `.checklist--no`, `.link-list` | Long-form pages, table of contents card, "what we do not do" lists (cross in a soft red disc), list of link cards (404) |
| `.empty-state` | 404 with a signpost illustration (inline SVG) |
| `.sponsored`, `.pricing` | Monetisation |

## Map

The map page (`/mapa`, ADR-0022, styles in `app/styles/map.css`, components in `components/map/`) follows the same rules
as the rest of the system. What is specific to it:

- **Markers are neutral.** One colour and one shape per kind of facility: schools are petrol (`#0b5d6b`, a circle with a
  small roof), preschools burnt orange (`#b3470f`, a rounded square). Both have a white outline and measure at least 3:1
  against white and against typical map tiles (petrol 7.5:1 on white and 5.1:1 on water blue; orange 5.5:1 on white and
  3.7:1 on water blue). Clusters are neutral ink (`#15202b`) with a white count. Colour, size and order never encode a
  score, rank, review or sponsorship. The two kind colours are defined once in `lib/map-markers.ts` (`MARKER_COLOR`) and
  mirrored as `--map-school` and `--map-preschool` for the legend and kind labels. They do not change in dark mode: the
  tiles stay light (the map has no dark style), and a changed colour would break the legend.
- **Legend and kind label** (`.map-legend`, `.map-kind`): a swatch with the marker's shape plus the word "Szkoła" or
  "Przedszkole", so colour is never the only cue.
- **Info card** (`.map-card`): a `.card` over the bottom of the map (full width on phones, 22rem from 36rem), not a Google
  info window. It takes focus when a marker is selected; the close button is a 44px target.
- **Result list** (`.map-list`, `.map-list__item`): alphabetical, name as a link to the profile, kind, gmina, public or
  private; preschools add tuition and free places, with "brak danych" for a missing value. It is the keyboard and
  screen-reader alternative to the map and the whole page when no map is available.
- **Phone layout.** Below 62rem the filters sit in a bottom sheet (`.map-filters[data-open]`) behind a "Filtry (n)"
  button: `role="dialog"` while open, Escape closes, Tab stays inside, focus returns to the button. A "Mapa" / "Lista"
  toggle (`aria-pressed`) shows one at a time. From 62rem the filters and list stay on the left and the map is sticky on
  the right. Without a map the filters and the list sit side by side.
- **Status text.** The result count is a `role="status"` line ("Placówek w tym obszarze: N."); the truncation notice is
  "Przybliż mapę, aby zobaczyć wszystkie placówki." (with a map) or "Pokazano tylko część placówek. Zawęź filtry, aby
  zobaczyć wszystkie." (list only). The unavailable notice is a `.notice--caution` with `role="note"`.
- **Motion.** `prefers-reduced-motion: reduce` makes the map jump instead of pan and removes every transition on the page.
- **Out of scope here:** a dark map style (the tiles stay light), custom controls beyond Google's defaults.

## Responsive

Phone first; the reference width is 360px and no page scrolls sideways at that width (checked at 360, 768 and
1366 px in light and dark).

- Header: brand and "Menu" button on one row below 62rem; the full nav inline from 62rem.
- Tables: `.table-wrap` scrolls inside itself, never the page. Below 40rem `.table--stack` turns each row into a
  labelled block (cells carry `data-label`), used for the trend table, provenance, preschool facts and the ranking.
  The comparison keeps its columns and pins the attribute column (`.table--sticky`, 7.5rem on phones) while 10.5rem
  school columns scroll. On phones that geometry keeps a whole header link plus its focus ring inside the window
  between the sticky column and the edge fade, and the links carry a matching `scroll-margin-inline`.
- Source links in provenance and fact tables are 44px targets at every width.
- The trend chart keeps a 30rem minimum width inside a focusable scroll region so its labels stay readable (two or
  three years are drawn to the full phone width).
- Touch targets: buttons, inputs, selects, nav and pagination links, tabs and checkbox rows are at least 44px high.
  Links inside running text are exempt (WCAG 2.5.8 inline exception).

## Accessibility

- Landmarks: one `header`, `nav` "Główna", `main#main`, `footer` with `nav` "Serwis" and "Informacje prawne".
- Skip link "Przejdź do treści" is the first focusable element and becomes visible on focus.
- Every control has a visible `label`. Status and error messages keep their `role="status"` / `role="alert"`.
- AI results (search intent, methodology answer, explanation) take focus when they arrive (`tabindex="-1"` region).
  The advisor chat shows a `role="status"` notice when the conversation reaches its limit and the message box becomes
  read-only, described by that notice.
- `:focus-visible` draws a 3px ring in `--color-focus`, offset 2px (inputs: the ring sits on the border, which also
  takes the focus colour).
- A ring is drawn outside its element, so it is clipped by any ancestor with `overflow` other than `visible`. Those
  containers are `.table-wrap`, `.trend__scroll` and `.tabs`. Table and trend links sit inside cell or box padding
  (at least 8px). The moderation tabs and nav links have their rings drawn inset (`outline-offset: -3px`).
- `.visually-hidden` is the screen-reader-only utility.
- Decorative SVG (hero map, 404 signpost, icons) is `aria-hidden`; the trend chart has `role="img"` with a title and
  description, and the same numbers are in the table below it.
- `prefers-reduced-motion: reduce` removes transitions, the card lift on hover and the button press offset. There are
  no animations.
- Print hides navigation, AI panels, pagination and the hero art, and keeps score blocks unbroken.

## Print

Printing is on white paper, whatever the OS colour scheme or `data-theme` says. The `@media print` block in
`globals.css` redefines every colour token (including the deep band and the highlight) to black, white and `#666`,
and sets all shadow tokens to `none`. The dark selectors are repeated in that block (`:root:not([data-theme="light"])`
and `:root[data-theme="dark"]`) because a bare `:root` would lose to them on specificity. The footer and full-bleed
bands print white. Filled `.button` and `.button--secondary` print as a white box with a black label and a black 1px
border. Tones keep their meaning through their text, icon and border, not through colour.

## Contract with the end-to-end suite

`tests/e2e/tests/*.spec.ts` asserts on text, roles and some classes. When restyling, keep: the score number first
in a ranking score cell; number and `±` in one element (`.score__value`); `Grupa porównawcza: N uczniów` alone in
its own element and the methodology version visible in the score block; no `±` and no "przedział 95%" anywhere on a
`limited` or `insufficient_cohort` profile (the hero whisker is only drawn for a published score); no `select` or
sort button on the ranking; exactly one `h1` per page; `section.ai-search`, `.ai-msg`, `.token`, `.pricing`,
`.result-count`, `.catchment__school li`, `.notice--caution`; no extra `role="status"` on form pages; no "Wygenerowano
przez AI" text on a profile or the methodology page before an AI answer exists; no "wynik", "ocena", "ranking" or
star characters inside `main` on preschool pages; navigation "Główna" keeps a visible "Doradca" link at desktop width.
