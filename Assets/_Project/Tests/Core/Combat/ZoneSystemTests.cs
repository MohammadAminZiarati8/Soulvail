using System;
using System.Collections.Generic;
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
/// CC §6.4's healing ground: where a zone is placed, who it reaches, when it pulses, when it stops,
/// and what it costs per frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <see cref="Health"/> throughout</b>, and against a real
/// <see cref="CombatBlackboard"/> — <c>HealthTests</c>' shape and its reason: a fake would agree with
/// whatever the caller did, and half of what these rows are about is which pool actually moved and by
/// how much. The one row that needs a <see cref="PlayerCombat"/> builds one; <b>no row here needs a
/// <c>RunSession</c></b>, which is why the six that do live in <c>SpawnHealZoneTests</c> beside the
/// skill that casts this.
/// </para>
/// <para>
/// <b>Consecrate's authored numbers throughout</b> (CC §6.4): 3.5 m, 6 s, 3 hit points every 0.5 s,
/// against CC §7's 140 — so a failure reads as "the skill we ship stopped healing" rather than as an
/// arithmetic puzzle. The times are exact in binary and that is deliberate: a zone spawned at 0 with a
/// 0.5 s interval schedules 0.5, 1.0, 1.5 with no drift at all, so a row that lands on a pulse is
/// landing on it rather than nearly.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ZoneSystemTests
{
    private const string OathboundId = "character.oathbound";

    // CC §6.4's Consecrate, and CC §7's class.
    private const float Radius = 3.5f;
    private const float Duration = 6f;
    private const float Heal = 3f;
    private const float Interval = 0.5f;

    private const float MaxHp = 140f;
    private const float AegisMax = 30f;
    private const float AegisRechargeDelay = 4f;
    private const float AegisRefillPerSecond = 15f;
    private const float HitIFrames = 0.5f;

    /// <summary>Twelve pulses of 3 — GD's quarter of a bar, and the number the ordering turns on.</summary>
    private const float WholeZone = 36f;

    private const int EnemyCapacity = 8;

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private CombatBlackboard _blackboard;
    private Health _health;
    private ZoneSystem _zones;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _blackboard = new CombatBlackboard();
        _health = Unshielded();
        _zones = new ZoneSystem(_health, _blackboard, _events);
    }

    // ---- Where it goes (rules 1, 2) ---------------------------------------------------------------

    [Test]
    public void Zone_SpawnsWhereThePlayerIs()
    {
        _blackboard.PlayerPosition = new Vector3(3f, 0f, -2f);

        int id = _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        Assert.That(_zones.Count, Is.EqualTo(1));
        Assert.That(_zones.PositionAt(0), Is.EqualTo(new Vector3(3f, 0f, -2f)));
        Assert.That(_zones.RadiusAt(0), Is.EqualTo(Radius).Within(Tolerance));

        ZoneSpawned spawned = _events.Single<ZoneSpawned>();

        Assert.That(spawned.Id, Is.EqualTo(id), "The event carries the id Spawn handed back.");
        Assert.That(spawned.Position, Is.EqualTo(new Vector3(3f, 0f, -2f)));
        Assert.That(spawned.Radius, Is.EqualTo(Radius).Within(Tolerance));

        Assert.That(
            spawned.Duration,
            Is.EqualTo(Duration).Within(Tolerance),
            "So a view can size a decal and run its own countdown without a read.");

        // An id, never an index: the ids are issued from 1 so that a default-initialised field reads
        // as nobody rather than as the first zone of the run (EnemyRegistry's rule).
        Assert.That(id, Is.EqualTo(1));
    }

    [Test]
    public void Zone_DoesNotFollowThePlayer()
    {
        _blackboard.PlayerPosition = Vector3.Zero;

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        // Ten metres away, which is three radii: if the zone followed, the pulse below would land.
        _blackboard.PlayerPosition = new Vector3(10f, 0f, 0f);

        Assert.That(
            _zones.PositionAt(0),
            Is.EqualTo(Vector3.Zero),
            "Placed, not carried — rule 1, and the whole reason the skill is a decision.");

        _health.ApplyDamage(40f, 0f);

        float before = _health.Current;

        _zones.Tick(Interval);

        Assert.That(_health.Current, Is.EqualTo(before), "And it healed nobody, because nobody is in it.");
    }

    [Test]
    public void Blackboard_CarriesThePlayerPosition()
    {
        var intents = new RecordingIntents();
        var combat = new PlayerCombat(Character(), _events, intents, EnemyCapacity);
        var registry = new EnemyRegistry(EnemyCapacity);

        var snapshot = new WorldSnapshot(EnemyCapacity)
        {
            PlayerPosition = new Vector3(5f, 0f, 1f),
        };

        combat.Tick(1f / 60f, 0f, snapshot, registry.Alive, Vector3.UnitZ);

        Assert.That(
            combat.Blackboard.PlayerPosition,
            Is.EqualTo(new Vector3(5f, 0f, 1f)),
            "Written down, not decided: the position the body reported, in the table a cast reads.");

        // And it is refilled rather than remembered, so a zone cast on the tick the player moved is
        // placed at their feet — which is what the live-session row in SpawnHealZoneTests proves over
        // a whole RunSession.
        snapshot.PlayerPosition = new Vector3(-4f, 0f, 9f);

        combat.Tick(1f / 60f, 1f / 60f, snapshot, registry.Alive, Vector3.UnitZ);

        Assert.That(combat.Blackboard.PlayerPosition, Is.EqualTo(new Vector3(-4f, 0f, 9f)));

        // A blank blackboard has no position, like it has no target (CombatBlackboard.Reset).
        combat.Blackboard.Reset();

        Assert.That(combat.Blackboard.PlayerPosition, Is.EqualTo(Vector3.Zero));
    }

    // ---- The pulse (rules 4, 5) -------------------------------------------------------------------

    [Test]
    public void Zone_PulsesOnItsInterval()
    {
        Hurt(40f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _zones.Tick(0.49f);

        Assert.That(_health.Current, Is.EqualTo(100f).Within(Tolerance), "Not yet.");

        _zones.Tick(0.5f);

        Assert.That(_health.Current, Is.EqualTo(103f).Within(Tolerance), "The first pulse.");

        _zones.Tick(0.99f);

        Assert.That(_health.Current, Is.EqualTo(103f).Within(Tolerance), "Still the first.");

        _zones.Tick(1f);

        Assert.That(_health.Current, Is.EqualTo(106f).Within(Tolerance), "The second.");
        Assert.That(_events.Count<ZoneHealed>(), Is.EqualTo(2), "Two pulses, two events.");
    }

    [Test]
    public void Zone_PulsesAreAbsolute()
    {
        // The same six seconds at two frame rates. An accumulator would drift and a skip-don't-catch-up
        // loop would lose the pulses a long frame swallowed; the schedule is absolute, so both land
        // twelve (rule 4).
        Assert.That(PulsesOverSixSeconds(steps: 30, step: 0.2f), Is.EqualTo(12));
        Assert.That(PulsesOverSixSeconds(steps: 6, step: 1f), Is.EqualTo(12));
    }

    [Test]
    public void Zone_HealsOnlyWhatIsInside()
    {
        Hurt(40f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        // 3.4 m of a 3.5 m radius: inside, and inside by less than one step of the player's own
        // movement, which is the resolution the mechanic actually has to have.
        _blackboard.PlayerPosition = new Vector3(3.4f, 0f, 0f);

        _zones.Tick(0.5f);

        Assert.That(_health.Current, Is.EqualTo(103f).Within(Tolerance));

        _blackboard.PlayerPosition = new Vector3(3.6f, 0f, 0f);

        _zones.Tick(1f);

        Assert.That(
            _health.Current,
            Is.EqualTo(103f).Within(Tolerance),
            "Two hundred millimetres out is out. Nothing partial, nothing faded.");

        Assert.That(
            _events.Count<ZoneHealed>(),
            Is.EqualTo(1),
            "And a pulse that reached nobody says nothing at all, where a pulse that healed nothing "
                + "at full health does say so — the two are different facts.");
    }

    [Test]
    public void Zone_ContainmentIsFlat()
    {
        Hurt(40f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        // A metre up and two out. In three dimensions that is 2.24 m, which is also inside — so the
        // row is built where the two readings disagree about the *answer* rather than the distance:
        // 3.4 m out and 3 m up is 4.53 m in space and 3.4 m on the floor.
        _blackboard.PlayerPosition = new Vector3(2f, 1f, 0f);

        _zones.Tick(0.5f);

        Assert.That(_health.Current, Is.EqualTo(103f).Within(Tolerance));

        _blackboard.PlayerPosition = new Vector3(3.4f, 3f, 0f);

        _zones.Tick(1f);

        Assert.That(
            _health.Current,
            Is.EqualTo(106f).Within(Tolerance),
            "XZ only (AR §18.4). Counting height would make standing on a step leave a zone the "
                + "player is visibly inside of.");
    }

    [Test]
    public void Zone_InAndOutBetweenPulsesCostsNothing()
    {
        Hurt(40f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _blackboard.PlayerPosition = new Vector3(10f, 0f, 0f);

        _zones.Tick(0.2f);
        _zones.Tick(0.3f);

        _blackboard.PlayerPosition = Vector3.Zero;

        _zones.Tick(0.4f);
        _zones.Tick(0.5f);

        Assert.That(
            _health.Current,
            Is.EqualTo(103f).Within(Tolerance),
            "In full: standing in it when the pulse lands is the whole of the contract, and nothing "
                + "accumulates time inside.");
    }

    // ---- Its life (rules 4, 9) --------------------------------------------------------------------

    [Test]
    public void Zone_ExpiresAfterItsDuration()
    {
        Hurt(40f);

        int id = _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _events.Clear();

        // One tick past the end, which is where a frame boundary usually lands.
        _zones.Tick(6.01f);

        Assert.That(_zones.Count, Is.Zero);
        Assert.That(_events.Single<ZoneExpired>().Id, Is.EqualTo(id));

        Assert.That(
            _events.Count<ZoneHealed>(),
            Is.EqualTo(12),
            "Every pulse it was alive for, caught up inside one tick — and not one more.");

        Assert.That(_health.Current, Is.EqualTo(100f + WholeZone).Within(Tolerance));

        _zones.Tick(6.5f);

        Assert.That(
            _events.Count<ZoneHealed>(),
            Is.EqualTo(12),
            "The pulse due at 6.5 never lands: the zone was over before it came round.");
    }

    [Test]
    public void Zone_PulsesOnTheTickItExpires()
    {
        // **The coincidence the spec never ruled and the authored skill turns on.** Six seconds over a
        // half-second interval puts the twelfth pulse on the expiry second exactly, so the order
        // inside Tick decides whether Consecrate is worth 36 hit points or 33 — and nothing else in
        // this fixture sees it, because Zone_ExpiresAfterItsDuration ticks at 6.01 and steps over the
        // coincidence. Pulses first: the zone is alive up to and including its last instant.
        Hurt(40f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _events.Clear();

        _zones.Tick(Duration);

        Assert.That(_zones.Count, Is.Zero, "It is over.");

        Assert.That(
            _events.Count<ZoneHealed>(),
            Is.EqualTo(12),
            "Twelve, not eleven. Retiring first would quietly make every authored zone worth one "
                + "pulse less than its own arithmetic says.");

        Assert.That(
            _health.Current,
            Is.EqualTo(100f + WholeZone).Within(Tolerance),
            "Which is the quarter bar the skill is authored to be.");

        Assert.That(
            IndexOfFirst<ZoneHealed>(),
            Is.LessThan(IndexOfFirst<ZoneExpired>()),
            "And the last pulse is announced before the zone that paid it ends, in the order of one "
                + "tick's events.");
    }

    [Test]
    public void Zone_SecondCastPlacesASecondZone()
    {
        Hurt(60f);

        var source = new object();

        int first = _zones.Spawn(Radius, Duration, Heal, Interval, 0f, source);

        // The first zone is ticked up to the recast rather than left to catch up afterwards, so that
        // what the tick below counts is one pulse from each rather than a backlog from one.
        _zones.Tick(0.5f);
        _zones.Tick(1f);

        // The *same* source, which is the one that matters: TimedEffects.Hold refreshes a pair rather
        // than queuing a second note, because a second note would come due on the first cast's clock
        // and cut a refreshed shield short. A zone is the opposite and deliberately so — it is a place
        // rather than state on the player, so a recast puts down a second place (rule 9).
        int second = _zones.Spawn(Radius, Duration, Heal, Interval, 1f, source);

        Assert.That(_zones.Count, Is.EqualTo(2));
        Assert.That(second, Is.Not.EqualTo(first), "Two zones, two ids.");

        _events.Clear();

        // The first's clock says 1.5 and the second's says 1.5 as well — one from 0 + 3 × 0.5, the
        // other from 1 + 0.5 — so this tick is where both are due and each is on its own schedule.
        _zones.Tick(1.5f);

        Assert.That(_events.Count<ZoneHealed>(), Is.EqualTo(2), "Both pulsed, on their own clocks.");
        Assert.That(_health.Current, Is.EqualTo(92f).Within(Tolerance), "80 + 3 + 3, then 3 + 3.");

        _events.Clear();

        // And they end at their own moments rather than together.
        _zones.Tick(6f);

        Assert.That(_events.Single<ZoneExpired>().Id, Is.EqualTo(first));
        Assert.That(_zones.Count, Is.EqualTo(1));
    }

    [Test]
    public void Zone_OverlappingZonesBothPulse()
    {
        Hurt(60f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        // A metre away: both circles cover the player, and nothing anywhere de-duplicates them.
        _blackboard.PlayerPosition = new Vector3(1f, 0f, 0f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _zones.Tick(Interval);

        Assert.That(
            _health.Current,
            Is.EqualTo(86f).Within(Tolerance),
            "Healed twice, because two zones are two zones. Standing in the overlap is the reward "
                + "for a recast that had somewhere to put the second one.");

        Assert.That(_events.Count<ZoneHealed>(), Is.EqualTo(2));
    }

    [Test]
    public void Zone_CapacityThrows()
    {
        // **Built on ZoneSystem alone, and never through a handler** — sixteen is TimedEffects'
        // ceiling and eight is GrantedShieldPool's, so a version of this row that cast a skill could
        // trip a different capacity and pass while testing the wrong refusal. Nothing here holds a
        // shield or a timed effect at all.
        for (int i = 0; i < ZoneSystem.Capacity; i++)
        {
            _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());
        }

        Assert.That(_zones.Count, Is.EqualTo(ZoneSystem.Capacity));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object()));

        Assert.That(
            thrown.Message,
            Does.Contain(ZoneSystem.Capacity.ToString()),
            "The message names the capacity, or the fix is a guess.");

        Assert.That(_zones.Count, Is.EqualTo(ZoneSystem.Capacity), "And the refusal changed nothing.");
    }

    [Test]
    public void Zone_ClearDropsEverything()
    {
        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());
        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());
        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _events.Clear();

        _zones.Clear();

        Assert.That(_zones.Count, Is.Zero);

        Assert.That(
            _events.Count<ZoneExpired>(),
            Is.Zero,
            "The run's end publishes nothing: the scope is going away and with it every subscriber "
                + "an event could reach (ProjectileSystem.Clear's silence).");
    }

    [Test]
    public void Zone_BoundaryLeavesItRunning()
    {
        // **M2-10's rule that a door heals nobody, seen from the other side**: a zone outlives the
        // wave it was cast during. What a boundary actually does to the player is `Targeter.Reset`
        // and nothing wider — StageFlow.Advance says so in its own remarks, and calls
        // PlayerCombat.Reset deliberately *not* — so this row drives both, plus the wider reset the
        // boundary refuses, and asserts the zone is untouched by any of them.
        //
        // The whole boundary is not driven here, and that is named rather than implied: clearing a
        // stage needs an enemy killed, a kill needs a cone report from a Unity adapter this assembly
        // has no player for, and the fixture would be larger than the system it is testing. What is
        // reachable is every call the boundary makes.
        var intents = new RecordingIntents();
        var combat = new PlayerCombat(Character(), _events, intents, EnemyCapacity);
        var zones = new ZoneSystem(combat.Health, combat.Blackboard, _events);
        var projectiles = new ProjectileSystem(_events, 8);

        combat.Health.ApplyDamage(40f, 0f);

        zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        combat.Targeter.Reset();
        combat.Reset();
        projectiles.Clear();

        Assert.That(zones.Count, Is.EqualTo(1), "Still standing where it was cast.");

        // The blackboard was blanked by the wide reset and the next tick refills it, which is what
        // PlayerCombat does every frame of a live run; written here rather than ticked because the
        // subject of the row is the zone, not the table.
        combat.Blackboard.PlayerPosition = Vector3.Zero;

        combat.Health.ApplyDamage(40f, 1f);

        float before = combat.Health.Current;

        zones.Tick(Interval);

        Assert.That(
            combat.Health.Current,
            Is.EqualTo(before + Heal).Within(Tolerance),
            "And still pulsing, on the clock it was placed on.");
    }

    // ---- What a pulse is worth (rules 7, 8) -------------------------------------------------------

    [Test]
    public void Zone_HealedCarriesTheActualAmount()
    {
        Hurt(2f);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _events.Clear();

        _zones.Tick(Interval);

        Assert.That(
            _events.Single<ZoneHealed>().Amount,
            Is.EqualTo(2f).Within(Tolerance),
            "What was restored, not what was authored: two of the three points had somewhere to go.");

        Assert.That(_health.Current, Is.EqualTo(MaxHp).Within(Tolerance));
    }

    [Test]
    public void Zone_HealedIsZeroAtFullHealth()
    {
        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _events.Clear();

        _zones.Tick(Interval);

        Assert.That(
            _events.Count<ZoneHealed>(),
            Is.EqualTo(1),
            "Published — a view that never heard about it could not tell a player standing in a zone "
                + "at full health from one standing outside it.");

        Assert.That(
            _events.Single<ZoneHealed>().Amount,
            Is.Zero,
            "And honest: nothing was restored, so nothing is what it says.");
    }

    [Test]
    public void Zone_DoesNotHealTheDead()
    {
        _health.ApplyDamage(MaxHp + 1f, 0f);

        Assert.That(_health.IsDead, Is.True);

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        _zones.Tick(Interval);

        Assert.That(
            _health.Current,
            Is.Zero,
            "Health.Heal's own contract — zero when dead — pinned here rather than re-implemented.");

        Assert.That(_events.Single<ZoneHealed>().Amount, Is.Zero);
    }

    [Test]
    public void Zone_DoesNotResetTheAegisDelay()
    {
        // **The promise Health.Heal's remarks made to this task before it existed** (Health.cs:301):
        // a heal neither starts i-frames nor holds off the Aegis refill, because "a heal that reset
        // the Aegis delay would make a Consecrate zone (M3-11) actively counterproductive for the
        // Oathbound". The numbers are HealthTests' own Shield_RechargesAfterDelay, with a zone
        // pulsing through the whole of the wait.
        Health health = Oathbound();
        var zones = new ZoneSystem(health, _blackboard, _events);

        health.ApplyDamage(80f, 0f);

        Assert.That(health.Shield, Is.Zero, "The Aegis took its 30 and the rest went to HP.");
        Assert.That(health.Current, Is.EqualTo(90f).Within(Tolerance));

        zones.Spawn(Radius, 20f, Heal, Interval, 0f, new object());

        for (int step = 1; step <= 39; step++)
        {
            health.Tick(0.1f, step * 0.1f);
            zones.Tick(step * 0.1f);
        }

        Assert.That(
            health.Current,
            Is.GreaterThan(90f),
            "The fixture's own claim: the zone really did heal through the wait, so this row is "
                + "about a heal rather than about an empty clock.");

        Assert.That(health.Shield, Is.Zero, "And the 4 s delay is still running, untouched.");

        health.Tick(0.5f, 4.4f);
        zones.Tick(4.4f);

        Assert.That(
            health.Shield,
            Is.EqualTo(6f).Within(1e-3f),
            "Exactly what it would have been with no zone at all: 0.4 s past the deadline at 15/s.");
    }

    // ---- The budget (rule 10) ---------------------------------------------------------------------

    [Test]
    public void Zone_AllocatesNothing()
    {
        var silent = new SilentEvents();
        var health = new Health(new Stat(MaxHp), null, 0f);
        var zones = new ZoneSystem(health, _blackboard, silent);

        _blackboard.PlayerPosition = Vector3.Zero;

        // A full table, a player standing in every one of them, and a life long enough that none of
        // them retires inside the measurement — the steady state a fight holds, rather than a table
        // emptying itself. Four thousand seconds rather than a million, because a zone may not
        // schedule more than ZoneSystem.MaxPulses in its life and 4 000 / 0.5 is 8 000 of them: the
        // budget is a real door and this row is the first thing that walked into it.
        for (int i = 0; i < ZoneSystem.Capacity; i++)
        {
            zones.Spawn(Radius, 4_000f, Heal, Interval, 0f, new object());
        }

        float now = 0f;

        AllocationAssert.None(() =>
        {
            now += 0.25f;

            zones.Tick(now);
        });

        Assert.That(now, Is.GreaterThan(2_000f), "The probe is live rather than measuring a no-op.");
        Assert.That(zones.Count, Is.EqualTo(ZoneSystem.Capacity));
    }

    // ---- Guards -----------------------------------------------------------------------------------

    [Test]
    public void Zone_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new ZoneSystem(null, _blackboard, _events));
        Assert.Throws<ArgumentNullException>(() => new ZoneSystem(_health, null, _events));
        Assert.Throws<ArgumentNullException>(() => new ZoneSystem(_health, _blackboard, null));

        // A zone with no owner is one nothing could ever dismiss — Modifier's rule two layers down.
        Assert.Throws<ArgumentNullException>(
            () => _zones.Spawn(Radius, Duration, Heal, Interval, 0f, null));

        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.PositionAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.RadiusAt(0));

        _zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.PositionAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.PositionAt(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.RadiusAt(1));
    }

    [Test]
    public void Zone_SpawnRefusesNonsense()
    {
        // Every float door, every way. NaN is the one that matters and the one a `<= 0` test would
        // wave through (AR §18.3): a NaN radius makes every containment test answer no, a NaN
        // interval makes every pulse comparison false, and both are a zone that stands there doing
        // nothing with nothing logged.
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _zones.Spawn(bad, Duration, Heal, Interval, 0f, new object()), $"radius {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _zones.Spawn(Radius, bad, Heal, Interval, 0f, new object()), $"duration {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _zones.Spawn(Radius, Duration, bad, Interval, 0f, new object()), $"heal {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _zones.Spawn(Radius, Duration, Heal, bad, 0f, new object()), $"interval {bad}");
        }

        // The clock is refused rather than trusted, which is ProjectileSystem's answer rather than
        // SkillRunner's, and the reason is the catch-up loop: an unreadable now is a schedule that
        // cannot be compared against in either direction.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _zones.Spawn(Radius, Duration, Heal, Interval, float.NaN, new object()));
        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.Tick(float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => _zones.Tick(float.PositiveInfinity));

        // And the pulse budget, which is a bound on the catch-up loop rather than on taste: six
        // seconds at a microsecond interval is not a fast zone, it is six million heals inside one
        // frame.
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => _zones.Spawn(Radius, Duration, Heal, 1e-6f, 0f, new object()));

        Assert.That(thrown.Message, Does.Contain(ZoneSystem.MaxPulses.ToString()));

        Assert.That(_zones.Count, Is.Zero, "And not one of them left anything behind.");
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    /// <summary>Takes <paramref name="amount"/> off, so a pulse has somewhere to go.</summary>
    private void Hurt(float amount) => _health.ApplyDamage(amount, 0f);

    /// <summary>
    /// How many pulses one Consecrate lands over its whole life at a given frame time.
    /// </summary>
    /// <remarks>
    /// <c>now</c> is computed from the step index rather than accumulated, which is
    /// <c>HealthTests.Shield_RechargesAfterDelay</c>'s care for the same reason: thirty additions of
    /// 0.1f drift, and a row that lands on a deadline by drifting onto it is a coin toss.
    /// </remarks>
    private static int PulsesOverSixSeconds(int steps, float step)
    {
        var events = new RecordingEvents();
        var blackboard = new CombatBlackboard();
        var health = new Health(new Stat(MaxHp), null, 0f);
        var zones = new ZoneSystem(health, blackboard, events);

        // Full health, so the count is about the schedule rather than about the headroom.
        zones.Spawn(Radius, Duration, Heal, Interval, 0f, new object());

        for (int i = 1; i <= steps; i++)
        {
            zones.Tick(i * step);
        }

        Assert.That(zones.Count, Is.Zero, "Six seconds is six seconds either way.");

        return events.Count<ZoneHealed>();
    }

    /// <summary>Where the first <typeparamref name="T"/> sits in the recorded order.</summary>
    private int IndexOfFirst<T>()
        where T : struct
    {
        IReadOnlyList<object> all = _events.All;

        for (int i = 0; i < all.Count; i++)
        {
            if (all[i] is T)
            {
                return i;
            }
        }

        return -1;
    }

    private static Health Unshielded() => new Health(new Stat(MaxHp), null, 0f);

    private static Health Oathbound() =>
        new Health(
            new Stat(MaxHp),
            new ShieldSpec(AegisMax, AegisRechargeDelay, AegisRefillPerSecond),
            HitIFrames);

    /// <summary>CC §7's class, for the two rows that need a whole <see cref="PlayerCombat"/>.</summary>
    private static CharacterSpec Character() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(AegisMax, AegisRechargeDelay, AegisRefillPerSecond),
        HitIFrames);
}
