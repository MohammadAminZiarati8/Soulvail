# SS-17c — Sunscar Stage 15 registration and pacing

**Size:** S · **Depends on:** SS-17b · **Branch:** `ss-17c-hierophant-stage`

**Design refs:** [GD §9.1](../../GameDesign.md#91-boss-design-rules-non-negotiable), [GD §9.5](../../GameDesign.md#95-the-sunscar--stage-15-the-sunscar-hierophant).

## Goal

Fight the Hierophant from a normal Sunscar run or a DT-01 Stage 15 start.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Data/Enemies/Hierophant.asset` | Enemy data, tuned HP and SS-17b prefab |
| `Data/Enemies/HierophantBoss.asset` | Central boss spawn, beats and summons |
| `Tests/Game/Authoring/HierophantStageTests.cs` | Stage, phase, damage probe and clear flow |
| `Core/Run/RunSession.cs` | Additive boss factory entry |
| `Data/Modes/Sunscar.asset` | Additive Stage 15 schedule entry |
| `Prefabs/Composition/BootScope.prefab` | Additive enemy and boss registrations |
| `Data/Localisation/English.asset`, `Data/Localisation/Pseudo.asset` | Additive boss name entry |
| `Tests/Game/Content/SunscarTests.cs` | Update schedule and protect regular stages |

Unity `.meta` companions follow their assets.

## API and content

`boss.hierophant` points to `enemy.hierophant`, with name key `enemy.hierophant.name`
and English display name **Sunscar Hierophant**. Use the `HierophantBehaviour` factory
and existing `BossBehaviour` wrapper.

## Behaviour

1. Sunscar Stage 15 spawns one boss centrally in the Dunes. Sunscar Stages 5, 10, 14 and 16 and the Jungle boss schedule retain their authored entries.
2. The wrapper owns 1.5 s beats at 66% and 33%. It spawns `enemy.jackal-priest` and `enemy.jackal`, one each, during the first beat. Both are targetable adds with zero XP. The priest can heal the wounded slinger and a hit interrupts the beam. Its boss exclusion applies. The second beat or boss death clears survivors.
3. Fit HP to 75–105 s, aiming for 90 s, from Stage 15 DT-01 arrival using the Ranger's actual starting gear and level, banked skill points, no actives and an open firing lane. Include both beats. Record arrival level, first bow hit, authored HP and elapsed time. Boss death opens the north portal to Stage 16's regular waves.
4. On the live Dunes, each strip centre and sampled walkable points along the strip have an unobstructed walk-speed route across a long edge within the full warning, allowing for collision radius. Check both members of a final pair. Validate this with the actual arena, pathing and player dimensions before signing off.

Tests > Behaviour > API > Files. Built code wins; record deviations.

## Tests

| Test | Given / when / then |
|---|---|
| `Schedule_RegistersStage15Only` | Boot content and Sunscar/Jungle schedules / resolve / correct boss, preserved neighbours and earlier bosses |
| `Phases_SummonAndClearJackals` | Damage through both beats, including a wounded slinger / tick / one of each, mend and interruption, zero XP, no survivors through final beat or boss death |
| `RangerProbe_WithinTargetAndOpensStage16` | DT-01 / kill with ordinary attacks / timing and portal requirements |
| `SealLayout_AllowsWalkingEscape` | Dunes geometry and walkable samples / exercise all three strips and final pair / reachable long edge within warning |

## Manual verification

1. **[Editor]** Enter through DT-01. Walk the warnings near cover, disrupt the priest, dodge the pair and take the north portal.
2. **[device]** Repeat on a portrait phone and verify both cues and safe edges are legible through weather.

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
