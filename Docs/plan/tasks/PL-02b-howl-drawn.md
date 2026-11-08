# PL-02b — Howl authored

**Size:** S · **Depends on:** PL-02a · **Branch:** `dev` (direct)
**Design refs:** CH §3.7 (Howl); MK-07b (an active authored)

## Goal

Howl in the Packleader's second Lv-11 row, learnable and auto-cast. It already heals and hastes
(PL-02a); the bars climbing are its only drawing until PL-02c.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Skills/Packleader/Howl*.asset` | — | **New.** Four ranks; icon `Icon_Placeholder` |
| `Data/Effects/Packleader/Howl*.asset` | — | **New.** The `Howl` effect, rank `ModifyStat`s and the cooldown cut |
| `Game/Authoring/HowlDefinition.cs` | Game | **New.** The bonus (0.25), validated as `Howl` does |
| `Data/Paths/Packleader.asset` | — | Howl appended as the second row, Lv 11 |
| `Data/Localisation/English.asset`, `Pseudo.asset` | — | Howl's name and four rank lines |
| `Tests/Game/Authoring/HowlAuthoredTests.cs` | Tests.Game | **New** |

## Behaviour

1. **Ranks** (PL-02a rule 6): 1 — the active, bonus 0.25, cooldown 14 s, trigger
   `LowestPetHpFraction Below 0.5`, auto-cast on; 2 — Player `HowlHeal` +0.1; 3 — Player
   `HowlSeconds` +2; 4 — cooldown −3 s (`ModifySkillCooldown`).
2. The path has two rows, Pack Hounds then Howl, both Lv 11.
3. Howl is a circle in the arc once learned (no code: the arc reads the learned active).
4. Every rank has a name and a one-line effect in English and Pseudo, from `LocKey`s.

## Tests

| Test | Given / When / Then |
|---|---|
| `Howl_FourRanks_Numbers_Trigger` (1) | Each rank's effect, stat, value; trigger, 14 s, auto-cast on |
| `Path_TwoRows_PackHoundsThenHowl_At11` (2) | Row order and levels |
| `HowlDefinition_ToEffect_Bonus` (1, 3) | 0.25; a bad bonus is refused, naming the asset |
| `HowlRanks_HaveLocKeys` (4) | Both tables |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 at stage 12: learn Howl, let a hound drop below half: Howl fires on its own,
   bars climb, bites quicken for 4 s (no howl animation or pulse until PL-02c).

## Out of scope

- The howl clip, glow and pulse (PL-02c). Pack Tactics, Thick Pelts (PL-03).

## As built

_Filled at merge._
