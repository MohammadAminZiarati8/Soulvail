# Soulvail — Invariants

Architecture §18, kept in its own file so it can be read without the other 36 KB of
[Architecture.md](Architecture.md). References written as **AR §18.x** point here.

## 18. Invariants

**Rules the code depends on.** Breaking one produces a bug that doesn't look like its cause. You may
change any of them — read the reason first and change it with the rule. Format: rule — why it holds
(test that pins it, where one exists). A task that adds an invariant adds a line here. Toolchain
traps are in [Traps.md](Traps.md).

### 18.1 Ordering

**`RunTicker` frame:** held pause returns early → `LevelUpPhase` → `CommandPhase` → snapshot →
clear intents → core tick → enemy moves → minion moves → `Physics.SyncTransforms()` → facts →
knockbacks (`FrameOrderTests`).

- Every command adapter (`TapToFocusAdapter.Poll`, `SkillSlotInput.Poll`, the movement-skill press)
  is polled from `CommandPhase`, never an `ITickable` or a uGUI `onClick` — else a tap lands a frame
  late or casts against stale senses.
- One `Physics.SyncTransforms()` between bodies and facts; never `autoSyncTransforms` — else sweeps
  resolve against last frame's bodies.
- `LevelUpPhase` sits above commands and below the `IsRunning` guard; a level-up opens between
  ticks; a paused frame is skipped (costs zero simulated time, so `RunState.Time` is play time); the
  levelling tick finishes. Inside it the splash is read first, only one screen opens per frame, and
  the pause is given back before it is taken (`RunPause.Pause` throws for a second holder).
- `ApplyMinionMoves` follows `ApplyEnemyMoves`, above the flush, and resolves ids through
  `MinionViews` — both registries number from 1, so a minion id through `EnemyViews` walks an enemy.
  `SnapshotBuilder` copies minions after enemies and before `WriteSenses`, which never covers minion
  slots.
- A view answering core with a fact takes `Step(dt)` from `RunTicker`, never its own `Update`.

**`RunSession.Tick`:** time → lure expiry → ingest (enemies + minions) → Veilrot → combat → take
the shot → skills → timed effects → zones → enemy behaviours → minions → projectiles → death drain +
rise → (dead? end) → XP drain → director → stage flow → motor → intent.

- `LureSystem.Tick` above ingest; a decoy redirects in `EnemySystem.Perceive` only (the one writer
  of the four player fields). `QuarryIsADecoy` has one reader, `ChaserBehaviour.EnterStrike`. Not a
  targeting system.
- `Veilrot.Tick` directly above combat and sole writer of `CombatBlackboard.Veilrot`; each Claiming
  drain step calls `PlayerCombat.AnnounceDeath` (the one `PlayerDied` publisher).
- `TryTakeShot` drains right after combat; a projectile weapon writes `PendingShot`, not a
  `ConeHitIntent`.
- `SkillRunner.Tick` after combat, before behaviours and projectiles — HP triggers and Bulwark read
  this tick's state.
- `TimedEffects.Tick` right after the runner, on `SimulatedClock` (never `IClock`); `ZoneSystem.Tick`
  right after it; within a zone, pulses before retirement; zones spawn at
  `CombatBlackboard.PlayerPosition`.
- `MinionSystem` ingests beside enemies and ticks after behaviours, above the death check; inside:
  expire → choose → walk → strike. A Wight is never an `EnemyAgent`; its kills go through
  `EnemySystem.ApplyDamage`.
- Projectiles tick after behaviours and before the death check (a shot never lands the tick it left).
- `EnemySystem.DrainDeaths` after every killing pass, above the death check, handed to
  `RisePassive.OnDeaths` on the same line. Core never subscribes to its own events. One
  `IRandom.Drops` draw per death, before anything can refuse the raise.
- XP drains into `LevelTracker` after the death check, before the director.
- Director after the death check, before the motor. `StageFlow` after the director, before the
  motor.
- `PlayerMotor.Tick` integrates velocity before facing.
- A boss's `BossPhases.Tick(Health.Fraction, dt)` runs after this frame's damage, before it acts;
  read `Health`, never `EnemyBlackboard.HpFraction` (frozen on a corpse).

**Stage boundary and snapshots**

- The boundary snapshot is taken on entering `Clear` — before the Sanctum opens. Purchases are
  refused outside the Sanctum.
- Every `RunSnapshot` is captured before anything draws for its stage (`RunSession.Start`'s first
  statement; the `Clear` edge).
- At a boundary: set `EnemySystem.Depth` before `EnemySystem.Clear`; `SpawnDirector.Clear()` before
  recomposing the `WavePlan`; clear projectiles and `Targeter` before recomposing; sweep decoys and
  the army silently; leave zones standing. Call `Targeter.Reset()`, never `PlayerCombat.Reset()`
  (that would heal).
- The sweep is silent, so views tear down on `StageArrived` (`MinionViews`). `ProjectileViews` has
  the same gap, unanswered.

**`RunSession.Start`**

- Restore order: `SplashFlow.TryDerive` + `Restore` → `LevelTracker.Restore` →
  `SkillTree.Restore` → runner told about Actives → `SkillRunner.Restore` →
  `LevelUpFlow.GrantOverflow` → wallet, banishes, Pacts, `Veilrot.Restore` → `Health.Restore`
  **last** (saved HP is absolute and clamped to the live max). Overflow and the borrowed branch are
  derived, not saved. Ordeals restore above the opening `Compose`. A new restore adds itself here.
- Compose the stage before `RunStarted`; begin the director after `SpawnAll`.

**Enemies**

- `EnemySystem.ApplyDamage` multiplies each hit by `DamageTaken.Value` before `Health`;
  `EnemyDamaged` reports the applied result (`DamageTaken_ScalesTheHit`).

- `ApplyDamage` leaves the corpse registered; a behaviour retiring another agent uses
  `DespawnAtEndOfTick`; the walk stays forward (order feeds `TargetScorer`'s tie-break).
- Ingest runs before anything reads an enemy, in two passes split by writer.
- `EnemyAgent.Initialise`: no-argument `Stat.RemoveAll()` on all stats, re-base `MaxHp`, then
  `Health.Reset()`. `Spawn` applies depth after registration, before `EnemySpawned`; depth refills
  health after the `MaxHp` modifier.
- An explosion comes from the spec's `ExplosionSpec`, never the behaviour kind, published after its
  `EnemyDied`.
- `EnemyHitFeedback.ResetVisuals` restores the opaque material before writing the colour.

**Events**

- Run lifecycle events publish before `IsRunning` moves. `EnemySpawned` after registration,
  `EnemyDespawned` after removal.
- A subscriber that must hear a run open subscribes from a constructor on the dependency chain,
  never its own `Start`.

### 18.2 The boundary

- `IRunSession.Tick` takes the snapshot alone; `Dt` rides on it; no `IClock` in the session.
- A port grows a member when its mechanic lands.
- Core decides every outcome. Unity reports a fact only when the answer needs colliders core lacks
  (`ReportConeHits`, `ReportChargeHits`); standing geometric questions are senses on the snapshot.
  Core holds no walls.
- `EnemyTickContext` is built once per tick; no behaviour stores it.
- Downstream of `SnapshotBuilder`, integrate `snapshot.Dt`, never `Time.deltaTime` (cosmetic timers
  excepted).
- `WorldSnapshot.Clear()` doesn't zero `Enemies`; readers stop at `EnemyCount`; writers assign every
  field.
- `IntentBuffer.Clear()` lowers flags only; readers check the flag first.
- `PlayerView.Velocity` is what core asked for, never `CharacterController.velocity`.
- Animation never gates a damage frame; no clip carries an Animation Event. `applyRootMotion` is off
  on every rig; importers' root node is empty.
- Build-now-show-later objects are instantiated under a deactivated root (`ArenaPool`), never
  instantiated then deactivated.
- Spawn points come from the arena via `WorldSnapshot.SpawnPoints`; `SpawnDirector.Begin` copies
  them.
- `FollowCamera` yaw is 0. If it ever turns, the −yaw goes into `SnapshotBuilder`, not core.
- A Wight moves through `IIntentSink.MinionMove`, never `EnemyMove`; `WorldSnapshot.Minions` is its
  own array sized from `MinionSystem.MaxConcurrent`.
- No live object leaves `RunState` (`Motor`, `Combat`, `Enemies`, `Projectiles` are `internal`
  with scalar reads). `RunState`'s constructor and setters are `internal`; tests go through
  `RunSession`.

### 18.3 Numbers and identity

- Float guards are `!(value > 0f)`, never `value <= 0f` (NaN). Guard before arithmetic that
  launders the sign. NaN is refused at every `Stat` door.
- A struct with an invariant checks both ends — `default(T)` skips the constructor.
- `ContentId` grammar: `^[a-z0-9]+(\.[a-z0-9_-]+)+$`. Loosen freely; tightening breaks saved
  content.
- `SeededRandom` streams: Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4. New ones take the next
  index; never renumber.
- `TreeRules.BranchCount` is the run's count; `SkillTreeSpec.BranchCount` (3) is per class. A
  borrowed branch is always index 3, appended, never interleaved.
- Choosing a position is exactly one draw: draw a start index, then walk deterministically.
- GD §12's formulas beat its tables.
- `WaveComposer` spends in `int` against a cumulative share, never a float remainder.
- Depth scaling is `PercentMult`. `StatCurve.stageOffset` is 0 or 1.
- Enemy despawn compacts (order feeds the tie-break).
- A `Stat` is lazy only while nothing subscribes to `Changed`.

### 18.4 Combat and perception

- All perception is XZ. Exception: `LineOfSightSense`, a 3D ray at `EyeHeight` 1.1 m on the `Cover`
  mask only. An unmeasured sight line or path means "can see".
- `Health.Tick` credits only the part of the step past the recharge deadline. `Health` publishes
  nothing. `HasShield` means "has a `ShieldSpec`"; use `Shield > 0` for "up".
- A telegraph is a promise: the director never cancels, moves or caps one away (the cap counts
  pending spawns). `Clear` is the exception.
- A Spitter's aim never cancels (the dodge is the flight); a Chaser's windup can.
- Walking in follows `PathDirectionToPlayer`; walking out is `−DirectionToPlayer`.
- `EnemyRegistry.Alive` means registered; check `IsAlive`.
- `Targeter`'s cadence accumulator is never reset by an immediate retarget. `FocusedTargetId` lags
  `Focus(id)` by a tick. `TargetScorer` breaks exact ties to the lowest id, even against the
  incumbent.
- `EnemySpec` doesn't validate `EnemyBehaviourKind`; `EnemySystem.Tick`'s dispatch is the loud place.
- A pooled `EnemyView` resets everything in `OnDespawn`; `EnemyViews` re-applies tint and scale on
  every spawn. Its `CharacterController` moves the body; the trigger `CapsuleCollider` is what
  sweeps hit.
- The move stick's touch region is the left 45 % of the screen; every later on-screen control must
  be a raycast target.

### 18.5 Soft spots

- `IsCurrentBlocked` suppressing the invulnerability retarget has no test.
- `RunSession.Tick`'s order has no single assertion.
- A `Health` driven to `MaxHp` 0 dies without a `DamageResult`.
- `Targeter`'s 2 s focus-drop delay and `FocusResolver`'s 3 m radius are `const`s.
