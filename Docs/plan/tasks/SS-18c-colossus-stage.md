# SS-18c — Sunscar Stage 20 registration and tuning

**Size:** S · **Depends on:** SS-18b · **Branch:** `ss-18c-colossus-stage`

**Design refs:** [GD §9.1](../../GameDesign.md#91-boss-design-rules-non-negotiable), [GD §9.6](../../GameDesign.md#96-the-sunscar--stage-20-the-sundial-colossus).

## Goal

Make the Colossus playable in a normal Sunscar run and through DT-01 Stage 20.

## Files

| Path under `Assets/_Project/` | Purpose |
|---|---|
| `Data/Enemies/SundialColossus.asset` | Enemy data, tuned HP, boss XP and prefab |
| `Data/Enemies/SundialColossusBoss.asset` | Central spawn, beats and adds |
| `Tests/Game/Authoring/ColossusStageTests.cs` | Stage flow, phase and live arena probes |
| `Core/Run/RunSession.cs` | Additive `ColossusBehaviour` factory |
| `Data/Modes/Sunscar.asset` | Additive Stage 20 roster entry |
| `Prefabs/Composition/BootScope.prefab` | Additive enemy and boss registrations |
| `Data/Localisation/English.asset`, `Data/Localisation/Pseudo.asset` | Additive boss name |
| `Tests/Game/Content/SunscarTests.cs` | Additive Stage 20 schedule and fixture catalogue |
| `Tests/Game/Authoring/HierophantStageTests.cs` | Extend existing Sunscar schedule expectation |

Unity `.meta` companions follow their assets.

## API and content

`boss.sundial_colossus` points to `enemy.sundial_colossus`. Use the `BossBehaviour`
wrapper with authored HP and name key `enemy.sundial_colossus.name`, English **Sundial Colossus**.

## Behaviour

1. Sunscar Stage 20 spawns one boss at the Dunes central spawn. Preserve Sunscar's earlier bosses, Stages 19 and 21 waves and all Jungle bosses.
2. Use 1.5 s beats at 66% and 33%. Phase 1 summons `enemy.jackal` twice with zero XP. Phase 2 clears survivors and summons none. Boss death clears any owned adds. Outside beats the boss takes ordinary damage. A hit skipping both thresholds follows the wrapper's existing deepest-phase rule.
3. Tune authored HP for 75–120 s including beats. Use a fixed seed, the DT-01 Stage 20 Ranger's starting gear and level, banked skill points, walking dodges and ordinary bow fire during open lanes. Log level, HP, first bow hit and seconds from spawn to kill. Boss XP grants at most one level. Boss death opens the north gate. Walking through starts Stage 21's regular waves.
4. Validate both passes and Stomp in the live Dunes at the Ranger's walk speed and collider size, including open floor, the boundary and near cover. Each armed attack needs a collision-free walking exit before its relevant impact. Exercise the return after walking out of the forward pass. If an attack stat needs a change, spec the correction before adjusting Core.

Tests > Behaviour > API > Files. Built code wins. Record deviations.

## Tests

| Test | Given / when / then |
|---|---|
| `Schedule_RegistersStage20Only` | Boot catalogue / resolve maps and name / correct boss and preserved stage entries |
| `Beats_SummonOnceAndClearAdds` | Ordinary thresholds, skip and boss death / tick / correct beats, add lifecycle, vulnerability and zero add XP |
| `RangerProbe_MeetsPacingAndOpensStage21` | DT-01 stage entry / fight and reach gate / target interval, at most one level, north gate and regular waves |
| `Warnings_AllowWalkSpeedEscapeInDunes` | Open, wall and cover samples / walk through both sweeps and Stomp cases / safe exit and no hit |

## Manual verification

1. **[Editor]** DT-01 Sunscar Stage 20. Fight through both beats, confirm the Slingers leave at 33%, and use the north gate.
2. **[device]** Read both sweep directions and the stomp clear edge at the normal portrait camera scale.

## As built

Commit body ≤1 500 bytes after implementation. Delete the spec and tick its ROADMAP row then.
