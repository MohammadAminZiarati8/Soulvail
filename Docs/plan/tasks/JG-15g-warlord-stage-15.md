# JG-15g — Stage 15 is the Warlord's: data, stones, spear, lane probe

**Size:** M · **Depends on:** JG-15f · **Branch:** `dev` (direct)
**Design refs:** GD §9.1 rules 5, 6, §9.2; AR §18.1 (facts phase), §18.2; MK-08b (probe adapter)

## Goal

The Jungle's stage 15 spawns the Mossguard Warlord in a stone arena; Unity answers its lane probe,
so a charge into a stone dazes it. Stages 5, 20, 25, 30 keep the Croaker, 10 the Mother.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Data/Enemies/MossguardWarlord.asset` | — | **New.** `enemy.mossguard_warlord` |
| `Data/Enemies/MossguardWarlordBoss.asset` | — | **New.** `boss.mossguard_warlord` |
| `Prefabs/Arenas/Jungle/Cover_WarlordStones.prefab` | — | **New.** Variant of `Cover_MotherStones` with the Warlord's layout |
| `Prefabs/Projectiles/WarlordSpear.prefab` | — | **New.** `ProjectileView` on the `MossguardSpear` mesh, tip first |
| `Game/Adapters/LaneProbeAdapter.cs` | Game | **New.** Answers `LaneProbe` with a Cover-mask sphere cast |
| `Tests/Game/Adapters/LaneProbeAdapterTests.cs` | Tests.Game | **New** |
| *small edits* | | `Jungle.asset` boss roster row; `BootScope.prefab` catalog lists; English/Pseudo name rows; `RunScope` registers the adapter; `RunTicker` resolves it right after `_bindProbe`; `JungleTests` |

## Public API

```csharp
public sealed class LaneProbeAdapter {
  public LaneProbeAdapter(LayerMask cover);
  public bool Probe(in LaneProbeIntent probe, out float distance);
  public void Resolve(IntentBuffer intents, IRunSession session);
}
```

## Behaviour

1. **Enemy:** `Boss` kind; `MaxHp` set by rule 6; `MoveSpeed` 1.4; `ContactDamage` 20;
   `XpValue` 500; `ThreatCost` 40; `BodyScale` 2.03 (Mossguard 1.27 × 1.6); body
   `Mossguard_Warlord.prefab`; projectile `WarlordSpear`; `MarksLanding` on; cover
   `Cover_WarlordStones`. Name `enemy.mossguard_warlord.name` = "Mossguard Warlord".
2. **Boss:** phases 1 / 0.66 / 0.33, no phase summons (the war cry is its adds); `BeatSeconds` 1.5;
   `SpawnsCentral` on.
3. **Roster:** a `_bossRoster` row `boss.mossguard_warlord`, `_atStage` 15. Stage 15 stays
   unscripted.
4. **Stones:** five stones, layer Cover, colliders ≥ 1.2 m radius, ~6.5 m from the centre at uneven
   angles so every approach has a stone within a charge's 14 m, none within 3 m of the centre.
5. **Probe:** `Probe` sphere-casts `Radius` along `Direction` for `Reach` on the cover mask; hit →
   distance to contact. `Resolve` answers every `LaneProbes` entry via `ReportLaneStop` in the
   facts phase, after the bind probe.
6. **Fight length:** `Jungle_Warlord_FightIsInTheBand` — Mother's formula at stage 15 with 33 DPS ×
   0.5 uptime (the front guard), guards excluded, plus two beats; `MaxHp` chosen to land ~95 s,
   inside 75–120. The number goes in *As built*.

## Tests

| Test | Given / When / Then |
|---|---|
| `Jungle_Warlord_HasItsNumbers` (1) / `Jungle_WarlordBoss_HasItsPhases` (2) |
| `Jungle_FifthStages_HaveTheirBosses` — 10 Mother, 15 Warlord, others Croaker (3) |
| `Jungle_StagesFifteenAndSixteen_AreNotScripted` stays green (3) |
| `Jungle_WarlordStones_AreCoverAndRingTheCentre` (4) |
| `LaneProbe_StoneAhead_ReportsDistance` / `LaneProbe_Clear_ReportsMiss` / `Resolve_AnswersEach` (5) |
| `Jungle_Warlord_FightIsInTheBand` (6) / `Jungle_StageFifteen_StartsWithTheWarlord` — DT-01 start |

## Manual verification (Editor / device)

1. **[Editor]** DT-01 stage 15: the Warlord spawns central among five stones; roll behind him and
   shoot; bait a charge into a stone — 2 s daze; Binding Shot him to a stone; Snare his lane.
2. **[Editor]** Fight lands in 75–120 s with a Marksman build; adds vanish at 66 % and 33 %.

## Out of scope

- Balance beyond rule 6; a second arena layout; Gravemaw (deferred, GD §9.2).

## As built

_Filled at merge._
