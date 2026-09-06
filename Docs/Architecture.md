# Soulvail — Architecture

**Version:** 1.0 — decided 2026-09-06
**Companions:** [GameDesign.md](GameDesign.md) · [Characters.md](Characters.md) · [CoreCombat.md](CoreCombat.md) · [adr/](adr/) (why each decision was made)
**Status:** Decided. Nothing here is implemented yet. Change it through a superseding ADR, not by routing around it.

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
  COMMANDS (immediate) ─────►┌────────────┐
    Charge tap, tap-to-focus │            │─────► EVENTS
    pick node, toggle auto   │            │       EnemyDied, PlayerDamaged, LeveledUp,
                             │    CORE    │       SkillCast, StageCleared, RunEnded …
  FACTS (immediate) ────────►│            │
    cone hit ids, contacts   │  Tick(dt,  │─────► INTENTS (per tick)
    projectile impacts       │  snapshot) │       PlayerMove(v), EnemyMove(id, dir, speed),
                             │            │       EnemyAction(id, Lunge…), Spawn(spec, pos)
  TICK (every frame) ───────►└────────────┘
    dt + spatial snapshot
```

| Channel | What | When | How |
|---|---|---|---|
| **Commands** | Discrete player input | The instant it happens | Method call on an inbound port. A Charge press delayed by a tick would feel broken. |
| **Facts** | Discrete physical results | The instant they happen | Method call: `ReportConeHits(ids)`, `ReportContact(enemyId)`. Core turns facts into outcomes. |
| **Tick** | `dt` + where things are | Every frame | `session.Tick(dt, snapshot)`. Core throttles its own expensive work internally. |
| **Events** | What happened | As it happens | Outbound port `IDomainEvents`. Views, audio, haptics, analytics subscribe. |
| **Intents** | What core wants the body to do | Every tick | Written into a preallocated intent buffer the views read after the tick. |

### 4.2 The snapshot

With all logic in core, core already owns HP, cooldowns, states, and targets. The snapshot is only what core cannot know: **where things are.**

```csharp
public struct WorldSnapshot
{
    public float    Dt;
    public Vector2  MoveInput;                 // stick, already deadzoned/banded by the input adapter
    public Vector3  PlayerPosition;
    public Vector3  PlayerVelocity;
    public int      EnemyCount;
    public EnemySense[] Enemies;               // preallocated to the concurrency cap
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

- `System.Numerics` vectors — pure C#, no `UnityEngine`. Adapters convert at the boundary.
- **Zero allocations.** Preallocated arrays sized to the device-tier concurrency cap, structs, int IDs. Rebuilt in place every frame.
- Core ticks **every frame with variable `dt`**. The targeting scorer runs on its own 0.1s accumulator inside core; the director on 0.5s. Frequency is a tuning detail inside core, not an architectural boundary.

### 4.3 Frame order (Unity side)

```
1. Input adapter reads touches → commands sent immediately; move vector cached
2. SnapshotBuilder fills WorldSnapshot from transforms + NavMesh
3. RunTicker.Tick():  session.Tick(dt, snapshot)     ← core does everything
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
| `Run` | Run lifecycle, stage flow, mode rules | `RunSession` (the inbound façade), `RunState`, `StageFlow` (FSM), `ModeSpec` |
| `Combat` | Health, shields, damage, i-frames, targeting, weapons, skills | `Health`, `Stat`, `Targeter`, `TargetScorer`, `Weapon`, `SkillRunner`, `CombatBlackboard` |
| `Ai` | Enemy and boss behaviour | `EnemyAgent`, `EnemyBlackboard`, `StateMachine<T>`, per-archetype behaviours, boss phases |
| `Director` | Spawn budget and composition | `ThreatBudget`, `SpawnDirector`, `WaveComposer` |
| `Progression` | XP, levels, tree, offers, Veilrot | `XpCurve`, `SkillTree`, `OfferGenerator`, `Veilrot` |
| `Economy` | Essence, Shards, Sanctum services, unlocks | `Wallet`, `Sanctum`, `Unlocks` |
| `Content` | Immutable spec records + lookup | `ContentCatalog`, `CharacterSpec`, `EnemySpec`, `SkillSpec`, `ContentId`, `TagSet` |
| `Effects` | Composable effect primitives | `IEffect`, `EffectRegistry`, primitives |
| `Persistence` | DTOs, versioning, migrations | `PlayerProfile`, `RunSnapshot`, `IMigration` |
| `Ports` | Every interface the outside implements or calls | see §6 |
| `Events` | Domain event records | `readonly struct` per event |

---

## 6. Ports

| Direction | Port | Purpose | Implemented by |
|---|---|---|---|
| Inbound | `IRunSession` | `Start(mode, class)`, `Tick(dt, snapshot)`, `ReportConeHits`, `ReportContact`, `ReportProjectileHit`, `EndRun` | Core |
| Inbound | `IPlayerCommands` | `Charge()`, `CastSkill(slot)`, `FocusTarget(worldPoint)`, `ClearFocus()`, `SetAutoCast(skillId, bool)` | Core |
| Inbound | `IProgressionCommands` | `ChooseOffer(index)`, `Reroll()`, `Banish(skillId)`, `BuyHeal()`, `BuyCleanse()` | Core |
| Outbound | `IClock` | `Now` (core time, seconds) | `UnityClock` |
| Outbound | `IRandom` | Named streams: `Spawn`, `Offers`, `Affixes`, `Drops`, `Misc` | `SeededRandom` (xorshift/PCG, seedable) |
| Outbound | `IDomainEvents` | `Publish<T>(in T evt)` | `DomainEventHub` (scoped, typed fan-out) |
| Outbound | `ISaveStore` | Async load/save of profile and run snapshot | `LocalJsonSaveStore` now, `SyncingSaveStore` later |
| Outbound | `ILocalizer` | `string Get(LocKey key, params)` | `TableLocalizer` |
| Outbound | `IIntentSink` | Where core writes per-tick intents | `IntentBuffer` (preallocated) |

Ports are the *only* things in core that mention the outside world. If a core class needs something not on this list, the answer is a new port or a new snapshot field — never a Unity reference.

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

- **Core classes use constructor injection.** `new RunSession(clock, random, events, catalog, intents)`. No attributes in core.
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
    public float Value { get; }                     // cached; recomputed on change
    public void Add(Modifier m);                    // Flat → PercentAdd → PercentMult, in that order
    public void RemoveAll(object source);           // buff ended, node removed, Rot threshold crossed back
    public IReadOnlyList<Modifier> Modifiers { get; } // for the debug panel: "47 = 13 + 15% (node) + 45% (Pact) × 1.2 (Focus)"
}
```

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
│   │                              SnapshotBuilder, IntentBuffer, DomainEventHub, InputAdapter
│   ├── Authoring/                 ScriptableObject definitions + ToSpec()
│   ├── Views/                     PlayerView, EnemyView, projectiles, VFX, reticles
│   ├── Presentation/              HUD, LevelUp, Sanctum, Menu presenters
│   ├── Controls/                  FloatingStick, skill buttons
│   └── Pooling/
├── Editor/                        Soulvail.Editor.asmdef (validation, tooling)
├── Tests/Core/                    Soulvail.Tests.Core.asmdef (EditMode, NUnit, → Core only)
├── Data/                          SO instances: Characters/ Enemies/ Skills/ Modes/ Tuning/
├── Prefabs/  Scenes/  Art/  Audio/  Materials/
```

- `Core` under `Assets/` with an asmdef is the pragmatic choice; a separate .NET class library would give better tooling but needs a `dotnet` toolchain that isn't installed. Revisit if wanted.
- Third-party packages (VContainer) come through the Package Manager, never inside `_Project`.

---

## 13. Conventions and banned patterns

**Conventions**
- Namespaces mirror folders. Private fields `_camelCase`; `[SerializeField] private`, never public fields. File-scoped namespaces. One class per file. `.editorconfig` enforces.
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
