using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The two things the Warden of Ash makes of the ground: a ring that expands from where it stood
/// and a crack that opens under where you were. Geometry, the once-per-body discipline, the arm
/// and open windows, and what both cost per frame. GD §9.1 rules 1 and 2, GD §9.2, M4-02 rules 2,
/// 3, 4, 5 and 8.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <see cref="PlayerCombat"/> throughout</b> — <c>ZoneSystemTests</c>' bargain
/// for its reason: a fake would agree with whatever the caller did, and half of what these rows
/// are about is whether the player's hit points actually moved and by how much. The one thing the
/// real object brings that matters here is i-frames, which is why every row that expects two
/// separate hits spaces them past <c>CharacterSpec.HitIFrames</c>.
/// </para>
/// <para>
/// <b>The Warden's shipped numbers throughout</b>, off <c>WardenBehaviour</c>'s own constants
/// rather than retyped, so a retune of the slam reddens the rows that describe it instead of
/// leaving them quietly testing a ring nothing ships.
/// </para>
/// <para>
/// <b>Nothing here drives a boss.</b> What chooses an attack, what telegraphs it and where the
/// origin comes from are <c>WardenBehaviourTests</c>' — this file is about what happens once a
/// ring is in the ground, which is exactly the split <c>ProjectileSystemTests</c> and
/// <c>SpitterBehaviourTests</c> have.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ShockwaveAndFissureTests
{
    private const string OathboundId = "character.oathbound";

    /// <summary>CC §7's class, as <c>Oathbound.asset</c> ships it.</summary>
    private const float MaxHp = 140f;

    private const float AegisMax = 30f;
    private const float HitIFrames = 0.5f;

    private const int EnemyCapacity = 8;

    /// <summary><c>Warden.asset</c>'s contact damage, which is what both hazards cost.</summary>
    private const float Damage = 22f;

    /// <summary>The Warden's telegraph — <c>Warden.asset</c>'s <c>_windupTime</c>.</summary>
    private const float Arm = 0.9f;

    private const float Tolerance = 1e-3f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _player;
    private ShockwaveSystem _shockwaves;
    private FissureSystem _fissures;

    /// <summary>
    /// The counter the allocation row drives its clock from. A field rather than a local, so the
    /// measured closure is built over it once instead of capturing a fresh variable — a closure
    /// created inside the measurement would be the allocation it reported.
    /// </summary>
    private float _allocationClock;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _player = new PlayerCombat(Oathbound(), _events, _intents, EnemyCapacity);
        _shockwaves = new ShockwaveSystem(_events);
        _fissures = new FissureSystem(_events);
        _allocationClock = 0f;
    }

    // ---- The ring: where it comes from and where it goes (rules 2, 4) ----------------------------

    [Test]
    public void Shock_ExpandsFromWhereItSlammed()
    {
        // The origin is taken once and never revised. Nothing in the world is asked about it again
        // — which is what makes the boss's own walk (WardenBehaviourTests) irrelevant to the ring.
        var slamPoint = new Vector3(4f, 0f, -3f);

        int id = Emit(slamPoint);

        Assert.That(id, Is.EqualTo(1), "Ids are issued from 1, so 0 can mean nobody.");
        Assert.That(_shockwaves.ActiveCount, Is.EqualTo(1));
        Assert.That(_shockwaves.OriginAt(0), Is.EqualTo(slamPoint));

        ShockwaveEmitted emitted = _events.Single<ShockwaveEmitted>();

        Assert.That(emitted.Id, Is.EqualTo(id));
        Assert.That(emitted.Origin, Is.EqualTo(slamPoint), "The event carries a complete ring: a "
            + "view integrates its own radius rather than asking core where the ring is.");
        Assert.That(emitted.Speed, Is.EqualTo(WardenBehaviour.ShockwaveSpeed).Within(Tolerance));
        Assert.That(
            emitted.MaxRadius,
            Is.EqualTo(WardenBehaviour.ShockwaveMaxRadius).Within(Tolerance));

        // Ticked with the player somewhere else entirely, so nothing about the tick could move it.
        _shockwaves.Tick(0.2f, new Vector3(-20f, 0f, 20f), _player);

        Assert.That(_shockwaves.OriginAt(0), Is.EqualTo(slamPoint));
    }

    [Test]
    public void Shock_RadiusIsAbsoluteRatherThanAccumulated()
    {
        Emit(Vector3.Zero);

        // Read at 0.25 s, then at 0.5 s, then at 0.25 s again: the radius is a function of the
        // clock and of nothing the system has been doing since (rule 5).
        Assert.That(
            _shockwaves.RadiusAt(0, 0.25f),
            Is.EqualTo(WardenBehaviour.ShockwaveSpeed * 0.25f).Within(Tolerance));

        Assert.That(
            _shockwaves.RadiusAt(0, 0.5f),
            Is.EqualTo(WardenBehaviour.ShockwaveSpeed * 0.5f).Within(Tolerance));

        Assert.That(
            _shockwaves.RadiusAt(0, 0.25f),
            Is.EqualTo(WardenBehaviour.ShockwaveSpeed * 0.25f).Within(Tolerance));
    }

    [Test]
    public void Shock_HitsABodyOnce()
    {
        // Three metres out and standing still: the ring's edge reaches him, passes over him, and
        // keeps going to the ceiling. One hit, not one a frame — the once-per-body discipline
        // ConeOverlapQuery has, with the one body V1 has.
        var standing = new Vector3(0f, 0f, 3f);

        Emit(Vector3.Zero);

        float before = _player.Health.Current + _player.Health.Shield;

        // Right past the ceiling, a frame at a time, so the ring is over by the end of the walk.
        TickShockFor(1.2f, standing);

        Assert.That(_shockwaves.ActiveCount, Is.Zero, "And the ring is gone.");

        float taken = before - (_player.Health.Current + _player.Health.Shield);

        Assert.That(taken, Is.EqualTo(Damage).Within(Tolerance), "Exactly one ring's worth.");

        // The stronger half of the claim, and the one i-frames could have hidden: the player was
        // hurt once, so exactly one PlayerDamaged went out.
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        Assert.That(_events.Count<ShockwavePassed>(), Is.EqualTo(1));
        Assert.That(_events.Single<ShockwavePassed>().Id, Is.EqualTo(1));
    }

    [Test]
    public void Shock_MissesSomeoneWhoWalkedOut()
    {
        // Rule 2's safe answer, asserted: a body past the ceiling when the ring arrives takes
        // nothing at all, even though the band's front edge has swept over the ground it is
        // standing on. Outside MaxRadius is safe ground *exactly*, which is what makes "walk out
        // of it" a rule a player can learn rather than a tolerance they have to feel out.
        var outside = new Vector3(0f, 0f, WardenBehaviour.ShockwaveMaxRadius + 0.5f);

        Emit(Vector3.Zero);

        TickShockFor(2f, outside);

        Assert.That(_player.Health.Current, Is.EqualTo(MaxHp).Within(Tolerance));
        Assert.That(_player.Health.Shield, Is.EqualTo(AegisMax).Within(Tolerance));
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero);

        // And the ring still ended, so this is a miss rather than a ring that never grew.
        Assert.That(_events.Count<ShockwavePassed>(), Is.EqualTo(1));
    }

    [Test]
    public void Shock_IsFlat()
    {
        // Rule 4, AR §18.4: three metres up is still three metres out. Counting height would let
        // a step in the arena floor make a ring pass under the player's feet.
        var aloft = new Vector3(0f, 3f, 3f);

        Emit(Vector3.Zero);

        TickShockFor(1.2f, aloft);

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1), "Height is a camera's business.");
    }

    // ---- The crack: where it opens and when it bites (rules 1, 3, 4) ------------------------------

    [Test]
    public void Fissure_OpensWhereThePlayerWas()
    {
        // Placed at A while the player stands at A, then the player walks to B. It fires at A.
        var a = new Vector3(2f, 0f, 2f);
        var b = new Vector3(12f, 0f, -12f);

        int id = Open(a);

        Assert.That(id, Is.EqualTo(1));
        Assert.That(_fissures.PositionAt(0), Is.EqualTo(a));
        Assert.That(_fissures.RadiusAt(0), Is.EqualTo(WardenBehaviour.FissureRadius).Within(Tolerance));
        Assert.That(_fissures.HasFiredAt(0), Is.False, "Arming, not yet open.");

        FissureArmed armed = _events.Single<FissureArmed>();

        Assert.That(armed.At, Is.EqualTo(a));
        Assert.That(armed.ArmSeconds, Is.EqualTo(Arm).Within(Tolerance), "The window rides on the "
            + "event, so what is drawn cannot disagree with what bites.");

        // Ticked with the player at B for the whole arm, which is the only way it could have
        // tracked — and it does not.
        TickFissureFor(Arm + 0.1f, b);

        Assert.That(_fissures.ActiveCount, Is.EqualTo(1), "Fired, still drawn.");
        Assert.That(_fissures.PositionAt(0), Is.EqualTo(a));
        Assert.That(_events.Count<FissureFired>(), Is.EqualTo(1));
    }

    [Test]
    public void Fissure_ArmsBeforeItBites()
    {
        // GD §9.1 rule 1's telegraph, as a clock: nothing for the whole arm, then the bite on the
        // tick it ends. A body standing in it the whole time, so what is being measured is the
        // window rather than the geometry.
        var inside = new Vector3(1f, 0f, 0f);

        Open(Vector3.Zero);

        // Up to but not including the deadline.
        for (float now = 0.1f; now < Arm - Tolerance; now += 0.1f)
        {
            _fissures.Tick(now, inside, _player);

            Assert.That(
                _events.Count<FissureFired>(),
                Is.Zero,
                $"Still arming at {now:0.0} s of {Arm}.");
        }

        Assert.That(_player.Health.Shield, Is.EqualTo(AegisMax).Within(Tolerance));

        _fissures.Tick(Arm, inside, _player);

        Assert.That(_events.Count<FissureFired>(), Is.EqualTo(1), "*At* the deadline, not after.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));

        // 22 into a 30-point Aegis, so nothing reached hit points — the arithmetic, so the row is
        // about the window landing rather than about a number moving somewhere.
        Assert.That(_player.Health.Shield, Is.EqualTo(AegisMax - Damage).Within(Tolerance));
        Assert.That(_player.Health.Current, Is.EqualTo(MaxHp).Within(Tolerance));

        // And once: it does not keep biting for the rest of its open window.
        TickFissureFor(Arm + WardenBehaviour.FissureOpenSeconds + 1f, inside);

        Assert.That(_events.Count<FissureFired>(), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
    }

    [Test]
    public void Fissure_MissesSomeoneWhoMoved()
    {
        // Rule 3's safe answer: standing in it while it arms costs nothing, and one step off it is
        // a complete answer. The step happens half way through the arm, which is a window GD §9.1
        // rule 1 guarantees is at least 0.6 s long.
        var inside = new Vector3(0f, 0f, 1f);
        var away = new Vector3(0f, 0f, WardenBehaviour.FissureRadius + 1f);

        Open(Vector3.Zero);

        _fissures.Tick(Arm * 0.5f, inside, _player);

        Assert.That(_events.Count<FissureFired>(), Is.Zero);

        _fissures.Tick(Arm, away, _player);

        Assert.That(_events.Count<FissureFired>(), Is.EqualTo(1), "It fired.");
        Assert.That(_events.Count<PlayerDamaged>(), Is.Zero, "And it missed.");
        Assert.That(_player.Health.Shield, Is.EqualTo(AegisMax).Within(Tolerance));
    }

    [Test]
    public void Fissure_IsFlat()
    {
        // Rule 4 again, on the other hazard and for the same reason.
        var aloft = new Vector3(0f, 3f, 1f);

        Open(Vector3.Zero);

        _fissures.Tick(Arm, aloft, _player);

        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1));
    }

    [Test]
    public void Fissure_FiresThenCloses()
    {
        Open(Vector3.Zero);

        TickFissureFor(Arm, Vector3.Zero);

        Assert.That(_fissures.ActiveCount, Is.EqualTo(1));
        Assert.That(_fissures.HasFiredAt(0), Is.True, "Open rather than arming.");
        Assert.That(_events.Count<FissureClosed>(), Is.Zero);

        _fissures.Tick(Arm + WardenBehaviour.FissureOpenSeconds, Vector3.Zero, _player);

        Assert.That(_fissures.ActiveCount, Is.Zero);
        Assert.That(_events.Single<FissureClosed>().Id, Is.EqualTo(1));
    }

    [Test]
    public void Fissure_FiresOnTheTickItCloses()
    {
        // **One tick that swallows both deadlines lands both, in order.** The alternative silently
        // makes every authored fissure harmless whenever a frame is long enough to cover the open
        // window — a 30 fps phone and a short window — which is ZoneSystem.Tick's
        // pulse-before-expiry ruling reached again, and worth a whole attack here rather than one
        // pulse.
        Open(Vector3.Zero);

        _fissures.Tick(Arm + WardenBehaviour.FissureOpenSeconds + 1f, Vector3.Zero, _player);

        Assert.That(_events.Count<FissureFired>(), Is.EqualTo(1));
        Assert.That(_events.Count<PlayerDamaged>(), Is.EqualTo(1), "It bit on the way out.");
        Assert.That(_events.Count<FissureClosed>(), Is.EqualTo(1));
        Assert.That(_fissures.ActiveCount, Is.Zero);
    }

    // ---- Rule 5: the same fight at either frame rate ----------------------------------------------

    [Test]
    public void Systems_TickIdenticallyAtBothRates()
    {
        // **The whole of rule 5 in one row**: the same hazards, the same simulated seconds, one
        // run at 30 fps and one at 120, and what is compared is the damage taken, the events
        // published and the ring's radius at the end. Absolute times are what make this true —
        // an accumulator would drift by four times as many additions on one side.
        (float taken, int hits, float radius) slow = Rate(1f / 30f);
        (float taken, int hits, float radius) fast = Rate(1f / 120f);

        Assert.That(slow.hits, Is.EqualTo(2), "The fixture's own claim: both hazards landed.");

        Assert.That(fast.hits, Is.EqualTo(slow.hits));
        Assert.That(fast.taken, Is.EqualTo(slow.taken).Within(Tolerance));
        Assert.That(fast.radius, Is.EqualTo(slow.radius).Within(Tolerance));
    }

    // ---- Rule 8: a ceiling that drops rather than throwing ----------------------------------------

    [Test]
    public void Capacity_DropsRatherThanThrows()
    {
        // **The deliberate disagreement with M3-11b.** A ninth zone throws, because it was
        // unreachable at Consecrate's cooldown and a refusal there could only mean a bug. A fifth
        // ring is different: an add-heavy phase plus a player-placed zone plus a slam is exactly
        // where a ceiling gets hit, and a run-ending exception during the first boss fight is the
        // worst outcome available.
        for (int i = 0; i < ShockwaveSystem.Capacity; i++)
        {
            Assert.That(Emit(Vector3.Zero), Is.EqualTo(i + 1));
        }

        Assert.That(_events.Count<ShockwaveEmitted>(), Is.EqualTo(ShockwaveSystem.Capacity));

        int dropped = Emit(Vector3.Zero);

        Assert.That(dropped, Is.Zero, "0 means nobody, which is what a dropped ring is.");
        Assert.That(_shockwaves.ActiveCount, Is.EqualTo(ShockwaveSystem.Capacity));
        Assert.That(
            _events.Count<ShockwaveEmitted>(),
            Is.EqualTo(ShockwaveSystem.Capacity),
            "And nothing was published for it, because nothing happened.");

        // The same rule on the other hazard.
        for (int i = 0; i < FissureSystem.Capacity; i++)
        {
            Assert.That(Open(Vector3.Zero), Is.EqualTo(i + 1));
        }

        Assert.That(Open(Vector3.Zero), Is.Zero);
        Assert.That(_fissures.ActiveCount, Is.EqualTo(FissureSystem.Capacity));

        // And the run carries on: both tables still tick, still bite and still empty themselves.
        TickShockFor(2f, Vector3.Zero);

        Assert.That(_shockwaves.ActiveCount, Is.Zero);

        // A ring emitted after the drop is issued the next id, so dropping cost the sequence
        // nothing either.
        Assert.That(Emit(Vector3.Zero), Is.EqualTo(ShockwaveSystem.Capacity + 1));
    }

    // ---- The budget -------------------------------------------------------------------------------

    [Test]
    public void Tick_AllocatesNothing()
    {
        // A silent sink on both ends: RecordingEvents boxes every payload, so the four events a
        // full table publishes would be counted as core allocating when it is the fake doing it.
        var silent = new SilentEvents();
        var player = new PlayerCombat(Oathbound(), silent, _intents, EnemyCapacity);
        var shockwaves = new ShockwaveSystem(silent);
        var fissures = new FissureSystem(silent);

        // A full table of each, slowed and stretched so none of them retires inside the
        // measurement — the steady state a phase-3 fight holds, rather than a table emptying
        // itself. Ten thousand iterations at a sixtieth is nearly three minutes of simulated time,
        // which is why the numbers are what they are rather than the Warden's.
        for (int i = 0; i < ShockwaveSystem.Capacity; i++)
        {
            shockwaves.Emit(Vector3.Zero, 0.001f, 1_000f, Damage, 0.1f, 0f);
        }

        for (int i = 0; i < FissureSystem.Capacity; i++)
        {
            fissures.Open(Vector3.Zero, WardenBehaviour.FissureRadius, 1_000f, 1_000f, Damage, 0f);
        }

        // **The player is inside every ring's ceiling and outside every edge**, which is what puts
        // the geometry on the measured path: the containment test runs on all four rings every
        // tick and answers no. What is deliberately *not* measured here is the bite itself — one
        // `PlayerCombat.ApplyDamage` per body per ring, which is that class's own probe
        // (`PlayerCombatTests`), and which by construction happens at most once per ring anyway.
        var watching = new Vector3(0f, 0f, 500f);

        AllocationAssert.None(() =>
        {
            _allocationClock += 1f / 60f;

            shockwaves.Tick(_allocationClock, watching, player);
            fissures.Tick(_allocationClock, watching, player);
        });

        Assert.That(_allocationClock, Is.GreaterThan(100f), "The probe is live rather than "
            + "measuring a no-op.");
        Assert.That(shockwaves.ActiveCount, Is.EqualTo(ShockwaveSystem.Capacity));
        Assert.That(fissures.ActiveCount, Is.EqualTo(FissureSystem.Capacity));
        Assert.That(
            player.Health.Shield,
            Is.EqualTo(AegisMax).Within(Tolerance),
            "Nothing bit, which is what keeps this row about the walk rather than about damage.");
    }

    // ---- Guards -----------------------------------------------------------------------------------

    [Test]
    public void Shock_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new ShockwaveSystem(null));

        Assert.Throws<ArgumentNullException>(
            () => _shockwaves.Tick(0f, Vector3.Zero, null));

        Assert.Throws<ArgumentOutOfRangeException>(() => _shockwaves.OriginAt(0));

        // Every float door, every way. NaN is the one that matters and the one a `<= 0` test would
        // wave through (AR §18.3): a NaN speed is a ring whose radius is never a number, so it
        // passes over nobody and never reaches its ceiling — a slot held for the rest of the run
        // with nothing logged.
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(Vector3.Zero, bad, 7f, Damage, 1.2f, 0f), $"speed {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(Vector3.Zero, 8f, bad, Damage, 1.2f, 0f), $"maxRadius {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(Vector3.Zero, 8f, 7f, bad, 1.2f, 0f), $"damage {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(Vector3.Zero, 8f, 7f, Damage, bad, 0f), $"thickness {bad}");
        }

        // And the two positions a non-finite value can arrive at, each of which makes every
        // squared distance NaN — a ring that is drawn and can never hit anything.
        foreach (float bad in new[] { float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(new Vector3(bad, 0f, 0f), 8f, 7f, Damage, 1.2f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(new Vector3(0f, 0f, bad), 8f, 7f, Damage, 1.2f, 0f));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Emit(Vector3.Zero, 8f, 7f, Damage, 1.2f, bad), $"now {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwaves.Tick(bad, Vector3.Zero, _player), $"now {bad}");
        }

        Assert.That(_shockwaves.ActiveCount, Is.Zero, "And not one of them left anything behind.");

        Emit(Vector3.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() => _shockwaves.OriginAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _shockwaves.OriginAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _shockwaves.RadiusAt(0, float.NaN));
    }

    [Test]
    public void Fissure_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new FissureSystem(null));

        Assert.Throws<ArgumentNullException>(() => _fissures.Tick(0f, Vector3.Zero, null));

        Assert.Throws<ArgumentOutOfRangeException>(() => _fissures.PositionAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _fissures.RadiusAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _fissures.HasFiredAt(0));

        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Open(Vector3.Zero, bad, Arm, 0.6f, Damage, 0f), $"radius {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Open(Vector3.Zero, 2.5f, bad, 0.6f, Damage, 0f), $"arm {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Open(Vector3.Zero, 2.5f, Arm, bad, Damage, 0f), $"open {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Open(Vector3.Zero, 2.5f, Arm, 0.6f, bad, 0f), $"damage {bad}");
        }

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Open(new Vector3(0f, 0f, bad), 2.5f, Arm, 0.6f, Damage, 0f));

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Open(Vector3.Zero, 2.5f, Arm, 0.6f, Damage, bad), $"now {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissures.Tick(bad, Vector3.Zero, _player), $"now {bad}");
        }

        Assert.That(_fissures.ActiveCount, Is.Zero);
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    /// <summary>One ring with the Warden's shipped numbers, emitted at <paramref name="origin"/>.</summary>
    private int Emit(Vector3 origin) => _shockwaves.Emit(
        origin,
        WardenBehaviour.ShockwaveSpeed,
        WardenBehaviour.ShockwaveMaxRadius,
        Damage,
        WardenBehaviour.ShockwaveThickness,
        0f);

    /// <summary>One crack with the Warden's shipped numbers, opened at <paramref name="at"/>.</summary>
    private int Open(Vector3 at) => _fissures.Open(
        at,
        WardenBehaviour.FissureRadius,
        Arm,
        WardenBehaviour.FissureOpenSeconds,
        Damage,
        0f);

    /// <summary>
    /// Ticks the rings from zero to <paramref name="seconds"/>, a sixtieth at a time.
    /// </summary>
    /// <remarks>
    /// The clock is computed from the step index rather than accumulated, which is
    /// <c>ZoneSystemTests.PulsesOverSixSeconds</c>' care for the same reason: sixty additions of
    /// <c>1/60</c> drift, and a row that lands on a deadline by drifting onto it is a coin toss.
    /// </remarks>
    private void TickShockFor(float seconds, Vector3 playerPosition)
    {
        const float step = 1f / 60f;

        int frames = (int)MathF.Ceiling(seconds / step);

        for (int i = 1; i <= frames; i++)
        {
            _shockwaves.Tick(i * step, playerPosition, _player);
        }
    }

    /// <summary>Ticks the cracks from zero to <paramref name="seconds"/>, a sixtieth at a time.</summary>
    private void TickFissureFor(float seconds, Vector3 playerPosition)
    {
        const float step = 1f / 60f;

        int frames = (int)MathF.Ceiling(seconds / step);

        for (int i = 1; i <= frames; i++)
        {
            _fissures.Tick(i * step, playerPosition, _player);
        }
    }

    /// <summary>
    /// One two-second fight at <paramref name="step"/> seconds a frame: a ring and a crack, a
    /// player standing where both reach, and what came of it.
    /// </summary>
    /// <remarks>
    /// The crack is opened a second in, so the two hazards are spaced past
    /// <c>CharacterSpec.HitIFrames</c> and the second hit is a second hit rather than a blocked
    /// one — which is what makes the damage total a real comparison between the two rates.
    /// </remarks>
    private (float Taken, int Hits, float Radius) Rate(float step)
    {
        var events = new RecordingEvents();
        var intents = new RecordingIntents();
        var player = new PlayerCombat(Oathbound(), events, intents, EnemyCapacity);
        var shockwaves = new ShockwaveSystem(events);
        var fissures = new FissureSystem(events);

        var standing = new Vector3(0f, 0f, 2f);

        // A slow ring, so two seconds is not enough to retire it and its radius at the end is a
        // number worth comparing.
        shockwaves.Emit(Vector3.Zero, 2f, WardenBehaviour.ShockwaveMaxRadius, Damage, 1.2f, 0f);

        float before = player.Health.Current + player.Health.Shield;

        var opened = false;

        int frames = (int)MathF.Ceiling(2f / step);

        for (int i = 1; i <= frames; i++)
        {
            float now = i * step;

            if (!opened && now >= 1f)
            {
                fissures.Open(standing, WardenBehaviour.FissureRadius, 0.6f, 0.6f, Damage, now);

                opened = true;
            }

            shockwaves.Tick(now, standing, player);
            fissures.Tick(now, standing, player);
        }

        // The radius is read at the same simulated second on both sides, which is the comparison
        // rule 5 is actually about.
        return (
            before - (player.Health.Current + player.Health.Shield),
            events.Count<PlayerDamaged>(),
            shockwaves.RadiusAt(0, 2f));
    }

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(AegisMax, 4f, 15f),
        HitIFrames);
}
