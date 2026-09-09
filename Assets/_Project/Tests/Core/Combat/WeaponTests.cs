using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M1-10's eight rules: the cadence, the damage frame, what a modifier may and may not move, and
/// the two things <c>PlayerCombat</c> does with the answers — announce the swing, and ask the body
/// what the swing touched.
/// </summary>
/// <remarks>
/// <para>
/// The Censer's numbers throughout (CC §4.1 and §7) — 13 damage, 3 swings a second, an 8 m 60° arc
/// with the damage at 0.4 of the swing — so a failure reads as "the weapon we ship changed rhythm"
/// rather than as an arithmetic puzzle. Two rows override the fire rate, and say why.
/// </para>
/// <para>
/// <b>Ticks are 10 ms and start at zero.</b> Coarser than a frame on purpose: at 100 Hz the
/// boundaries this fixture cares about — 0.1333 s for the damage frame, 0.3333 s for the end of a
/// swing — land inside a tick rather than on one, which is the case a schedule built from absolute
/// times has to get right. <see cref="NoDeadFrameBetweenSwings"/> is the deliberate exception and
/// arranges a tick that lands exactly on a boundary.
/// </para>
/// <para>
/// The <c>PlayerCombat</c> rows reach enemies only through <see cref="EnemyRegistry"/>, for the
/// reason <c>PlayerCombatTests</c> gives: <c>EnemyAgent</c>'s position and vulnerability setters
/// are <c>internal</c> and <c>Soulvail.Tests.Core</c> has no <c>InternalsVisibleTo</c>. "Cannot be
/// damaged right now" is therefore spelled as a corpse still in the registry — the same half of CC
/// §3.6 that fixture uses, and the only half a test can reach until M7-01's Warden.
/// </para>
/// </remarks>
[TestFixture]
public sealed class WeaponTests
{
    private const string OathboundId = "character.oathbound";
    private const string HuskId = "enemy.husk";

    /// <summary>Room for every row's enemies, and the buffer <c>PlayerCombat</c> preallocates.</summary>
    private const int EnemyCapacity = 8;

    // CC §7, Attack.
    private const float Damage = 13f;
    private const float SwingsPerSecond = 3f;
    private const float WeaponRange = 8f;
    private const float ConeAngleDeg = 60f;
    private const float DamageFrameFraction = 0.4f;

    /// <summary>0.3333 s — one swing of the Censer.</summary>
    private const float Interval = 1f / SwingsPerSecond;

    /// <summary>Seconds per tick in this fixture. See the class remarks.</summary>
    private const float Step = 0.01f;

    /// <summary>The body's facing for the <c>PlayerCombat</c> rows: +Z, where a run starts.</summary>
    private static readonly Vector3 Facing = Vector3.UnitZ;

    private RecordingEvents _events;
    private RecordingIntents _intents;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
    }

    // ---- Rules 1, 2 and 4: the cadence ---------------------------------------------------------

    [Test]
    public void NoTarget_NeverSwings()
    {
        var weapon = new Weapon(Spec());
        int starts = Run(weapon, seconds: 2f, targetInRange: false).Starts;

        // CC §4.2's idle rule: no enemy in range, no swing. Not a suppressed swing or a silent one
        // — the whole point is that an empty arena costs no VFX, no audio and no cone query.
        Assert.That(starts, Is.Zero);
        Assert.That(weapon.IsSwinging, Is.False);
    }

    [Test]
    public void Target_SwingsImmediately()
    {
        var weapon = new Weapon(Spec());

        WeaponTick tick = weapon.Tick(Step, 0f, targetInRange: true);

        // No opening delay, because there is no such thing as being caught reloading: the first
        // tick of a run with something in range is a swing.
        Assert.That(tick.SwingStarted, Is.True);
        Assert.That(weapon.IsSwinging, Is.True);
        Assert.That(tick.DamageFrame, Is.False, "The swing has only just begun — the damage is 40 % in.");
    }

    [Test]
    public void DamageFrame_AtFortyPercent()
    {
        var weapon = new Weapon(Spec());
        int frames = 0;
        float firstFrameAt = float.NaN;

        for (int i = 0; i < 30; i++)
        {
            float now = i * Step;
            WeaponTick tick = weapon.Tick(Step, now, targetInRange: true);

            if (!tick.DamageFrame)
            {
                continue;
            }

            frames++;

            if (float.IsNaN(firstFrameAt))
            {
                firstFrameAt = now;
            }
        }

        // 0.4 × 0.3333 = 0.1333, so the first tick at or past it is 0.14. The window is one tick
        // wide either side, which is what makes this an assertion about the fraction rather than
        // about the tick rate: at 0.5 it would land at 0.17, at 0.3 at 0.11.
        Assert.That(firstFrameAt, Is.InRange(DamageFrameFraction * Interval, (DamageFrameFraction * Interval) + Step));

        // Once per swing, not once per tick for the rest of it. The opposite mistake would be
        // silent in a single-swing row and would multiply the game's damage by twenty.
        Assert.That(frames, Is.EqualTo(1), "One damage frame in the first 0.3 s — the swing is 0.333 s long.");
    }

    [Test]
    public void ThreeSwingsPerSecond()
    {
        var weapon = new Weapon(Spec());
        (int Starts, int Frames, float FirstStartInterval) run = Run(weapon, seconds: 1f, targetInRange: true);

        // CC §4.1's rate, and the number the TTK invariant of GD §6.2 rests on: 3 swings × 13
        // damage = 39 ≥ 36, so a Husk dies on the third damage frame.
        Assert.That(run.Starts, Is.EqualTo(3));
        Assert.That(run.Frames, Is.EqualTo(3), "A swing that starts owes a damage frame.");
    }

    [Test]
    public void NoDeadFrameBetweenSwings()
    {
        // 4 /s and 50 ms ticks, so 0.25 lands exactly on the end of the first swing. The Censer's
        // own 0.3333 s never coincides with a tick boundary, and this rule is specifically about
        // the tick that does.
        var weapon = new Weapon(Spec(swingsPerSecond: 4f));
        const float exactStep = 0.05f;

        Assert.That(weapon.Tick(exactStep, 0f, true).SwingStarted, Is.True, "Sanity: the first swing starts at zero.");

        float startedAgainAt = float.NaN;

        for (int i = 1; i <= 5; i++)
        {
            float now = i * exactStep;

            if (weapon.Tick(exactStep, now, true).SwingStarted)
            {
                startedAgainAt = now;
                break;
            }
        }

        // Ends and restarts on the same tick. A weapon that waited for the next one would lose a
        // frame per swing — 5 % of the game's damage at 60 fps, and a visible hitch in the rhythm
        // at the fire rates M1-13's Focus ramp reaches.
        Assert.That(startedAgainAt, Is.EqualTo(0.25f).Within(1e-6f));
        Assert.That(weapon.IsSwinging, Is.True, "…and it is swinging again, not resting.");
    }

    // ---- Rule 1: what a modifier may move ------------------------------------------------------

    [Test]
    public void FireRateModifier_ChangesNextSwing()
    {
        var weapon = new Weapon(Spec());

        // +30 % is M1-13's Focus cap (CC §4.3), so this is the rate the game will actually reach
        // rather than an invented one. 3.9 /s is a 0.2564 s interval.
        weapon.FireRate.Add(new Modifier(ModifierKind.PercentAdd, 0.3f, this));

        (int Starts, int Frames, float FirstStartInterval) run = Run(weapon, seconds: 1f, targetInRange: true);

        // 1 / 0.2564 = 3.9 swings a second, which over a whole second is 3 or 4 depending on where
        // the tick grid falls. The count is the loose assertion and the interval is the tight one:
        // it is the interval the rule is about.
        Assert.That(run.Starts, Is.InRange(3, 4));
        Assert.That(
            run.FirstStartInterval,
            Is.EqualTo(1f / (SwingsPerSecond * 1.3f)).Within(Step),
            "The swing interval follows the live FireRate stat, not the authored one.");
    }

    [Test]
    public void ModifierMidSwing_DoesNotShortenCurrentSwing()
    {
        var weapon = new Weapon(Spec());

        Assert.That(weapon.Tick(Step, 0f, true).SwingStarted, Is.True, "Sanity: a swing is running.");

        // +100 % — twice the fire rate, so a recomputed swing would end at 0.1667 instead of
        // 0.3333. Far bigger than anything the game applies, precisely so that the failure would
        // be unmissable rather than a rounding argument.
        weapon.FireRate.Add(new Modifier(ModifierKind.PercentAdd, 1f, this));

        float startedAgainAt = float.NaN;

        for (int i = 1; i <= 40; i++)
        {
            float now = i * Step;

            if (weapon.Tick(Step, now, true).SwingStarted)
            {
                startedAgainAt = now;
                break;
            }
        }

        // The swing you are watching finishes at the speed it began at. Without this the Focus
        // ramp of M1-13 would visibly snap the animation shorter as it climbed, and a swing could
        // be cut off before the damage frame it had already promised.
        Assert.That(startedAgainAt, Is.EqualTo(Interval).Within(Step),
            "The running swing keeps the interval it started with; the modifier applies to the next one.");
    }

    // ---- Rule 3: the frame belongs to the swing ------------------------------------------------

    [Test]
    public void TargetLeavesMidSwing_FrameStillFires()
    {
        var weapon = new Weapon(Spec());

        Assert.That(weapon.Tick(Step, 0f, true).SwingStarted, Is.True, "Sanity: a swing is running.");

        int frames = 0;

        // Past the end of the swing at 0.3333, so the last assertion is about a swing that has
        // finished rather than one still running.
        for (int i = 1; i <= 40; i++)
        {
            // In range for the first 50 ms, gone for the rest — well before the damage frame at
            // 0.1333. The enemy that provoked the swing walked away, or died to something else.
            bool inRange = i * Step < 0.05f;

            if (weapon.Tick(Step, i * Step, inRange).DamageFrame)
            {
                frames++;
            }
        }

        // The swing lands anyway, and the cone resolves against whatever is standing there. That is
        // what makes a swing aimed at one Husk kill the two beside it, and what stops the weapon
        // stuttering every time a target dies mid-windup.
        Assert.That(frames, Is.EqualTo(1));
        Assert.That(weapon.IsSwinging, Is.False, "…and having finished, it does not start another.");
    }

    // ---- Rule 7 and the derived number ---------------------------------------------------------

    [Test]
    public void DpsOneSecond_Is39()
    {
        var weapon = new Weapon(Spec());

        // CC §4.1's "single-target DPS is only 39 — deliberately low". It is what CC §3.2's
        // finisher bonus is weighed against, so it has to track the live stats rather than the spec.
        Assert.That(weapon.DpsOneSecond, Is.EqualTo(39f).Within(1e-4f));

        weapon.Damage.Add(new Modifier(ModifierKind.PercentAdd, 1f, this));

        Assert.That(weapon.DpsOneSecond, Is.EqualTo(78f).Within(1e-4f));
    }

    // ---- Rules 5 and 6: what PlayerCombat does with it ------------------------------------------

    [Test]
    public void PlayerCombat_EmitsConeHit_OnDamageFrame()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();
        var playerAt = new Vector3(2f, 0f, -1f);

        registry.Spawn(Enemy(), playerAt + new Vector3(0f, 0f, 5f));

        TickTo(combat, registry, playerAt, seconds: 0.2f);

        Assert.That(_intents.ConeHits.Count, Is.EqualTo(1), "One question per damage frame, not one per tick.");

        ConeHitIntent cone = _intents.LastConeHit;

        // The geometry travels with the request, so the wedge that was drawn is the wedge that
        // hits — resolving it against wherever the player is when the fact comes back would
        // silently widen every swing by a frame of movement.
        Assert.That(cone.RequestId, Is.EqualTo(1), "Ids start at 1, so a default-constructed intent is not a real one.");
        Assert.That(cone.Origin, Is.EqualTo(playerAt));
        Assert.That(cone.FacingXZ.X, Is.EqualTo(Facing.X).Within(1e-6f));
        Assert.That(cone.FacingXZ.Y, Is.EqualTo(Facing.Z).Within(1e-6f), "FacingXZ.Y is the world Z, not a height.");
        Assert.That(cone.Range, Is.EqualTo(WeaponRange).Within(1e-6f));
        Assert.That(cone.AngleDeg, Is.EqualTo(ConeAngleDeg).Within(1e-6f));

        // Written down rather than sent and forgotten: M1-11 matches the fact that comes back
        // against this, and a report carrying any other id is stale.
        Assert.That(combat.PendingConeRequestId, Is.EqualTo(1));
    }

    [Test]
    public void PlayerCombat_PublishesPlayerAttacked_OnSwingStart()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        registry.Spawn(Enemy(), new Vector3(0f, 0f, 5f));

        TickTo(combat, registry, Vector3.Zero, seconds: 1f);

        // One per swing, at the start of it. CC §4.2 puts the damage 40 % in, so a view that
        // started the windup on the damage frame would play the whole animation late.
        Assert.That(_events.Count<PlayerAttacked>(), Is.EqualTo(3));
        Assert.That(_events.Count<PlayerAttacked>(), Is.EqualTo(_intents.ConeHits.Count),
            "A swing that starts owes a cone; a cone without a swing would be damage with no tell.");

        PlayerAttacked attacked = _events.Of<PlayerAttacked>()[0];

        // The same facing the cone carries, from the same read. Two answers to "which way did he
        // swing" is one answer too many.
        Assert.That(attacked.FacingXZ.X, Is.EqualTo(_intents.ConeHits[0].FacingXZ.X).Within(1e-6f));
        Assert.That(attacked.FacingXZ.Y, Is.EqualTo(_intents.ConeHits[0].FacingXZ.Y).Within(1e-6f));
    }

    [Test]
    public void PlayerCombat_NoSwing_WhenTargetBlocked()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();
        EnemyAgent corpse = registry.Spawn(Enemy(), new Vector3(0f, 0f, 4f));

        // CC §3.6's "cannot be damaged right now", reached through the half of it a test can spell.
        corpse.Health.ApplyDamage(1000f, 0f);

        TickTo(combat, registry, Vector3.Zero, seconds: 1f);

        Assert.That(combat.Targeter.IsCurrentBlocked, Is.True, "Sanity: it is facing something it cannot hurt.");

        // Facing is held and the weapon is silent, which is the game saying "go around". Swinging
        // anyway would also kill whatever stood behind it and make the block look like a lie.
        Assert.That(_intents.ConeHits, Is.Empty);
        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero);
        Assert.That(combat.FaceDirection, Is.Not.Null, "…while still looking straight at it.");
    }

    [Test]
    public void PlayerCombat_NoSwing_WhenTargetBeyondRange()
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        PlayerCombat combat = Combat();

        // 9 m: inside the 12 m acquire range, outside the 8 m cone. The gap between the two is
        // deliberate (CC §3.1 derives one from the other), and this is what lives in it — a target
        // the character turns to face and walks towards without swinging at thin air.
        registry.Spawn(Enemy(), new Vector3(0f, 0f, 9f));

        TickTo(combat, registry, Vector3.Zero, seconds: 1f);

        Assert.That(combat.Targeter.CurrentTargetId, Is.Not.EqualTo(-1), "Sanity: it is targeted, just not reachable.");
        Assert.That(_intents.ConeHits, Is.Empty);
        Assert.That(_events.Count<PlayerAttacked>(), Is.Zero);
        Assert.That(combat.PendingConeRequestId, Is.EqualTo(-1), "Nothing was asked, so nothing is owed an answer.");
    }

    // ---- Rule 8: the allocation budget ----------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        var weapon = new Weapon(Spec());

        // Warmed past the first swing, so the stats' lazy recompute and any first-call cost land
        // outside the measurement. The measured ticks all carry the same `now`, which keeps the
        // weapon mid-swing: a window spanning a damage frame would measure the WeaponTick the
        // caller is *meant* to act on rather than the per-frame path.
        for (int i = 0; i < 200; i++)
        {
            weapon.Tick(Step, i * Step, targetInRange: true);
        }

        AllocationAssert.None(() => weapon.Tick(Step, 2f, targetInRange: true));
    }

    // ---- Beyond the spec's Tests table ----------------------------------------------------------

    [Test]
    public void Reset_ReturnsToRest()
    {
        // Not in the table, and the one rule Reset has that nothing else would catch: a new stage
        // must not inherit the previous one's swing clock. Without the reset of _nextSwingAt, a
        // run whose clock restarts at zero would refuse to swing until it had passed the old
        // schedule again.
        var weapon = new Weapon(Spec());

        Run(weapon, seconds: 1f, targetInRange: true);
        weapon.Reset();

        Assert.That(weapon.IsSwinging, Is.False);
        Assert.That(weapon.Tick(Step, 0f, targetInRange: true).SwingStarted, Is.True,
            "A reset weapon swings on the first tick of the new run, as a fresh one does.");
    }

    [Test]
    public void FireRateAtZero_DoesNotSwing()
    {
        // Not in the table. Stat clamps nothing by design (ADR-0008), so a −100 % PercentMult is a
        // legitimate way for a future Pact to say "your weapon is silenced" — and read literally it
        // is a swing interval of infinity. Refusing to start the swing says the same thing
        // reversibly; scheduling one would leave IsSwinging true for the rest of the run.
        var weapon = new Weapon(Spec());
        weapon.FireRate.Add(new Modifier(ModifierKind.PercentMult, -1f, this));

        Assert.That(Run(weapon, seconds: 1f, targetInRange: true).Starts, Is.Zero);
        Assert.That(weapon.IsSwinging, Is.False, "Silenced, not jammed.");

        weapon.FireRate.RemoveAll(this);

        Assert.That(weapon.Tick(Step, 1f, targetInRange: true).SwingStarted, Is.True,
            "…and it fires again the moment the modifier comes off.");
    }

    [Test]
    public void Spec_InvalidNumbers_Throw()
    {
        // Not in the table, and the same family as ContentTests' spec rows: these are the guards
        // that stand between a designer's typo and a weapon that never swings.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(damage: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(damage: float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(swingsPerSecond: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(range: -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(range: float.PositiveInfinity));

        // A zero arc hits nothing however well it is aimed, and more than a full circle is a
        // second lap that cannot hit twice.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(coneAngleDeg: 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(coneAngleDeg: 361f));

        // The frame has to land inside the swing. At exactly 1 it would coincide with the tick that
        // ends the swing, and a fire-rate modifier could reorder a hit and its successor.
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(damageFrame: -0.1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => Spec(damageFrame: 1f));

        // Zero is legal at both ends of that range: a full circle, and damage on the swing's first
        // frame. Every guard above would be satisfied by a constructor that rejected everything.
        Assert.DoesNotThrow(() => Spec(coneAngleDeg: 360f, damageFrame: 0f));
        Assert.DoesNotThrow(() => Spec());
    }

    [Test]
    public void Ctor_NullSpec_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Weapon(null));
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    /// <summary>
    /// Ticks <paramref name="weapon"/> for <paramref name="seconds"/> at <see cref="Step"/>,
    /// counting what came out and measuring the gap between the first two swing starts.
    /// </summary>
    private static (int Starts, int Frames, float FirstStartInterval) Run(
        Weapon weapon,
        float seconds,
        bool targetInRange)
    {
        int ticks = (int)MathF.Round(seconds / Step);
        int starts = 0;
        int frames = 0;
        float firstStart = float.NaN;
        float secondStart = float.NaN;

        for (int i = 0; i < ticks; i++)
        {
            float now = i * Step;
            WeaponTick tick = weapon.Tick(Step, now, targetInRange);

            if (tick.SwingStarted)
            {
                starts++;

                if (float.IsNaN(firstStart))
                {
                    firstStart = now;
                }
                else if (float.IsNaN(secondStart))
                {
                    secondStart = now;
                }
            }

            if (tick.DamageFrame)
            {
                frames++;
            }
        }

        return (starts, frames, secondStart - firstStart);
    }

    /// <summary>
    /// Ticks <paramref name="combat"/> for <paramref name="seconds"/> at <see cref="Step"/>, with
    /// the player standing still at <paramref name="playerAt"/> and facing +Z.
    /// </summary>
    private static void TickTo(
        PlayerCombat combat,
        EnemyRegistry registry,
        Vector3 playerAt,
        float seconds)
    {
        var snapshot = new WorldSnapshot(EnemyCapacity);
        snapshot.PlayerPosition = playerAt;

        int ticks = (int)MathF.Round(seconds / Step);

        for (int i = 0; i < ticks; i++)
        {
            combat.Tick(Step, i * Step, snapshot, registry.Alive, Facing);
        }
    }

    private PlayerCombat Combat() => new(Character(), _events, _intents, EnemyCapacity);

    /// <summary>The Censer of CC §7, with the numbers a row may need to override.</summary>
    private static WeaponSpec Spec(
        float damage = Damage,
        float swingsPerSecond = SwingsPerSecond,
        float range = WeaponRange,
        float coneAngleDeg = ConeAngleDeg,
        float damageFrame = DamageFrameFraction)
    {
        return new WeaponSpec(WeaponKind.Cone, damage, swingsPerSecond, range, coneAngleDeg, damageFrame);
    }

    /// <summary>The Oathbound of CC §7.</summary>
    private static CharacterSpec Character() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        140f,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        Spec(),
        // Required as of M1-13, and switched off here with a MaxMultiplier of 1: every row in
        // this fixture ticks a centred stick, and CC §4.3's ramp would quietly speed the Censer
        // up under assertions written against 3 swings a second. FocusTrackerTests is the ramp's
        // fixture; here it is background, held still.
        new FocusSpec(0.4f, 1f, 1f),
        new ShieldSpec(30f, 4f, 15f),
        0.5f);

    /// <summary>GD §8.1's Husk.</summary>
    private static EnemySpec Enemy() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        36f,
        3.5f,
        1,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        EnemyBehaviourKind.Static);
}
