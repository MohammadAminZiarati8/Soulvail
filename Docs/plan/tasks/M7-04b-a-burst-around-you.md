# M7-04b — A burst around you

**Size:** M · **Depends on:** M7-04a · **Branch:** `m7-04b-a-burst-around-you`
**Design refs:** CH §3.1, §3.2, §4.2; CC §6.4; GD §13.1, §16.4 (P1); AR §11.2, §14, §18.1, §18.2, §18.4; ADR-0006, ADR-0009 · **Ledger rows:** [1](../ROADMAP.md#carry-forward-into-m7) — one device row

## Goal

An Active can hit everything around the player at once: damage in a radius, a shove away from the
centre, and a ring on the floor that says so. It is the verb CC §6.4's Sever and CH §4.2's Rot Nova
have waited for since M3, and the routine two Keystones reuse.

## What counting found

- **No player-side radial hit exists.** `ShockwaveSystem` and `FissureSystem` hurt the player only.
  `EnemySystem.Explode` is private, hurts the player only, and refuses a chain on purpose.
- **The walk exists twice, privately.** `ZoneSystem.Burn` and `ProjectileSystem.LandOnEnemies` each
  walk `EnemyRegistry.Alive`, skip `!IsAlive`, test the XZ distance inclusively, and call
  `EnemySystem.ApplyDamage(id, amount, now, player)`. This is the third copy, and the first public one.
  The other two are left alone: each is a pulse or a landing with its own bookkeeping, and moving them
  is a refactor nothing here needs.
- **A shove has one door**: `IIntentSink.EnemyKnockback(in EnemyKnockbackIntent)`, which `PlayerCombat`
  already holds as `_intents` and calls for a cone and a Charge. `EnemyKnockbackIntent`'s remarks say
  *"Radial knockback is a different mechanic and will carry its own direction when something needs
  it"*. The struct needs nothing: the direction is per enemy.
- **Two Keystones want the same routine from inside a damage pass.** Unbroken and Martyr fire
  when the Aegis breaks ([M7-04e](M7-04e-the-oathbounds-keystones.md)), and Second Death fires when a
  Wight kills ([M7-04f](M7-04f-the-gravecallers-keystones.md)). A burst that ran inside
  `EnemySystem.ApplyDamage` would re-enter it, which M2-08 rule 4 refuses. So rule 6 states where a
  burst may be called from, and both Keystones obey it.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Effects/Burst.cs` | Core | **New.** The primitive and its handler |
| `Game/Authoring/BurstDefinition.cs` | Game | **New.** The Inspector half — `SpawnBurnZoneDefinition`'s shape |
| `Game/Views/BurstFlashes.cs` | Game | **New.** A ring on the floor for every burst: a handful of quads, a flash and a fade |
| `Tests/Core/Effects/BurstTests.cs` | Tests.Core | **New.** The routine, the primitive, the registration, the refusals |
| `Tests/Game/Views/BurstFlashesTests.cs` | Tests.Game | **New.** The ring's size, place, colour, fade and reuse |
| *small edits* | Core, Game, Scene | `Core/Combat/PlayerCombat.cs` — **one method**, `Burst` (rules 1–6); `Core/Events/CombatEvents.cs` — `BurstReleased` (rule 5); `Core/Run/RunSession.cs` — one `Register` line, unconditional (rule 7); `Scenes/Run.unity` — a `BurstFlashes` under the decal root with eight quads; `Game/Composition/RunScope.cs` — `_burstFlashes` injected, and a missing reference refused at build (the zone prefab's guard) |
| *ripple* | — | None. No node casts a `Burst` until [M7-04h](M7-04h-the-oathbounds-twenty-seven.md), which adds it to `SkillTreeValidationTests.Registry()` |

Only these files change. Anything else is a deviation: say so in *As built*.

## Public API

```csharp
namespace Soulvail.Core.Combat;

public sealed class PlayerCombat
{
    /// <summary>
    /// Hits every living enemy within <paramref name="radius"/> of <paramref name="center"/>, on XZ,
    /// for <paramref name="damage"/>, and shoves each one <paramref name="knockback"/> metres away from
    /// the centre. Publishes one <see cref="BurstReleased"/>. Returns how many it reached.
    /// </summary>
    /// <remarks>Never called from inside <c>EnemySystem.ApplyDamage</c> — rule 6.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="enemies"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> is not finite and above zero; <paramref name="damage"/> or
    /// <paramref name="knockback"/> is negative or not finite.
    /// </exception>
    public int Burst(EnemySystem enemies, Vector3 center, float radius, float damage, float knockback, float now);
}
```

```csharp
namespace Soulvail.Core.Effects;

/// <summary>
/// A burst at the caster's feet: every enemy within <see cref="Radius"/> takes
/// <see cref="WeaponMultiple"/> × the live weapon damage and is shoved <see cref="Knockback"/> metres
/// outward (M7-04b).
/// </summary>
public sealed class Burst : IEffect
{
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="radius"/> not finite and above zero; <paramref name="weaponMultiple"/> or
    /// <paramref name="knockback"/> negative or not finite; or both zero.
    /// </exception>
    public Burst(float radius, float weaponMultiple, float knockback);

    public float Radius { get; }
    public float WeaponMultiple { get; }
    public float Knockback { get; }
}

public sealed class BurstHandler : IEffectHandler<Burst>
{
    public BurstHandler(PlayerCombat combat, EnemySystem enemies, SimulatedClock clock);

    /// <summary>Reads the weapon's damage once, then calls <c>PlayerCombat.Burst</c> at <c>Blackboard.PlayerPosition</c>.</summary>
    public void Apply(Burst effect, object source);

    /// <summary>Does nothing: a burst is over the tick it happens.</summary>
    public void Remove(Burst effect, object source);
}
```

```csharp
namespace Soulvail.Core.Events;

/// <summary>A burst went off: where, how wide, and how many it reached (rule 5).</summary>
public readonly struct BurstReleased
{
    public BurstReleased(Vector3 center, float radius, int hits);
    public readonly Vector3 Center;
    public readonly float Radius;
    public readonly int Hits;
}
```

```csharp
namespace Soulvail.Game.Views
{
    /// <summary>
    /// A ring on the floor for every <c>BurstReleased</c>: a quad scaled to the radius, flashed and
    /// faded over <c>_seconds</c>. Eight quads, the oldest reused when all are showing.
    /// </summary>
    public sealed class BurstFlashes : MonoBehaviour
    {
        [Inject] public void Construct(DomainEventHub hub);
        public int Showing { get; }
    }
}
```

## Behaviour

1. **The burst hits every living enemy inside the radius, once.** `PlayerCombat.Burst` walks
   `enemies.Registry.Alive` in its order. It skips a body whose `IsAlive` is false and tests the XZ
   distance to `center` with `<=`, so the edge is inside. Each hit is `enemies.ApplyDamage(id, damage,
   now, this)` — `this` so a Bloater the burst kills goes off against the player it was fired by
   (M2-08 rule 3). The span stays valid while walked, because a corpse stays registered (AR §18.1's
   `ApplyDamage` row).
2. **The shove is radial, and the damage reads the weapon.** Each enemy the burst reached gets
   `EnemyKnockbackIntent(id, direction, knockback)`:
   - **the direction** is the XZ unit vector from `center` to the body;
   - **no shove, still damage**, for a body closer than `1e-4` m, which has no direction;
   - **no shove** for an agent whose authored `Spec.MoveSpeed` is 0 — [M7-03b](M7-03b-the-choirmother.md)
     rule 9's test, one door along, so a burst never walks the Choirmother's ring into a pillar;
   - **no shove at all** when `knockback` is 0.

   Damage is `WeaponMultiple × Weapon.Damage.Value`, read once by the handler at the cast. So a
   burst rides every weapon node, Overflow, Kindling's ramp and the Gravecaller's Veilrot feeding. That
   keeps an Active relevant at depth, where a flat number dies with stage 20.
3. **A burst is not a weapon hit.** It does not call `Kindling.OnWeaponHitLanded`, does not count as a
   swing for `_hitCount`, and does not touch `PendingConeRequestId`. CH §3.3 counts the orb's hits. A
   burst feeding the ramp would make every Emberwright Active a second weapon.
4. **A zero multiple is a pure shove.** `Burst(r, 0, k)` calls no `ApplyDamage`, so no
   `EnemyDamaged` and no blocked flash, and shoves everything inside. [M7-04e](M7-04e-the-oathbounds-keystones.md)'s
   Unbroken is that shape. A burst of neither damage nor shove is refused where it is authored.
5. **`BurstReleased(center, radius, hits)` is published once, after the last hit and shove**, whether
   or not anything was inside. `FissureFired`'s reasoning: a burst that only flashed on a hit would
   teach that the empty ones never fired. `BurstFlashes` is its reader.
6. **Where a burst may be called from, written down because two Keystones need it.**
   - **From a tick step that is not inside a damage pass:** the skills step (this task), and the
     Aegis-break drain [M7-04e](M7-04e-the-oathbounds-keystones.md) adds.
   - **From `MinionSystem`'s strike after its `ApplyDamage` has returned**
     ([M7-04f](M7-04f-the-gravecallers-keystones.md)).
   - **Never from inside `EnemySystem.ApplyDamage`**, or from a handler it publishes to. That is
     M2-08 rule 4's recursion.
   - A burst that sets off a Bloater re-enters nothing, because `Explode` hurts only the player.
   - The method's remarks carry the rule. AR §18.1 gains the row in M7-04e, where the first caller
     inside a tick's damage passes lands.
7. **`BurstHandler` is registered for every class, unconditionally.**
   - It needs the player, the enemies and the clock, and every run has them. So Sever, Rot Nova and
     Flame Wave are lendable at CH §5.4's half-tree moment, and M6-08 rule 5's Arcana argument holds
     for them.
   - `Apply` reads `Blackboard.PlayerPosition`, which is `SpawnHealZoneHandler`'s shape.
   - `Remove` does nothing.
   - The line goes beside `SpawnBurnZone`'s in `RunSession.Start`, after `enemies` exists.
8. **The ring says what was hit, in the player's colour.**
   - `BurstFlashes` places a quad at `Center`, 0.01 m above the floor, scaled to `2 × Radius`, tinted
     `Palette.Player`.
   - It flashes to `_peakAlpha` (0.6) and fades to nothing over `_seconds` (0.3), on unscaled time:
     it is cosmetic, AR §18.2's exception.
   - **Eight quads, the oldest reused.** A ninth burst inside 0.3 s takes the ring that has shown
     longest. It never refuses and never instantiates after `Awake`.
   - `RunScope` refuses a scene whose `_burstFlashes` or quads are missing, with the zone prefab's
     message shape. A burst nobody can see is GD §16.4's P1 broken in silence.
9. **Nothing allocates on a tick.** The walk is a span, each hit a struct result, each shove a struct
   intent, the event a struct. `BurstFlashes` reuses its quads and one `MaterialPropertyBlock`.

**When the spec disagrees with itself, the Tests table wins, then Behaviour, then Public API, then Files.** When it disagrees with code an earlier task built, the code wins. Either way, name the rule you resolved in *As built* — never fix it quietly.

## Tests

| Test | Given / When / Then |
|---|---|
| `Burst_HitsEveryLivingEnemyWithin` | three Husks inside 6 m, one at 7 m, one corpse at 2 m / `Burst(6, 20, 0)` / three `EnemyDamaged` of 20, returns 3 — rule 1 |
| `Burst_TheEdgeIsInclusive` | a Husk at exactly 6 m / — / hit — rule 1 |
| `Burst_IsMeasuredOnXZ` | a Husk 5 m out and 3 m up / radius 5 / hit — rule 1, AR §18.4 |
| `Burst_ShovesAwayFromTheCentre` | Husks north and east of the centre / knockback 2 / one intent each, directions (0,1) and (1,0), distance 2 — rule 2 |
| `Burst_TheBodyAtTheCentreIsHurtAndNotShoved` | a Husk at the centre / — / damaged, no intent — rule 2 |
| `Burst_DoesNotShoveAnImmobileBody` | an agent authored `MoveSpeed` 0 inside / knockback 3 / damaged, no intent — rule 2 |
| `Burst_ScalesWithTheLiveWeapon` | a `Burst(6, 1.5, 0)` cast, then `WeaponDamage +50 %` and a second cast / — / 1.5 × base, then 2.25 × base — rule 2 |
| `Burst_DoesNotFeedKindling` | an Emberwright at 10 stacks / a burst that hits three / still 10 — rule 3 |
| `Burst_ZeroMultipleIsAPureShove` | `Burst(4, 0, 3)` over two Husks / — / no `EnemyDamaged`, two intents — rule 4 |
| `Burst_PublishesOnceEvenEmpty` | nothing inside / — / one `BurstReleased(center, r, 0)` — rule 5 |
| `Burst_TheEventIsLast` | a spy on `EnemyDamaged` and `BurstReleased` / a burst over two / both damage events precede the burst event — rule 5 |
| `Burst_SetsOffABloater` | a Bloater at 1 HP inside, the player inside its blast / — / `EnemyExploded(HitPlayer: true)`, no recursion — rules 1, 6 |
| `Burst_KillsPayLikeAnyOther` | three one-hit Husks / a burst / `DrainXp` and `DrainKills` carry three — rule 1 |
| `Burst_CastByAnActive` | an Active whose `OnCast` is `Burst(6, 1.5, 2)`, trigger met / a skills tick / the Husks around the player take 1.5 × weapon, `SkillCast` published — rule 7 |
| `Burst_IsRegisteredForEveryClass` | a run of each of the four classes / — / `EffectRegistry.CanApply(new Burst(…))` true — rule 7 |
| `Burst_RemoveDoesNothing` | a cast / `Remove` / nothing happens and nothing throws — rule 7 |
| `Burst_RefusesAnImpossibleBurst` | radius 0, −1, NaN, ∞; multiple −1, NaN, ∞; knockback −1, NaN, ∞; multiple and knockback both 0 / construct / throws naming the field — rule 4 |
| `Burst_AllocatesNothing` | 10 000 bursts over twenty Husks / `AllocationAssert.None` / zero — rule 9 |
| `Flash_DrawsTheRadius` | *(in `BurstFlashesTests`)* `BurstReleased((3, 0, 4), 6, 2)` / one frame / one quad active at (3, 0.01, 4), scale 12 — rule 8 |
| `Flash_IsThePlayersColour` | the same / — / its property block's colour's RGB is `Palette.Player` — rule 8 |
| `Flash_FadesAndHides` | one burst / 0.3 s of unscaled time / alpha 0, quad inactive, `Showing` 0 — rule 8 |
| `Flash_ANinthReusesTheOldest` | nine bursts in one frame / — / eight showing; the first ring now draws the ninth — rule 8 |
| `Flash_UnsubscribesWhenDestroyed` | destroy it / publish a burst / nothing throws — AR §8 |
| `Scope_RefusesAMissingFlashes` | `RunScope` with `_burstFlashes` null / build / `MissingReferenceException` naming the field — rule 8 |

**Guard rows are implied, not listed:** the handler's null arguments; `BurstDefinition.ToEffect`'s
rewrap naming the asset; a null `enemies` into `PlayerCombat.Burst`.

## Manual verification (Editor / device)

_No node casts one until [M7-04h](M7-04h-the-oathbounds-twenty-seven.md)._ The probe is a temporary
Active, reverted:

1. **[Editor]** Author a throwaway Active with `Burst(6, 1.5, 2)` on Tempered Vow's slot in a scratch
   tree, play an Oathbound into a wave. *Expected: a cyan ring the size of the burst, every Husk
   inside hurt and pushed outward, none outside touched.*
2. **[device]** The ring's legibility at 400 dpi against the floor, and eight bursts in one frame
   under Swarm. Row 1's.

## Out of scope

- **Any node that casts it.** The three trees, M7-04h to M7-04j.
- **Moving `ZoneSystem.Burn` or `ProjectileSystem.LandOnEnemies` onto this routine.** Rule 1's
  finding; a refactor with no caller asking for it.
- **Line of sight.** CC §6.4's *"damage all in LOS"* is read as *all within the radius*: a player-side
  sight line is a `Game` sense nothing measures for the player. M7-04h's Verdict states the reading.
- **Audio.** M7-07.

## As built

_Filled at merge, **6 000 bytes or fewer, measured** (`awk '/^## As built/,0' <spec> | wc -c`)._
