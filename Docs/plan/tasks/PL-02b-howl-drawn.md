# PL-02b — Howl drawn and authored

**Size:** M · **Depends on:** PL-02a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Howl); MK-07b

## Goal

Howl in the Packleader's Lv-11 row: every hound throws its head back, a cyan pulse rolls out, the
bars climb.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Skills/Packleader/Howl*.asset` | — | **New.** Four ranks; trigger and cooldown from PL-02a |
| `Data/Effects/Packleader/Howl*.asset` | — | **New** |
| `Game/Authoring/HowlDefinition.cs` | Game | **New.** The bonus |
| `Data/Paths/Packleader.asset` | — | Howl in the second Lv-11 row |
| `Game/Views/PetView.cs` | Game | `Howl` clip on `PackHowled`; a faint cyan glow while the attack-speed buff lasts |
| `Game/Views/HowlPulseView.cs` | Game | **New.** A cyan ring from the lead to 6 m in 0.5 s |
| `Tests/Game/Authoring/HowlAuthoredTests.cs` | Tests.Game | **New** |
| *small edits* | | `Prefabs/Vfx/HowlPulse.prefab`; `RunScope`; `English`/`Pseudo`; `SkillIconBook` |

## Behaviour

1. Howl is a circle in the arc once learned, auto-cast on by default.
2. On `PackHowled` every standing hound plays `Howl`; a cyan pulse rolls out from the lead.
3. Each hound glows faintly cyan while its buff lasts; the glow ends with it.
4. The heal shows as each bar climbing; no number pops.

## Tests

| Test | Given / When / Then |
|---|---|
| `Howl_FourRanks_Numbers_Trigger` (1) |
| `Howled_EveryStandingHoundHowls` (2) |
| `Pulse_PlayerCyan_SixMetres` (2) |
| `Glow_EndsWithBuff` (3) |

## Manual verification (Editor / device)

1. **[Editor]** Let a hound drop below half: Howl fires on its own, all hounds howl, bars climb,
   bites quicken for 4 s.

## Out of scope

- Pack Tactics, Thick Pelts (PL-03).

## As built

_Filled at merge._
