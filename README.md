# Soulvail

A fast, top-down, auto-aim action roguelite for Android. Pick a Vailkeeper, descend through endless stages of a collapsing afterlife, build a skill tree as you level, and see how deep you get before the Veil claims you.

**Engine:** Unity 6.3 (`6000.3.23f1`) · URP · Input System · Android, landscape

## Docs

| | |
|---|---|
| [Docs/GameDesign.md](Docs/GameDesign.md) | What the game is. Pillars, loops, enemies, bosses, difficulty math, Veilrot. |
| [Docs/Characters.md](Docs/Characters.md) | Classes, skills, the in-run skill tree, levelling. |
| [Docs/CoreCombat.md](Docs/CoreCombat.md) | Spec for movement, targeting, basic attack, and skill casting. |
| [Docs/Architecture.md](Docs/Architecture.md) | Hexagonal architecture: pure-C# core, Unity as body and senses, VContainer, data flow, scalability foundations. |
| [Docs/adr/](Docs/adr/) | Architecture Decision Records — why each decision was made. |

## Working on it

- Branches: `main` (tagged milestones) ← `dev` (integration) ← `feature/<slug>` (one task each, PR into `dev`).
- Binary assets are tracked with Git LFS — run `git lfs install` once after cloning.
- Set up Unity's YAML merge driver once per clone (see `CLAUDE.md`).
