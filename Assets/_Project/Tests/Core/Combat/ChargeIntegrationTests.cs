using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// M1-15's seven rules: the press becomes an intent and an event, the motor is suspended and handed
/// back at rest, the i-frames cover the dash and its trail, a report becomes damage and knockback
/// once per enemy per dash, a late report is ignored, the ramp treats a dash as movement, and none
/// of it allocates.
/// </summary>
/// <remarks>
/// <para>
/// <b>Driven and observed through the ports, like M1-11's fixture and for the same reason.</b>
/// <c>RunState.Combat</c> is <c>internal</c> and <c>Soulvail.Tests.Core</c> has no
/// <c>InternalsVisibleTo</c> (M0-10), so a dash is asked for with <c>IPlayerCommands.MovementSkill</c>,
/// answered with <c>IRunSession.ReportChargeHits</c>, and watched through the intents and events
/// that come out. That is exactly the vocabulary M1-16's view and button will have.
/// </para>
/// <para>
/// <b>One row is the exception, and it has to be.</b> <c>Health_InvulnerableDuringWindow</c> builds
/// a <see cref="PlayerCombat"/> directly, because nothing can hurt the player from outside core
/// until M1-18's chasers land — there is no <c>ReportContact</c> yet — and i-frames that block
/// nothing are the one part of CC §5 that cannot be proved by watching. The same trade
/// <c>ConeHitsToDamageTests</c> makes when it reaches for <c>EnemySystem</c> to spell a zero-damage
/// swing.
/// </para>
/// <para>
/// CC §7's Charge row throughout — 10 m over 0.22 s, i-frames for the duration plus 0.05 s, 20
/// damage and 5 m of knockback, a 2.5 s cooldown — against GD §8.1's 36 HP Husk, so a failure reads
/// as "the dodge we ship stopped behaving" rather than as an arithmetic puzzle. Two rows override
/// one number each, and say which and why.
/// </para>
/// <para>
/// The Focus ramp is authored off (<c>MaxMultiplier</c> 1) for every row but the one that is about
/// it, as it is in every M1 fixture that is not <c>FocusTrackerTests</c>: a ramp climbing under
/// rows that stand perfectly still would speed the Censer up beneath assertions that are not about
/// the Censer at all.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ChargeIntegrationTests
{
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";
    private const string HuskId = "enemy.husk";
    private const int Seed = 99;

    /// <summary>Room for every row's enemies, and the buffers the run preallocates.</summary>
    private const int EnemyCapacity = 8;

    /// <summary>
    /// The device cap a run composes its stages under (M2-05). This fixture's mode has an empty
    /// roster, so nothing is composed and the director is inert — it is here because a run needs
    /// one, not because any row is about it.
    /// </summary>
    private const int DeviceCap = 8;

    // CC §7, Charge.
    private const float Distance = 10f;
    private const float Duration = 0.22f;
    private const float CooldownSeconds = 2.5f;
    private const float InputBuffer = 0.15f;
    private const float ChargeDamage = 20f;
    private const float Knockback = 5f;
    private const float IFrameTrail = 0.05f;

    /// <summary>
    /// <c>PlayerCombat.ChargeReportGrace</c>, which is <c>private const</c> there. Duplicated rather
    /// than exposed: it is an internal tolerance on a round trip, not a number anything outside core
    /// is entitled to reason about, and the one row that depends on it says so in its own comment.
    /// </summary>
    private const float ReportGrace = 0.1f;

    // CC §7's Censer and GD §8.1's Husk — background in every row here, and named so that the
    // numbers a failure prints are recognisable.
    private const float SwingDamage = 13f;
    private const float SwingsPerSecond = 3f;
    private const float WeaponRange = 8f;
    private const float HuskMaxHp = 36f;

    // CC §7, Survivability and Movement.
    private const float MaxHp = 140f;
    private const float MoveSpeed = 5.4f;
    private const float HitIFrames = 0.5f;

    // CC §4.3's ramp, used by the one row that is about it.
    private const float FocusDelay = 0.4f;
    private const float FocusRampTime = 1f;
    private const float FocusMaxMultiplier = 1.3f;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    /// <summary>
    /// How many frames a row will wait for something it is expecting. Well past a dash's 27 frames
    /// and a cooldown's 300, so a row that is genuinely stuck fails as a stuck row rather than as a
    /// timeout that might have been bad luck.
    /// </summary>
    private const int MaxFrames = 600;

    /// <summary>Where a run starts looking, and so where a dash with a centred stick goes.</summary>
    private static readonly Vector2 Forward = new(0f, 1f);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private ContentCatalog _catalog;
    private RunSession _session;
    private WorldSnapshot _snapshot;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk() }, new[] { Descent() });
        _session = new RunSession(_catalog, new FixedRandom(Seed), _events, _intents, EnemyCapacity, DeviceCap);

        // The player stands at the origin and — except in the one row that pushes the stick — never
        // touches it, so a dash goes where the character is looking and every enemy's spawn position
        // is also its distance.
        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
    }

    // ---- Rule 1 and 2: the press becomes an intent and an event ---------------------------------

    [Test]
    public void MovementSkill_EmitsChargeIntent_AndEvent()
    {
        StartRun();

        _session.MovementSkill();
        _session.Tick(_snapshot);

        // One intent, on the tick after the press and not on the press itself: the command records,
        // the tick decides. See RunSession.MovementSkill.
        Assert.That(_intents.Charges.Count, Is.EqualTo(1));

        ChargeIntent charge = _intents.LastCharge;

        // +Z, because the stick is centred and a run starts looking that way — CC §5's "facing
        // direction if the stick is neutral", which is what makes a dash with no thumb on the stick
        // a step forward rather than a dash to nowhere.
        Assert.That(charge.DirectionXZ.X, Is.EqualTo(Forward.X).Within(1e-5f));
        Assert.That(charge.DirectionXZ.Y, Is.EqualTo(Forward.Y).Within(1e-5f));
        Assert.That(charge.Distance, Is.EqualTo(Distance).Within(1e-5f));
        Assert.That(charge.Duration, Is.EqualTo(Duration).Within(1e-5f));

        ChargeStarted started = _events.Single<ChargeStarted>();

        // The event and the intent agree about the direction, which is the whole reason the event
        // carries one: a trail drawn along a different vector from the one the body travels is a
        // dash that visibly misses its own VFX.
        Assert.That(started.DirectionXZ.X, Is.EqualTo(charge.DirectionXZ.X).Within(1e-5f));
        Assert.That(started.DirectionXZ.Y, Is.EqualTo(charge.DirectionXZ.Y).Within(1e-5f));
    }

    // ---- Rule 4: the motor is suspended, then handed back at rest -------------------------------

    [Test]
    public void PlayerMove_SuppressedWhileActive()
    {
        StartRun();

        _session.MovementSkill();

        // 0.16 s in ten steps, which is inside the 0.22 s dash with room to spare — long enough
        // that a suppression which only covered the first frame would be caught.
        _snapshot.Dt = 0.016f;

        for (int i = 0; i < 10; i++)
        {
            _session.Tick(_snapshot);
        }

        Assert.That(_session.State.Time, Is.LessThan(Duration), "Sanity: the dash is still in flight.");

        // Not one, including on the tick the dash began: a ChargeIntent and a PlayerMoveIntent are
        // both instructions about where the character goes, and the body must never hold two.
        Assert.That(_intents.PlayerMoves, Is.Empty);
        Assert.That(_intents.Charges.Count, Is.EqualTo(1), "…and the dash itself was still issued.");
    }

    [Test]
    public void PlayerMove_ResumesAfter_WithZeroVelocity()
    {
        StartRun();

        // The stick full over to +X and held there for a quarter of a second, which is four times
        // CC §2.4's 0.06 s ramp: the motor is at top speed when the dash begins, so the velocity it
        // is holding while suspended is the largest one it could carry over.
        _snapshot.MoveInput = new Vector2(1f, 0f);

        for (int i = 0; i < 30; i++)
        {
            _session.Tick(_snapshot);
        }

        Assert.That(
            _intents.LastPlayerMove.Velocity.X,
            Is.EqualTo(MoveSpeed).Within(1e-3f),
            "Sanity: running flat out when the dodge is pressed.");

        _session.MovementSkill();
        _intents.Clear();

        int frames = TickUntilPlayerMoveResumes();

        // The first of those frames is the tick the dash *begins* on — a press is recorded between
        // ticks and acted on by the next one — so the movement itself is the rest, and it lasts CC
        // §7's 0.22 s to within the frame a 120 fps clock can promise. The intent that then arrives
        // is the first one since: the tick the movement ends on is the tick the motor is handed
        // back.
        Assert.That((frames - 1) * Frame, Is.EqualTo(Duration).Within(Frame));
        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(1));

        // Exactly zero, with the stick still pushed hard over. A suspended motor keeps whatever it
        // was carrying, and letting that out would fling the character on at running speed for a
        // frame after a dodge they have already finished.
        Assert.That(_intents.LastPlayerMove.Velocity, Is.EqualTo(Vector3.Zero));

        _session.Tick(_snapshot);

        Assert.That(_intents.PlayerMoves.Count, Is.EqualTo(2));

        // And then it accelerates from rest, one frame of CC §2.4's ramp at a time — 0.75 m/s at
        // 120 fps — rather than resuming at the speed it had before.
        Assert.That(_intents.LastPlayerMove.Velocity.X, Is.GreaterThan(0f));
        Assert.That(_intents.LastPlayerMove.Velocity.X, Is.LessThan(MoveSpeed));
    }

    // ---- Rules 2 and 3: the i-frames, and the edge that ends them -------------------------------

    [Test]
    public void Health_InvulnerableDuringWindow()
    {
        // Built directly, because nothing outside core can hurt the player until M1-18 — see the
        // fixture remarks. The registry stays empty: this row is about the dash and the damage, and
        // an enemy would only add a target to face.
        var registry = new EnemyRegistry(EnemyCapacity);
        var combat = new PlayerCombat(Oathbound(), _events, _intents, EnemyCapacity);
        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };

        combat.Charge.Request(0f);
        combat.Tick(Frame, 0f, snapshot, registry.Alive, Vector3.UnitZ);

        // Mid-dash: the whole point of the mechanic.
        combat.Tick(Frame, 0.1f, snapshot, registry.Alive, Vector3.UnitZ);

        Assert.That(combat.ApplyDamage(10f, 0.1f).Blocked, Is.True, "0.1 s in, still dashing.");

        // Past the movement and inside the trail. CC §5 is explicit that these 0.05 s are not
        // padding: they are what stops a dodge that visually cleared an attack from being undone by
        // the frames between the glass and the simulation.
        combat.Tick(Frame, 0.26f, snapshot, registry.Alive, Vector3.UnitZ);

        Assert.That(combat.ApplyDamage(10f, 0.26f).Blocked, Is.True, "0.26 s in, inside the trail.");

        combat.Tick(Frame, 0.3f, snapshot, registry.Alive, Vector3.UnitZ);

        DamageResult landed = combat.ApplyDamage(10f, 0.3f);

        // 0.27 has passed, so the flag is down and the Aegis takes the hit like any other.
        Assert.That(landed.Blocked, Is.False);
        Assert.That(landed.Applied, Is.EqualTo(10f).Within(1e-4f));
        Assert.That(landed.ToShield, Is.EqualTo(10f).Within(1e-4f), "Shield before HP, as ever.");
    }

    [Test]
    public void ChargeEnded_PublishedOnce()
    {
        StartRun();

        _session.MovementSkill();

        float startedAt = -1f;
        float endedAt = -1f;

        for (int i = 0; i < MaxFrames && _session.State.Time < 0.5f; i++)
        {
            _session.Tick(_snapshot);

            if (startedAt < 0f && _events.Count<ChargeStarted>() == 1)
            {
                startedAt = _session.State.Time;
            }

            if (endedAt < 0f && _events.Count<ChargeEnded>() == 1)
            {
                endedAt = _session.State.Time;
            }
        }

        Assert.That(startedAt, Is.GreaterThanOrEqualTo(0f), "Sanity: the dash fired at all.");

        // Measured from the start rather than against the run's clock, because the press is recorded
        // between ticks and the dash therefore begins one frame into the run: what CC §5 fixes is
        // the length of the window, not the moment it opens.
        //
        // 0.27 is the 0.22 s of movement plus the 0.05 s trail, and the tolerance is one frame
        // because a tick either side of the boundary is exactly what a 120 fps clock can promise.
        Assert.That(endedAt - startedAt, Is.EqualTo(Duration + IFrameTrail).Within(Frame));

        // Once. It is an edge, and an edge that fired every frame afterwards would leave anything
        // listening for "I can be hurt again" doing it sixty times a second for the rest of the run.
        Assert.That(_events.Count<ChargeEnded>(), Is.EqualTo(1));
        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1), "…and the dash began once too.");
    }

    // ---- Rule 5: the report becomes damage and knockback ----------------------------------------

    [Test]
    public void ReportChargeHits_DamagesAndKnocksBack()
    {
        int husk = StartRun(At(5f))[0];

        StartCharge();

        _session.ReportChargeHits(new[] { husk });

        EnemyDamaged damaged = _events.Single<EnemyDamaged>();

        Assert.That(damaged.Id, Is.EqualTo(husk));
        Assert.That(damaged.Amount, Is.EqualTo(ChargeDamage).Within(1e-4f));
        Assert.That(damaged.HpFraction, Is.EqualTo(16f / HuskMaxHp).Within(1e-4f), "36 − 20.");
        Assert.That(damaged.Killed, Is.False);

        Assert.That(_intents.Knockbacks.Count, Is.EqualTo(1));

        EnemyKnockbackIntent shove = _intents.Knockbacks[0];

        Assert.That(shove.Id, Is.EqualTo(husk));
        Assert.That(shove.Distance, Is.EqualTo(Knockback).Within(1e-5f));

        // The dash's direction, not the direction from the player to the Husk. Everything a Charge
        // passes through is swept the same way, which is what makes it read as a plough.
        Assert.That(shove.DirectionXZ.X, Is.EqualTo(Forward.X).Within(1e-5f));
        Assert.That(shove.DirectionXZ.Y, Is.EqualTo(Forward.Y).Within(1e-5f));
    }

    [Test]
    public void ReportChargeHits_OncePerEnemyPerCharge()
    {
        int[] ids = StartRun(At(4f), At(5f));

        StartCharge();

        // Three reports for one dash, which is the normal case rather than a mistake: the body
        // sweeps a 10 m line frame by frame and says what it touched each time, and the second Husk
        // is only reached on the third frame.
        _session.ReportChargeHits(new[] { ids[0] });
        _session.ReportChargeHits(new[] { ids[0] });
        _session.ReportChargeHits(new[] { ids[0], ids[1] });

        IReadOnlyList<EnemyDamaged> damaged = _events.Of<EnemyDamaged>();

        Assert.That(damaged.Count, Is.EqualTo(2), "One per enemy per Charge — not one per report.");
        Assert.That(damaged[0].Id, Is.EqualTo(ids[0]));
        Assert.That(damaged[0].HpFraction, Is.EqualTo(16f / HuskMaxHp).Within(1e-4f), "20 once, not 60.");
        Assert.That(damaged[1].Id, Is.EqualTo(ids[1]));
        Assert.That(damaged[1].HpFraction, Is.EqualTo(16f / HuskMaxHp).Within(1e-4f));

        // The shoves are deduplicated by the same memory, for the same reason: an enemy shunted 5 m
        // three times for one dash would be launched across the arena.
        Assert.That(_intents.Knockbacks.Count, Is.EqualTo(2));
        Assert.That(_intents.Knockbacks[0].Id, Is.EqualTo(ids[0]));
        Assert.That(_intents.Knockbacks[1].Id, Is.EqualTo(ids[1]));
    }

    [Test]
    public void ReportChargeHits_OutsideWindow_Ignored()
    {
        int husk = StartRun(At(5f))[0];

        // Before anything has dashed. The window is a clock that starts at negative infinity, so
        // this is refused by the same comparison a late report is.
        _session.ReportChargeHits(new[] { husk });

        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero, "Nobody has dashed yet.");

        float startedAt = StartCharge();

        // Past the movement *and* past the grace the round trip is allowed — the frame that would
        // have finished the sweep is long gone, and this is the body talking about a dash that is
        // over.
        TickTo(startedAt + Duration + ReportGrace + 0.01f);

        _events.Clear();
        _intents.Clear();

        _session.ReportChargeHits(new[] { husk });

        Assert.That(_events.Count<EnemyDamaged>(), Is.Zero);
        Assert.That(_intents.Knockbacks, Is.Empty);
    }

    [Test]
    public void Charge_KillsLowHpEnemy()
    {
        // 15 HP against the Charge's 20: the one row where a dash finishes something, and the reason
        // CC §5 calls it a damaging dodge rather than an escape.
        _catalog = new ContentCatalog(new[] { Oathbound() }, new[] { Husk(maxHp: 15f) }, new[] { Descent() });
        _session = new RunSession(_catalog, new FixedRandom(Seed), _events, _intents, EnemyCapacity, DeviceCap);

        int husk = StartRun(At(5f))[0];

        StartCharge();

        _session.ReportChargeHits(new[] { husk });

        EnemyDamaged damaged = _events.Single<EnemyDamaged>();

        // Overkill reports what landed, not what was swung — 15, not 20. The same promise a swing
        // makes (M1-11), because it is the same Health underneath.
        Assert.That(damaged.Amount, Is.EqualTo(15f).Within(1e-4f));
        Assert.That(damaged.Killed, Is.True);

        EnemyDied died = _events.Single<EnemyDied>();

        Assert.That(died.Id, Is.EqualTo(husk));
        Assert.That(died.SpecId, Is.EqualTo(new ContentId(HuskId)));

        // Shoved anyway. It was passed through, and a corpse sliding while it dissolves reads better
        // than one that plants itself the instant it dies.
        Assert.That(_intents.Knockbacks.Count, Is.EqualTo(1));
        Assert.That(_intents.Knockbacks[0].Id, Is.EqualTo(husk));
    }

    // ---- Rule 6: a dash is movement -------------------------------------------------------------

    [Test]
    public void Focus_DropsOnCharge()
    {
        // The only row with CC §4.3's ramp switched on, because it is the only one about it.
        var catalog = new ContentCatalog(
            new[] { Oathbound(focusMaxMultiplier: FocusMaxMultiplier) },
            new[] { Husk() },
            new[] { Descent() });

        var session = new RunSession(catalog, new FixedRandom(Seed), _events, _intents, EnemyCapacity, DeviceCap);

        session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, SpawnPlan.Empty));

        // One step past the 0.4 s delay plus the 1.0 s climb, taken whole: this row is about what a
        // dash does to a full ramp, and how many frames it took to earn is FocusTrackerTests'.
        _snapshot.Dt = FocusDelay + FocusRampTime + 0.1f;
        session.Tick(_snapshot);

        IReadOnlyList<FocusRampChanged> ramp = _events.Of<FocusRampChanged>();

        Assert.That(ramp.Count, Is.EqualTo(1));
        Assert.That(ramp[0].Level, Is.EqualTo(1f), "Sanity: standing still has paid out in full.");

        _events.Clear();

        session.MovementSkill();

        _snapshot.Dt = Frame;
        session.Tick(_snapshot);

        // Straight to zero on the tick the dash begins, with the stick untouched throughout. CC §5's
        // dash is movement whatever the thumb is doing, and a ramp that survived it would pay out
        // for the dodge it is meant to be the alternative to.
        Assert.That(_events.Single<FocusRampChanged>().Level, Is.EqualTo(0f));
    }

    // ---- The command belongs to a live run ------------------------------------------------------

    [Test]
    public void Commands_WhenNotRunning_Throw()
    {
        int[] report = { 1 };

        // Before a run, because a press arriving here is a button that is listening when it should
        // not be — the input map is disabled outside a run, so this is a wiring mistake rather than
        // something the player did.
        Assert.Throws<InvalidOperationException>(() => _session.MovementSkill());
        Assert.Throws<InvalidOperationException>(() => _session.ReportChargeHits(report));

        StartRun();
        _session.End();

        // And after one, which is the case that actually happens: a finger already on the button as
        // the run ends, and a sweep resolved on the frame it ended.
        Assert.Throws<InvalidOperationException>(() => _session.MovementSkill());
        Assert.Throws<InvalidOperationException>(() => _session.ReportChargeHits(report));
    }

    // ---- Rule 7: the allocation budget ----------------------------------------------------------

    [Test]
    public void ChargePath_AllocatesNothing()
    {
        // Silent ports, for the reason M1-11's allocation row gives: the fixture's recorders are
        // what would allocate rather than the code under test — RecordingEvents boxes every payload
        // and RecordingIntents grows a list per dash.
        var events = new QuietEvents();
        var intents = new SilentIntents();

        // Two numbers overridden, and both only to make the measured loop short. The cooldown drops
        // to a tenth of a second so a whole dash fits in four ticks instead of three hundred, and
        // the dummy gets a billion hit points so a thousand dashes never kill it and every iteration
        // measures the same path — the one where damage lands, an event goes out and a knockback is
        // written.
        var catalog = new ContentCatalog(
            new[] { Oathbound(chargeCooldown: 0.1f) },
            new[] { Husk(maxHp: 1e9f) },
            new[] { Descent() });

        var session = new RunSession(catalog, new FixedRandom(Seed), events, intents, EnemyCapacity, DeviceCap);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), At(3f)) })));

        Assert.That(events.LastSpawnedId, Is.GreaterThan(0), "Sanity: the dummy is out there.");

        int[] report = { events.LastSpawnedId };

        // A tenth of a second a tick, which is the whole shape of the loop below. The press is made
        // one tick before it fires, so it is 0.1 s old when it is read and still inside CC §5's
        // 0.15 s buffer; the dash then runs 0.22 s and its trail 0.05 s more, so the fourth tick is
        // the first one past both — and the cooldown, at 0.1 s from the start, is long gone. Every
        // iteration therefore leaves the session in exactly the state the next one starts from.
        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = 0.1f };

        AllocationAssert.None(
            () =>
            {
                session.MovementSkill();
                session.Tick(snapshot);

                // Inside the window, on the tick the dash began — where a real sweep's first frame
                // reports from.
                session.ReportChargeHits(report);

                session.Tick(snapshot);
                session.Tick(snapshot);
                session.Tick(snapshot);
            },
            iterations: 1_000);
    }

    // ---- Fixture helpers -----------------------------------------------------------------------

    /// <summary>A point <paramref name="metres"/> straight ahead of the player, on +Z.</summary>
    /// <remarks>
    /// Straight ahead so that a run's starting facing already points at it: nothing here resolves
    /// where a dash actually goes — that is M1-16's — but a Husk inside the Censer's range is what
    /// keeps the weapon swinging in the background, which is the state the real thing dashes from.
    /// </remarks>
    private static Vector3 At(float metres) => new(0f, 0f, metres);

    /// <summary>
    /// Starts a run with one Husk per position and returns their ids, in plan order.
    /// </summary>
    private int[] StartRun(params Vector3[] positions)
    {
        var entries = new SpawnPlan.Entry[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            entries[i] = new SpawnPlan.Entry(new ContentId(HuskId), positions[i]);
        }

        _session.Start(new RunConfig(
            new ContentId(DescentId), new ContentId(OathboundId), Seed, 1, new SpawnPlan(entries)));

        IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();
        var ids = new int[spawned.Count];

        for (int i = 0; i < ids.Length; i++)
        {
            ids[i] = spawned[i].Id;
        }

        return ids;
    }

    /// <summary>
    /// Presses the button and ticks once, which is the tick the dash begins on. Returns the run
    /// time it began at, and clears the record so a row asserts on the dash alone.
    /// </summary>
    private float StartCharge()
    {
        _session.MovementSkill();
        _session.Tick(_snapshot);

        Assert.That(_intents.Charges.Count, Is.EqualTo(1), "Sanity: the dash fired.");

        float startedAt = _session.State.Time;

        _events.Clear();
        _intents.Clear();

        return startedAt;
    }

    /// <summary>
    /// Ticks until core writes a player-move intent again, and returns how many frames that took.
    /// </summary>
    /// <remarks>
    /// The intent is the signal because it is the only one there is from outside core: the dash's
    /// own <c>IsActive</c> lives behind an internal <c>RunState.Combat</c>, and the first
    /// <c>PlayerMoveIntent</c> after a dash is precisely what M1-16's view will see.
    /// </remarks>
    private int TickUntilPlayerMoveResumes()
    {
        for (int i = 1; i <= MaxFrames; i++)
        {
            _session.Tick(_snapshot);

            if (_intents.PlayerMoves.Count > 0)
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"The motor was never handed back within {MaxFrames} ticks. A dash lasts {Duration} s — "
                + "check that the suspension is keyed to IsActive rather than to something that "
                + "never clears.");
    }

    /// <summary>Ticks until the run's simulated clock has reached <paramref name="time"/>.</summary>
    /// <remarks>
    /// One tick with whatever <c>Dt</c> is needed, rather than a stream of frames, for the reason
    /// <c>ConeHitsToDamageTests.TickTo</c> gives: the rows that use it care about crossing a
    /// deadline to the hundredth of a second, and a frame-sized step would land wherever it landed.
    /// </remarks>
    private void TickTo(float time)
    {
        _snapshot.Dt = time - _session.State.Time;

        _session.Tick(_snapshot);

        _snapshot.Dt = Frame;
    }


    /// <summary>
    /// Descent as this fixture needs it: endless, from stage 1, and with an <b>empty roster</b>.
    /// </summary>
    /// <remarks>
    /// Empty because <c>RunSession.Start</c> resolves every roster id against the catalog before
    /// it announces a run, and no row here is about a schedule -- what these rows spawn comes from
    /// a <c>SpawnPlan</c>. A roster would couple every one of them to content they do not use.
    /// </remarks>
    private static ModeSpec Descent() => new ModeSpec(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Array.Empty<RosterEntry>());

    /// <summary>The Oathbound of CC §7, with the two numbers a row overrides.</summary>
    private static CharacterSpec Oathbound(
        float chargeCooldown = CooldownSeconds,
        float focusMaxMultiplier = 1f) => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, SwingDamage, SwingsPerSecond, WeaponRange, 60f, 0.4f),
        new FocusSpec(FocusDelay, FocusRampTime, focusMaxMultiplier),
        new MovementSkillSpec(
            MovementSkillKind.Charge,
            Distance,
            Duration,
            chargeCooldown,
            InputBuffer,
            ChargeDamage,
            Knockback,
            IFrameTrail),
        new ShieldSpec(30f, 4f, 15f),
        HitIFrames);

    /// <summary>GD §8.1's Husk, with the one number two rows override.</summary>
    /// <remarks>
    /// Authored <c>Static</c>, as it is in every M1 fixture: it stands where it was spawned, so
    /// nothing here depends on an AI M1-18 has not written yet.
    /// </remarks>
    private static EnemySpec Husk(float maxHp = HuskMaxHp) => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp,
        3.5f,
        1,
        threatCost: 4,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>
    /// An <see cref="IDomainEvents"/> that discards everything except the id of the last enemy
    /// spawned, which the allocation row needs before it starts measuring.
    /// </summary>
    /// <remarks>
    /// <c>typeof(T) == typeof(EnemySpawned)</c> rather than a pattern match, for the reason M1-11's
    /// copy of this class gives: <c>evt is EnemySpawned</c> on a generic value boxes on every
    /// publish, and the measured window publishes an <see cref="EnemyDamaged"/>, a
    /// <see cref="ChargeStarted"/> and a <see cref="ChargeEnded"/> a thousand times each.
    /// </remarks>
    private sealed class QuietEvents : IDomainEvents
    {
        /// <summary>The id of the most recent <see cref="EnemySpawned"/>, or −1.</summary>
        public int LastSpawnedId { get; private set; } = -1;

        public void Publish<T>(in T evt)
            where T : struct
        {
            if (typeof(T) == typeof(EnemySpawned))
            {
                LastSpawnedId = ((EnemySpawned)(object)evt).Id;
            }
        }
    }

    /// <summary>An <see cref="IIntentSink"/> that keeps nothing, so no list can grow mid-measurement.</summary>
    private sealed class SilentIntents : IIntentSink
    {
        public void PlayerMove(in PlayerMoveIntent intent)
        {
            // Deliberately nothing.
        }

        public void ConeHit(in ConeHitIntent intent)
        {
            // Deliberately nothing.
        }

        public void Charge(in ChargeIntent intent)
        {
            // Deliberately nothing.
        }

        public void EnemyMove(in EnemyMoveIntent intent)
        {
            // Deliberately nothing.
        }

        public void EnemyKnockback(in EnemyKnockbackIntent intent)
        {
            // Deliberately nothing.
        }
    }
}
