# LV-02c — Overflow leaves core and the modes

**Size:** M · **Depends on:** LV-02b · **Branch:** `dev`
**Design refs:** owner 2026-11-05 (Overflow goes away)

## Goal

A level past a full tree is a banked point and nothing else; `OverflowSpec`, `OverflowGranted` and every grant path are gone, and old saves still load.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ModeSpec.cs` | Core | `OverflowSpec`, ctor argument and `Overflow` removed |
| `Core/Events/ProgressionEvents.cs` | Core | `OverflowGranted` removed |
| `Core/Progression/LevelUpFlow.cs` | Core | `GrantOverflow`, `GrantOne`, `OverflowLevels`, ctor argument removed |
| `Core/Run/RunSession.cs` | Core | resume derivation removed |
| `Game/Authoring/ModeDefinition.cs` | Game | `_overflow`, `OverflowBlock` removed |
| `Data/Modes/Jungle.asset`, `Descent.asset` | — | `_overflow` block removed |
| `Tests/Core/Progression/LevelUpFlowTests.cs` | Tests.Core | Overflow rows → banked rows |
| `Tests/Core/Run/RunSessionResumeTests.cs` | Tests.Core | resume rows |
| *small edits* | | `RunState.OverflowLevels` removed; `JungleTests.SharedBlocks` |
| *ripple* | | every `new LevelUpFlow(…)`/`new ModeSpec(…)` naming `overflow` in Tests.Core/Tests.Game |

## Public API

```csharp
public LevelUpFlow(SkillTree tree, LevelTracker progression, SkillRunner runner, EffectRegistry effects,
                   IDomainEvents events, Veilrot veilrot, Ordeals ordeals = null, bool isFree = false,
                   PathFlow path = null);
```

## Behaviour

1. `Settle` on a full tree leaves the points banked; nothing applies a modifier.
2. Draw-mode `Open` on a full tree with points pending opens nothing and leaves them banked.
3. Resume accepts any save where `Level − 1 ≥ taken + pending`; the difference grants nothing. A save where it is negative is still refused.
4. No mode or asset carries an Overflow block.

## Tests

| Test | Given / When / Then |
|---|---|
| `Settle_FullTree_KeepsPointsBanked` | full tree, 2 pending / Settle / pending 2, stats unchanged |
| `Open_FullTree_OpensNothing` | draw mode, full / Open / no offer, pending kept |
| `Resume_FormerOverflowLevels_GrantNothing` | save level 30, 20 taken, 2 pending / resume / loads, max HP = growth only |
| `Resume_TooManyPicks_StillRefused` | taken + pending > level − 1 / resume / throws |

## Out of scope

- Marksman ranks (LV-02d).
