# Soulvail — Architecture §18 as it stood at M4-05b

_The long form of [Architecture.md §18](../../Architecture.md#18-invariants), moved here verbatim on 2026-09-20 when that section was rewritten as rule, reason and origin only. Each row's argument — the failing test, the alternatives refused, the task history — is kept below and in the owning task's spec *As built*. Links are re-based to this folder._

---

## 18. Invariants

**Rules the code currently depends on.** Each was decided in a task, is load-bearing somewhere, and
breaking it produces a bug that does not look like its cause. This is the answer to *"may I change
this?"* — the answer is yes, but read the reason first and change the reason with it.

Toolchain traps — things that lie to you rather than rules the code relies on — live in
[Traps.md](../../Traps.md). The two files are deliberately separate: one is about our design, the other
is about Unity's.

### 18.1 Ordering

| Invariant | Break it and | Set in |
|---|---|---|
| `RunTicker`'s frame order: commands → snapshot → clear intents → core tick → bodies → **sync** → facts → knockbacks | a tap lands a frame late; a cleared buffer erases an unread intent; a sweep resolves against last frame's arena | M0-16, M1-09, M1-12, M1-15. **Asserted since M2-11b** by `Tests/PlayMode/FrameOrderTests.cs`, by observation rather than by reading the method. **Every step is now covered, including *commands*, which was the one exemption until M3-10a**: the first two members of that phase reach core only through the Input System, which the PlayMode assembly deliberately does not reference, and `SkillSlotInput` reaches it through an `int` a button wrote |
| **Every command adapter is polled from `RunTicker.CommandPhase` and none of them is an `ITickable`.** Three members: `TapToFocusAdapter.Poll()` (M1-09), **`SkillSlotInput.Poll()`** (M3-10a), the movement-skill press (M1-16). Their order *within* the phase is not load-bearing and is named as such; being *in* it is | registered as `ITickable`s instead, the order commands land in — and whether they land before or after the snapshot — is whatever order `RunScope` happened to register them in, decided by an edit somewhere else entirely and invisible until something goes subtly wrong. A uGUI `Button` calling `IPlayerCommands` from its own `onClick` has the same defect for a different reason: Unity orders the `EventSystem`'s `Update` against nothing. **The consequence is a tap acted on one frame late, or a cast resolved against senses taken before the player asked for it** | M1-09, M1-16, **M3-10a.** `FrameOrderTests.Frame_SlotPollSitsBesideTapToFocus` is the first row to observe this phase at all: it presses a slot, runs one frame, and asserts the command arrived **while the snapshot still read empty** — ordering only, never what a physics sweep found (M3-08a's lesson about this fixture's flake rate). `SkillBarPresenterTests.Input_PressIsSentInCommandPhase` is the EditMode half, over a whole `RunTicker` |
| **`Physics.SyncTransforms()` sits between the bodies step and the fact phase**, and the row above is not true without it | `Physics.autoSyncTransforms` is **0 project-wide**, so moving a transform does not move the collider the physics scene holds. The ordering is then honoured by the call order and *quietly not by the code*: a cone or a dash sweep resolves against where the bodies stood at the end of the previous frame, so a swing misses an enemy that stepped into it this frame. Rare, silent, and indistinguishable from bad aim. **One flush at the one seam, not the project setting** — the setting pays the same cost on every transform write in the game rather than once at the only point that asks physics a question | **M2-15a.** Diagnosed across M2-12a…M2-15, where `FrameOrderTests` failed *intermittently* — 9/11 then 11/11 on one unchanged tree — which is why it read as flakiness for four tasks. Three green PlayMode runs after the fix. **Adapter fixtures still call `SyncTransforms` themselves** (`ConeOverlapQueryTests`, `LineOfSightSenseTests`, `SnapshotBuilderTests`): they move transforms directly rather than through `RunTicker`, so this line is not theirs to inherit |
| `RunSession.Tick`: time → ingest → combat → **skills** → **timed expiry** → **zones** → enemy behaviours → **projectiles** → (dead? end) → **xp drain** → **director** → **stage flow** → motor → intent | the gun aims at where enemies *were*; the motor turns before it knows its facing | M1-06, M1-08, M2-05, M2-07a, M2-10, M3-01a, M3-06, M3-11a-ii, M3-11b |
| **`TimedEffects.Tick` sits immediately after `SkillRunner.Tick`, and therefore also above `ProjectileSystem.Tick`.** Both halves are the mechanic's rather than a preference | *After the runner*, because it may cast this frame: expiring first would take a grant back and hand the same one straight over again on the tick a skill recasts — a `ShieldGrantExpired` for a shield the player never stopped having, and one instant between the two with no shield at all. *Above the projectile step*, for the reason the row below gives the runner: a grant that expired **after** this tick's arrivals were resolved would have absorbed a hit it was no longer entitled to. Each half is proved RED under its own swap and GREEN under the other's (`Timed_TicksAfterTheRunner`, `Timed_TicksBeforeTheProjectileStep`), because a row asserting only that both things happened passes against the wrong order. **The clock it reads is written on the line beside `State.Time += Dt`** and is a `SimulatedClock`, never `IClock`: `IEffectHandler<T>.Apply` is handed no time, and a wall-clock deadline would drain a shield through a level-up screen at `timeScale` 0 | M3-11a-ii |
| **`ZoneSystem.Tick` sits immediately after `TimedEffects.Tick`, and inside one zone the pulses come before the retirement.** Two orderings in one row because both are the mechanic's | *After the runner*, so a zone cast this tick exists this tick — a player who drops below 60 % is standing on healing ground in the same frame. *After the timed effects*, because these are the game's two expiry mechanisms and interleaving them would make the order a shield comes off in depend on whether a zone happened to end on the same tick; proved RED under its own swap and alone (`Zone_TicksAfterTheTimedEffects`, 10 passed / 1 failed with the line moved above `_timed.Tick`). *Pulses before the retirement*, because a zone is alive up to and including its last instant and the two clocks meet exactly there: Consecrate's 6 s over its 0.5 s interval puts the twelfth pulse on the expiry second, so retiring first makes the authored skill worth 33 hit points instead of 36 with nothing anywhere recording the deduction (`Zone_PulsesOnTheTickItExpires`, one of four rows that redden under that swap). **The position it places a zone at is `CombatBlackboard.PlayerPosition`, not a `Tick` parameter**, because `Spawn` happens inside `IEffectHandler<T>.Apply` — the skills step, not this class's tick — so no parameter of this class's reaches the moment that needs it, and a cached one would be a frame stale | M3-11b |
| **`SkillRunner.Tick` sits between `PlayerCombat.Tick` and the enemy behaviours, which puts it *above* `ProjectileSystem.Tick`.** The sequence row above says where; this row is why the second half of it is not free to move | *Above combat*: every trigger is a predicate over the `CombatBlackboard`, and `PlayerCombat.Tick`'s `UpdateBlackboard` fills seven of the nine fields one can read — so a Consecrate triggered on HP fires a frame after the hit that should have caused it, on every hit, for ever. *Below the projectile step*: `IncomingProjectiles` is written by that step and nowhere else, at the end of its own pass, so read above it the field is the count of bolts **still in the air before this tick's arrivals are resolved** — which is exactly what CC §6.4's Bulwark means by *"an enemy projectile is inbound"*, a shield raised **before** the bolt lands. Read below it, the same field describes the sky *after* the hit and the archetype's whole answer becomes a shield put up over a wound. **The staleness is the mechanic, not a lag to fix** | **M3-06.** Two rows in `SkillRunnerTests`, each red under its own swap and neither under the other's: `Session_TicksTheRunnerAfterCombatAndBeforeTheBehaviours` (the cast lands on the first tick after the damage, not the second) and `Session_TicksTheRunnerBeforeTheProjectileStep` (the `SkillCast` **precedes** the `PlayerDamaged` in that one tick's recorded order, rather than merely both happening) |
| `StageFlow` ticks **after** the director and **before** the motor, **and the boundary snapshot is taken on entering `Clear`** — `RunSession.Tick` reads the phase before ticking the flow and compares it after | the flow reads `IsStageComplete` one frame stale, so every stage ends a frame late; or a stage ends in an arena whose run ended this tick, because the death check is upstream of both. Take the snapshot on the *phase* rather than on the *edge* and a stage parked at its own door writes a file once a frame; take it at the next stage's `StageArrived` and the beat in between — the gate wait, which is GD §7.3's "put the phone down" point — is unsaved | M2-10, M2-14a |
| **Every `RunSnapshot` is captured before anything has drawn for the stage it describes.** The opening capture is the first statement in `RunSession.Start`, above the composition; the boundary capture is on entering `Clear`, three phases above the recompose at the end of `Transition` | a resumed run restores a position that has already spent the composition, so it deals itself a *different* stage under the same number — the same seed producing different waves, which reads as a content bug for a week. This is why `Start` takes the position and announces it in two separate places, and why nothing may draw between entering `Clear` and the crossing | M2-14a, ledger row 1 |
| A stage boundary calls `SpawnDirector.Clear()` **before** recomposing the run's one `WavePlan` | the director keeps ticking against a plan being rewritten underneath it: it sizes its per-wave arrays from the plan's dimensions at `Begin` and trusts them for ever, so a stage that grows a wave — GD §12.2's W(n) does, from two to three at stage 5 — walks `SweepTheDead` off the end of them on the first frame of the new arrival. **Nobody may recompose a plan a director is still holding** | M2-10 |
| A stage boundary sets `EnemySystem.Depth` **before** `EnemySystem.Clear`, and clears the projectiles and `Targeter` before recomposing the plan | the first body of the new stage is priced at the stage that just ended, with nothing reporting it; a bolt fired at the old arena's floor lands on the player at coordinates that no longer mean anything | M2-10 |
| The boundary calls `player.Targeter.Reset()` and **never** `PlayerCombat.Reset()` | every stage boundary becomes a free heal, which deletes the attrition GD §12.5's death horizon is made of and pre-empts the Sanctum's heal before the Sanctum exists | M2-10 |
| Projectiles tick **after** the enemy behaviours and **before** the death check | a shot fired this tick lands on the tick it left, erasing the flight the player is meant to walk out of; or a killing bolt leaves the player at zero hit points for a frame, still playing | M2-07a |
| The director ticks **after** the death check and **before** the motor | a wave is telegraphed into an arena whose run ended this tick; or the director sees a player position the rest of the tick did not | M2-05 |
| **Experience is drained into the `LevelTracker` after the death check and before the director** — `RunSession.Tick` calls `State.Progression.Grant(State.Enemies.DrainXp())` between the two. It is granted on the *tick*, never on the fact that reported the kill | put it above the death check and a run that ended this tick levels nobody's screen: the `LeveledUp` lands in a `RunScope` mid-teardown, where nothing can show it. Put it below the director and the stage flow, and a `LeveledUp` earned by a stage's **last** kill arrives *after* that tick's `StageCleared` and after the boundary snapshot taken with it — so M3-01b's write carries the level the player had a frame ago rather than the one they just earned, and the run resumes one level short. A kill reported between ticks by `ReportConeHits` accrues on `EnemySystem.PendingXp` and is paid on the next tick, at most a frame late, which is the lag every fact already has (ADR-0003) | M3-01a |
| **`RunSession.Start`'s restore block is an order, and it is now six lines long**: `LevelTracker.Restore` (M3-01b rule 7) → **`SkillTree.Restore(takenNodeIds)`** (M3-03 rule 5) → the runner is told about the restored Actives (M3-06 rule 5) → **`SkillRunner.Restore(manualSkillIds)`** (M3-07b rule 7) → **`LevelUpFlow.GrantOverflow(derived)`** (M3-08a rule 9) → **`Health.Restore(hp, shield)` last**, after everything that can move `MaxHp`. Three of the five gaps are load-bearing and the others are written in anyway, because the block is read as an order and a line placed outside it invites the next one to be placed anywhere. **The slots go below the actives**: a slot naming a skill the runner has not been told about yet is indistinguishable from a stale id, so `Restore` would drop every slot in silence and the run would come back with empty buttons and no error. **Overflow goes above `Health.Restore` for `SkillTree.Restore`'s reason, from a second writer**: fourteen Overflow levels are +28 % `MaxHp`, so replayed after the clamp a run saved at 170 of 179 comes back at 140. **It is also the one line here that is derived rather than read** — `Level − 1 − TakenNodeCount − PendingLevelUps`, because nothing on the snapshot carries it and `CurrentVersion` deliberately stayed 3; a result below zero is a save whose picks do not add up and refuses the run. **No task owns this row and every task adds to it** | the saved hit points are absolute and are clamped against the **live** maximum, and the tree is what moves that maximum. A `+20 max HP` node replayed after the clamp means a run saved at **150 of 160 comes back at 140 of 160**: the player loses the difference once per resume, silently, in their disfavour, and the only symptom is a bar slightly shorter than the one they put the phone down in front of. `PlayerShield` has the same shape against `ShieldSpec`'s maximum the day a node moves it (M3-11). **This is why M3-03 depends on M3-01b rather than the other way round** — the absolute capture had to exist before there was anything that could move the maximum it is clamped against | **M3-03**, extended by **M3-07b.** `RunSessionResumeTests.Start_RestoresNodesBeforeHealth` is the tree row and `Start_RestoresSlotsAfterTheRunnerKnowsTheActives` is the slot one, and each fails with its own two lines swapped rather than merely reporting a different number. Both restores are silent and gated for `LevelTracker.Restore`'s reasons: nothing may publish before `RunStarted`. **They differ on what they refuse** — the tree throws for an unknown node, because a taken node *is* the run's power; the runner drops an unknown slot, because a slot is only where a button sits |
| `RunSession.Start` composes the stage **before** `RunStarted` and begins the director **after** `SpawnAll` | a mode that introduces nothing at its own starting stage throws with the run announced (ledger row 3 again); or wave 1's concurrency check cannot see the arena's dressed-in enemies | M2-05 |
| `EnemySystem.ApplyDamage` leaves the corpse **registered**, which is what lets a behaviour kill itself from inside `EnemySystem.Tick`'s own pass — **and a behaviour that retires *another* agent queues through `EnemySystem.DespawnAtEndOfTick`, never `Despawn`** | a direct `Despawn` compacts the registry in place: `EnemyRegistry.Despawn` is an order-preserving *shift*, so it moves the tail of the array `Registry.Alive` spans down and nulls the slot that falls off the end, and the forward walk reads past its own length into a null. **The old wording here predicted the answer would be a backwards walk; M4-01b is the day the debt came due and it refused that answer** — reversing the pass changes the order every enemy in the arena acts in, which is the order `TargetScorer`'s tie-break reads and a seeded run replays. A queue drained below the loop costs one array sized at the registry's capacity and changes nothing else. It is also what makes a same-tick clear-then-summon safe: nothing is freed during the walk, so a summon cannot recycle an agent the span still points at | M2-08, **M4-01b** |
| **A boss's phase check runs after this frame's damage and before the boss acts**, inside `BossBehaviour.Tick`: `BossPhases.Tick(Health.Fraction, dt)` first, then the clear, the beat, the summon and the announcement, and only then the delegated behaviour | `PlayerCombat.Tick` resolves this frame's cone above the enemy behaviours, so a hit that crosses 33 % opens its beat on the frame it landed rather than one later. The fraction is read from `Health`, **never from `EnemyBlackboard.HpFraction`**: `EnemySystem.Perceive` skips the dead, so a corpse's copy is frozen at its last *living* reading and a machine driven from it reads a dead boss as healthy — which is also why `SpawnDirector` ends a boss stage on `IsAlive` and not on a fraction reaching zero. The adds are cleared on the beat's **first** tick rather than its last, because the point of the beat is to reset pressure now (GD §9.1 rule 3) | **M4-01b.** `Order_CrossingIsSeenTheFrameTheHitLands` asserts the beat is open in the events of the frame the hit landed and that `BossPhaseChanged` precedes `BossBeatStarted` within it; `Boss_ACorpseIsNotAHealthyBoss` is the corpse half, pinned against M4-01a's `Blackboard_ACorpseKeepsItsLastReading` |
| An explosion is triggered by the spec carrying an `ExplosionSpec`, **never by the behaviour kind**, and is published after the `EnemyDied` that caused it | "explodes on death" stops being true for a Bloater killed by a Charge, a cone or its own fuse — and M7-02's Volatile affix needs a second mechanism instead of a spec block | M2-08 |
| Ingest runs before anything reads an enemy | every distance is one frame stale, and it reads as an AI bug | M1-06 |
| `EnemySystem.Ingest` is two passes **split by writer** — snapshot-keyed copy, then registry-keyed derive | a lagging enemy carries a distance computed from the previous frame's position | M1-06 |
| `PlayerMotor.Tick` integrates velocity **before** facing | a frame of rotation is discarded every time the player starts moving, and no test written from rest would see it | M0-07 |
| `EnemyAgent.Initialise` re-bases `Health.MaxHp` **before** `Health.Reset()` | a recycled enemy arrives at the previous archetype's hit points, with nothing reporting it | M1-05 |
| `EnemyAgent.Initialise` calls `Stat.RemoveAll()` on all three stats **before** re-basing them, with the **no-argument** overload | a recycled Husk wears the last one's depth scaling — and later its affixes and debuffs. A source token would cover only its own source, so every future source would have to be listed at this line, and that list gets one entry short | M2-03 |
| `EnemySystem.Spawn` applies depth **after** registration and **before** `EnemySpawned` | a health bar built on the spawn event is sized to the unscaled maximum for its first frame | M2-03 |
| `DepthScaling.Apply` refills health **after** the `MaxHp` modifier goes on | a stage-20 Husk stands at stage-1 hit points behind a part-filled bar, and nothing reports it — `Health.Reset` fills `Current` from `MaxHp.Value` | M2-03 |
| `EnemyHitFeedback.ResetVisuals` restores the opaque material **before** writing the colour | the dissolve's alpha is honoured for one frame by the transparent material | M1-12 |
| Both run lifecycle events publish **before** `IsRunning` moves | anything reading the session from inside a lifecycle event is reading the state it is *leaving* — deliberately | M0-10 |
| `EnemySpawned` is published **after** registration; `EnemyDespawned` **after** removal | a handler resolving the id finds nothing, or finds a ghost | M1-06 |
| A subscriber that must hear the opening of a run subscribes **from a constructor on the dependency chain**, never from a `Start` of its own | VContainer orders no two entry points' `Start`s, so the opening population is dropped in silence | M1-07 |
| A view that answers core with a **fact** cannot own its own `Update` — it takes a `Step(dt)` from `RunTicker` | core writes intents later in the frame than the reader that applies them, and the intent list reads empty *every* frame | M1-16 |
| **`RunTicker.LevelUpPhase` sits above `CommandPhase`, below the `IsRunning` guard, and the pause returns before either** — the level-up is opened between ticks, never inside one, and a held pause skips the frame rather than clocking it at zero | *Below the phase*: a tap that lands on the level-up screen would also focus an enemy or spend the Charge on the frame it opened. *Inside the tick*: the boundary snapshot is taken on entering `Clear`, the same tick as a stage's last kill, so a draw made in-tick would be captured at a stream position it had already advanced — and killing the app with a pick owed would be a **free reroll** (M3-04's hole, closed here). *Clocked at zero instead of gated*: a `Dt = 0` tick still walks the whole pipeline — targeting re-resolves, the director and the flow are asked, a `Weapon` whose next swing was already due fires once — and spends a snapshot build per frame on the one screen GD §11.4 wants cheap. **The tick that earns the level finishes**: the level is published mid-tick, and that tick's intents, cone and boundary snapshot all still happen. **A gated frame therefore costs zero simulated seconds**, so `RunState.Time` is *play* time and a stopwatch is play + screens — M3-15 must say which it quotes (ledger row 8) | **M3-08a.** Four rows in `FrameOrderTests`: `Frame_LevelUpPhaseRunsAboveCommands`, `Frame_TheLevellingTickCompletes`, `Frame_PausedFrameDoesNotTickCore`, `Frame_PausedTicksCostNoSimulatedTime`. The fixture reaches the phase through `IProgressionCommands` rather than `RunState`, which it cannot build one of (§18.2) |

### 18.2 The boundary

- **`IRunSession.Tick` takes the snapshot alone.** `Dt` rides on the snapshot; two ways to say how
  much time passed is one too many. There is **no `IClock` in the session** — simulated time is the
  sum of each tick's `Dt`. Wall-clock is a different number and a different port (M0-09, M0-10).
- **A port grows a member when the mechanic that needs it lands, not before** (M0-09).
- **Core decides every outcome an enemy causes, and calls `PlayerCombat.ApplyDamage` directly.
  Unity owes core a *fact* only when the answer depends on colliders core does not hold; when the
  geometric question is a standing one rather than an instant, it owes a *sense* on the snapshot
  instead.** Contact (Husk, M1-18; Bloater, M2-08) is a core-perceived XZ distance; a projectile
  impact (Spitter, M2-07a) lands at an arrival time core itself computed. `ReportConeHits` and
  `ReportChargeHits` remain what facts are *for* — a wedge and a swept line, both questions about
  which colliders a shape touched. The fact route was rejected on three counts: it moves the moment
  of damage into the frame's physics phase, one step after the tick that decided it; it makes enemy
  damage non-reproducible from a seed, which is what M2-13 and M2-14 are being built to preserve;
  and it makes an enemy need a body with a trigger before it can hurt anyone, which inverts §3. **The
  price was that core holds no walls, so a shot passed through a cover pillar** — ledger row 13,
  **paid at M2-11b with a sense and not a fact**: `LineOfSightSense` fills
  `EnemySense.HasLineOfSight` and a Spitter simply does not begin a wind-up it cannot see through.
  Core still holds no walls and never will (ledger row 7, M2-07a; §18.4).
- **`EnemyTickContext` is built once per tick by `RunSession`, never per agent — and no behaviour
  may store it.** One reading of the clock decides the whole arena, and a behaviour that kept the
  struct would be keeping this tick's clock, this tick's player and this tick's ports; both
  implementers therefore unpack it into fields on the way in and clear them in a `finally` on the way
  out. It is also the one place the five references and the two floats are guarded, which is what
  lets `Tick` stay unguarded on a path walked once per enemy per frame (M2-07b). **The census joined
  it at M2-08** — refused in M2-07b because a handle on it would let a behaviour damage its
  neighbours, and granted once a Bloater needed to end its *own* life through the one door damage
  reaches an enemy through. The concession stays bounded because the blast that follows is resolved
  by `EnemySystem` off the spec, not by the behaviour.
- **Everything downstream of `SnapshotBuilder` integrates `snapshot.Dt`, never `Time.deltaTime`.**
  The clamp only protects the simulation if brain and body take the same step. Purely cosmetic
  view timers are the deliberate exception (M0-16).
- **`WorldSnapshot.Clear()` does not zero `Enemies`.** Every reader stops at `EnemyCount`, and
  **anything writing into a reused slot must assign every field of it, zeroes included** — an
  unwritten field silently inherits what the enemy that last held that slot put there (M0-05, M1-07).
- **`IntentBuffer.Clear()` lowers `HasPlayerMove` and leaves the stored intent alone.** Every
  intent reader checks its flag first, or it applies last frame's velocity and the player slides
  (M0-06).
- **`PlayerView.Velocity` is the velocity core asked for, never `CharacterController.velocity`** —
  the latter collapses to zero against a wall, which core would read back as "the player stopped
  trying to move" (M0-16).
- **Animation never gates a damage frame, and no clip carries an Animation Event.** Core owns the
  cadence — CC §4.2 puts the damage 40 % of the way through the swing — so `PlayerAnimatorView`
  hears about a swing only after the decision is made. An event that dealt damage would move the
  fight into an FBX's timeline, where anyone re-exporting an art asset could retime it. The
  consequence runs one way: the clip is scaled to the weapon, which is why the view *derives*
  `AttackSpeed` from the measured interval between real swings rather than holding a constant that
  M1-13's Focus ramp would silently drift away from (M2-art).
- **`applyRootMotion` is off on every rig, and every animation importer's root node is left empty.**
  Core owns velocity; the only thing that may move a body is the intent `PlayerView` applies. Nine
  of the 173 KayKit clips do carry root translation, so this is a live guard, not a formality
  (M2-art).
- **An arena the player has not reached is instantiated under a *deactivated* root, never
  instantiated and then deactivated.** A body built under an inactive parent runs no `Awake` and no
  `OnEnable`, so nothing of it is drawn, lit or navigable; instantiating into the live scene and
  switching it off a line later runs both, which enables its renderers for a frame and has its
  `NavMeshSurface` add and then remove a second set of navigation data **on top of the arena the
  player is still fighting in**. `ArenaPool` keeps two roots for exactly this, and raising is a
  reparent plus a `SetActive` (M2-11a). **Every future "build it now, show it later" owes the same
  shape.**
- **Where a body may spawn is a property of the arena, not of the run.** It arrives on
  `WorldSnapshot.SpawnPoints` and reaches `SpawnDirector.Begin` when a stage leaves arrival — and
  the director **copies** it there, because the snapshot is one buffer refilled every frame and a
  held reference would be M2-10's "nobody may recompose a plan a director is still holding" with a
  different noun (M2-11a).
- **`FollowCamera`'s yaw must stay 0.** It is the only reason `SnapshotBuilder`'s straight-through
  stick mapping is camera-relative. The day the camera can turn, the −yaw rotation goes into the
  builder and **never into core** (M0-18).
- **A live object is never handed out of `RunState`.** `Motor`, `Combat`, `Enemies` and
  `Projectiles` are `internal` with public scalar reads, because a public handle on something with a
  `Tick` lets a view double-integrate a frame with nothing in the compiler to object. **Every future
  `RunState` field that hands out a mutable object owes the same question** (M0-16, M1-06, M1-08,
  M2-07a).
- **`RunState`'s constructor and setters are `internal`, and `Soulvail.Tests.Core` has no
  `InternalsVisibleTo`** — deliberately, so tests reach state through `RunSession`, the intended
  route. It is also why that constructor carries no argument guards: they would be unreachable.
  The first test that wants to build one directly has a decision to make on purpose (M0-10).

### 18.3 Numbers and identity

- **A non-positive guard on a float is spelled `!(value > 0f)`, never `value <= 0f`.** Every
  comparison against NaN is false, so the natural spelling admits NaN — and one NaN in a spec or a
  facing is permanent (M0-07).
- **Guard the argument *before* the arithmetic that launders it.** `radius * radius` turns −1 into
  1, so a nonsense radius silently becomes a plausible one-metre one (M1-09).
- **NaN is refused at every door into a `Stat`** — it does not produce a wrong number, it produces
  *silence*: `Changed` compares two values, every comparison against NaN is false, and the event
  stops firing (M1-01).
- **A struct with an invariant needs the check at both ends.** `default(ContentId)` and
  `default(Modifier)` both carry an invalid value past the constructor's guard (M0-08, M1-01).
- **`ContentId`'s grammar is `^[a-z0-9]+(\.[a-z0-9_-]+)+$`.** Loosening it later is safe;
  **tightening it invalidates content references already written to disk** (M0-08).
- **`SeededRandom`'s stream indices — Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4 — are part of
  what a seed means.** Never reorder or renumber; a new stream takes the next free index (M0-04).
- **Choosing a position makes exactly one draw, whatever it then finds.** Both spawners — 
  `EnemySystem.ApplyRespawn` (M1-19) and `SpawnDirector` (M2-05) — draw a starting index once and
  then *walk* the candidates deterministically, so a refused position costs the same draw as an
  accepted one. Consumption that depended on where the player was standing, or on how full the
  arena was, is the one thing a seed cannot survive: the same seed would replay differently the
  moment the player stood somewhere else. **Anything that later picks a place from a list owes the
  same shape** (M1-19, M2-05).
- **GD §12's formulas are authoritative and GD §12.1's own table is not.** `B(40)` is 1,876.9;
  the table's stage-40 row says 1,772 and the "44×" beneath it follows from the same wrong number.
  Stages 1, 5, 10 and 20 all agree to a rounding, which is what makes the row the error. The code
  and its tests assert the formula; **do not "fix" them to match the table.** GameDesign.md wants a
  one-line correction, flagged for the owner in M2-03 and not made there (M2-03).
- **`WaveComposer` tracks a stage's spend as `int`, and each wave's allowance as the *cumulative*
  share minus that spend — never as a running `float` remainder.** Threat costs are whole numbers,
  so a spend is exactly representable and a remainder carried wave to wave is not. Written as a
  running float, stage 1 composes **nine** Husks instead of GD §12.1's ten: `B·1/3` is 13.333334,
  less the 12 it buys, plus `B·2/3` = 26.666667 comes to 27.999999, and the tenth Husk costs 4 of
  the 4 that are not quite there. The cumulative form also makes the last wave's fraction exactly
  1, so the stage's final allowance is exactly what it has left and `UnspentThreat` conserves the
  budget to the point. **Anything that divides a budget across steps and spends it in whole units
  owes the same shape** (M2-04).
- **Depth scaling is `PercentMult`, never `PercentAdd`.** Depth must multiply with an Elite's 2.2×
  rather than pool with it (GD §8.3) — pooling would make a deep Elite markedly weaker than the
  design says, and every individual number would still look right (M2-03).
- **`StatCurve`'s `stageOffset` is 0 or 1 and nothing else.** It exists only to spell the
  difference between GD §12.3's `h`/`d`, which step from `n−1`, and `s`, which steps on `n`. A free
  shift would silently give the first few stages no scaling at all (M2-03).
- **Enemy despawn compacts rather than swapping with the last.** Spawn order feeds
  `TargetScorer`'s tie-break, so a swap would let two runs from one seed diverge on the strength of
  who died first (M1-05).
- **A lazy cache and a "fires only when the value changed" event cannot both be lazy** — deciding
  whether to raise means computing at mutation time. `Stat` keeps laziness only while nothing is
  subscribed (M1-01).

### 18.4 Combat and perception

- **All perception is XZ** — distances, directions, the 6 m ally radius, spawn safety. The height
  between a player capsule's centre and an enemy's is a rendering detail, and counting it would
  inflate every distance `Reach` is checked against. **Any new sense that measures a separation
  owes the same treatment** (M1-06).
- **`LineOfSightSense` is the one named exception, and it measures occlusion rather than
  separation.** A pillar is a solid with a height, so "is there something in between" is asked in
  full 3D — both ends of the ray lifted to `EyeHeight` (1.1 m, chest height on a 2 m capsule), which
  is low enough that GD §7.2's 1.5 m pillar blocks it and high enough that the floor, a kerb or a
  tier's lip does not. **Every *distance* a Spitter uses stays XZ**; only this question leaves the
  ground plane, and a ray taken between the raw positions would have the arena's own floor deciding
  fights (M2-11b).
- **That sense's mask is `Cover` and nothing else — never the Enemy layer.** GD §7.2 makes cover a
  property of the arena, not of the crowd, and a Spitter that could not fire because a Husk was
  standing in front of it would read as broken three systems from its cause. M2-11a putting the
  pillars on a layer of their own is what makes one mask sufficient (M2-11b).
- **An unmeasured sight line means "can see", and the failure mode is why.** A sense answering
  *false* when it has not looked switches the whole ranged archetype off in silence; one answering
  *true* degrades to the arena M2-07b shipped, which is visible and already playtested. The same
  choice `NavPathSense` makes — no path yet is the straight line, not paralysis — and it is why
  `SnapshotBuilder` writes `true` for every slot when no sense is composed, rather than leaving the
  `false` `EnemyViews` used to put there (M2-11b).
- **`Health.Tick` credits only the slice of its step past the recharge deadline**, not the whole
  `dt` — so the Aegis is worth the same at 30 fps as at 120. Anything else that resumes on a
  deadline mid-step owes the same arithmetic (M1-02).
- **`Health` publishes nothing.** A component both sides of a fight use cannot name either side's
  events; its owner turns `DamageResult` into events (M1-02, M1-08, M1-11).
- **`Health.HasShield` means "has a `ShieldSpec`", not "the shield is up"** — a depleted shield
  must still recharge. Ask `Shield > 0` for the other question (M1-02).
- **A telegraph is a promise, and nothing may break it.** `SpawnDirector` never cancels a
  `SpawnTelegraphed`, never moves one, and does not let the concurrency cap eat one — which is why
  the cap counts the *pending* as well as the living, refusing new rings instead of dropping issued
  ones. A ring the player dodged that produced nothing, or produced something two metres away,
  teaches them not to trust the next one, and GD §7.1's whole warning contract goes with it. The
  single exception is `Clear`, where there is no arena left for the body to appear in (M2-05).
- **A Spitter's aim never cancels, and that is the deliberate opposite of the Chaser's windup.** The
  dodge window on a thrown shot is the *flight*, not the telegraph — a Spitter that abandoned its aim
  whenever the player moved would never fire, because moving is what the player does. Anything ranged
  after it owes the same shape, and anything melee owes the Chaser's (M2-07b).
- **A walk *in* follows `PathDirectionToPlayer`; a walk *out* never does.** Negating a path direction
  points away from the next waypoint rather than away from the player, which walks a retreating enemy
  into the pillar it was just routed around. A retreat is `−DirectionToPlayer` and nothing else, and
  it can back into geometry — the `CharacterController` slides it along, and GD §7.2 guarantees no
  dead ends (M2-07b).
- **`EnemyRegistry.Alive` means *registered*, not breathing.** A corpse stays until its death has
  been published and the view has had its frame. Every reader that cares checks `IsAlive`. The
  naming is a wart (M1-05).
- **`Targeter`'s cadence accumulator is never reset or consumed by an immediate retarget** — the
  schedule and the "current target is invalid" trigger are independent by construction, or a stream
  of dying targets starves the scheduled decision for ever (M1-04).
- **`Targeter.FocusedTargetId` lags `Focus(id)` by one tick on purpose** — between ticks there is
  no candidate span to check the id's liveness, range or existence against (M1-04).
- **`TargetScorer` resolves an exact tie to the lowest id even when one tied candidate is the
  incumbent.** Hysteresis is a bonus applied *before* the comparison, not a veto after it (M1-03).
- **`EnemySpec` deliberately does not validate `EnemyBehaviourKind`.** The loud place for an
  unrecognised kind is `EnemySystem.Tick`'s dispatch — the one site that knows the full set (M1-05).
- **A pooled `EnemyView` must forget its last life in `OnDespawn`** — scale, material, alpha,
  collider, knockback — and **a forgotten reset does not fail, it produces a half-transparent
  unhittable Husk.** Anything added to `Enemy.prefab` that remembers something joins that list
  (M1-19).
- **The archetype's tint and body scale are on that list as of M2-06, and they are the entry
  `OnDespawn` does not undo by itself.** `EnemyHitFeedback.ResetVisuals` restores the look the body
  was *rented* with, not the prefab's; what undoes a Bloater is `EnemyViews` telling the next
  rental it is a Husk, which it does on **every** spawn — an archetype with no authored look gets
  `EnemyLook.Default` rather than being skipped. Both halves are load-bearing: without the reset a
  corpse returns to the pool mid-dissolve, without the unconditional re-apply the next Husk spawns
  Bloater-red, **which reads as a rendering bug three systems from its cause** (M2-06).
- **`EnemyView` carries two colliders and they are not interchangeable**: the `CharacterController`
  moves the body, the trigger `CapsuleCollider` is what sweeps query and what `EnemyViews` indexes.
  A sweep mask that started matching the controller would double-report every hit (M1-18).
- **The floating stick's touch region is the left 45 % of the screen, full height**, so a tap
  reaches the arena only in the right 55 %. **Every on-screen control added later must be a raycast
  target**, or it will steal the focus every time it is pressed (M1-09).

### 18.5 Known soft spots

Not bugs today. **Anything a named task must deal with lives in the **current** milestone's
[carry-forward ledger](../ROADMAP.md#carry-forward-into-m4), not here** — one list, one owner per
row, and a row leaves when its owner's *As built* says so. (This link pointed at M2's table for two
milestones after M2 closed; corrected at M3-15. **It is the milestone in progress that is meant, not a
fixed table** — follow the ROADMAP's newest `Carry-forward into M<n>` heading.) What remains below has
no owning task yet:

| Soft spot | Bites at |
|---|---|
| `IsCurrentBlocked` suppressing the invulnerability retarget has **no test** — the outcome is identical, only the frequency changes | whenever a Warden-like enemy exists |
| `RunSession.Tick`'s ordering rule has no assertion — no public route from session to blackboard | M1-08 made it observable; still unpinned |
| A `Health` driven to `MaxHp` 0 dies without a `DamageResult` to say so | the first effect that removes max HP |
| `Targeter`'s 2 s focus-drop delay and `FocusResolver`'s 3 m radius are `const`s, not authored data | when either must differ per class |
