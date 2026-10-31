# Soulvail — Progress

Overwritten, never appended; history is `git log`. **Capped at 3 500 bytes.** Owner checks and device notes: [OWED.md](OWED.md).

_Updated 2026-10-31, MK-02b built._

## Current State

| | |
|---|---|
| **Milestone** | None planned. M0–M5 tagged; `m6` owner to tag. |
| **Last merged** | **MK-02b**: Marksman path, eight ranks, Ranger slots, loc and placeholder icons. |
| **Next task** | **MK-03**: tree screen with path cards, rank pips and level gates. |
| **Verified** | EditMode 4 834: 4 833 pass, 0 fail, 1 known clock inconclusive; MK-02b 7/7. Compiler errors 0; new analyzer warnings 0; format clean (9 files). Boot/Jungle start smoke pass; root passes alone (batch teardown issue below). |

## What works

Portrait; Heroes/Places. Jungle 30 stages; Veilrot, Sanctum, disc off; Ranger alone.
Threat-budgeted waves → door; fifth-stage bosses: Descent Warden, Jungle Croaker except 10 Mother.
Mother: rooted, three facing shield buds/phase, five-lob barrage, stone-blocked pulsing spray/goo,
ringed pods swell 1 s → Frogs, 66/33% beats. Scripts: 1 = 3 Frogs; 2 = 2 Pitchers;
3 = both; 4 = 2 Puffballs; 6 = 2 Panthers; 7–9 mixed waves. Stage 11: Mossguard, then one + four
Frogs; larger Mossguard, shield blocks, 2 m spear stab. Stages 12–14: shield/spit, pincer, exam; 76 → 110 → 128
threat. All five have bodies; Pitcher lobs ringed; Descent capsules.
Ranger: pale trails, cyan Volley/enemy, Space roll, flinch; first arrow ~¼ s after stopping.
Levels bank points; paused tree picks. Death pays Shards. `TestStart._on` hides test start.
Boss bar one line, none overhead; boss adds pay no XP. Saves run v4/profile v5, older load.
Clash UI; `LocKey` strings. Ranger offers three path slots from level 11; Marksman has Steady Aim (5 ranks, level 11) and Eagle Eye (3, level 14). Boot collects path skills, loc and cyan-tinted placeholders. Choosing a path in play awaits MK-03's screen.

## What does not work yet

1. Balance: Gravecaller over / Emberwright under GD §12.4's band; Warden passes 120 s near stage 33; no natural run past 19.
2. Sanctum plays as zero; a Pact is ~1 offer in 62.
3. Past stage ~26 late waves are all Bloaters/Puffballs (`WaveComposer.Upgrade`).
4. No audio, no Elites, three bosses; no feel pass, no second language.

## Known issues

1. `ArenaView.OnValidate` warns "0 spawn point(s)" on reload; harmless.
2. `PlayerStat.ContactDamage` refused by `PlayerStats.Resolve` — awaiting a ruling.
3. `TimeManager.asset` re-serialises on Editor open; revert before every commit.
4. A save the tree refuses (`SkillTree.Restore` throws) strands the player; Continue offers the dead run.
5. Save with huge XP (`1e38`) hangs `LevelTracker`.
6. A bolt in the air at a stage boundary leaves its body (`ProjectileViews`, no `StageArrived` teardown).
7. A pooled body teleported by `Bind` can snap back on first `Move` (`autoSyncTransforms` 0).
8. `SanctumOpened` publishes before `IsSanctumOpen`; presenter draws a frame late.
9. Nothing damages a Wight. The Knight's Charge root curve snaps 0.22 m.
10. HUD ~15 draw calls (no atlas). No PlayMode row taps Continue. Disk near full.
11. Mother phase beats clear phase summons only; pod Frogs can survive (existing code; JG-12h As built).
12. Boot smoke batch: unloading a run can make CoverViews.Dispose reactivate a destroyed cover (MK-02b verification).
