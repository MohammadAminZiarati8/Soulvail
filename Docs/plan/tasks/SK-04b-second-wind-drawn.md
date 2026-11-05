# SK-04b — Second Wind drawn and authored

**Size:** M · **Depends on:** SK-04a, SK-01b · **Branch:** `dev` (direct)
**Design refs:** CH §3.5 (Second Wind); GD §5.2 (the roll button); FX-05 (the Immune pop)

## Goal

Second Wind is in the game: the roll button shows its charges, and a hit the guard turns away pops
"Dodged" over the Ranger.

## Files

| Path (under `Assets/_Project/`) | Kind | Purpose |
|---|---|---|
| `Game/Controls/SkillButton.cs` | Game | Roll button: charge pips and refill sweep |
| `Game/Views/RangerAnimatorView.cs` | Game | Guard blocks reuse FX-05's pop with the "Dodged" key |
| `Data/Skills/Skirmisher/SecondWind{,2,3}.asset` | assets | **New.** + their effect assets |
| `Tests/Game/Controls/RollChargesTests.cs` | Tests.Game | **New** |
| *small edits* | | `Data/Paths/Skirmisher.asset` (inserted after Hit and Run, unlock 14), `English`/`Pseudo`, `SkillIconBook`, `StatusIconBook` (`status.second-wind`), `SkirmisherPathTests` |

## Behaviour

1. **Assets:** `skill.skirmisher.second-wind`, `-2`, `-3`, SK-04a rule 6 exactly; Passive.
2. **Roll button:** with `MaxCharges` 2, two small pips sit on the button's rim; a filled pip is a
   ready charge. The cooldown sweep shows the refilling charge, and the button stays bright while
   one charge is ready. With 1 charge the button looks exactly as today.
3. **Dodge pop:** a hit blocked by the guard shows FX-05's world-space pop with `ui.combat.dodged`
   (*"Dodged"*); Killshot's blocks keep *"Immune"*.
4. Status icon `status.second-wind`: a placeholder in the MK-05 row style.
5. **Loc:** *"Second Wind — Roll cooldown −0.5 s."*; ranks *"Roll cooldown −1 s. The first hit
   within 1 s after a roll misses."*, *"Two roll charges."*

## Tests

| Test | Given / When / Then |
|---|---|
| `Assets_MatchTheRankTable` / `Path_SecondWindAtFourteen` (1) |
| `OneCharge_NoPips` / `TwoCharges_TwoPips_FillAsReady` (2) |
| `OneReady_ButtonBright_SweepShowsRefill` (2) |
| `GuardBlock_PopsDodged` / `KillshotBlock_StillImmune` (3) |
| `Loc_Keys` (4, 5) |

## Manual verification (Editor / device)

1. **[Editor]** Skirmisher with Second Wind 3: roll twice in a row; the pips empty and refill one
   at a time. Roll into a Frog's bite: "Dodged" pops and no health is lost; the next bite lands.

## Out of scope

- Final icon art; sound.

## As built

_Filled at merge._
