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
| `Combat` | Health, shields, damage, i-frames, targeting, weapons, skills | `Health`, `Stat`, `Targeter`, `TargetScorer`, `Weapon`, `SkillRunner`, `CombatBlackboard` |
| `Ai` | Enemy and boss behaviour | `EnemyAgent`, `EnemyBlackboard`, `StateMachine<T>`, per-archetype behaviours, boss phases |
| `Director` | Spawn budget and composition | `ThreatBudget`, `SpawnDirector`, `WaveComposer` |
| `Progression` | XP, levels, tree, offers, Veilrot | `XpCurve`, `SkillTree`, `OfferGenerator`, `Veilrot` |
| `Economy` | Essence, Shards, Sanctum services, unlocks | `Wallet`, `Sanctum`, `Unlocks` |
| `Content` | Immutable spec records + lookup | `ContentCatalog`, `CharacterSpec`, `EnemySpec`, `ModeSpec`, `SkillSpec`, `ContentId`, `TagSet` |
| `Effects` | Composable effect primitives | `IEffect`, `EffectRegistry`, primitives |
| `Persistence` | DTOs, versioning, migrations | `PlayerProfile`, `RunSnapshot`, `IMigration` |
| `Ports` | Every interface the outside implements or calls | see §6 |
| `Events` | Domain event records | `readonly struct` per event |

`ModeSpec` moved from `Run` to `Content` at M2-02. `Run` owns the *rules* a mode implies — stage flow, what a run does when a stage is cleared — but the mode itself is an immutable spec resolved from a `ContentId`, and §10.1 has always drawn it in the catalog beside `CharacterSpec` and `EnemySpec`. The two sections disagreed; §10.1 was right, and this row was the sketch.

---

## 6. Ports

| Direction | Port | Purpose | Implemented by |
|---|---|---|---|
| Inbound | `IRunSession` | `Start(RunConfig)`, `Tick(WorldSnapshot)`, `End()`, plus `IsRunning` / `State`, and the facts as they land: `ReportConeHits` (M1-11), `ReportChargeHits` (M1-15), a projectile fact with M2-07. Contact is ledger row 7 — M1-18 chose a core-side call over a fact; M2-08 settles it | Core |
| Inbound | `IPlayerCommands` | `FocusTarget(worldPoint)`, `ClearFocus()` (M1-09), `MovementSkill()` (M1-15); `CastSkill(slot)` and `SetAutoCast(skillId, bool)` with M3-06/07 | Core |
| Inbound | `IProgressionCommands` | `ChooseOffer(index)`, `Reroll()`, `Banish(skillId)`, `BuyHeal()`, `BuyCleanse()` | Core |
| Outbound | `IClock` | `UtcNow` (wall-clock, `DateTimeOffset`) — **and nothing else** (M2-01). Never simulated time: that is the sum of each tick's `Dt` (§18.2) | `UnityClock` |
| Outbound | `IRandom` | Named streams: `Spawn`, `Offers`, `Affixes`, `Drops`, `Misc` | `SeededRandom` (xorshift/PCG, seedable) |
| Outbound | `IDomainEvents` | `Publish<T>(in T evt)` | `DomainEventHub` (scoped, typed fan-out) |
| Outbound | `ISaveStore` | Async load/save of profile and run snapshot | `LocalJsonSaveStore` now, `SyncingSaveStore` later |
| Outbound | `ILocalizer` | `string Get(LocKey key, params)` | `TableLocalizer` |
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
    Task<PlayerProfile?> LoadProfile();
    Task SaveProfile(PlayerProfile profile);
    Task<RunSnapshot?>  LoadRun();
    Task SaveRun(RunSnapshot run);
    Task ClearRun();
}
```

- **Async from day one**, even though the local adapter is a synchronous file write — a server adapter must not change a signature.
- `PlayerProfile` (Shards, unlocks, settings) and `RunSnapshot` (written at every stage boundary, deleted on death — Android kills backgrounded apps).
- **Every DTO carries `int Version`.** Migrations are pure core functions with a fixture test per version.
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

Toolchain traps — things that lie to you rather than rules the code relies on — live in
[Traps.md](Traps.md). The two files are deliberately separate: one is about our design, the other
is about Unity's.

### 18.1 Ordering

| Invariant | Break it and | Set in |
|---|---|---|
| `RunTicker`'s frame order: commands → snapshot → clear intents → core tick → bodies → facts → knockbacks | a tap lands a frame late; a cleared buffer erases an unread intent; a sweep resolves against last frame's arena | M0-16, M1-09, M1-12, M1-15 |
| `RunSession.Tick`: time → ingest → combat → enemy behaviours → (dead? end) → **director** → motor → intent | the gun aims at where enemies *were*; the motor turns before it knows its facing | M1-06, M1-08, M2-05 |
| The director ticks **after** the death check and **before** the motor | a wave is telegraphed into an arena whose run ended this tick; or the director sees a player position the rest of the tick did not | M2-05 |
| `RunSession.Start` composes the stage **before** `RunStarted` and begins the director **after** `SpawnAll` | a mode that introduces nothing at its own starting stage throws with the run announced (ledger row 3 again); or wave 1's concurrency check cannot see the arena's dressed-in enemies | M2-05 |
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

### 18.2 The boundary

- **`IRunSession.Tick` takes the snapshot alone.** `Dt` rides on the snapshot; two ways to say how
  much time passed is one too many. There is **no `IClock` in the session** — simulated time is the
  sum of each tick's `Dt`. Wall-clock is a different number and a different port (M0-09, M0-10).
- **A port grows a member when the mechanic that needs it lands, not before** (M0-09).
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
- **`FollowCamera`'s yaw must stay 0.** It is the only reason `SnapshotBuilder`'s straight-through
  stick mapping is camera-relative. The day the camera can turn, the −yaw rotation goes into the
  builder and **never into core** (M0-18).
- **A live object is never handed out of `RunState`.** `Motor`, `Combat` and `Enemies` are
  `internal` with public scalar reads, because a public handle on something with a `Tick` lets a
  view double-integrate a frame with nothing in the compiler to object. **Every future `RunState`
  field that hands out a mutable object owes the same question** (M0-16, M1-06, M1-08).
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

Not bugs today. **Anything a named task must deal with lives in the
[carry-forward ledger](plan/ROADMAP.md#carry-forward-into-m2), not here** — one list, one owner per
row, and a row leaves when its owner's *As built* says so. What remains below has no owning task yet:

| Soft spot | Bites at |
|---|---|
| `IsCurrentBlocked` suppressing the invulnerability retarget has **no test** — the outcome is identical, only the frequency changes | whenever a Warden-like enemy exists |
| `RunSession.Tick`'s ordering rule has no assertion — no public route from session to blackboard | M1-08 made it observable; still unpinned |
| A `Health` driven to `MaxHp` 0 dies without a `DamageResult` to say so | the first effect that removes max HP |
| `Targeter`'s 2 s focus-drop delay and `FocusResolver`'s 3 m radius are `const`s, not authored data | when either must differ per class |
