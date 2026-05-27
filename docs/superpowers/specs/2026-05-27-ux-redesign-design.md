# ArkManager UX Redesign — "Field Manual" Visual Language

**Date:** 2026-05-27
**Sub-project:** C of 4 (productization). See "Decomposition context" below.
**Status:** Design approved, ready for implementation plan.

## Decomposition context

Productizing ArkManager ("обкликать") splits into four independent sub-projects,
each with its own spec → plan → implementation cycle:

| | Sub-project | Goal |
|---|---|---|
| **A** | Packaging / distribution (self-contained `.app`, ad-hoc sign, `.dmg`, build script) | "download" is real |
| **B** | Zero-touch onboarding (auto-provision wine/steamcmd without Terminal/sudo, in-app progress) | "it just works" |
| **C** | **UX redesign (this spec)** | doesn't look "колхозно" / generic |
| **D** | Cross-platform launchers (Windows native, Linux proton) — deferred | beyond macOS |

**This spec covers only C.** macOS-first; distribution without an Apple Developer
account (ad-hoc sign + one-time right-click→Open) was chosen for A but does not
affect this spec.

## Goal & non-goals

**Goal:** Replace the current "programmer-grade" UI with a distinctive, coherent
visual language ("Field Manual") that does **not** read as a generic AI dashboard.
Establish a reusable design system (tokens, typography, components) and apply it to
all 8 screens.

**Explicit non-goals (do NOT touch in this sub-project):**

- No `.NET` packaging / self-contained publish (A).
- No wine/steamcmd provisioning logic or onboarding flow rework (B). Doctor/Install
  get **restyled** here, but their install logic stays as-is.
- No cross-platform launcher work (D).
- No runtime language switching / i18n framework. UI is unified to **English**
  static copy. (i18n can come later as its own project.)
- **No business-logic / behavior changes.** This is a View + theme-layer redesign.
  ViewModels may gain *minimal display-only additions* (formatters, derived
  display strings) — enumerated per-screen below — but no changes to server
  lifecycle, RCON, backup, config, or mod logic.

## Current-state diagnosis (from live screenshots)

Verified by running the app and capturing all 8 tabs:

- **Giant black voids** — empty Server log, RCON output, Mods list dominate the screen.
  No empty states.
- **No button hierarchy** — Start/Stop/Copy/Clear all identical gray; glyph icons in
  button text (`▶ ■ 📋 🗑`) are mismatched.
- **Status as debug print** — `Stopped pid: up:— cpu:— ram:—` cramped on one line.
- **Mixed ru/en** on every screen (`Сервер / Session name`, `Пароли`, `Свободное место`,
  `9 бэкап(ов)`, `автобэкап через 04:54`, `нажмите Check для проверки`).
- **Backups shows raw absolute paths** instead of name + relative time + size.
- **No accent system** — everything is gray `#2a2f44` boxes on darker bg; flat, no focus.

The sidebar + content shell is fine structurally and is **kept**. The problem is the
content layer: hierarchy, color, empty states, unified language, human-readable data.

## Visual language: "Field Manual" (C1)

Warm survival field-manual feel: bone/parchment text on warm charcoal, ember-amber
accent, slab display type. Approved over a colder "tactical terminal" variant.

### Color tokens

Defined once as brushes; **all hardcoded hex in Views is removed** in favor of these.

| Token | Hex | Use |
|---|---|---|
| `Bg` | `#1a1611` | window / page background |
| `Rail` | `#15110c` | sidebar |
| `Panel` | `#221d15` | cards, tiles, list rows |
| `Panel2` | `#1d1810` | inputs, inset surfaces |
| `Console` | `#100d09` | log / code surfaces |
| `Line` | `#332a1d` | hairline borders / dividers |
| `Line2` | `#473a27` | stronger borders (inputs, ghost btn) |
| `Text` | `#ece3d3` | primary text (bone) |
| `Muted` | `#9c8d72` | secondary text, labels |
| `Accent` | `#e08a2b` | ember amber — primary actions, active nav |
| `AccentOn` | `#1a1207` | text/icon on accent fill |
| `AccentInk` | `#f2a64a` | accent-colored text (links, active label) |
| `AccentSoft` | `rgba(224,138,43,.13)` | active nav / soft fills |
| `AccentGlow` | `rgba(224,138,43,.30)` | primary button shadow |
| `Ok` | `#8fbf52` | running/online, success, INFO log |
| `OkSoft` / `OkLine` | `…,.13` / `…,.30` | status chip bg/border |
| `Danger` | `#d9685a` | destructive (delete), ERROR log |
| `Warn` | `#d9a441` | warnings, WARN log |

Log line severities: INFO=`Ok`, WARN=`Warn`, ERROR=`Danger`, JOIN/event=`AccentInk`,
timestamp=`Muted`.

### Typography

Three families, **embedded** as `AvaloniaResource` ttf (offline, no system dependency):

- **Display — Zilla Slab** (600/700): page titles (`h1`), large stat values, brand.
- **UI / body — IBM Plex Sans** (400/500/600): labels, buttons, body text.
- **Mono — IBM Plex Mono** (400/500): technical readouts, logs, ports, paths,
  section labels (uppercase, `.14em` tracking), relative time, size badges.

(Replaces the default Inter-only stack, which reads as generic.)

Type scale (style classes on `TextBlock`):

| Class | Family / size / weight | Use |
|---|---|---|
| `h1` | Zilla Slab 700 / 25px | page title |
| `stat` | Zilla Slab 600 / 22px | vitals values |
| `section` | Plex Mono 10px upper `.14em` / Muted | panel section header |
| `body` | Plex Sans 13px | default |
| `meta` | Plex Mono 11px / Muted | captions, relative time, paths |

### Iconography

Single line-icon set, ~1.7 stroke, **no emoji anywhere**. Implemented as keyed
`StreamGeometry`/`PathIcon` resources (e.g., `Icon.Play`, `Icon.Stop`, `Icon.Server`,
`Icon.Rcon`, `Icon.Install`, `Icon.Config`, `Icon.Mods`, `Icon.Backups`, `Icon.Doctor`,
`Icon.Settings`, `Icon.Copy`, `Icon.Trash`, `Icon.Restore`, `Icon.Folder`, `Icon.Clock`,
`Icon.Plus`). Brand mark = a simple line dino-skull geometry.

## Theme architecture (Avalonia)

Replace scattered hex with a central, maintainable theme layer.

- `Themes/Tokens.axaml` — `ResourceDictionary` of `SolidColorBrush` for every token above.
- `Themes/Typography.axaml` — `FontFamily` resources (embedded ttf) + `TextBlock`
  style classes (`h1`, `stat`, `section`, `meta`, …).
- `Themes/Icons.axaml` — keyed geometries for the icon set + brand mark.
- `Themes/Controls.axaml` — `Style` definitions for the component classes below.
- Merge all four into `App.axaml` `Application.Resources` (after FluentTheme).
- Fonts dropped in `Assets/Fonts/`, referenced as `avares://ArkManager.App/Assets/Fonts/#<Family>`.
- App stays force-dark (existing). Tokens are dark-only; no light variant needed.

Acronym caveat from CLAUDE.md (`[ObservableProperty]` capitalizes only first char after
`_`) still applies to any new VM fields.

## Component patterns (the system)

All screens compose from these. Classes are Avalonia `Classes="…"` style selectors.

1. **Buttons:** `primary` (accent fill, glow, AccentOn text), `ghost` (transparent,
   Line2 border), `icon` (38×38 square, Muted, Line2 border). Icon via `PathIcon`,
   never glyphs in text. `danger` modifier for destructive.
2. **Status chip / pill:** rounded, dot + label; `Ok`/`OkSoft` for online, `Accent`
   for info (e.g., auto-backup countdown).
3. **Vitals tile:** Panel card, `section` label + `stat` value (e.g., `38%`, `5.2GB`).
4. **Panel + section header:** Panel surface; header = small rotated accent tick +
   `section` label + bottom hairline.
5. **Segmented control:** inset Panel2 track, active segment = AccentSoft + AccentInk
   (used for Config ini tabs).
6. **Form row:** `170px` label column → control; label may have a muted hint line.
7. **Inputs:** Panel2 bg, Line2 border, 8px radius; `mono` modifier for numeric/technical
   (ports, ids). **Toggle switch** replaces checkboxes for on/off (e.g., RCON enabled).
8. **List row:** Panel card with leading type-icon, title + mono meta line, trailing
   size badge, and hover action icon-buttons (restore / Finder / delete).
9. **Console / code surface:** `Console` bg, header bar (`section` title + filter on
   right), monospace body with severity colors.
10. **Empty state:** centered muted icon + one body line + one mono hint — replaces every
    black void.

## Per-screen application (English copy)

Shell — `MainWindow`: brand mark + "ArkManager" / "ASA CONTROL" subtitle; nav items with
line icons and active ember left-bar; **Doctor + Settings pinned to bottom** via spacer.

1. **Server** — header (title + identity sub `session · map` + status chip with uptime);
   action row (Start `primary` / Stop `ghost` / Copy+Clear `icon` / filter input); 4 vitals
   tiles (Uptime, CPU, Memory, Players); console panel. Empty state when stopped:
   "Server is stopped. Press Start to boot." *VM add: `ServerIdentity` display string.*
2. **RCON** — header; connection bar (host / port / password / Connect `primary` /
   Disconnect `ghost`); output console; command input + Send + quick-command `ghost`
   chips (saveworld, DoExit). Empty state when not connected.
3. **Install** — panels: SteamCMD, Dedicated Server (install path + actions), Server
   version (readable rows: installed build / latest build / last updated); console.
   *Restyle only — install logic untouched (deep onboarding = B).*
4. **Config** — segmented ini tabs (Basic / GameUserSettings.ini / Game.ini / CLI Preview);
   Basic = grouped panels (Server / Network / Passwords) with form rows + RCON toggle;
   raw ini tabs = mono code surface; CLI Preview = console surface; footer Save `primary`
   / Reload `ghost` + status.
5. **Mods** — add-input + Add `primary`; list rows (id, resolved name, reorder, remove);
   footer (CurseForge, Resolve names, Reload). Empty state: "No mods. Paste a CurseForge
   ID to add one." *VM add: nothing required (names already resolved).*
6. **Backups** — subtitle `N snapshots · total size`; auto-backup countdown pill; create
   row (note input + Create `primary`); human-readable list rows (type icon, friendly
   name/note, relative time, size badge, row actions). Empty state.
   *VM adds: `DisplayName`, `RelativeTime`, `Size` (formatted), `TotalSize`, snapshot count.*
7. **Doctor** — check rows as list archetype (ok/fail status icon, name, mono detail,
   inline fix action); overall status pill. Remove the empty right-hand log void — show
   install log inline only when non-empty. *Restyle only.*
8. **Settings** — grouped panels (Wine / Paths / Auto-management / App data) with form
   rows, browse `icon` buttons, footer Save `primary`.

### Copy glossary (ru → en, representative)

`Сервер`→Server · `Пути`→Paths · `Пароли`→Passwords · `Дополнительно`→Advanced ·
`Свободное место`→Disk free · `9 бэкап(ов)`→9 snapshots · `автобэкап через 04:54`→
Auto-backup in 04:54 · `нажмите Check для проверки`→Click Check to refresh ·
`Путь установки`→Install path · `установлен`→installed · `Открыть в Finder`→Show in Finder ·
`Сколько последних бэкапов хранить`→Backups to keep · `Restore (с очисткой)`→Restore (clean).

## Pure-logic additions (testable)

These are display-only helpers added to Core (or VM) and covered by xUnit in
`ArkManager.Core.Tests`:

- `RelativeTime(DateTime)` → "today, 23:28" / "yesterday, 22:55" / "3 days ago".
- `HumanSize(long bytes)` → "69.2 MB" / "612 MB" / "1.2 GB".
- `BackupDisplayName(BackupEntry)` → note if present, else "Auto snapshot" / "Manual".

## Verification

Avalonia UI is not unit-testable visually, so verification is layered:

1. **Pure helpers** (relative time, size, display name) — xUnit, table-driven.
2. **Build** — `dotnet build ArkManager.slnx` clean (no XAML-compile errors; watch the
   Avalonia gotchas in CLAUDE.md: `RowSpacing` singular, `PlaceholderText` not `Watermark`).
3. **Launch + screenshot pass** — run the app and capture all 8 tabs via the established
   harness (AppleScript arrow-key nav + `screencapture -R` of the window bounds), then
   visually confirm each screen matches the system. This harness is proven working this
   session.
4. **No-regression** — `dotnet test ArkManager.slnx` stays green (no logic changed).

## Risks / notes

- **Font licensing** — Zilla Slab, IBM Plex Sans/Mono are all OFL/open; safe to embed.
- **PathIcon set** — building ~16 clean line geometries is the bulk of the icon work;
  keep them simple, 24×24 viewbox.
- **Scope creep into B** — Doctor/Install are tempting to "fix" beyond styling. Hold the
  line: restyle only here.
- **VM additions stay display-only** — if a screen seems to need real logic, stop and
  flag it rather than expanding scope.
