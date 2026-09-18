# M3-05 — Effect primitives: the registry, and `ModifyStat` as the first of them

**Size:** M · **Depends on:** M3-01a (`PlayerStat.XpGain` addresses the tracker's stat) · **Branch:** `m3-05-effect-registry`
**Design refs:** AR §5, §11.1, §11.2, §13, §18.2, §18.3; ADR-0008, ADR-0009, ADR-0010 · **Ledger rows:** none directly; row 1 through M3-12, whose nodes are made of this

## Goal

An effect is data a node carries and a handler the run supplies, matched by type in a registry nobody has to edit to add the eleventh — ADR-0009 in code, with the one primitive nearly every passive is made of.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/EffectRegistry.cs` | Core | `IEffect`, `IEffectHandler<T>`, `EffectRegistry` — the module's whole vocabulary in one file, `RunEvents.cs`' precedent |
| `Core/Effects/PlayerStat.cs` | Core | `PlayerStat` and `PlayerStats`, the address table — enum beside its resolver, `MovementSkillSpec.cs`' precedent |
| `Core/Effects/ModifyStat.cs` | Core | the primitive and its handler, one file per pair |
| `Tests/Core/Effects/EffectRegistryTests.cs` | Tests.Core | dispatch, refusal, no statics, no allocation |
| `Tests/Core/Effects/ModifyStatTests.cs` | Tests.Core | the address table and the handler against real stats |
| *small edits* | | `RunState` + `internal EffectRegistry Effects`; `RunSession.Start` builds `PlayerStats` over the combat, the motor and the tracker, and a registry with `ModifyStatHandler` registered — after the objects exist, before `RunStarted` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Effects;

/// Something a node, a Pact, an affix or an Ordeal does — as data. A marker: each primitive is
/// its own sealed class, and the registry keys on that type.
public interface IEffect { }

/// The run-side half of a primitive: what applying the data to this run's live objects means.
public interface IEffectHandler<in TEffect> where TEffect : IEffect
{
    void Apply(TEffect effect, object source);
    void Remove(TEffect effect, object source);
}

public sealed class EffectRegistry
{
    public int HandlerCount { get; }

    public void Register<TEffect>(IEffectHandler<TEffect> handler) where TEffect : IEffect;   // one per type
    public bool CanApply(IEffect effect);                 // for validation: is there a handler for this type?
    public void Apply(IEffect effect, object source);     // KeyNotFoundException naming the type when there is not
    public void Remove(IEffect effect, object source);
}

/// Every player number that exists as a Stat in code. Closed: adding one is a Stat on a core
/// object, a member here, a line in PlayerStats.Resolve and a row in the test that walks this enum.
public enum PlayerStat
{
    MaxHp,                  // Health.MaxHp
    WeaponDamage,           // Weapon.Damage
    FireRate,               // Weapon.FireRate
    MoveSpeed,              // PlayerMotor.Speed
    MovementSkillCooldown,  // ChargeSkill.Cooldown
    XpGain,                 // LevelTracker.XpGain
}

/// Where each PlayerStat lives this run. Built once in RunSession.Start from the live objects.
public sealed class PlayerStats
{
    public PlayerStats(PlayerCombat combat, PlayerMotor motor, LevelTracker progression);

    public Stat Resolve(PlayerStat stat);      // ArgumentOutOfRangeException for a member it does not know
}

/// One Modifier on one stat, as data. "+2 damage and +15 %" is two of these.
public sealed class ModifyStat : IEffect
{
    public ModifyStat(PlayerStat stat, ModifierKind kind, float value);   // value finite; kind one of the three

    public PlayerStat Stat { get; }
    public ModifierKind Kind { get; }
    public float Value { get; }
}

public sealed class ModifyStatHandler : IEffectHandler<ModifyStat>
{
    public ModifyStatHandler(PlayerStats stats);
    // Apply:  stats.Resolve(effect.Stat).Add(new Modifier(effect.Kind, effect.Value, source))
    // Remove: stats.Resolve(effect.Stat).RemoveAll(source)
}
```

```csharp
// RunState (added)
internal EffectRegistry Effects { get; }
```

## Behaviour

1. **Data and handler are split, and the split is what ADR-0009's "registered handler" means here.** The effect is an immutable record the catalog shares across every run (AR §10.1 — runtime state never lives in a spec); the handler is built per run and holds the run's live objects. The registry matches the two by the effect's CLR type, so there is no `switch (effect.Type)` anywhere (AR §13): a new primitive is one file holding a record and a handler, plus one `Register` line in `Start`. Open for extension, closed for modification, in as many words.
2. **`Register<T>` refuses a second handler for one type.** Two handlers for one effect are two opinions about what it does, and the second would silently win or silently lose depending on dictionary semantics nobody should have to know. `Apply` and `Remove` dispatch on `effect.GetType()`; an effect nobody registered throws `KeyNotFoundException` naming the type. The loud place is the apply, the same argument as `EnemyBehaviourKind`'s dispatch (AR §18.4) — but M3-03's tree asks `CanApply` for every effect it holds at `Start`, so in a live run the throw is unreachable and the error arrives before `RunStarted`.
3. **Nothing allocates on `Apply` or `Remove` after registration.** A dictionary probe and a typed call through a private binding; effects are classes, so nothing boxes. Applying happens on a pick, not on a frame, but a level-up screen is a moment the frame budget is already spending on UI.
4. **`PlayerStat` is a closed set and an enum is the right shape** (ADR-0010: enums for closed sets). Its members are exactly the player numbers that exist as a `Stat` in code today: `Health.MaxHp`, `Weapon.Damage`, `Weapon.FireRate`, `PlayerMotor.Speed`, `ChargeSkill.Cooldown`, `LevelTracker.XpGain`. `PlayerStats.Resolve` is a switch over it with a loud `default` — `Stat.Pool`'s shape — and `Stats_ResolveEveryMember` walks `Enum.GetValues`, so a member added without a resolver line fails in the suite rather than at a pick.
5. **Not addressable, on purpose, and each becomes addressable in the task whose node wants it:** `Weapon.Range` and `ConeAngleDeg` (forwarded floats, by `Weapon`'s own remark), `MovementSkillSpec.Damage` and `Knockback` (read off the spec — `PlayerCombat.ResolveChargeHits` says *"when M3-12 gives them one, this is the line that changes"*), the shield's three numbers (authored on `ShieldSpec`; a recharge node moves `RechargeDelay`). Each is a `Stat` on its owner, a `PlayerStat` member, a resolver line and a test row — **M3-12 budgets for as many of these as its twelve nodes need**, and Wide Censure, Charge damage and Unbroken are the likely three.
6. **`ModifyStat` is one `Modifier`.** `Apply` adds `new Modifier(Kind, Value, source)` to the resolved stat; a node granting "+2 damage and +15 %" carries two of them, which is `Stat.Add`'s own remark. `Remove` is `RemoveAll(source)` on that stat — it takes **the source** back from the stat, both of the example's modifiers at once, not the one effect. Stated, and right for V1: nothing removes a node (respec was deleted with v0.1's meta systems, CH §7), and the first timed buff (M3-06 / M3-11) owns its own clock and calls `Remove` for its own source, which is exactly a source being taken back.
7. **The source is whoever owns the effect, passed by the caller.** M3-03 passes the `SkillSpec` — shared, immutable, taken once per run, so a reference stable for the life of the run and never confused with another node's. `Remove` with a source that has nothing on the stat is not an error (`Stat.RemoveAll`'s contract), so a caller can clean up unconditionally.
8. **`PercentAdd` is the kind a node reaches for; `PercentMult` is reserved for the loud multipliers** — `ModifierKind`'s own remark and `FocusTracker`'s argument, and GD §13.1's *"additively within a family, multiplicatively across"*. The kind is data and the author's choice; this task refuses nothing on it, and M3-12 owes one line per node saying which and why.
9. **`RunState.Effects` is `internal`** (AR §18.2's standing question): `Apply` is public on the registry, so a public handle would let a view put a modifier on the player with nothing in the compiler to object. M3-03's tree is the only production caller.
10. **Nothing in a live run calls `Apply` until M3-03** — M2-01's and M2-13a's shape, for the same reason: the registry and the first primitive are one review, the tree that applies them is another.

## Tests

| Test | Given / When / Then |
|---|---|
| `Register_TwiceForOneType_Throws` | a handler for a fixture effect / `Register` twice / throws (rule 2) |
| `Register_Null_Throws` | — / `Register<T>(null)` / throws |
| `Apply_DispatchesToTheHandler` | a fixture effect and a counting handler / `Apply(effect, source)` / the handler's `Apply` ran once with that effect and that source |
| `Remove_DispatchesToTheHandler` | same / `Remove` / once |
| `Apply_TwoTypesTwoHandlers` | two fixture effect types / `Apply` each / each reached its own handler and not the other's (rule 1) |
| `Apply_Unregistered_ThrowsNamingTheType` | an effect with no handler / `Apply` / `KeyNotFoundException`, message contains the type's name (rule 2) |
| `CanApply_AnswersRegistration` | one registered, one not / `CanApply` / true, false |
| `Apply_AllocatesNothing` | registered, warm-up / 10 000 × `Apply` / allocated-bytes delta == 0 (rule 3) |
| `Registry_HoldsNoStatics` | reflection over `EffectRegistry` / — / no static fields — a registry is the classic singleton temptation and AR §1's fourth sentence says no |
| `Stats_ResolveEveryMember` | a real `PlayerCombat`, `PlayerMotor`, `LevelTracker` / for each `Enum.GetValues(typeof(PlayerStat))` / `Resolve` returns the very instance — `ReferenceEquals` with `combat.Health.MaxHp`, `combat.Weapon.Damage`, … (rule 4) |
| `Stats_UnknownMember_Throws` | `(PlayerStat)99` / `Resolve` / `ArgumentOutOfRangeException` |
| `Effect_RecordsFields` | — / ctor / each reads back |
| `Effect_Guards` | value NaN, ∞; kind 99 / ctor / throws each |
| `Handler_ApplyAddsOneModifier` | `ModifyStat(WeaponDamage, PercentAdd, 0.15)`, a Censer at 13 / `Apply(source)` / `Weapon.Damage.Value` 14.95, `ModifierCount` 1, the modifier's `Source` is `source` (rule 6) |
| `Handler_TwoEffectsOneSourcePool` | +2 Flat and +15 % PercentAdd, one source / both / `(13 + 2) × 1.15` (ADR-0008's order) |
| `Handler_RemoveTakesTheSourceBack` | the two above / `Remove` of the flat one only / **both** gone, `ModifierCount` 0 — rule 6's documented behaviour, pinned |
| `Handler_RemoveUnknownSource_IsSilent` | a stat with nothing from `source` / `Remove` / no throw, no change (rule 7) |
| `Handler_MaxHpMovesHealthLive` | `MaxHp` +20 Flat on a 140 class at 100 HP / `Apply` / `Health.MaxHp.Value` 160, `Current` 100 unchanged (`Health` rule 6), `Fraction` 0.625 |
| `Handler_XpGainScalesGrants` | `XpGain` +50 % / `Grant(10)` / `Xp` 15 — the M3-01a seam |
| `Handler_MoveSpeedReachesTheMotor` | `MoveSpeed` +10 % on 3 m/s / `Apply`, tick the motor / velocity magnitude 3.3 |
| `Handler_AllocatesNothingAfterWarmUp` | apply and remove once (the list's capacity) / 10 000 × `Apply` then `Remove` / allocated-bytes delta == 0 |
| `State_HandsOutNoRegistry` | reflection over `RunState` / — / `Effects` is not public (rule 9) |

"allocated-bytes delta == 0" in any spec means `AllocationAssert.None(body, iterations)` from M0-02 — never the raw `GC` API.

**Guard rows are implied, not listed:** every new spec type gets a validation row, every public constructor a null row, every `float` door a non-finite row.

## Manual verification (Editor / device)

None. Nothing is visible and nothing calls it in a run; the honest check is six assemblies compiling, which the suite makes.

## Out of scope

- **Every other primitive.** `OnKillTrigger`, `Heal`, `SpawnZone`, `ApplyStatus`, `ChainDamage` — each is a file pair in this shape, registered with one line, added by the task whose node or active needs it: M3-11 for what Consecrate and Bulwark do on cast, M3-12 for whatever its passives need beyond a stat. ADR-0009: *build the first ten as they are needed*, and this is the first.
- **Timed effects.** A modifier with a duration needs a clock, and the registry deliberately has none — `Stat` is untimed and the *source* owns the clock (its own remark). M3-06's runner is the first source with one.
- **Enemy-side stats** — M7-02's affixes want an `EnemyStat` table and a second handler in the same shape, addressing `EnemyAgent`'s three stats. Not a second registry.
- **The stats rule 5 lists.** Each is M3-12's, one at a time, with its node.
- **Authoring** — M3-02b's `EffectDefinition` and `ModifyStatDefinition` are the Inspector half of this.

## As built

**Built exactly the Files table:** three new Core files under `Core/Effects/` (a new folder inside an
existing assembly — no asmdef, no `csc.rsp`, all five new files pure C# with file-scoped
namespaces), two new test files under `Tests/Core/Effects/`, and the two small edits. Seven files
touched, nothing else in `git status`.

**Two deviations, both additive, both named here.**

1. **`RunState.Effects` is a constructor parameter, not a field assigned after construction.** The
   Files table says "`RunState` + `internal EffectRegistry Effects`" without saying how it gets
   there. It goes in through the `internal` constructor beside `progression`, because every other
   live object on that type does and because the alternative — an `internal set` — would be the
   first settable handle on a class whose entire remark is that its setters are the two scalars
   `StageIndex` and `Time`. The ripple is one call site, `RunSession.Start`.
2. **Five test rows beyond the Tests table's twenty-two**, of which four are the guard rows the
   spec says are implied rather than listed: `Apply_NullArguments_Throw` (the registry's five
   doors), `Stats_NullArguments_Throw`, `Handler_NullArguments_Throw`, and
   `Remove_Unregistered_ThrowsNamingTheType` — the `Remove` half of rule 2's loud absence, which
   the table spells only for `Apply`. The fifth is not a guard row and is called out on purpose:
   **`Registries_DoNotShareHandlers`** puts a handler in one registry and asks a second one, which
   is the half of `Registry_HoldsNoStatics` reflection cannot see. Reflection proves there is no
   static *field*; it cannot prove two instances do not share a table, and "the registry that
   applied the last run's nodes to this run's player" is the failure AR §1 is actually about.

**Three decisions the spec left open, and how they went.**

- **`Register` refuses a second handler with `InvalidOperationException`**, not
  `ArgumentException`: the argument is fine, the registry's state is what makes the call wrong.
- **`Apply`, `Remove` and `CanApply` guard `effect` and `source` for null.** The spec guards
  constructors; these are the doors M3-03 will call from a loop over authored content, and a null
  in an effect list is a content bug that should name its parameter rather than arrive as a
  `NullReferenceException` from inside a dictionary probe. Costs nothing on the happy path.
- **`ModifyStat`'s constructor does not validate `PlayerStat`**, matching the Public API's comment
  (*"value finite; kind one of the three"*). The loud place is `PlayerStats.Resolve`, which is the
  one site that knows the full set — `MovementSkillKind`'s argument, quoted in the file. Worth
  knowing for M3-02b: `CanApply` answers "is there a handler for this type", not "is this effect's
  address resolvable", so an authored `(PlayerStat)99` would survive M3-03's validation sweep and
  throw at the pick. Closing that would mean a second validation door on the effect itself, which
  is M3-02b's or M3-14b's call to make with the authoring in front of it.

**One thing the table asks for that no test can see.** `RunSession.Start` builds the `PlayerStats`,
the registry and the `ModifyStatHandler`, and registers it — but `RunState.Effects` is `internal`
(rule 9) and `Soulvail.Tests.Core` has no `InternalsVisibleTo` (AR §18.2, deliberate), so there is
no route for a test to observe that the registration happened. The Tests table has no row for it
and correctly so; rule 10 means nothing in a live run would notice either. **M3-03 is the first
task that can prove the line is there**, and the first that would fail if it were not.

**Verification.** 1148 EditMode / 0 / 0, twice consecutively, against M3-01b's 1121 — **27 new
rows**, which is the twenty-two above plus the five. PlayMode 11/11. Six assemblies, zero compile
errors, zero analyzer warnings. Every one of the seven files confirmed in its intended assembly
through `GetAssemblyNameFromScriptPath`, and all eight new types confirmed present in
`Soulvail.Core` / `Soulvail.Tests.Core` by name rather than assumed (Traps §5).

**Manual verification:** none, as specced. Nothing is visible and nothing in a run calls `Apply`.
