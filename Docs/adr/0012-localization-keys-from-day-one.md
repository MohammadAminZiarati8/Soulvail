# ADR-0012 — Every user-facing string is a localisation key from the first HUD label

**Status:** Accepted · **Date:** 2026-09-06

## Context

Android is a global market. Retrofitting localisation onto shipped UI is one of the worst jobs in game development: every raw string in every prefab, every concatenation, every hard-coded number format.

## Decision

No raw user-facing string anywhere. Text goes through `ILocalizer.Get(LocKey, params)` from the first HUD label. Core references `LocKey`s in specs (skill names, node descriptions); the Unity adapter resolves them from string tables. Numbers are formatted through the localiser too.

## Consequences

- **+** Adding a language is adding a table.
- **+** Designer-authored text (node descriptions) is already in a translatable place.
- **−** Slight friction on day one — a key instead of a literal. Near zero cost now; thousands of edits later.
