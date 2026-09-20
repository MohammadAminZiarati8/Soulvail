using System;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The corpse decoy from the dropping end: how long one stands, how many may stand at once, which
/// one an enemy is pointed at, what the door refuses, and what a Shroudstep does that a Charge does
/// not.
/// </summary>
/// <remarks>
/// <para>
/// <b>The Gravecaller's Shroudstep throughout</b> — CH §3.2's 6 m blink, a 3 s decoy and the
/// Charge's 2.5 s cooldown — so a failure reads as "the skill we authored stopped working" rather
/// than as an arithmetic puzzle. The times are exact in binary, which is deliberate: a decoy
/// dropped at 0 for 3 s expires at 3.0 with no drift at all, so a row that lands on the expiry is
/// landing on it rather than nearly.
/// </para>
/// <para>
/// <b>Half of these rows need nothing but a <see cref="LureSystem"/>, and half need a whole run.</b>
/// The <c>Lure_</c> and <c>Spec_</c> rows are about a store and a door and build one object each;
/// the <c>Skill_</c> rows are about <c>PlayerCombat.TickCharge</c>'s start edge, and they are driven
/// through <c>IPlayerCommands.MovementSkill</c> against a real <c>RunSession</c> for
/// <c>ChargeIntegrationTests</c>' reason: <c>RunState.Combat</c> is <c>internal</c> with no
/// <c>InternalsVisibleTo</c> (AR §18.2), and the vocabulary a press and a decoy arrive in is
/// exactly the one M5-05's view will read.
/// </para>
/// <para>
/// <b>What an enemy does with a decoy is not here</b> — that is <c>LurePerceptionTests</c>, beside
/// the perception step that does the redirecting. This fixture never builds an enemy.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LureSystemTests
{
    private const string GravecallerId = "character.gravecaller";
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";
    private const int Seed = 99;

    /// <summary>CH §3.2: a decoy taunts for three seconds.</summary>
    private const float DecoySeconds = 3f;

    // CH §3.2's Shroudstep, against CC §7's Charge for the rows that compare the two.
    private const float BlinkDistance = 6f;
    private const float BlinkDuration = 0.05f;
    private const float DashDistance = 10f;
    private const float DashDuration = 0.22f;
    private const float Cooldown = 2.5f;
    private const float InputBuffer = 0.15f;
    private const float IFrameTrail = 0.05f;

    // CH §4's Bone Bolt and CC §7's Censer — the weapons the two classes carry, neither of which
    // any row here is about. Nothing is ever in range, so neither ever fires.
    private const float BoltDamage = 9f;
    private const float BoltsPerSecond = 4f;
    private const float BoltRange = 12f;
    private const float BoltSpeed = 40f;
    private const float BoltRadius = 0.8f;
    private const float SwingDamage = 13f;
    private const float SwingsPerSecond = 3f;
    private const float ConeRange = 8f;

    private const float PlayerMaxHp = 140f;

    private const int EnemyCapacity = 8;
    private const int DeviceCap = 8;
    private const int ProjectileCapacity = 8;

    /// <summary>60 fps doubled — the rate a phone actually ticks at when it is keeping up.</summary>
    private const float Frame = 1f / 120f;

    /// <summary>
    /// How many frames a <c>Skill_</c> row waits for the blink it asked for. The press is buffered
    /// for 0.15 s and acted on the next tick, so this is many times over.
    /// </summary>
    private const int MaxFrames = 64;

    private const float Tolerance = 1e-4f;

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private LureSystem _lures;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _lures = new LureSystem(_events);
    }

    // ---- Rule 9 and the lifetime: a decoy is a place and a moment --------------------------------

    [Test]
    public void Lure_DropsAndStands()
    {
        int id = _lures.Drop(At(5f, 5f), 0f, DecoySeconds);

        _lures.Tick(2.9f);

        Assert.That(id, Is.GreaterThan(0), "Ids are issued from 1 so that 0 can mean nobody.");
        Assert.That(_lures.Count, Is.EqualTo(1), "A tenth of a second short of three seconds.");

        Assert.That(_lures.TryGetLure(At(0f, 0f), out Vector3 position), Is.True);
        Assert.That(position, Is.EqualTo(At(5f, 5f)), "Where it was dropped, unrevised.");

        DecoySpawned spawned = _events.Single<DecoySpawned>();

        Assert.That(spawned.Id, Is.EqualTo(id));
        Assert.That(spawned.Position, Is.EqualTo(At(5f, 5f)));
        Assert.That(spawned.Duration, Is.EqualTo(DecoySeconds).Within(Tolerance),
            "The view is handed the countdown as well as the place.");
    }

    [Test]
    public void Lure_RotsOnTime()
    {
        int id = _lures.Drop(At(5f, 5f), 0f, DecoySeconds);

        // Exactly three seconds. `>=` rather than `>`, so the decoy is gone on the instant its own
        // arithmetic says it is over rather than one tick later.
        _lures.Tick(DecoySeconds);

        Assert.That(_lures.Count, Is.Zero);
        Assert.That(_lures.TryGetLure(At(0f, 0f), out _), Is.False,
            "And the arena goes back to walking at the player.");

        Assert.That(_events.Single<DecoyExpired>().Id, Is.EqualTo(id));
    }

    // ---- Rules 3 and 4: which decoy, and how many ------------------------------------------------

    [Test]
    public void Lure_NearestWins()
    {
        _lures.Drop(At(0f, 0f), 0f, DecoySeconds);
        int second = _lures.Drop(At(20f, 0f), 0f, DecoySeconds);

        Assert.That(_lures.TryGetLure(At(18f, 0f), out Vector3 position), Is.True);
        Assert.That(position, Is.EqualTo(At(20f, 0f)), "Two metres beats eighteen.");

        Assert.That(second, Is.GreaterThan(0), "And it was a real drop, not a refusal.");
    }

    [Test]
    public void Lure_ATieGoesToTheOldest()
    {
        _lures.Drop(At(-4f, 0f), 0f, DecoySeconds);
        _lures.Drop(At(4f, 0f), 0f, DecoySeconds);

        // Dead centre. The rule is not about this frame — at a capacity of two a tie is nearly
        // unreachable — it is about two identical frames having to agree, which they cannot if the
        // answer depends on which decoy was dropped last (rule 3).
        Assert.That(_lures.TryGetLure(At(0f, 0f), out Vector3 position), Is.True);
        Assert.That(position, Is.EqualTo(At(-4f, 0f)), "The first dropped keeps the tie.");
    }

    [Test]
    public void Lure_RefusesAThirdSilently()
    {
        _lures.Drop(At(0f, 0f), 0f, DecoySeconds);
        _lures.Drop(At(1f, 0f), 0f, DecoySeconds);

        int refused = LureSystem.NoLure;

        Assert.That(() => refused = _lures.Drop(At(2f, 0f), 0f, DecoySeconds), Throws.Nothing,
            "One lost decoy is better than an exception that ends a run (rule 4).");

        Assert.That(refused, Is.EqualTo(LureSystem.NoLure));
        Assert.That(_lures.Count, Is.EqualTo(LureSystem.Capacity), "The two standing are kept.");
        Assert.That(_events.Count<DecoySpawned>(), Is.EqualTo(2), "And nothing was announced.");
    }

    // ---- AR §18.3: the door ----------------------------------------------------------------------

    [Test]
    public void Lure_RefusesNonFiniteInput()
    {
        Assert.That(
            () => _lures.Drop(new Vector3(float.NaN, 0f, 0f), 0f, DecoySeconds),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "Every distance test would answer nonsense about it.");

        Assert.That(
            () => _lures.Drop(At(0f, 0f), 0f, float.NaN),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "A decoy that never expires is a permanent taunt.");

        Assert.That(
            () => _lures.Drop(At(0f, 0f), 0f, 0f),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "And one that expires on the instant it is dropped is a cooldown spent on nothing.");

        Assert.That(
            () => _lures.Drop(At(0f, 0f), 0f, float.PositiveInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "Infinity passes a `> 0` test, which is why it is asked about separately.");

        Assert.That(
            () => _lures.Drop(At(0f, 0f), float.NaN, DecoySeconds),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "A non-finite clock is an expiry nothing can ever be compared against.");

        Assert.That(
            () => _lures.Tick(float.PositiveInfinity),
            Throws.TypeOf<ArgumentOutOfRangeException>());

        Assert.That(() => new LureSystem(null), Throws.TypeOf<ArgumentNullException>());

        Assert.That(_lures.Count, Is.Zero, "Nothing was dropped by a refusal.");
    }

    // ---- Rule 9: the two silent sweeps ----------------------------------------------------------

    [Test]
    public void Lure_ClearIsSilent()
    {
        _lures.Drop(At(0f, 0f), 0f, DecoySeconds);
        _lures.Drop(At(1f, 0f), 0f, DecoySeconds);

        _lures.Clear();

        Assert.That(_lures.Count, Is.Zero);
        Assert.That(_events.Count<DecoyExpired>(), Is.Zero,
            "The scope or the arena is going away and a farewell would reach nobody (rule 9).");

        // And the ids go back to 1 with the bodies, as EnemyRegistry.Clear does.
        Assert.That(_lures.Drop(At(0f, 0f), 0f, DecoySeconds), Is.EqualTo(1));
    }

    // ---- Rule 11: the per-frame cost -------------------------------------------------------------

    [Test]
    public void Lure_AllocatesNothing()
    {
        // Through the silent sink, because RecordingEvents boxes every payload it is handed — the
        // fake's allocation would be reported as this class's. See SilentEvents.
        var lures = new LureSystem(new SilentEvents());

        var now = 0f;

        AllocationAssert.None(() =>
        {
            now += 1f;

            lures.Drop(At(now, 0f), now, DecoySeconds);
            lures.TryGetLure(At(0f, 0f), out _);
            lures.Tick(now);
        });
    }

    // ---- Rule 5: the field is kind-conditional ---------------------------------------------------

    [Test]
    public void Spec_DecoyDurationIsKindConditional()
    {
        Assert.That(
            () => new MovementSkillSpec(
                MovementSkillKind.Charge, DashDistance, DashDuration, Cooldown, InputBuffer,
                20f, 5f, IFrameTrail, DecoySeconds),
            Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("decoyDuration"),
            "A Charge with a decoy duration is a forgotten field — nothing would read it.");

        Assert.That(
            () => new MovementSkillSpec(
                MovementSkillKind.Shroudstep, BlinkDistance, BlinkDuration, Cooldown, InputBuffer,
                0f, 0f, IFrameTrail, 0f),
            Throws.TypeOf<ArgumentOutOfRangeException>()
                .With.Property("ParamName").EqualTo("decoyDuration"),
            "And a Shroudstep without one is a Charge with a shorter distance (CH §3.2).");

        Assert.That(
            () => new MovementSkillSpec(
                MovementSkillKind.Shroudstep, BlinkDistance, BlinkDuration, Cooldown, InputBuffer,
                0f, 0f, IFrameTrail, float.NaN),
            Throws.TypeOf<ArgumentOutOfRangeException>(),
            "NaN fails `!(value > 0f)` with everything else (AR §18.3).");
    }

    [Test]
    public void Spec_TheShippedOathboundIsUnchanged()
    {
        // The eight-argument call every fixture in the suite makes, unedited. The ninth argument is
        // defaulted precisely so that this stays true and no shipped asset is rewritten (rule 5).
        var charge = new MovementSkillSpec(
            MovementSkillKind.Charge, DashDistance, DashDuration, Cooldown, InputBuffer,
            20f, 5f, IFrameTrail);

        Assert.That(charge.DecoyDuration, Is.Zero, "A Charge leaves nothing behind.");

        Assert.That(charge.Kind, Is.EqualTo(MovementSkillKind.Charge));
        Assert.That(charge.Distance, Is.EqualTo(DashDistance).Within(Tolerance));
        Assert.That(charge.Duration, Is.EqualTo(DashDuration).Within(Tolerance));
        Assert.That(charge.Cooldown, Is.EqualTo(Cooldown).Within(Tolerance));
        Assert.That(charge.InputBuffer, Is.EqualTo(InputBuffer).Within(Tolerance));
        Assert.That(charge.Damage, Is.EqualTo(20f).Within(Tolerance));
        Assert.That(charge.Knockback, Is.EqualTo(5f).Within(Tolerance));
        Assert.That(charge.IFrameTrail, Is.EqualTo(IFrameTrail).Within(Tolerance));
    }

    // ---- Rules 1 and 6: the payload on the start edge --------------------------------------------

    [Test]
    public void Skill_AShroudstepDropsADecoyWhereItLeft()
    {
        RunSession session = Session(Gravecaller());

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame, PlayerPosition = At(5f, 5f) };

        Blink(session, snapshot);

        DecoySpawned spawned = _events.Single<DecoySpawned>();

        Assert.That(spawned.Position, Is.EqualTo(At(5f, 5f)),
            "CH §3.2 leaves a corpse *behind*; at the destination it would taunt the arena "
                + "straight at the player and invert the mechanic (rule 6).");
        Assert.That(spawned.Duration, Is.EqualTo(DecoySeconds).Within(Tolerance));

        // And the blink is what moved, not the decoy: the body is told to travel six metres, and
        // nothing published afterwards revises where the corpse is.
        Assert.That(_intents.LastCharge.Distance, Is.EqualTo(BlinkDistance).Within(Tolerance));

        snapshot.PlayerPosition = At(11f, 5f);

        for (int i = 0; i < MaxFrames; i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(_events.Count<DecoySpawned>(), Is.EqualTo(1), "One blink, one corpse.");
        Assert.That(_events.Of<DecoySpawned>()[0].Position, Is.EqualTo(At(5f, 5f)),
            "Nothing about a decoy changes after it is dropped.");
    }

    [Test]
    public void Skill_AChargeDropsNothing()
    {
        RunSession session = Session(Oathbound());

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame, PlayerPosition = At(5f, 5f) };

        Blink(session, snapshot);

        Assert.That(_events.Count<DecoySpawned>(), Is.Zero,
            "The kind selects the payload and the Oathbound's has none (rule 5).");
    }

    [Test]
    public void Skill_TheBlinkStillDodges()
    {
        RunSession session = Session(Gravecaller());

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame, PlayerPosition = At(5f, 5f) };

        Blink(session, snapshot);

        // The whole of rule 1: a Shroudstep is a ChargeSkill with a different payload, so
        // everything a Charge emits it emits, in the same shapes.
        ChargeIntent intent = _intents.LastCharge;

        Assert.That(intent.Distance, Is.EqualTo(BlinkDistance).Within(Tolerance), "CH §3.2's 6 m.");
        Assert.That(intent.Duration, Is.EqualTo(BlinkDuration).Within(Tolerance), "A blink, not a dash.");

        Assert.That(_events.Count<ChargeStarted>(), Is.EqualTo(1),
            "The cue half of the pair, unmoved by the corpse.");
        Assert.That(_events.Single<ChargeStarted>().DirectionXZ, Is.EqualTo(intent.DirectionXZ),
            "One direction, two doors — the intent and the announcement agree.");

        // And the i-frames: the movement ends at 0.05 s and the protection 0.05 s after that, so
        // the one ChargeEnded a dodge gets arrives at 0.1 and not before.
        Assert.That(_events.Count<ChargeEnded>(), Is.Zero, "Not on the tick it started.");

        for (int i = 0; i < MaxFrames && _events.Count<ChargeEnded>() == 0; i++)
        {
            session.Tick(snapshot);
        }

        Assert.That(_events.Count<ChargeEnded>(), Is.EqualTo(1),
            $"The trail is {IFrameTrail} s past a {BlinkDuration} s blink and it does end.");
    }

    // ---- Helpers ---------------------------------------------------------------------------------

    /// <summary>A point on the ground plane. Y is a rendering detail here (AR §18.4).</summary>
    private static Vector3 At(float x, float z) => new(x, 0f, z);

    /// <summary>
    /// Asks for the movement skill and ticks until it fires, leaving the session on the tick the
    /// blink began.
    /// </summary>
    /// <remarks>
    /// Through <c>IPlayerCommands</c>, which is the port a thumb arrives on: the request is
    /// buffered and acted on by the next <c>ChargeSkill.Tick</c>, so this is one press and a
    /// handful of frames rather than a poke at state no view can reach.
    /// </remarks>
    private void Blink(RunSession session, WorldSnapshot snapshot)
    {
        session.MovementSkill();

        for (int i = 0; i < MaxFrames; i++)
        {
            session.Tick(snapshot);

            if (_intents.Charges.Count > 0)
            {
                return;
            }
        }

        Assert.Fail($"No dash within {MaxFrames} ticks. The movement skill never fired.");
    }

    /// <summary>A run of <paramref name="character"/>, started and standing in an empty arena.</summary>
    private RunSession Session(CharacterSpec character)
    {
        var catalog = new ContentCatalog(
            new[] { character },
            Array.Empty<EnemySpec>(),
            new[] { Descent() },
            Array.Empty<SkillSpec>(),
            Array.Empty<SkillTreeSpec>(),
            Array.Empty<BossSpec>());

        var session = new RunSession(
            catalog,
            new FixedRandom(Seed),
            _events,
            _intents,
            new RunRecorder(new FixedRandom(Seed), new FixedClock(default), _events),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            character.Id,
            Seed,
            1,
            new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
            restore: null));

        // Cleared after Start, so a row counting DecoySpawned or ChargeStarted is counting its own
        // press rather than the run's opening announcements.
        _events.Clear();
        _intents.Clear();

        return session;
    }

    /// <summary>
    /// The Gravecaller as M5-02 authored it, with M5-03's decoy duration on its Shroudstep.
    /// </summary>
    /// <remarks>
    /// <b>No Aegis and the Focus ramp switched off</b>, for <c>PlayerProjectileTests</c>' reason:
    /// CH §4 gives the class neither a shield nor anything these rows are about, and a ramp
    /// climbing under rows that stand perfectly still would move numbers nothing here asserts.
    /// </remarks>
    private static CharacterSpec Gravecaller() => new(
        new ContentId(GravecallerId),
        new LocKey("character.gravecaller.name"),
        PlayerMaxHp,
        new MovementSpec(3.1f, 0.06f, 0.08f, 720f),
        new TargetingSpec(BoltRange, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(
            WeaponKind.Projectile, BoltDamage, BoltsPerSecond, BoltRange, 360f, 0.15f,
            BoltSpeed, BoltRadius),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Shroudstep, BlinkDistance, BlinkDuration, Cooldown, InputBuffer,
            0f, 0f, IFrameTrail, DecoySeconds),
        null,
        0.5f);

    /// <summary>The Oathbound of CC §7 — the one class in this build whose dash leaves nothing.</summary>
    private static CharacterSpec Oathbound() => new(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        PlayerMaxHp,
        new MovementSpec(5.4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, SwingDamage, SwingsPerSecond, ConeRange, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(
            MovementSkillKind.Charge, DashDistance, DashDuration, Cooldown, InputBuffer,
            20f, 5f, IFrameTrail),
        null,
        0.5f);

    /// <summary>
    /// Descent with an <b>empty roster</b>, for the reason every fixture that starts a run gives:
    /// <c>RunSession.Start</c> resolves every roster id before it announces a run, and nothing here
    /// spawns anything at all.
    /// </summary>
    private static ModeSpec Descent() => new(
        new ContentId(DescentId),
        new LocKey("mode.descent.name"),
        1,
        true,
        0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>());
}
