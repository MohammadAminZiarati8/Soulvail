# Soulvail — Architecture

**Version:** 1.0 — decided 2026-09-06
**Companions:** [GameDesign.md](GameDesign.md) · [Characters.md](Characters.md) · [CoreCombat.md](CoreCombat.md) · [adr/](adr/) (why each decision was made)
**Status:** Decided; M0 and M1 are built against it. A *decision* changes through a superseding ADR, never by routing around it. The code sketches are the shape at decision time — for anything already built, the code and the task's *As built* footer are the truth, and a sketch is corrected when it misleads (§11.1 was, in M1-01), not kept in step.

---

## 1. Principles

Five sentences that decide most arguments before they start:

1. **Core is the brain. Unity is the body and the senses.** Senses report, the brain decides, the body acts.
2. **Unity reports facts. Core decides outcomes. Unity renders consequences.**
3. **Commands and facts in, events and intents out.** Never the other way.
4. **Nothing is static.** No singletons, no service locator, no global bus. If it's shared, it's in a container scope.
5. **Numbers are data, not code.** Every tunable is a spec record; every gameplay number is a `Stat` with a modifier stack.

---

## 2. Hexagonal shape

```
                         ┌───────────────────────────────────────────┐
     UNITY (Game)        │               CORE (pure C#)              │        UNITY (Game)
                         │                                           │
   Input adapter ───────►│  inbound ports          outbound ports    │◄─────── Clock adapter
   Physics facts ───────►│  ┌────────────┐         ┌─────────────┐   │◄─────── Random adapter
   Snapshot builder ────►│  │ IRunSession│  domain │ IDomainEvents│──┼───────► Event fan-out → views, audio, haptics
                         │  │ IPlayerCmds│ ◄─────► │ ISaveStore  │──┼───────► Local JSON now, sync later
   Views ◄───────────────┼──│ intents    │         │ ILocalizer  │   │◄─────── String tables
                         │  └────────────┘         └─────────────┘   │
                         │                                           │
                         │  modules: Run · Combat · Ai · Director ·  │
                         │           Progression · Economy · Content │
                         └───────────────────────────────────────────┘
                                             ▲
                                   Tests (NUnit, no scene)
```

- **Core** — `Soulvail.Core`, `noEngineReferences: true`. Domain, application logic, ports. Knows nothing about Unity. Fully unit-tested.
- **Game** — `Soulvail.Game`. Every adapter (input, physics, clock, random, save, localisation), every view (player, enemies, HUD, VFX), and the composition roots (VContainer `LifetimeScope`s).
- Dependency arrow points one way: **Game → Core.** The compiler enforces it.

See [ADR-0001](adr/0001-hexagonal-pure-core.md).

---

## 3. The boundary — what is "logic"

**All game logic lives in core, including enemy and boss behaviour.** The line is not "AI vs. not AI"; it is *decision vs. execution*.

| Core decides | Unity executes / senses |
|---|---|
| Player HP, shield, i-frames, damage, death | `CharacterController.Move`, collision resolution |
| Player desired velocity (accel, speed mods, Charge state) | Applying that velocity; reporting the resulting position |
| Enemy state machines, targets, timers, attacks | Moving the enemy toward the direction core chose |
| Which enemies to spawn, when, where (director) | Instantiating the pooled view at that point |
| Target scoring, hysteresis, tap-to-focus resolution | The tap's screen→world raycast; the reticle visual |
| Cooldowns, auto-cast triggers, skill effects | The skill's VFX, audio, cone overlap query |
| XP, levels, tree, offers, Veilrot, Essence, Shards | Level-up screen presentation |
| Run flow (arrival → waves → clear → gate → sanctum) | Scene/prefab loading, barrier VFX |

**Pathfinding is a sense, not a decision.** NavMesh belongs to Unity. The snapshot carries `PathDirectionToPlayer` per enemy — Unity answers *"which way is the player, around obstacles?"*; core decides *whether* to go. Core remains the sole decider.

**Presentation is not logic.** Camera follow, hit-flash timing, dissolve animations, UI tweens, particles, audio mixing, haptic patterns — all Unity, all reacting to core events, none influencing outcomes.

---

## 4. Data flow

### 4.1 Three channels in, two out

```
  COMMANDS (immediate) ─────►┌──────────────┐
    Charge tap, tap-to-focus │              │─────► EVENTS
    pick node, toggle auto   │              │       EnemyDied, PlayerDamaged, LeveledUp,
                             │     CORE     │       SkillCast, StageCleared, RunEnded …
  FACTS (immediate) ────────►│              │
    cone hit ids, contacts   │Tick(snapshot)│─────► INTENTS (per tick)
    projectile impacts       │              │       PlayerMove(v), EnemyMove(id, dir, speed),
                             │              │       EnemyAction(id, Lunge…), Spawn(spec, pos)
  TICK (every frame) ───────►└──────────────┘
    spatial snapshot; dt rides inside it
```

| Channel | What | When | How |
|---|---|---|---|
| **Commands** | Discrete player input | The instant it happens | Method call on an inbound port. A Charge press delayed by a tick would feel broken. |
| **Facts** | Discrete physical results | The instant they happen | Method call: `ReportConeHits(ids)`, `ReportContact(enemyId)`. Core turns facts into outcomes. |
| **Tick** | Where things are, and how long since the last one | Every frame | `session.Tick(snapshot)` — `dt` is a field on the snapshot (§4.2), so there is exactly one way to say how much time passed. Core throttles its own expensive work internally. |
| **Events** | What happened | As it happens | Outbound port `IDomainEvents`. Views, audio, haptics, analytics subscribe. |
| **Intents** | What core wants the body to do | Every tick | Written into a preallocated intent buffer the views read after the tick. |

### 4.2 The snapshot

With all logic in core, core already owns HP, cooldowns, states, and targets. The snapshot is only what core cannot know: **where things are.**

```csharp
public sealed class WorldSnapshot
{
    public WorldSnapshot(int enemyCapacity);   // > 0; the device tier's concurrency cap

    public float    Dt;
    public Vector2  MoveInput;                 // stick, already deadzoned/banded by the input adapter
    public Vector3  PlayerPosition;
    public Vector3  PlayerVelocity;

    public int      EnemyCount;                // live entries are Enemies[0..EnemyCount)
    public readonly EnemySense[] Enemies;      // preallocated to the cap, never replaced
    public int      EnemyCapacity { get; }

    public ref EnemySense AddEnemy();          // claims the next slot to fill in place; throws when full
    public void Clear();                       // scalars to zero, EnemyCount = 0
}

public struct EnemySense
{
    public int      Id;
    public Vector3  Position;
    public Vector3  Velocity;
    public Vector2  PathDirectionToPlayer;     // NavMesh-derived, a sense
    public bool     HasLineOfSight;
}
```

- `System.Numerics` vectors — pure C#, no `UnityEngine`. Adapters convert at the boundary, through `Num`'s extension methods.
- **Zero allocations.** One snapshot per run, preallocated to the device-tier concurrency cap, struct entries, int IDs. Refilled in place every frame: `Clear`, then one `AddEnemy` per enemy the builder can see.
- **A class, not a struct.** `AddEnemy` hands back a reference into `Enemies` and advances `EnemyCount`; passed by value, the builder would be filling a copy the ticker never sees.
- **`Clear` leaves the `Enemies` contents alone**, which is what makes refilling O(1) rather than a capacity-sized write every frame to erase data nobody may read. **Every reader stops at `EnemyCount`** — anything past it is last frame's enemies.
- **A full snapshot throws.** Silently dropping the enemy would make core blind to it; growing the array would allocate mid-frame. Respecting the cap — by choosing which enemies matter — is the builder's job.
- Core ticks **every frame with variable `dt`**. The targeting scorer runs on its own 0.1s accumulator inside core; the director on 0.5s. Frequency is a tuning detail inside core, not an architectural boundary.

### 4.3 Frame order (Unity side)

```
1. Input adapter reads touches → commands sent immediately; move vector cached
2. SnapshotBuilder fills WorldSnapshot from transforms + NavMesh
3. RunTicker.Tick():  session.Tick(snapshot)         ← core does everything
4. Views read intents → CharacterController.Move, animation triggers
5. Physics queries requested by intents run (cone overlap, contacts) → facts reported to core
6. Event subscribers already fired during step 3 → VFX, audio, HUD updated
```

Facts reported in step 5 are consumed by core in the same call; their outcomes emit events immediately. One-frame lag on positions is irrelevant.

### 4.4 Why not a fixed timestep

Determinism would be nice, but `CharacterController` resolves collisions in Unity, so the simulation is not deterministic regardless. Variable `dt` is simpler. Core never reads wall-clock (`IClock`) or `UnityEngine.Random` (`IRandom`), so the door to replay/verification stays ajar without paying for it now. See [ADR-0003](adr/0003-commands-facts-tick-events-intents.md).

---

## 5. Core structure

Modules are folders (and namespaces) inside `Soulvail.Core`. Split into separate assemblies only if compile times demand it.

| Module | Owns | Key types |
|---|---|---|
| `Run` | Run lifecycle, stage flow, mode rules | `RunSession` (the inbound façade), `RunState`, `RunConfig`, `StageFlow` (FSM) |
| `Combat` | Health, shields, damage, i-frames, targeting, weapons, skills | `Health`, `Stat`, `Targeter`, `TargetScorer`, `Weapon`, `SkillRunner`, `CooldownRules`, `CombatBlackboard` |
| `Ai` | Enemy and boss behaviour | `EnemyAgent`, `EnemyBlackboard`, `StateMachine<T>`, per-archetype behaviours, boss phases |
| `Director` | Spawn budget and composition | `ThreatBudget`, `SpawnDirector`, `WaveComposer` |
| `Progression` | XP, levels, tree, offers, Veilrot | `LevelTracker`, `SkillTree`, `OfferGenerator`, `Veilrot` |
| `Economy` | Essence, Shards, Sanctum services, unlocks | `Wallet`, `Sanctum`, `Unlocks` |
| `Content` | Immutable spec records + lookup | `ContentCatalog`, `CharacterSpec`, `EnemySpec`, `ModeSpec`, `SkillSpec`, `ContentId`, `TagSet` |
| `Effects` | Composable effect primitives | `IEffect`, `EffectRegistry`, primitives |
| `Persistence` | DTOs, versioning, migrations | `PlayerProfile`, `RunSnapshot`, `IMigration` |
| `Ports` | Every interface the outside implements or calls | see §6 |
| `Events` | Domain event records | `readonly struct` per event |

`XpCurve` moved from `Progression` to `Content` at M3-01a, and for the reason below rather than
by analogy: it is authored data on the *mode*, the same kind of thing as GD §12's five curves, so
`ModeSpec` has to name it — and `ModeSpec` is `Content`. Leaving it here would have made `Content`
depend on `Progression` while `Progression` already depends on `Content` for M3-02a's skill specs.
`LevelTracker` is what `Progression` actually owns: the live level, the banked picks, and the
`XpGain` stat.

`ModeSpec` moved from `Run` to `Content` at M2-02. `Run` owns the *rules* a mode implies — stage flow, what a run does when a stage is cleared — but the mode itself is an immutable spec resolved from a `ContentId`, and §10.1 has always drawn it in the catalog beside `CharacterSpec` and `EnemySpec`. The two sections disagreed; §10.1 was right, and this row was the sketch.

---

## 6. Ports

| Direction | Port | Purpose | Implemented by |
|---|---|---|---|
| Inbound | `IRunSession` | `Start(RunConfig)`, `Tick(WorldSnapshot)`, `End()`, plus `IsRunning` / `State`, and the two facts: `ReportConeHits` (M1-11) and `ReportChargeHits` (M1-15). **It gains no member in M2** — ledger row 7 was settled at M2-07a: core decides every enemy outcome and calls `ApplyDamage` directly, so contact, blast and projectile impact are all core-side calls, and the `ReportContact` / `ReportProjectileHit` this row used to promise are gone rather than deferred (§18.2) | Core |
| Inbound | `IPlayerCommands` | `FocusTarget(worldPoint)`, `ClearFocus()` (M1-09), `MovementSkill()` (M1-15), `CastSkill(slot)` and `SetAutoCast(skillId, bool)` (M3-07a) | Core |
| Inbound | `IProgressionCommands` | **Written at M3-08a with four members and none of M6's**: `OpenLevelUp()` and `ChooseOffer(index)`, plus the two reads the frame loop asks above its own decision — `IsLevelUpPending` and `HasOffer`. The reads are on the port rather than only on `RunState` because `RunTicker` decides *when* a level-up opens, and `RunState`'s constructor is `internal` with no `InternalsVisibleTo` (§18.2), so the fixture that owns the frame order cannot build one; `RunState` carries the same reads for the screens that draw them. `Reroll()`, `Banish(skillId)`, `BuyHeal()` and `BuyCleanse()` are **M6-02, M6-05 and M6-06's** — a port grows a member when the mechanic lands | Core |
| Outbound | `IClock` | `UtcNow` (wall-clock, `DateTimeOffset`) — **and nothing else** (M2-01). Never simulated time: that is the sum of each tick's `Dt` (§18.2) | `UnityClock` |
| Outbound | `IRandom` | Named streams: `Spawn`, `Offers`, `Affixes`, `Drops`, `Misc`, plus `Capture()` / `Restore(in RandomState)` — where every stream stands, so a resumed run carries on instead of restarting each stream at draw 0 (M2-13a). On the port, never on `IRandomStream` | `SeededRandom` (xorshift/PCG, seedable) |
| Outbound | `IDomainEvents` | `Publish<T>(in T evt)` | `DomainEventHub` (scoped, typed fan-out) |
| Outbound | `ISaveStore` | Async load/save of profile and run snapshot | `LocalJsonSaveStore` now, `SyncingSaveStore` later |
| Outbound | `ILocalizer` | `string Get(LocKey key)` — **one member, and the `params` this row carried from M0 is gone rather than deferred** (M3-14a rule 5). Nothing in M3 needs it: both callers with a number in them format it beside the key, which is M3-09b rule 2's ruling (*"core says what kind of number it is; the screen formats it"*), and a `params object[]` allocates an array on every call from `HudPresenter` and `AutoCastRow`. **M6-10 adds the overload** the day a translated sentence needs a substitution *inside* it. A miss returns the key's own text, never empty and never a throw — `Port_HasOneMember` is the row | `TableLocalizer` (over one `LocalizationTable`, built at boot) |
| Outbound | `IIntentSink` | Where core writes per-tick intents | `IntentBuffer` (preallocated) |

Ports are the *only* things in core that mention the outside world. If a core class needs something not on this list, the answer is a new port or a new snapshot field — never a Unity reference.

**A port grows a member when the mechanic that needs it lands, not before.** `IRunSession` is `Start` / `Tick` / `End` through M0 because movement arrives on the snapshot and needs no facts; the `Report*` methods are listed above so the shape is known, but each one is added by the task that produces the fact it carries. A port written ahead of its callers is a guess, and an unused method is one nobody can test.

---

## 7. Composition — VContainer

No statics, no service locator, no `FindObjectOfType`. Everything is constructed by a `LifetimeScope` and injected. See [ADR-0002](adr/0002-vcontainer-no-service-locator.md).

```
BootScope (root, DontDestroyOnLoad)          RunScope (child, per run)
├── IClock          → UnityClock              ├── RunSession, RunState
├── IRandom         → SeededRandom            ├── IDomainEvents → DomainEventHub
├── ISaveStore      → LocalJsonSaveStore      ├── IIntentSink   → IntentBuffer
├── ILocalizer      → TableLocalizer          ├── SpawnDirector, pools
├── ContentCatalog  (built from SOs, §10.1)   ├── RunTicker : ITickable
├── Settings, DeviceTier                      ├── SnapshotBuilder
└── SceneLoader                               ├── Presenters (HUD, LevelUp, Sanctum)
                                              └── Views registered for [Inject]
```

- **Core classes use constructor injection.** `new RunSession(catalog, random, events, intents)`. No attributes in core.
- **No `IClock` in the session.** Simulated run time is the sum of each tick's `Dt`, which rides in on the snapshot (§4.2), so a clock there would be a second answer to "how much time has passed" — and the wrong one, since wall-clock keeps running while the game is paused or backgrounded. `IClock` (M2-01) is for persistence: when a save was written, how long the app was away. Different question, different code. Same rule as §6's: **a dependency arrives when the mechanic that needs it lands, not before.**
- **Core participates in Unity's lifecycle through entry points**, never by being a MonoBehaviour: `RunTicker : ITickable` calls `session.Tick`; `IDisposable` on the scope tears the run down.
- **MonoBehaviours get `[Inject]`** on a method or fields. Pooled prefabs are instantiated through `IObjectResolver.Instantiate` once at pool creation so injection happens once.
- **Scope = lifetime.** When `RunScope` disposes, the session, events, pools, and every subscription go with it. That is the answer to "what owns run state."
- A third scope (per stage) is trivial to add if ever needed.
- **There is exactly one sanctioned static *surface* in the project — one static that other files read — and it is `Game/Presentation/Palette.cs`, GD §16.4's colour language as a `static class` of `readonly Color` (M3-13a).** The distinction is load-bearing and is not "one static field": a handful of types hold a `private static readonly` constant of their own — `EnemyLook.BoneGrey`, `TelegraphRingView.FlatRotation` and `ZoneView.FlatRotation`, `FirstActiveHint.HintKey`, `OverflowToast.ToastKey`, `SaveDtos.EmptySlots` — and those are implementation details of one type that nothing else can reach. **What this row sanctions is the shared one**, because a shared static is the one that can become a service locator by accretion. The ban above is on static **mutable** state and on service location; a `readonly Color` is neither, owns nothing a run owns, and is inert under a disabled domain reload because there is nothing to reset. It is here rather than in §18 because §18 is about rules the *code depends on*, and this is a rule about what may be *written* — which is read when someone is about to write the next one. Two alternatives were weighed and rejected: an **injected instance**, which `TreeNodeView` and `ZoneView` cannot have (they are pooled templates instantiated from a prefab field, and nothing injects them individually); and a **`ScriptableObject` per prefab**, which is a dressing step per reader and treats a design law as a tuning knob, when the thing that makes `#FF4A1F` load-bearing is precisely that nobody may retune it. The precedent was shipped rather than invented: `ThreatArrows.Danger` was a `public static readonly Color` from M2-12a and `TelegraphRings` read it. **The next static argues against this row, not against nothing** — and the argument it has to beat is *"no run owns it, no test can be ordered by it, and no domain reload can leave it stale."*

---

## 8. Domain events

`IDomainEvents` is an outbound port; `DomainEventHub` is its Run-scoped adapter — a small typed dispatcher, one subscriber list per event type, `readonly struct` payloads, no allocation on publish. See [ADR-0004](adr/0004-scoped-domain-events.md).

Rules:
- **Outbound only.** Core publishes; Unity subscribes. Unity → core is always a method call on an inbound port. Two-way buses make flow untraceable.
- **Not static.** Scoped to the run; disposed with it. No cross-scene leaks.
- **Describe what happened**, not what to do: `PlayerDamaged`, never `ShakeCamera`.
- Subscribe in `OnEnable` / unsubscribe in `OnDisable` (views), or via scope disposal (presenters).
- In tests, `RecordingEvents` collects a list: *"killing this enemy emitted exactly one `LeveledUp`."*

---

## 9. Blackboards — per-agent, typed

A blackboard is **one agent's perception plus its own working memory.** Never global, never string-keyed. See [ADR-0005](adr/0005-per-agent-typed-blackboards.md).

```csharp
public sealed class EnemyBlackboard
{
    // Perception — written by snapshot ingestion, read-only to behaviours
    public float   DistanceToPlayer;
    public Vector2 DirectionToPlayer;
    public Vector2 PathDirectionToPlayer;
    public bool    HasLineOfSight;
    public int     AlliesNearby;

    // Working memory — written by the agent's own FSM states
    public int     TargetId;
    public Vector2 LungeDirection;   // set on Telegraph.Enter, consumed by Lunge
    public float   StateTimer;
}
```

- **One per enemy, one per boss, one `CombatBlackboard` for the player** (`HpFraction`, `EnemiesWithin6m`, `EnemiesWithin8m`, `Veilrot`, `IsFocused`, `IncomingProjectiles`).
- **Auto-cast trigger conditions are pure predicates over the `CombatBlackboard`**, carried by the `SkillSpec` as data: `bb => bb.HpFraction < 0.6f`.
- Write discipline: snapshot ingestion writes perception; the agent's behaviour writes working memory; nothing else writes.
- Not a bus. Cross-agent communication is events.
- If data-driven behaviour authoring ever needs dynamic keys, upgrade to `BlackboardKey<T>` over a typed store. Not for V1.

FSM + blackboard covers the V1 roster. A behaviour tree for bosses later would read the same blackboard.

---

## 10. Data

### 10.1 Definitions (authored, static)

ScriptableObjects are `UnityEngine.Object`, so core cannot see them. They are **authoring assets** on the Unity side, converted once at boot into immutable plain records and registered as the `ContentCatalog`. See [ADR-0006](adr/0006-content-catalog-from-scriptableobjects.md).

```
CharacterDefinition.asset ──► CharacterSpec  ─┐
EnemyDefinition.asset     ──► EnemySpec      ─┼──► ContentCatalog (BootScope singleton)
SkillDefinition.asset     ──► SkillSpec      ─┤
ModeDefinition.asset      ──► ModeSpec       ─┘
```

- SO fields are `[SerializeField] private`, read through a `ToSpec()` conversion. Ten lines per type.
- Specs are immutable. Runtime state never lives in a spec.
- Because core only sees plain records, the *source* can later be server-delivered JSON (live balance) — an adapter change.

### 10.2 Run state (live)

`RunState` inside `RunScope`. **Core is the single source of truth**; views never hold gameplay state, they render events. Dies with the scope.

### 10.3 Persistent (survives app kill)

Core defines the DTOs and the port; the adapter owns the medium. See [ADR-0007](adr/0007-save-store-async-local-first-versioned.md).

```csharp
public interface ISaveStore
{
    Task<PlayerProfile?> LoadProfile();   // Nullable<PlayerProfile> — null means "no save"
    Task SaveProfile(PlayerProfile profile);
    Task<RunSnapshot?>  LoadRun();        // Nullable<RunSnapshot>
    Task SaveRun(RunSnapshot run);
    Task ClearRun();
}
```

- **Async from day one**, even though the local adapter is a synchronous file write — a server adapter must not change a signature.
- **The two `?`s are `Nullable<T>`, not nullable references.** Both DTOs are `readonly struct`s, which is what makes these signatures compile as written: this project enables nullable reference types nowhere, so reading them as nullable references would mean switching the language feature on for one file on the strength of two return types (M2-13a).
- **No parameter is taken by `in`, deliberately, though both DTOs are structs.** An `async` method cannot have a by-ref parameter, so `SaveRun(in RunSnapshot)` would compile only while the adapter stays synchronous and would refuse the first `async` one — which is the one this ADR says is coming. `IRandom.Restore` *is* `in`: it is a core call with no async implementation imaginable (M2-13a).
- `PlayerProfile` (settings now; Shards and unlocks when the mechanics that own them land — M4-06, M6-08) and `RunSnapshot` (written at every stage boundary, deleted on death — Android kills backgrounded apps).
- **A run snapshot carries the seed *and* `RandomState`** — five stream positions, one per stream in index order. The seed selects each stream's sequence and the state says how far along it is; a resume needs both, and restoring position onto a generator built from the same seed is a complete restore. `IRandom.Capture()` / `Restore(in RandomState)` are the only door to it, deliberately not a settable position on `IRandomStream` (M2-13a, §11.4).
- **Every DTO carries `int Version`.** Migrations are pure core functions with a fixture test per version. `CurrentVersion` starts at 1, so `default(T)`'s version 0 is the value no writer can produce and every reader refuses — which is how §18.3's both-ends problem is closed here without a second concept (M2-13a).
- Server later: **local-first with sync.** Write locally (instant, offline-safe), push in the background, reconcile on launch. That is `SyncingSaveStore` wrapping `LocalJsonSaveStore`; core never learns it happened.

---

## 11. Scalability foundations

Six decisions that are near-free now and near-impossible after launch. The first is non-negotiable.

### 11.1 `Stat` with a modifier stack — [ADR-0008](adr/0008-stat-modifier-system.md)

Ten sources touch *damage* alone: tree passives, Pacts, class signatures, Veilrot thresholds, affixes, Ordeals, Focus, the Claiming, difficulty modifiers, depth scaling. Ad-hoc `if`s collapse at ~50 effects.

```csharp
public sealed class Stat
{
    public float Base { get; set; }
    public float Value { get; }                     // cached; recomputed on change, reads allocate nothing
    public int ModifierCount { get; }
    public void Add(in Modifier modifier);          // Flat → PercentAdd → PercentMult, in that order
    public int  RemoveAll(object source);           // buff ended, node removed, Rot threshold crossed back
    public int  RemoveAll();                        // wipe a pooled object clean — never "the buff ended"
    public void CopyModifiersTo(List<Modifier> destination);
    public void Describe(StringBuilder sb);         // the debug panel: "28.80 = (13.00 + 2.00) × 1.60 × 1.20"
    public event Action<Stat> Changed;              // only when Value actually moved
}
```

As built in M1-01, and two members differ from the sketch this section carried before it: modifiers are handed out by **copying into a caller's list** rather than as an `IReadOnlyList` the caller could reorder or hold past a removal, and `RemoveAll` **returns the count** it removed, which is what makes "a source with nothing on this stat is not an error" observable rather than assumed.

The no-argument `RemoveAll()` arrived in M2-03 as a **second overload, never a replacement**: taking a source back when a buff ends is a different question from wiping a rental clean, and one call site must not be able to mean the other by omission. Its only caller is `EnemyAgent.Initialise` — see §18.1.

Every gameplay number — damage, fire rate, speed, max HP, cooldown, XP gain — is a `Stat` from the first line of combat code.

### 11.2 Effects as an open set — [ADR-0009](adr/0009-effect-primitives-open-set.md)

Skills, nodes, affixes, Ordeals are all "an effect that does something." No `switch (effect.Type)`. A small library of **effect primitives** (`ModifyStat`, `OnKillTrigger`, `SpawnZone`, `ApplyStatus`, `ChainDamage`, …) that data composes; new primitives are new types registered with a handler — open for extension, closed for modification. Build the first ten as they're needed, in this shape from the first one. No DSL.

### 11.3 Stable content IDs and tags — [ADR-0010](adr/0010-stable-content-ids-and-tags.md)

- Every spec has a `ContentId` — a stable string (`skill.oathbound.consecrate`). **Never** an enum ordinal or array index; inserting content would break every save. Enums are for closed sets only (FSM states, modifier kinds).
- A `TagSet` on entities and effects: `Undead`, `Minion`, `Fire`, `Projectile`, `Elite`. Future mechanics ("+20% vs Undead", "Wights count as Minions") are tag queries, not type checks. Tags are registered names backed by a bitset for speed.

### 11.4 Random streams — [ADR-0011](adr/0011-random-streams.md)

`IRandom` exposes **named streams** (`Spawn`, `Offers`, `Affixes`, `Drops`, `Misc`), each independently seeded from the run seed. Adding a mechanic that consumes randomness must not change the spawn sequence of a seeded Daily run.

### 11.5 Localisation keys from the first string — [ADR-0012](adr/0012-localization-keys-from-day-one.md)

No raw user-facing string anywhere. `ILocalizer.Get(LocKey)` from the first HUD label. Costs nothing on day one; costs thousands of edits on day 300.

### 11.6 Save versioning with migration tests — [ADR-0007](adr/0007-save-store-async-local-first-versioned.md)

Covered in §10.3. A launched game's saves are a contract that cannot be broken; each migration ships with a fixture of the old format.

---

## 12. Assemblies and folders

```
Assets/_Project/
├── Core/                          Soulvail.Core.asmdef   (noEngineReferences: true)
│   ├── Run/  Combat/  Ai/  Director/  Progression/  Economy/
│   ├── Content/  Effects/  Persistence/  Ports/  Events/
├── Game/                          Soulvail.Game.asmdef   (→ Core, VContainer, InputSystem, uGUI, TMP, AI.Navigation)
│   ├── Composition/               BootScope, RunScope, installers
│   ├── Adapters/                  UnityClock, SeededRandom, LocalJsonSaveStore, TableLocalizer,
│   │                              SnapshotBuilder, IntentBuffer, DomainEventHub, InputAdapter, Num
│   ├── Authoring/                 ScriptableObject definitions + ToSpec()
│   ├── Views/                     PlayerView, EnemyView, projectiles, VFX, reticles
│   ├── Presentation/              HUD, LevelUp, Sanctum, Menu presenters
│   ├── Controls/                  FloatingStick, skill buttons
│   └── Pooling/
├── Editor/                        Soulvail.Editor.asmdef (validation, tooling)
├── Tests/Core/                    Soulvail.Tests.Core.asmdef (EditMode, NUnit, → Core only)
├── Tests/Game/                    Soulvail.Tests.Game.asmdef (EditMode; adapters, authoring, installers — no scene)
├── Tests/PlayMode/                Soulvail.Tests.PlayMode.asmdef (smoke tests only: Boot reaches Menu, Descend starts a run)
├── Data/                          SO instances: Characters/ Enemies/ Skills/ Modes/ Tuning/
├── Prefabs/  Scenes/  Art/  Audio/  Materials/
```

- `Core` under `Assets/` with an asmdef is the pragmatic choice; a separate .NET class library would give better tooling but needs a `dotnet` toolchain that isn't installed. Revisit if wanted.
- Third-party packages (VContainer) come through the Package Manager, never inside `_Project`.

---

## 13. Conventions and banned patterns

**Conventions**
- Namespaces mirror folders. Private fields `_camelCase`; `[SerializeField] private`, never public fields. File-scoped namespaces in pure C#; **block namespaces in every `MonoBehaviour` and `ScriptableObject`** ([Traps §5](Traps.md)). One class per file. `.editorconfig` enforces.
- Core: constructor injection, `readonly` where possible, `System.Numerics` for vectors, no `UnityEngine` ever.
- Game: `[Inject]` for dependencies; views are dumb — they read intents and render events.
- Every tunable is a spec field. Every gameplay number is a `Stat`.
- Every user-facing string is a `LocKey`. Every content reference is a `ContentId`.

**Banned**

| Pattern | Why | Instead |
|---|---|---|
| Statics, singletons, service locator | Hidden globals; lies about dependencies | Container scope |
| Static event bus | Leaks across scenes, unscoped | `IDomainEvents` in `RunScope` |
| `FindObjectOfType` / `FindWithTag` | Slow, hides dependencies | `[Inject]` |
| `GetComponent` in `Update` | Per-frame lookup on a phone | Cache in `Awake` |
| `Resources.Load` | Untrackable, no stripping | Direct references via authoring SOs |
| `UnityEngine.Random` / `Time` in core | Breaks purity and seeding | `IRandom` / `IClock` |
| `Dictionary<string, object>` blackboards | Untyped, boxing, unfindable writers | Typed per-agent classes |
| `switch (effect.Type)` | Every new effect edits the switch | Effect primitives + registry |
| Enum ordinals as content identity | Inserting content breaks saves | `ContentId` |
| Raw UI strings | Localisation retrofit | `LocKey` |
| Inheritance chains for enemies/skills | Rigid | Composition, data |
| LINQ / allocations in `Tick` paths | GC spikes on mobile | Structs, preallocated buffers |

---

## 14. Performance rules

- Core `Tick` budget: **< 1 ms** at the low-tier concurrency cap (18 enemies), measured on device.
- Snapshot and intent buffers preallocated; no per-frame allocations in core or in the adapters that feed it.
- Targeting scorer on a 0.1 s accumulator, director on 0.5 s — inside core.
- Views pool everything; no `Instantiate` during a wave.
- One shared material + atlas for enemies; GPU instancing; zero realtime shadows. Frame Debugger to verify batches.
- `Application.targetFrameRate` explicit; overlays drop to 30.
- Profile on the owner's phone. Editor numbers are fiction.

---

## 15. Testing

- **`Tests/Core`** — NUnit over `Soulvail.Core`. Fake `IClock`, seeded `IRandom`, `RecordingEvents`. Target: every rule has a test. *"A Lunger telegraphs 0.9 s then commits to a straight line"* is a unit test. *"A level-1 Oathbound kills a Husk in 3 swings"* is a unit test. *"Migrating a v1 profile yields a valid v2"* is a unit test with a fixture.
- **PlayMode tests** — near zero. Feel, touch, and GPU are tested on a phone.
- A PR that changes core logic without touching its tests is incomplete.

---

## 16. Known limits and non-goals

- **Multiplayer** — out of scope, not designed for. The intent model is closer to netcode-friendly than typical Unity code; it is not netcode.
- **Determinism / replay verification** — not available while Unity resolves collisions. `IClock` + `IRandom` keep the door ajar; server-side validation, if ever, would be statistical.
- **Physics-heavy mechanics** (bouncing projectiles, destructible terrain) push against the boundary. Each needs Unity to report more facts. Acceptable as the exception; a mechanic that is *mostly* physics may live in Unity.
- **Core growing into a monolith** — everything-in-core means core grows fastest. Modules from day one; split assemblies only when compile times demand it.

## 17. What we deliberately do not build now

A mod/plugin system · a scripting language · an effect DSL · a generic ECS · an abstraction over rendering · any netcode hook. Each is complexity paid today for a future that may not arrive. §11 is the opposite: near-zero cost now, near-infinite cost later.

---

## 18. Invariants

**Rules the code currently depends on.** Each was decided in a task, is load-bearing somewhere, and
breaking it produces a bug that does not look like its cause. This is the answer to *"may I change
this?"* — the answer is yes, but read the reason first and change the reason with it.

Each row is the rule, one clause of why, and where it was set. The argument behind a row — the failing
test, the alternatives refused, the history — is in the owning task's spec *As built*, and the long form
of this section as it stood at M4-05b is [archived](plan/archive/Architecture-18-invariants-history.md).
A task that adds an invariant adds a row in this shape; the argument goes in its *As built*.

Toolchain traps — things that lie to you rather than rules the code relies on — live in
[Traps.md](Traps.md). The two files are deliberately separate: one is about our design, the other
is about Unity's.

### 18.1 Ordering

| Invariant | Break it and | Set in |
|---|---|---|
| `RunTicker`'s frame order: commands → snapshot → clear intents → core tick → bodies → **sync** → facts → knockbacks | a tap lands a frame late; a cleared buffer erases an unread intent; a sweep resolves against last frame's arena | M0-16, M1-09, M1-12, M1-15; asserted by `Tests/PlayMode/FrameOrderTests.cs` since M2-11b, every step including commands since M3-10a |
| **Every command adapter is polled from `RunTicker.CommandPhase` and none is an `ITickable`** — `TapToFocusAdapter.Poll`, `SkillSlotInput.Poll`, the movement-skill press. Their order *within* the phase is not load-bearing; being *in* it is | as `ITickable`s their order against the snapshot is whatever `RunScope` registered, so a tap is acted on a frame late or a cast resolves against stale senses. A uGUI `Button` calling `IPlayerCommands` from `onClick` has the same defect | M1-09, M1-16, M3-10a (`FrameOrderTests.Frame_SlotPollSitsBesideTapToFocus`, `SkillBarPresenterTests.Input_PressIsSentInCommandPhase`) |
| **`Physics.SyncTransforms()` sits between the bodies step and the fact phase** — one flush at the one seam, never `autoSyncTransforms` project-wide | `autoSyncTransforms` is 0, so without it a cone or sweep resolves against where bodies stood last frame: rare, silent, reads as bad aim. Adapter fixtures that move transforms directly still call it themselves | M2-15a |
| `RunSession.Tick`: time → **lure expiry** → ingest (**enemies and minions together**) → combat → **take the shot** → **skills** → **timed expiry** → **zones** → enemy behaviours → **minions** → **projectiles** → **death drain + rise** → (dead? end) → **xp drain** → **director** → **stage flow** → motor → intent | the gun aims at where enemies *were*; the motor turns before it knows its facing | M1-06, M1-08, M2-05, M2-07a, M2-10, M3-01a, M3-06, M3-11a-ii, M3-11b, M5-01, M5-03, M5-04a, M5-04b |
| **`LureSystem.Tick` sits immediately above the ingest, and a corpse decoy redirects an enemy in `EnemySystem.Perceive` — the one site that writes the four player fields — never in a behaviour.** `PathDirectionToPlayer` is zeroed for a lured agent, so the straight-line fallback M1-19 built for a missing NavMesh is what steers it. `EnemyBlackboard.QuarryIsADecoy` says which, and `ChaserBehaviour.EnterStrike` is the **one** reader: every other way an enemy hurts the player already resolves against the real `RunState.PlayerPosition` | below the ingest, an enemy spends a frame walking at a corpse that has already rotted; redirected in the behaviours, it is four state machines to keep in step instead of one local; left steering by the body's path, a lured enemy walks past the corpse towards the player it is not chasing; without the flag, a Husk in reach of a decoy hits a player six metres away. **This is deliberately not a targeting system** — no `TargetId`, no threat table, no way to pull *some* enemies — and M7-01's Choir is what pays for renaming the four fields | M5-03 (`LurePerceptionTests.Run_TheExpiryIsAbovePerception`, `Perception_ALuredEnemyIsToldTheDecoy`, `Chaser_StrikesTheDecoyAndHurtsNobody`) |
| **`MinionSystem.Ingest` runs beside `EnemySystem.Ingest` and `MinionSystem.Tick` immediately after the enemy behaviours, above the death check; inside the system the order is expire → choose → walk → strike.** A Wight is its own type, never an `EnemyAgent`, and its kill goes through `EnemySystem.ApplyDamage` like every other death | split ingests let a Wight act on last frame's positions while the arena acts on this frame's; ticked *before* the behaviours, a Wight removes a Husk that never got to act; *below* the death check, a kill landing on the tick the player dies is silently lost. Expiring first stops a Wight in its last frame picking a target it will never reach; striking last means it swings from the position it was **reported** at rather than the one it is walking to. Reusing `EnemyAgent` would put a friendly body inside `SpawnDirector.IsStageComplete`, `PlayerCombat.BuildCandidates`, the XP path and `WaveComposer` — four exceptions found one at a time | M5-04a (`MinionSystemTests.Minion_IsNotAnEnemy`, `Minion_StrikesAtItsReach`, `Minion_ExpiresOnTime`) |
| **`RunTicker.ApplyMinionMoves` sits immediately after `ApplyEnemyMoves` and above `Physics.SyncTransforms()`, and it resolves ids through `MinionViews` — a second census and a second intent list, never the enemies'.** `SnapshotBuilder` copies the minion census in **after** the enemies and **before** `WriteSenses`, and `WriteSenses` is deliberately never extended to the minion slots | *beside the enemies*: both are bodies core decided a velocity for this tick, and a body stepped on `Time.deltaTime` while core integrated `snapshot.Dt` turns a hitch into a teleport (M1-18). *Above the flush*: nothing sweeps a Wight today, and a body written after it would be the one exception nobody remembered on the day something does. *Through its own census*: both registries number from 1, so a minion id resolved through `EnemyViews.TryGet` does not miss — it finds an **enemy** with that number and walks it. *After the enemies*: `LineOfSightSense`'s per-frame budget is sized from `snapshot.EnemyCount`, and a Wight inside it buys raycasts for bodies that never ask. *Before `WriteSenses`*: that method walks the same count. *Not extended*: `NavPathSense` only ever paths to the **player**, so a route computed for a Wight is to the wrong place, costed per Wight per frame, and read by nothing | M5-05a (`FrameOrderTests.Ticker_AMinionMoveWalksAMinion`, `Ticker_MinionsMoveBeforeTheFlush`; `MinionViewsTests.Minions_AreNotInTheEnemySlots`, `Minions_DoNotInflateTheSightBudget`, `Minions_HaveNoPathSearch`) |
| **`EnemySystem.DrainDeaths` is pulled after every pass that can kill an enemy and above the death check, and `RisePassive.OnDeaths` is offered the result on the same line.** Core does not subscribe to its own events: a death is banked in `ApplyDamage`'s kill branch beside `PendingXp` and `PendingKills`, and pulled once a tick into a buffer `RunSession` owns. **One draw from `IRandom.Drops` per death, before a boss, a full army or an unreadable chance can refuse the raise** | above the projectile step, a bolt's kill waits a frame to rise; below the death check, a kill landing on the tick the player dies silently loses its rise. A subscription would make core a listener to itself; `PendingKills` is a count and a Wight stands up *where the corpse fell*. A draw skipped at the cap would make a seeded run's later rises depend on how crowded an earlier fight was — and a sixth stream would be a `RandomState` field, so a **v4** save for a 25 % chance | M5-04b (`RiseTests.Run_ARiseOnTheTickThePlayerDies`, `Rise_DrawsOncePerDeathWhateverTheOutcome`, `Rise_DrawsFromTheDropsStream`) |
| **`PlayerCombat.TryTakeShot` is drained immediately after the combat step and above the skills block**, and a `WeaponKind.Projectile` damage frame writes `PendingShot` instead of a `ConeHitIntent` — an arrival is a point and a moment core computes itself, so the body is asked nothing | lower down, a bolt leaves aimed with positions that are already a frame stale; left undrained, the same shot is fired again on the next tick that reads it. The projectile step runs after the enemy behaviours, which is what gives a player's bolt the one-frame grace M2-07a gives a Spitter's — fired and landed in one tick, the flight the weapon exists for would not exist | M5-01 (`PlayerProjectileTests.Run_TheShotIsInTheAirTheTickItWasDecided`) |
| **`TimedEffects.Tick` sits immediately after `SkillRunner.Tick`, above `ProjectileSystem.Tick`**, and reads the `SimulatedClock` written beside `State.Time += Dt`, never `IClock` | expiring before the runner takes a grant back and re-grants it on a recast tick, with an instant of no shield between; expiring below the projectile step lets a lapsed grant absorb a hit; a wall clock would drain a shield through a level-up at `timeScale` 0 | M3-11a-ii (`Timed_TicksAfterTheRunner`, `Timed_TicksBeforeTheProjectileStep`) |
| **`ZoneSystem.Tick` sits immediately after `TimedEffects.Tick`; inside one zone, pulses come before retirement**; a zone is placed at `CombatBlackboard.PlayerPosition`, not a `Tick` parameter | a zone cast this tick must exist this tick; interleaving the two expiry mechanisms makes the order a shield comes off in depend on a zone ending the same tick; retiring first makes Consecrate worth 33 hit points not 36; `Spawn` runs inside `Apply`, so a cached position is a frame stale | M3-11b (`Zone_TicksAfterTheTimedEffects`, `Zone_PulsesOnTheTickItExpires`) |
| **`SkillRunner.Tick` sits between `PlayerCombat.Tick` and the enemy behaviours, above `ProjectileSystem.Tick`** | below combat, an HP trigger fires a frame after every hit, for ever; below the projectile step, `IncomingProjectiles` describes the sky *after* the hit and Bulwark becomes a shield over a wound. The staleness is the mechanic | M3-06 (`Session_TicksTheRunnerAfterCombatAndBeforeTheBehaviours`, `Session_TicksTheRunnerBeforeTheProjectileStep`) |
| `StageFlow` ticks **after** the director and **before** the motor, **and the boundary snapshot is taken on entering `Clear`** — phase read before the flow ticks, compared after | the flow reads `IsStageComplete` a frame stale; a stage ends in an arena whose run ended this tick; a snapshot on the *phase* writes a file once a frame, one at `StageArrived` leaves the gate wait unsaved | M2-10, M2-14a |
| **Every `RunSnapshot` is captured before anything has drawn for the stage it describes** — the opening capture is the first statement of `RunSession.Start`, the boundary capture is on entering `Clear` | a resumed run restores a stream position that already spent the composition and deals itself a different stage under the same number | M2-14a, M2 ledger row 1 |
| A stage boundary calls `SpawnDirector.Clear()` **before** recomposing the `WavePlan`; nobody may recompose a plan a director is still holding | the director sized its arrays from the old plan, and `SweepTheDead` walks off the end when a wave grows | M2-10 |
| A boundary sets `EnemySystem.Depth` **before** `EnemySystem.Clear`, and clears projectiles and `Targeter` before recomposing | the new stage's first body is priced at the old stage; a bolt lands at coordinates that no longer mean anything | M2-10 |
| **A boundary also clears the decoys and the army, silently, and a zone is the one thing it deliberately leaves standing** | a decoy lives 3 s and a Wight **20 s** against a boundary's 2 s of gate and arrival, so both cross one: the decoy taunts the next arena's bodies from the last arena's floor, and the Wight arrives walking at an enemy id the boundary has just wiped out of the registry. A zone is the player's own and survives on purpose (M3-11b) | M5-03, M5-04b (`StageFlowTests.Advance_SweepsTheArmy`) |
| **The sweep is silent, so the view side tears down on `StageArrived` instead** — `MinionViews` releases every standing body on the event `StageFlow.EnterArrival` publishes immediately after the sweep it mirrors, the same event `ArenaPool` swaps the room on | core publishes no `MinionDespawned` at a boundary (`EnemySystem.Clear`'s silence, for its reason), so a census built on the census events alone stands its whole army in the **next** arena for the rest of the run: eight bodies reporting under ids that no longer resolve, a pool with nothing left to hand out, and the entry for id 1 overwritten by the next stage's first Wight — which leaks that body out of the index entirely. **`ProjectileViews` has the identical gap and no answer yet**, and so will the decoy view: `ProjectileSystem.Clear` and `LureSystem.Clear` are swept at the same boundary and are just as silent. What differs is reachability — a bolt lives under a second and a decoy three, against a Wight's twenty | M5-05a (`MinionViewsTests.Minions_AreSweptAtAStageBoundary`), discharging M5-04b's finding on [ledger row 9](plan/ROADMAP.md#carry-forward-into-m5) |
| The boundary calls `player.Targeter.Reset()` and **never** `PlayerCombat.Reset()` | every boundary becomes a free heal, deleting GD §12.5's attrition | M2-10 |
| Projectiles tick **after** the enemy behaviours and **before** the death check | a shot lands the tick it left; a killing bolt leaves the player at zero for a frame, still playing | M2-07a |
| The director ticks **after** the death check and **before** the motor | a wave telegraphed into an ended run; a director seeing a position the rest of the tick did not | M2-05 |
| **Experience is drained into `LevelTracker` after the death check and before the director** (`State.Progression.Grant(State.Enemies.DrainXp())`), granted on the tick, never on the fact | above the death check, `LeveledUp` lands in a scope mid-teardown; below the director, a stage's last kill levels *after* the boundary snapshot and the run resumes a level short. Kills between ticks accrue on `EnemySystem.PendingXp`, at most a frame late (ADR-0003) | M3-01a |
| **`RunSession.Start`'s restore block is an order**: `LevelTracker.Restore` → `SkillTree.Restore(takenNodeIds)` → the runner told about the restored Actives → `SkillRunner.Restore(manualSkillIds)` → `LevelUpFlow.GrantOverflow(derived)` → **`Health.Restore(hp, shield)` last**. Overflow is derived, `Level − 1 − TakenNodeCount − PendingLevelUps`, and below zero refuses the run. **Every task that adds a restore adds to this row** | saved hit points are absolute and clamped against the **live** maximum, so any `MaxHp` mover replayed after the clamp costs the player hit points on every resume, silently; a slot restored before the runner knows its skill is dropped as stale. The tree throws on an unknown node, the runner drops an unknown slot; both are silent and gated because nothing may publish before `RunStarted` | M3-01b, M3-03, M3-06, M3-07b, M3-08a (`RunSessionResumeTests.Start_RestoresNodesBeforeHealth`, `Start_RestoresSlotsAfterTheRunnerKnowsTheActives`) |
| `RunSession.Start` composes the stage **before** `RunStarted` and begins the director **after** `SpawnAll` | a mode introducing nothing at its starting stage throws with the run announced; wave 1's concurrency check cannot see dressed-in enemies | M2-05 |
| **`EnemySystem.ApplyDamage` leaves the corpse registered; a behaviour retiring *another* agent queues through `EnemySystem.DespawnAtEndOfTick`, never `Despawn`; the forward walk stays forward** | `EnemyRegistry.Despawn` is an order-preserving shift, so a direct despawn inside the pass reads past the span into a null; reversing the walk would change the order every enemy acts in, which `TargetScorer`'s tie-break reads and a seeded run replays | M2-08, M4-01b |
| **A boss's phase check runs after this frame's damage and before the boss acts**: `BossPhases.Tick(Health.Fraction, dt)`, then clear, beat, summon, announce, then the delegated behaviour. The fraction is `Health`'s, **never `EnemyBlackboard.HpFraction`**; adds clear on the beat's *first* tick | a crossing seen a frame late; a corpse's blackboard is frozen at its last living reading, so a machine driven from it reads a dead boss as healthy — which is also why `SpawnDirector` ends a boss stage on `IsAlive` | M4-01b (`Order_CrossingIsSeenTheFrameTheHitLands`, `Boss_ACorpseIsNotAHealthyBoss`) |
| An explosion is triggered by the spec carrying an `ExplosionSpec`, **never by the behaviour kind**, and is published after the `EnemyDied` that caused it | "explodes on death" stops being true for a Bloater killed by anything but its fuse; M7-02's Volatile affix would need a second mechanism | M2-08 |
| Ingest runs before anything reads an enemy | every distance a frame stale, reads as an AI bug | M1-06 |
| `EnemySystem.Ingest` is two passes **split by writer** — snapshot-keyed copy, then registry-keyed derive | a lagging enemy carries a distance from last frame's position | M1-06 |
| `PlayerMotor.Tick` integrates velocity **before** facing | a frame of rotation discarded on every start from rest, invisible to a test written from rest | M0-07 |
| `EnemyAgent.Initialise` re-bases `Health.MaxHp` **before** `Health.Reset()` | a recycled enemy arrives at the previous archetype's hit points | M1-05 |
| `EnemyAgent.Initialise` calls the **no-argument** `Stat.RemoveAll()` on all three stats before re-basing | a recycled Husk wears the last one's depth scaling, later its affixes; a source token would need every future source listed at this line | M2-03 |
| `EnemySystem.Spawn` applies depth **after** registration and **before** `EnemySpawned` | a health bar built on the spawn event is sized to the unscaled maximum for a frame | M2-03 |
| `DepthScaling.Apply` refills health **after** the `MaxHp` modifier goes on | a stage-20 Husk at stage-1 hit points behind a part-filled bar | M2-03 |
| `EnemyHitFeedback.ResetVisuals` restores the opaque material **before** writing the colour | the dissolve's alpha honoured for a frame by the transparent material | M1-12 |
| Both run lifecycle events publish **before** `IsRunning` moves | a reader inside a lifecycle event sees the state it is *leaving* — deliberately | M0-10 |
| `EnemySpawned` is published **after** registration; `EnemyDespawned` **after** removal | a handler resolving the id finds nothing, or a ghost | M1-06 |
| A subscriber that must hear the opening of a run subscribes **from a constructor on the dependency chain**, never from a `Start` of its own | VContainer orders no two entry points' `Start`s; the opening population is dropped in silence | M1-07 |
| A view that answers core with a **fact** takes `Step(dt)` from `RunTicker` and never owns an `Update` | core writes intents after the reader applied them; the intent list reads empty *every* frame | M1-16 |
| **`RunTicker.LevelUpPhase` sits above `CommandPhase`, below the `IsRunning` guard, and a held pause returns before either** — the level-up opens between ticks, never inside one; a paused frame is skipped, not clocked at zero; the tick that earns the level finishes. A gated frame costs zero simulated seconds, so `RunState.Time` is *play* time | below the phase, a tap on the level-up screen also focuses or casts; inside the tick, a draw is captured at a stream position already advanced and killing the app with a pick owed is a free reroll; a `Dt = 0` tick still walks the whole pipeline and fires a due swing | M3-08a (`FrameOrderTests.Frame_LevelUpPhaseRunsAboveCommands`, `Frame_TheLevellingTickCompletes`, `Frame_PausedFrameDoesNotTickCore`, `Frame_PausedTicksCostNoSimulatedTime`) |

### 18.2 The boundary

- **`IRunSession.Tick` takes the snapshot alone.** `Dt` rides on it; there is **no `IClock` in the session** — simulated time is the sum of each tick's `Dt`, and wall-clock is a different port (M0-09, M0-10).
- **A port grows a member when the mechanic that needs it lands, not before** (M0-09).
- **Core decides every outcome an enemy causes and calls `PlayerCombat.ApplyDamage` directly.** Unity owes core a *fact* only when the answer depends on colliders core does not hold (`ReportConeHits`, `ReportChargeHits`); a standing geometric question is a *sense* on the snapshot (`LineOfSightSense` → `EnemySense.HasLineOfSight`). Contact and projectile impact are core-computed. Core holds no walls and never will (M1-18, M2-07a, M2-08, M2-11b; M2 ledger rows 7 and 13).
- **`EnemyTickContext` is built once per tick by `RunSession`, never per agent, and no behaviour may store it** — implementers unpack it into fields and clear them in a `finally`. It has carried the census since M2-08, bounded because the blast that follows is resolved by `EnemySystem` off the spec (M2-07b, M2-08).
- **Everything downstream of `SnapshotBuilder` integrates `snapshot.Dt`, never `Time.deltaTime`.** Purely cosmetic view timers are the exception (M0-16).
- **`WorldSnapshot.Clear()` does not zero `Enemies`.** Readers stop at `EnemyCount`, and **a writer into a reused slot assigns every field, zeroes included** (M0-05, M1-07).
- **`IntentBuffer.Clear()` lowers `HasPlayerMove` and leaves the stored intent alone.** Every reader checks its flag first, or the player slides (M0-06).
- **`PlayerView.Velocity` is the velocity core asked for, never `CharacterController.velocity`**, which collapses to zero against a wall (M0-16).
- **Animation never gates a damage frame, and no clip carries an Animation Event.** Core owns the cadence (CC §4.2); `PlayerAnimatorView` derives `AttackSpeed` from the measured interval between real swings (M2-art).
- **`applyRootMotion` is off on every rig, and every importer's root node is empty.** Nine of the 173 KayKit clips carry root translation, so this is a live guard (M2-art).
- **An unreached arena is instantiated under a *deactivated* root, never instantiated and then deactivated** — the second runs `Awake`/`OnEnable`, draws for a frame and doubles the navigation data under the live arena. `ArenaPool` keeps two roots; raising is a reparent plus `SetActive`. Every "build it now, show it later" owes the same shape (M2-11a).
- **Where a body may spawn is a property of the arena.** It arrives on `WorldSnapshot.SpawnPoints` and `SpawnDirector.Begin` **copies** it, because the snapshot is one buffer refilled every frame (M2-11a).
- **`FollowCamera`'s yaw stays 0.** The day it can turn, the −yaw rotation goes into `SnapshotBuilder` and never into core (M0-18).
- **A Wight's walk leaves through `IIntentSink.MinionMove`, never `EnemyMove`, and the struct is the same one.** The three facts are identical; the *registries* are not — `MinionSystem` and `EnemyRegistry` both issue ids from 1, so one shared door would let a Wight's id steer a Husk. `WorldSnapshot.Minions` is a second array for the same reason, sized from `MinionSystem.MaxConcurrent` so it cannot fall behind the cap (M5-04a).
- **A live object is never handed out of `RunState`.** `Motor`, `Combat`, `Enemies` and `Projectiles` are `internal` with scalar reads, because a public handle on something with a `Tick` lets a view double-integrate a frame. Every future mutable field owes the same question (M0-16, M1-06, M1-08, M2-07a).
- **`RunState`'s constructor and setters are `internal`, and `Soulvail.Tests.Core` has no `InternalsVisibleTo`** — tests go through `RunSession`, which is also why that constructor carries no argument guards (M0-10).

### 18.3 Numbers and identity

- **A non-positive guard on a float is spelled `!(value > 0f)`, never `value <= 0f`** — NaN passes the natural spelling and is permanent (M0-07).
- **Guard the argument *before* the arithmetic that launders it** — `radius * radius` turns −1 into 1 (M1-09).
- **NaN is refused at every door into a `Stat`** — it produces *silence*, not a wrong number, because `Changed` compares and stops firing (M1-01).
- **A struct with an invariant checks at both ends** — `default(ContentId)` and `default(Modifier)` pass the constructor's guard (M0-08, M1-01).
- **`ContentId`'s grammar is `^[a-z0-9]+(\.[a-z0-9_-]+)+$`.** Loosening is safe; tightening invalidates content already on disk (M0-08).
- **`SeededRandom`'s stream indices — Spawn 0, Offers 1, Affixes 2, Drops 3, Misc 4 — are part of what a seed means.** A new stream takes the next free index; never renumber (M0-04).
- **Choosing a position makes exactly one draw**: draw a start index, then *walk* the candidates deterministically, so a refusal costs what an acceptance costs and the seed replays whatever the player did. Anything that picks a place from a list owes the same shape (M1-19, M2-05).
- **GD §12's formulas are authoritative; a table row that disagrees is the error.** `B(40)` is 1 876.9, the code and its tests assert the formula, and the table was corrected to it at M2-15 (M2-03).
- **`WaveComposer` tracks spend as `int` and each wave's allowance as the *cumulative* share minus that spend, never a running `float` remainder** — a float composes nine Husks at stage 1 instead of GD §12.1's ten. Anything that divides a budget into whole units owes the same shape (M2-04).
- **Depth scaling is `PercentMult`, never `PercentAdd`** — it must multiply with an Elite's 2.2× rather than pool with it (GD §8.3) (M2-03).
- **`StatCurve`'s `stageOffset` is 0 or 1 and nothing else** — it spells GD §12.3's `n−1` against `n`; a free shift gives the first stages no scaling (M2-03).
- **Enemy despawn compacts rather than swapping with the last** — spawn order feeds `TargetScorer`'s tie-break (M1-05).
- **A lazy cache and a "fires only on change" event cannot both be lazy** — `Stat` is lazy only while nothing subscribes (M1-01).

### 18.4 Combat and perception

- **All perception is XZ** — distances, directions, the 6 m ally radius, spawn safety. Any new sense that measures a separation owes the same (M1-06).
- **`LineOfSightSense` is the one exception, measuring occlusion rather than separation**: a 3D ray with both ends at `EyeHeight` 1.1 m, so a 1.5 m pillar blocks it and the floor does not. Every *distance* a Spitter uses stays XZ (M2-11b).
- **That sense's mask is `Cover` and never the Enemy layer** — cover is the arena's, not the crowd's (M2-11b).
- **An unmeasured sight line means "can see"**; `SnapshotBuilder` writes `true` for every slot when no sense is composed, because *false* switches the ranged archetype off in silence. `NavPathSense` makes the same choice (M2-11b).
- **`Health.Tick` credits only the slice of its step past the recharge deadline**, so the Aegis is worth the same at 30 fps and 120. Anything that resumes on a deadline mid-step owes the same arithmetic (M1-02).
- **`Health` publishes nothing.** Its owner turns `DamageResult` into events (M1-02, M1-08, M1-11).
- **`Health.HasShield` means "has a `ShieldSpec`", not "the shield is up"**; ask `Shield > 0` for the other question (M1-02).
- **A telegraph is a promise.** `SpawnDirector` never cancels, moves or lets the concurrency cap eat a `SpawnTelegraphed` — the cap counts the *pending* as well as the living. `Clear` is the one exception (M2-05).
- **A Spitter's aim never cancels, the deliberate opposite of the Chaser's windup** — the dodge window is the *flight*. Ranged owes the Spitter's shape, melee the Chaser's (M2-07b).
- **A walk *in* follows `PathDirectionToPlayer`; a walk *out* is `−DirectionToPlayer` and never a negated path**, which points at the next waypoint's opposite rather than away from the player (M2-07b).
- **`EnemyRegistry.Alive` means *registered*, not breathing.** Readers that care check `IsAlive`; the name is a wart (M1-05).
- **`Targeter`'s cadence accumulator is never reset by an immediate retarget** — or a stream of dying targets starves the scheduled decision (M1-04).
- **`Targeter.FocusedTargetId` lags `Focus(id)` by one tick on purpose** — between ticks there is no candidate span to validate against (M1-04).
- **`TargetScorer` resolves an exact tie to the lowest id even against the incumbent** — hysteresis is a bonus before the comparison, not a veto after (M1-03).
- **`EnemySpec` does not validate `EnemyBehaviourKind`** — `EnemySystem.Tick`'s dispatch is the loud place, being the one site that knows the full set (M1-05).
- **A pooled `EnemyView` forgets its last life in `OnDespawn`** — scale, material, alpha, collider, knockback — and a forgotten reset produces a half-transparent unhittable Husk. The archetype's tint and scale are re-applied by `EnemyViews` on **every** spawn, `EnemyLook.Default` when none is authored (M1-19, M2-06).
- **`EnemyView` carries two colliders and they are not interchangeable**: the `CharacterController` moves the body; the trigger `CapsuleCollider` is what sweeps query and `EnemyViews` indexes (M1-18).
- **The floating stick's touch region is the left 45 % of the screen, full height.** Every on-screen control added later must be a raycast target, or it steals the focus (M1-09).

### 18.5 Known soft spots

Not bugs today. **Anything a named task must deal with lives in the current milestone's
[carry-forward ledger](plan/ROADMAP.md#carry-forward-into-m4), not here** — follow the ROADMAP's newest
`Carry-forward into M<n>` heading. What remains below has no owning task yet:

| Soft spot | Bites at |
|---|---|
| `IsCurrentBlocked` suppressing the invulnerability retarget has **no test** — the outcome is identical, only the frequency changes | whenever a Warden-like enemy exists |
| `RunSession.Tick`'s ordering rule has no assertion — no public route from session to blackboard | M1-08 made it observable; still unpinned |
| A `Health` driven to `MaxHp` 0 dies without a `DamageResult` to say so | the first effect that removes max HP |
| `Targeter`'s 2 s focus-drop delay and `FocusResolver`'s 3 m radius are `const`s, not authored data | when either must differ per class |
