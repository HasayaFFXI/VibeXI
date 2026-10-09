# Zerg — changelog

What each version of Zerg was, newest first. The number is in
`Directory.Build.props`; `CLAUDE.md`, "Versions", says when it moves and how
a version is cut. Work that has no version yet goes under "Unreleased" and
takes its number when the version is cut.

## Unreleased

Nothing yet.

## 0.2.1 — 2026-10-08

- A Target button, on the command bar over the Damage section and beside
  Include Skillchains in Compare: counts only the damage dealt to the
  targets picked from its menu, one or several, listed alphabetically. In
  Compare a row of the By target table can be pressed to the same end. What
  is picked is not saved, and Start clears it.
- A Type button beside it, in both places, and in Compare the rows of the
  By damage type table pressed the same way: counts only the damage of the
  types picked. The two filters work together, and each lists what the
  other leaves.
- Two counting rules added: isolating targets, with no DPS while one is
  isolated, and isolating damage types (`RULES.md`).

## 0.2.0 — 2026-10-08

The redesign of the UI: every colour and control is Zerg's own.

- A title bar of Zerg's own with the sections as tabs, and one command bar.
- A band of five figures with small marks and the state tag; the characters
  as one line of chips.
- Each section is four flush panes that the player resizes, folds and
  moves; each section's arrangement is remembered, and can be locked.
- Dense tables in which the row is the bar, with grouped headings, a mark
  on low accuracy and a Party line.
- Charts that name their lines at their ends.
- Compare as bands over panes, its rows shaded in the two runs' colours,
  which are settings.
- A Settings page of ruled rows, with a colour picker.
- Panels whose bars are all figures until the pointer is over them.
- New settings: `shadeCharacters`, `shadeActions`, `lowAccuracy`, `runA`,
  `runB`, `layouts`, `layoutLocked`.
- One counting rule added: the Party line (`RULES.md`).
- The version, at the right-hand end of the status line.

## 0.1.0 — 2026-10-05

The first native build: a Windows app in place of the browser build, reading
the event files the VibeXI addon writes. Damage, Healing and Compare, a
Settings page, and cards that float over the game as see-through panels.
