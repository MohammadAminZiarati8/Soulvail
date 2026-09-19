# Soulvail — Roadmap archive: M1, Combat feel

_Moved verbatim from [ROADMAP.md](../ROADMAP.md) on 2026-09-20, when the closed milestones' tables and ledgers were archived to keep the live file to the milestone in progress. Links are re-based to this folder. A stub heading stays in the ROADMAP for every heading here, so links into them still resolve. The milestone's log is [PROGRESS-M1.md](PROGRESS-M1.md)._

---

## M1 — Combat feel

**Goal:** the moment-to-moment loop — target, swing, dodge — feels good on device with untextured capsules.
**Done when:** [CC §8](../../CoreCombat.md) checklist passes on device and `m1` is tagged.

**Status: complete, tagged on Editor evidence — the same terms as `m0`, for the same missing phone.** 452 EditMode + 3 PlayMode tests green, zero errors, zero analyzer warnings, and no tuning edit needed anywhere: every CC §7 number already matched the asset shipping it. The owner playtested the fight and called it good, which is M1's question answered — but answered *in the Editor*, so the feel verdict is provisional and the three `[device]` rows (multi-touch, haptics, sustained 60 fps with 12 chasers) are carried as debt, joined by the whole of M1-20's checklist. One checklist row is deliberately unresolved rather than passed: the out-of-range focus tap, still in the parking lot with its three ways out.

| ID | Task | Size | Depends on | Status |
|---|---|---|---|---|
| [M1-01](../tasks/M1-01-stat-and-modifier.md) | `Stat` + `Modifier` stack: Flat → PercentAdd → PercentMult, by source, cached, describable | S | M0 | ☑ |
| [M1-02](../tasks/M1-02-health-and-aegis.md) | `Health`, `ShieldSpec`, `DamageResult`: HP, Aegis recharge, hit i-frames | M | 01 | ☑ |
| [M1-03](../tasks/M1-03-target-scorer.md) | `TargetCandidate`, `TargetingSpec`, `TargetScorer` | M | 01 | ☑ |
| [M1-04](../tasks/M1-04-targeter.md) | `Targeter`: cadence, immediate retarget, focus override, all-blocked state | S | 03 | ☑ |
| [M1-05](../tasks/M1-05-enemy-entities.md) | `EnemySpec`, `EnemyAgent`, `EnemyBlackboard`, `EnemyRegistry` | M | 02 | ☑ |
| [M1-06](../tasks/M1-06-enemy-system.md) | `EnemySystem`: spawn plan, snapshot ingestion, perception, enemy events | M | 05 | ☑ |
| [M1-07](../tasks/M1-07-enemy-authoring-and-views.md) | `EnemyDefinition`, `Husk.asset`, `EnemyView`, `EnemyViews`, dummy prefab | M | 06 | ☑ |
| [M1-08](../tasks/M1-08-player-combat.md) | `PlayerCombat`: health, targeting, `CombatBlackboard`, combat events; `RunSession` composes | M | 02 04 06 | ☑ |
| [M1-09](../tasks/M1-09-tap-to-focus-and-reticle.md) | `IPlayerCommands.FocusTarget/ClearFocus`, 3 m resolver, `TapToFocusAdapter`, `ReticleView` | M | 07 08 | ☑ |
| [M1-10](../tasks/M1-10-weapon-and-cone-intent.md) | `WeaponSpec`, `Weapon` cadence + damage frame, `ConeHitIntent` | M | 08 | ☑ |
| [M1-11](../tasks/M1-11-cone-hits-to-damage.md) | `ReportConeHits` fact → damage → `EnemyDamaged` / `EnemyDied`, despawn | S | 10 | ☑ |
| [M1-12](../tasks/M1-12-cone-overlap-and-hit-feedback.md) | `ConeOverlapQuery`, `RunTicker` fact loop, hit flash, dissolve death | M | 07 11 | ☑ |
| [M1-13](../tasks/M1-13-focus-ramp.md) | `FocusSpec`, `FocusTracker` → fire-rate modifier; ground glow | M | 10 | ☑ |
| [M1-14](../tasks/M1-14-charge-skill.md) | `MovementSkillSpec`, `ChargeSkill` (pure): cooldown, buffer, i-frame window | S | 02 | ☑ |
| [M1-15](../tasks/M1-15-charge-integration.md) | `ChargeIntent`, `IPlayerCommands.MovementSkill`, `ReportChargeHits`, knockback intent | M | 11 14 | ☑ |
| [M1-16](../tasks/M1-16-charge-view-and-skill-button.md) | `PlayerView` charge motion + sweep, `SkillButton`, movement-skill input | M | 15 | ☑ |
| [M1-17](../tasks/M1-17-hud.md) | `HudPresenter`, HP bar with ghost trail, shield ring, cooldown, death → Menu | M | 08 16 18 | ☑ |
| [M1-18](../tasks/M1-18-chaser-ai.md) | `ChaserBehaviour` FSM, `EnemyMoveIntent`, strike damage, telegraph event | M | 06 08 | ☑ |
| [M1-19](../tasks/M1-19-pooling-respawn-navmesh.md) | `ViewPool`, `RespawnPolicy` (keep 12 alive), NavMesh bake + path sense | M | 12 18 | ☑ |
| [M1-20](../tasks/M1-20-haptics.md) | `HapticsListener`, Android vibrator, toggle | S | 11 15 | ☑ |
| [M1-21](../tasks/M1-21-acceptance-and-tag.md) | M1 acceptance: CC §8 on device, tuning, tag `m1` | S | all | ☑ |

---

