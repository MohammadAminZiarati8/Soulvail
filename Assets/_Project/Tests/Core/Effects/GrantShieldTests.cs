using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Effects;

/// <summary>
/// CC §6.4's Bulwark: the primitive, the handler that grants and takes back, and the cast driven by
/// a bolt already in the air.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <c>Health</c> and a real <c>EffectRegistry</c> throughout</b> —
/// <c>SkillRunnerTests</c>' shape and its reason: a fixture handler would agree with whatever the
/// caller did, and what these rows are about is which <em>object</em> ends up holding the points and
/// what comes off when it stops.
/// </para>
/// <para>
/// <b>The ordering rows and the Bulwark rows drive a whole <c>RunSession</c> with a Spitter in the
/// arena</b>, because there is no other way to observe where in a tick an expiry runs. They live here
/// rather than in <c>TimedEffectsTests</c> because that fixture is a bare clock with a recording
/// handler and this one is the session; duplicating two hundred lines of arena into it to hold two
/// rows would be the worse trade.
/// </para>
/// <para>
/// <b>Every <c>Bulwark_*</c> row builds its own <c>SkillSpec</c>, and that is not a shortcut.</b>
/// <c>Bulwark.asset</c> is M3-12c's and nothing ships here, so there is no authored node in this
/// build to cast — the sixth consecutive task whose feature waits on M3-12.
/// </para>
/// <para>
/// <b>The two frame-order rows are proved M3-06's way: each is RED under its own swap and GREEN
/// under the other's.</b> A row asserting merely that both things happened passes against the wrong
/// order, so each one measures its own timings first and then arranges the coincidence it needs —
/// an expiry falling due on exactly the tick a bolt lands, and an expiry falling due on exactly the
/// tick a skill recasts. Half a frame of margin on either side, because a deadline computed to land
/// *on* an accumulated float is a coin toss.
/// </para>
/// </remarks>
[TestFixture]
public sealed class GrantShieldTests
{
    private const string OathboundId = "character.oathbound";
    private const string SpitterId = "enemy.spitter";
    private const string ModeId = "mode.test";
    private const string TreeId = "tree.oathbound";

    private const string ArenaOne = "arena.pillars";
    private const string ArenaTwo = "arena.tiered";

    private const string BulwarkId = "skill.test.bulwark";
    private const string PassiveB = "skill.test.passive";
    private const string PassiveC = "skill.test.c";

    /// <summary>CC §6.4's grant, and the number every row below counts in.</summary>
    private const float Points = 35f;

    /// <summary>CC §6.4's duration. Long enough that nothing expires unless a row means it to.</summary>
    private const float Seconds = 5f;

    /// <summary>
    /// A sandbag, and <c>SkillRunnerTests</c>' 140 would be the wrong number here.
    /// </summary>
    /// <remarks>
    /// <c>Bulwark_RefiresAfterItsCooldown</c> has to stand in the open for the whole of an
    /// eight-second cooldown, and the arena is not one Spitter for long: the director keeps
    /// composing waves, every body the fixture reports back is held in firing position, and the
    /// damage compounds. At 140 the run ends on the third bolt and at 900 it ends at about six
    /// seconds — both times the row fails with <em>"no run is running"</em>, which is a row about
    /// dying wearing the name of a row about a cooldown. Nothing here reads hit points as a
    /// fraction: every trigger in this fixture is either always true or never true, and the rows
    /// that measure a bolt measure the bolt.
    /// </remarks>
    private const float MaxHp = 100_000f;
    private const float WeaponDamage = 13f;
    private const float MoveSpeed = 3f;

    private const int EnemyCapacity = 32;
    private const int ProjectileCapacity = 8;
    private const int DeviceCap = 28;

    private const float Frame = 1f / 60f;

    /// <summary>Comfortably outside the director's minimum player distance.</summary>
    private const float Ring = 12f;

    /// <summary>The Spitter's standoff range (GD §8.1) — where the fixture holds every body.</summary>
    private const float StandoffRange = 14f;

    private static readonly Vector3 Standoff = new Vector3(0f, 0f, StandoffRange);

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private PlayerCombat _combat;
    private EffectRegistry _registry;
    private SimulatedClock _clock;
    private TimedEffects _timed;
    private GrantShieldHandler _handler;

    private RunSession _session;
    private FixedRandom _random;

    /// <summary>Every body the director has spawned, so the fixture can report it back each tick.</summary>
    private List<int> _alive;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();
        _combat = new PlayerCombat(Character(), _events, _intents, EnemyCapacity);
        _registry = new EffectRegistry();
        _clock = new SimulatedClock();
        _timed = new TimedEffects(_registry);
        _handler = new GrantShieldHandler(_combat.Health, _timed, _clock, _events);

        _registry.Register<GrantShield>(_handler);
    }

    // ---- The primitive (rule 5) ------------------------------------------------------------------

    [Test]
    public void Effect_Guards()
    {
        // Both doors, all four ways each. Zero is refused as well as negative, which is the one
        // asymmetry with Health.GrantShield worth saying out loud: zero points is a state the pool
        // *reaches* by being spent, and a state nobody should be able to *author*.
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantShield(0f, Seconds));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantShield(-1f, Seconds));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantShield(float.NaN, Seconds));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GrantShield(float.PositiveInfinity, Seconds));

        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantShield(Points, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantShield(Points, -1f));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GrantShield(Points, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new GrantShield(Points, float.PositiveInfinity));

        // And the legal one really is legal, so the row is not eight throws against a constructor
        // that refuses everything.
        var effect = new GrantShield(Points, Seconds);

        Assert.That(effect.Amount, Is.EqualTo(Points));
        Assert.That(effect.Duration, Is.EqualTo(Seconds));
    }

    // ---- The handler (rules 6, 8) ----------------------------------------------------------------

    [Test]
    public void Handler_ApplyGrantsAndHolds()
    {
        var effect = new GrantShield(Points, Seconds);
        var source = new object();

        _clock.Now = 12f;

        _registry.Apply(effect, source);

        Assert.That(_combat.Health.GrantedShield, Is.EqualTo(Points), "The points are on.");
        Assert.That(_timed.Count, Is.EqualTo(1), "And something is booked to take them off.");

        ShieldGranted granted = _events.Single<ShieldGranted>();

        Assert.That(granted.Amount, Is.EqualTo(Points));
        Assert.That(granted.Total, Is.EqualTo(Points), "The new total, so a view needs no read.");
        Assert.That(granted.Duration, Is.EqualTo(Seconds), "And the countdown to start.");

        // The deadline is now + duration against the *simulated* clock, which is the correction this
        // task was written around: read off a wall clock it would drain through a level-up screen at
        // timeScale 0. Asserted by ticking rather than by reading a field, because the deadline is
        // this class's business and the behaviour is everyone's.
        _timed.Tick(16.9f);

        Assert.That(_combat.Health.GrantedShield, Is.EqualTo(Points), "Not due at 4.9 s.");

        _timed.Tick(17f);

        Assert.That(_combat.Health.GrantedShield, Is.Zero, "Due at exactly 12 + 5.");
    }

    [Test]
    public void Handler_RemoveTakesItBack()
    {
        var effect = new GrantShield(Points, Seconds);
        var source = new object();

        _registry.Apply(effect, source);

        _events.Clear();

        _registry.Remove(effect, source);

        Assert.That(_combat.Health.GrantedShield, Is.Zero);

        ShieldGrantExpired expired = _events.Single<ShieldGrantExpired>();

        Assert.That(expired.Removed, Is.EqualTo(Points));
        Assert.That(expired.Total, Is.Zero);

        // A source holding nothing is not an error and says nothing — Stat.RemoveAll's contract, and
        // what lets the clock call this unconditionally.
        _events.Clear();

        Assert.That(() => _registry.Remove(effect, new object()), Throws.Nothing);
        Assert.That(_events.Count<ShieldGrantExpired>(), Is.Zero, "And it is silent about it.");
    }

    [Test]
    public void Handler_ExpiryReportsWhatIsLeft()
    {
        // Two grants, and the spend order is the *pool's* — grant order, pinned by
        // Health_SpendsTheOldestGrantFirst — not this clock's. A is granted first, so A pays for the
        // hit; what the expiry reports has to be what is left of A, and what remains has to be B's
        // twenty rather than zero.
        var first = new GrantShield(Points, Seconds);
        var second = new GrantShield(20f, 9f);

        var a = new object();
        var b = new object();

        _registry.Apply(first, a);
        _registry.Apply(second, b);

        Assert.That(_combat.Health.GrantedShield, Is.EqualTo(55f), "Two sources stack.");

        _combat.ApplyDamage(20f, now: 0f);

        _events.Clear();

        _timed.Tick(Seconds);

        ShieldGrantExpired expired = _events.Single<ShieldGrantExpired>();

        Assert.That(
            expired.Removed,
            Is.EqualTo(15f),
            "What remained of A, not what A was granted — a 35-point shield that already absorbed "
                + "20 takes 15 away with it, and removing 35 is how a pool goes negative.");

        Assert.That(
            expired.Total,
            Is.EqualTo(20f),
            "And what is left is B's grant. A view that read this as zero would erase points the "
                + "player still has.");

        Assert.That(_combat.Health.GrantedShield, Is.EqualTo(20f));
    }

    [Test]
    public void Handler_TakesNoWallClock()
    {
        // **The spec's `Handler_TakesNoClock`, renamed to say what it can still claim.** The handler
        // does take a clock — it has to, because Apply is handed no time — but it must never be the
        // wall-clock port: IClock is one member, DateTimeOffset UtcNow, and AR §18.2 says there is
        // no IClock in the session. A shield deadline taken from it would drain through a level-up
        // screen at timeScale 0 and through a backgrounded app.
        //
        // Reflection, and therefore a *test* rather than a RunCommand probe: a RunCommand refuses
        // the whole System.Reflection namespace before it executes anything.
        ConstructorInfo[] constructors = typeof(GrantShieldHandler).GetConstructors();

        Assert.That(constructors, Has.Length.EqualTo(1), "One way in.");

        ParameterInfo[] parameters = constructors[0].GetParameters();

        var takesSimulated = false;

        for (int i = 0; i < parameters.Length; i++)
        {
            Assert.That(
                parameters[i].ParameterType,
                Is.Not.EqualTo(typeof(IClock)),
                $"'{parameters[i].Name}' is the wall clock. Rule 2 and correction 2: the deadline "
                    + "is simulated seconds, because a shield is simulation.");

            takesSimulated |= parameters[i].ParameterType == typeof(SimulatedClock);
        }

        Assert.That(
            takesSimulated,
            Is.True,
            "And it says which clock it does take, so the row cannot pass against a handler that "
                + "quietly went back to reading a field of its own.");
    }

    [Test]
    public void Handler_Guards()
    {
        Assert.Throws<ArgumentNullException>(
            () => new GrantShieldHandler(null, _timed, _clock, _events));
        Assert.Throws<ArgumentNullException>(
            () => new GrantShieldHandler(_combat.Health, null, _clock, _events));
        Assert.Throws<ArgumentNullException>(
            () => new GrantShieldHandler(_combat.Health, _timed, null, _events));
        Assert.Throws<ArgumentNullException>(
            () => new GrantShieldHandler(_combat.Health, _timed, _clock, null));

        Assert.Throws<ArgumentNullException>(() => _handler.Apply(null, new object()));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(null, new object()));

        // A sourceless grant could never be taken back, which is Modifier's rule two layers down.
        Assert.Throws<ArgumentNullException>(
            () => _handler.Apply(new GrantShield(Points, Seconds), null));
        Assert.Throws<ArgumentNullException>(
            () => _handler.Remove(new GrantShield(Points, Seconds), null));

        // And the failed apply left nothing behind: the hold is taken *before* the grant precisely
        // so that the only failure either call has cannot leave a shield standing with nothing
        // booked to take it back.
        Assert.That(_combat.Health.GrantedShield, Is.Zero);
        Assert.That(_timed.Count, Is.Zero);
    }

    [Test]
    public void Cast_AllocatesNothing()
    {
        // A silent IDomainEvents rather than RecordingEvents, which stores each payload in a
        // List<object> and would box every struct — the row would be measuring the fake (Traps §7).
        var silent = new SilentEvents();
        var registry = new EffectRegistry();
        var timed = new TimedEffects(registry);
        var clock = new SimulatedClock();
        var combat = new PlayerCombat(Character(), silent, _intents, EnemyCapacity);

        registry.Register<GrantShield>(
            new GrantShieldHandler(combat.Health, timed, clock, silent));

        var runner = new SkillRunner(registry, combat.Blackboard, silent);

        runner.Add(Bulwark(cooldown: 0.5f, Never(), Points, Seconds));

        float now = 0f;

        // **Cast *and expire*, which is the shape M3-06's own allocation row stood in for before
        // this clock existed.** Without the expiry the row would measure a table filling up sixteen
        // deep and then throwing, rather than the steady state a fight actually holds.
        AllocationAssert.None(() =>
        {
            now += 1f;

            clock.Now = now;

            runner.Cast(0, now, auto: true);

            timed.Tick(now + Seconds);
        });

        // The probe is live rather than measuring a no-op: every iteration really did cast and
        // really did expire.
        Assert.That(now, Is.GreaterThan(10_000f));
        Assert.That(timed.Count, Is.Zero);
        Assert.That(combat.Health.GrantedShield, Is.Zero);
    }

    // ---- Over a live run (rules 3, 5) ------------------------------------------------------------

    [Test]
    public void Handler_ExpiryRunsThroughTheRegistry()
    {
        // No enemies at all, and a trigger that is true on the first tick: what is being measured is
        // the clock, and a bolt would be an uncontrolled second thing spending the shield.
        StartSession(Always(), cooldown: 100f, withSpitter: false);

        TickUntil(() => _events.Count<ShieldGranted>() > 0);

        float castAt = _session.State.Time;

        Assert.That(
            _session.State.PlayerGrantedShield,
            Is.EqualTo(Points),
            "A live run put a grant on the player for the first time in the project.");

        int expiry = TickUntil(() => _events.Count<ShieldGrantExpired>() > 0);

        Assert.That(expiry, Is.GreaterThan(0), "The grant never came off.");

        // **The first production call of EffectRegistry.Remove in a live run** (M3-05 rule 10's
        // other half): nothing in RunSession knows what a granted shield is, and the points come off
        // because the clock handed the pair back to the handler that applied them.
        ShieldGrantExpired expired = _events.Single<ShieldGrantExpired>();

        Assert.That(expired.Removed, Is.EqualTo(Points));
        Assert.That(expired.Total, Is.Zero);
        Assert.That(_session.State.PlayerGrantedShield, Is.Zero);

        // Five seconds, on the run's own clock, within the frame the deadline fell in.
        Assert.That(
            _session.State.Time - castAt,
            Is.EqualTo(Seconds).Within(Frame),
            "It expired on the second it was granted for, not on a frame count.");

        // And exactly once: a clock that forgot to drop the entry would take the same grant back
        // every tick for the rest of the run.
        Assert.That(
            TickUntil(() => _events.Count<ShieldGrantExpired>() > 0),
            Is.EqualTo(-1),
            "No second expiry in three thousand ticks.");
    }

    [Test]
    public void Timed_TicksBeforeTheProjectileStep()
    {
        // **Rule 3's second half, and it is arranged rather than hoped for.** A grant that is still
        // due to expire on the tick a bolt lands must come off *before* the bolt is resolved: a
        // shield that expired after this tick's arrivals would have absorbed a hit it was no longer
        // entitled to.
        //
        // Pass one measures the two moments with a duration long enough that nothing expires, and
        // records what a bolt costs with 35 points standing. Pass two sets the duration so the
        // deadline falls half a frame before the landing tick — due on exactly that tick, with
        // margin on both sides of it.
        StartSession(AtLeast(TriggerField.IncomingProjectiles, 1f), cooldown: 100f, duration: 1_000f);

        TickUntil(() => _events.Count<ShieldGranted>() > 0);

        float castAt = _session.State.Time;

        int landing = TickUntil(() => _events.Count<PlayerDamaged>() > 0);

        Assert.That(landing, Is.GreaterThan(0), "The fixture failed to land a bolt on the player.");

        float landAt = _session.State.Time;
        float absorbed = _events.Single<PlayerDamaged>().ToHp;

        Assert.That(
            _session.State.PlayerGrantedShield,
            Is.Zero,
            "The fixture's own claim, out loud: one bolt is bigger than the whole grant, so it "
                + "spends all 35 and spills the rest — which is what gives the row a PlayerDamaged "
                + "to be after at all.");

        StartSession(
            AtLeast(TriggerField.IncomingProjectiles, 1f),
            cooldown: 100f,
            duration: landAt - castAt - (Frame / 2f));

        TickUntil(() => _events.Count<ShieldGranted>() > 0);

        landing = TickUntil(() => _events.Count<PlayerDamaged>() > 0);

        Assert.That(landing, Is.GreaterThan(0), "The second pass failed to land the same bolt.");

        int expiredAt = IndexOfFirst<ShieldGrantExpired>();
        int damagedAt = IndexOfFirst<PlayerDamaged>();

        Assert.That(
            expiredAt,
            Is.GreaterThanOrEqualTo(0),
            "The grant fell due on the landing tick, which is the coincidence this row is built on.");

        Assert.That(
            expiredAt,
            Is.LessThan(damagedAt),
            "And it came off before the bolt was resolved, in the order of one tick's events.");

        Assert.That(
            _events.Single<ShieldGrantExpired>().Removed,
            Is.EqualTo(Points),
            "With all 35 still on it: the bolt had not reached it yet. Ticked below the projectile "
                + "step this reads zero, because the hit would have spent the grant first.");

        Assert.That(
            _events.Single<PlayerDamaged>().ToHp,
            Is.EqualTo(absorbed + Points).Within(0.001f),
            "So the player took the whole bolt rather than 35 less of it — the same bolt, measured "
                + "twice, with the only difference being which side of the arrival the expiry fell.");
    }

    [Test]
    public void Timed_TicksAfterTheRunner()
    {
        // **Rule 3's first half, and the same technique.** The runner may cast on the tick a grant
        // falls due, and the expiry has to be resolved *after* that cast: expiring first would take
        // the old grant back and hand the same one straight over again, announcing an end that never
        // happened and, for one instant between the two, leaving the player with no shield at all.
        //
        // Pass one measures the two casts a half-second cooldown produces. Pass two sets the
        // duration so the first grant falls due half a frame before the second cast — on exactly
        // that tick.
        const float Cooldown = 0.5f;

        StartSession(Always(), cooldown: Cooldown, withSpitter: false, duration: 1_000f);

        TickUntil(() => _events.Count<SkillCast>() > 0);

        float first = _session.State.Time;

        TickUntil(() => _events.Count<SkillCast>() > 0);

        float second = _session.State.Time;

        Assert.That(
            second - first,
            Is.EqualTo(Cooldown).Within(Frame),
            "The fixture's own claim: it really does recast on its cooldown.");

        StartSession(
            Always(),
            cooldown: Cooldown,
            withSpitter: false,
            duration: second - first - (Frame / 2f));

        TickUntil(() => _events.Count<SkillCast>() > 0);

        TickUntil(() => _events.Count<SkillCast>() > 0);

        Assert.That(
            _events.Count<ShieldGranted>(),
            Is.EqualTo(1),
            "The recast tick granted, which is the coincidence this row is built on.");

        Assert.That(
            _events.Count<ShieldGrantExpired>(),
            Is.Zero,
            "And nothing expired on it. Ticked above the runner, the grant that was due would have "
                + "been taken back and re-granted in the same frame — an expiry event for a shield "
                + "the player never stopped having.");

        Assert.That(
            _session.State.PlayerGrantedShield,
            Is.EqualTo(Points),
            "The shield stood through the whole of it.");
    }

    [Test]
    public void Bulwark_CastsOnAnInboundBolt()
    {
        // **CC §6.4's trigger, end to end.** `IncomingProjectiles` is written by ProjectileSystem at
        // the end of its own step and nowhere else, so read above that step it counts bolts still in
        // the air — a shield raised *before* the bolt lands rather than over the wound (M3-06
        // rule 7). The cooldown is a hundredth of a second so the skill is ready on every tick a
        // bolt is inbound, which is what puts the cast and the landing in the same frame; a long one
        // would fire once, seventy ticks earlier, and the ordering assertion would be vacuously true
        // across two different ticks.
        float bare = BareBolt();

        StartSession(AtLeast(TriggerField.IncomingProjectiles, 1f), cooldown: 0.01f);

        int landing = TickUntil(() => _events.Count<PlayerDamaged>() > 0);

        Assert.That(landing, Is.GreaterThan(0), "The fixture failed to land a bolt on the player.");

        int grantedAt = IndexOfFirst<ShieldGranted>();
        int castAt = IndexOfFirst<SkillCast>();
        int damagedAt = IndexOfFirst<PlayerDamaged>();

        Assert.That(
            grantedAt,
            Is.GreaterThanOrEqualTo(0),
            "The runner saw a bolt in the air on the tick it landed. Below the projectile step it "
                + "would have seen zero, because that step had just emptied the sky.");

        // **The spec's row says "SkillCast then the grant" and the code is the other way round.**
        // SkillRunner.Fire applies the cast effects, starts the cooldown, and *then* announces the
        // cast (its rule 9), so a handler reading CooldownFraction from inside SkillCast sees 1 and
        // a view drawing a buff sees it already applied.
        Assert.That(
            grantedAt,
            Is.LessThan(castAt),
            "The effects go on before the cast is announced — SkillRunner rule 9, from the other "
                + "side of the seam.");

        Assert.That(
            castAt,
            Is.LessThan(damagedAt),
            "And both are before the bolt lands rather than over the wound — the whole of the "
                + "archetype's answer, in the order of one tick's events.");

        // And the shield actually paid, which is the half an event order cannot prove: the same
        // bolt, measured against the same fixture with the trigger switched off, costs 35 more.
        Assert.That(
            _events.Single<PlayerDamaged>().ToHp,
            Is.EqualTo(bare - Points).Within(0.001f),
            "35 points of the bolt went into the grant instead of into the player.");
    }

    [Test]
    public void Bulwark_DoesNotCastWithNothingInTheAir()
    {
        // The other half of rule 5, and the manual step nobody can run until M3-12: stand where no
        // Spitter can see you and nothing casts, for the whole stage. Here it is an arena with no
        // Spitter in it at all, which is the same sky.
        StartSession(AtLeast(TriggerField.IncomingProjectiles, 1f), cooldown: 0.01f, withSpitter: false);

        Assert.That(
            TickUntil(() => _events.Count<SkillCast>() > 0),
            Is.EqualTo(-1),
            "Nothing cast, for three thousand ticks — fifty seconds of an empty sky.");

        Assert.That(_events.Count<ShieldGranted>(), Is.Zero);
        Assert.That(_session.State.PlayerGrantedShield, Is.Zero);
    }

    [Test]
    public void Bulwark_RefiresAfterItsCooldown()
    {
        // CC §6.4's eight seconds: available a little under half the time, which is what stops a
        // 35-point shield from being a wall. The trigger stays true — bolts keep coming — so what
        // holds the second cast back is the cooldown and nothing else.
        const float Cooldown = 8f;

        StartSession(AtLeast(TriggerField.IncomingProjectiles, 1f), cooldown: Cooldown);

        TickUntil(() => _events.Count<SkillCast>() > 0);

        float first = _session.State.Time;

        var bolts = 0;

        int second = TickUntil(() =>
        {
            bolts += _events.Count<PlayerDamaged>();

            return _events.Count<SkillCast>() > 0;
        });

        Assert.That(second, Is.GreaterThan(0), "It never fired again.");

        Assert.That(
            _session.State.Time - first,
            Is.GreaterThanOrEqualTo(Cooldown),
            "Not at 7.9 s: the effective cooldown is the floor's business and the wait is real.");

        Assert.That(
            _session.State.Time - first,
            Is.LessThan(Cooldown + 3f),
            "And it went off on the first inbound bolt after that rather than at some gap of its "
                + "own. Three seconds of slack because the sky is not continuously full: a Spitter "
                + "at standoff winds up for 0.4 s and recovers for 0.6, so there are stretches with "
                + "nothing in the air and the trigger legitimately false.");

        Assert.That(
            bolts,
            Is.GreaterThan(0),
            "The fixture's own claim: a bolt landed during the wait, so the trigger was true and "
                + "the cooldown is what refused — not an empty sky.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// What one Spitter bolt costs a player with no grant standing — the control
    /// <see cref="Bulwark_CastsOnAnInboundBolt"/> measures its own bolt against.
    /// </summary>
    /// <remarks>
    /// A trigger that is never true rather than a session with no skill, so that everything else
    /// about the run — the seed, the roster, the arena, the volley — is the same sequence.
    /// </remarks>
    private float BareBolt()
    {
        StartSession(Never(), cooldown: 0.01f);

        int landing = TickUntil(() => _events.Count<PlayerDamaged>() > 0);

        Assert.That(landing, Is.GreaterThan(0), "The control failed to land a bolt.");
        Assert.That(_events.Count<SkillCast>(), Is.Zero, "And it cast nothing while doing it.");

        return _events.Single<PlayerDamaged>().ToHp;
    }

    private static ContentId Id(string value) => new ContentId(value);

    private static TriggerSpec Below(TriggerField field, float threshold) =>
        new TriggerSpec(new[] { new TriggerClause(field, TriggerComparison.Below, threshold) });

    private static TriggerSpec AtLeast(TriggerField field, float threshold) =>
        new TriggerSpec(new[] { new TriggerClause(field, TriggerComparison.AtLeast, threshold) });

    /// <summary>True on the first tick of any run: a full bar is below 110 %.</summary>
    private static TriggerSpec Always() => Below(TriggerField.HpFraction, 1.1f);

    /// <summary>True never: a living player's bar is not below nothing.</summary>
    private static TriggerSpec Never() => Below(TriggerField.HpFraction, 0f);

    /// <summary>An Active whose one cast effect is a grant — Bulwark, without the asset.</summary>
    private static SkillSpec Bulwark(
        float cooldown,
        TriggerSpec trigger,
        float amount,
        float duration) =>
        new SkillSpec(
            Id(BulwarkId),
            new LocKey($"{BulwarkId}.name"),
            new LocKey($"{BulwarkId}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(cooldown, trigger, new IEffect[] { new GrantShield(amount, duration) }));

    private static SkillSpec Passive(string id, float damagePercent) =>
        new SkillSpec(
            Id(id),
            new LocKey($"{id}.name"),
            new LocKey($"{id}.desc"),
            SkillKind.Passive,
            new IEffect[]
            {
                new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, damagePercent),
            });

    private static SkillBranchSpec Branch(char letter, params ContentId[] tier) =>
        new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { tier });

    /// <summary>CC §7's class at the owner's retuned numbers, and with no Aegis.</summary>
    /// <remarks>
    /// The missing shield is load-bearing: it is what makes one 70-point bolt bigger than a 35-point
    /// grant *and* than what is left of the player's own defences, so a fully absorbed hit — which
    /// publishes no event at all (M3-11a-i rule 8) — is never what these rows are asserting against.
    /// </remarks>
    private static CharacterSpec Character() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        MaxHp,
        new MovementSpec(MoveSpeed, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, WeaponDamage, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f));

    private static ModeSpec Mode(IReadOnlyList<RosterEntry> roster) => new ModeSpec(
        Id(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        roster,
        new[] { Id(ArenaOne), Id(ArenaTwo) });

    /// <summary>The Spitter, with a bolt big enough to outlast a 35-point grant.</summary>
    private static EnemySpec Spitter() => new EnemySpec(
        Id(SpitterId),
        new LocKey("enemy.spitter.name"),
        maxHp: 40f,
        moveSpeed: 2.4f,
        targetPriority: 1,
        threatCost: 6,
        xpValue: 18f,
        isElite: false,
        contactDamage: 70f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 40f,
        EnemyBehaviourKind.Spitter,
        new ProjectileSpec(StandoffRange, speed: 12f, radius: 0.4f));

    /// <summary>
    /// A live run owning one Bulwark, whose trigger, cooldown and duration are the row's.
    /// </summary>
    /// <param name="withSpitter">
    /// Whether the roster holds anything. False is an empty sky — the rows about the clock rather
    /// than about the bolt, where an uncontrolled hit would spend the shield being measured.
    /// </param>
    private void StartSession(
        TriggerSpec trigger,
        float cooldown,
        bool withSpitter = true,
        float duration = Seconds)
    {
        SkillSpec node = Bulwark(cooldown, trigger, Points, duration);

        var tree = new SkillTreeSpec(
            Id(TreeId),
            Id(OathboundId),
            new[]
            {
                Branch('a', node.Id),
                Branch('b', Id(PassiveB)),
                Branch('c', Id(PassiveC)),
            });

        var catalog = new ContentCatalog(
            new[] { Character() },
            withSpitter ? new[] { Spitter() } : Array.Empty<EnemySpec>(),
            new[]
            {
                Mode(withSpitter
                    ? new[] { new RosterEntry(Id(SpitterId), 1) }
                    : Array.Empty<RosterEntry>()),
            },
            new[] { node, Passive(PassiveB, 0.05f), Passive(PassiveC, 0.05f) },
            new[] { tree });

        _random = new FixedRandom(7, Alternating(8_192));
        _alive = new List<int>();

        // **The fixture plays SnapshotBuilder**, which is what the bolt rows cost: a Spitter will not
        // begin a wind-up it cannot see through (M2-11b), and `EnemySense.HasLineOfSight` is a
        // *sense* filled by a Unity adapter — false by default in a core-only test, so nothing ever
        // fires. Collecting the spawned ids here is the only way to report them back each tick.
        var reporting = new WatchingEvents(_events)
        {
            OnPublish = payload =>
            {
                if (payload is EnemySpawned spawned)
                {
                    _alive.Add(spawned.Id);
                }
            },
        };

        _session = new RunSession(
            catalog,
            _random,
            reporting,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), reporting),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        // Resumed rather than fresh, because that is the door RunSession.Start adds a restored Active
        // through (M3-06 rule 5) — and it is the only door there is until a tree ships. One level per
        // node and nothing owed, which is the arithmetic M3-08a rule 9 refuses a run for getting
        // wrong.
        _session.Start(new RunConfig(
            Id(ModeId),
            Id(OathboundId),
            _random.Seed,
            stageIndex: 1,
            SpawnPlan.Empty,
            new RunSnapshot(
                RunSnapshot.CurrentVersion,
                Id(ModeId),
                Id(OathboundId),
                _random.Seed,
                1,
                new RandomState(101, 102, 103, 104, 105),
                MaxHp,
                0f,
                0f,
                Instant,
                2,
                0f,
                0,
                new[] { node.Id },
                new ContentId[SkillRunner.MaxManualSlots])));

        Assert.That(_session.State.OwnedActiveCount, Is.EqualTo(1), "The fixture owns its Bulwark.");

        _events.Clear();
    }

    /// <summary>
    /// Ticks one frame at a time, clearing the recorder before each, and stops on the tick
    /// <paramref name="done"/> first answers true — so the recorder holds exactly that tick's events
    /// when it returns.
    /// </summary>
    /// <returns>How many ticks it took, or −1 if it never happened.</returns>
    private int TickUntil(Func<bool> done)
    {
        for (int i = 1; i <= 3_000; i++)
        {
            _events.Clear();

            TickOnce();

            if (done())
            {
                return i;
            }
        }

        return -1;
    }

    private void TickOnce()
    {
        var snapshot = new WorldSnapshot(EnemyCapacity)
        {
            Dt = Frame,
            PlayerPosition = Vector3.Zero,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points8(),
        };

        // Every live body reported back at exactly the Spitter's standoff range, with line of sight.
        // Held there rather than integrated from its move intents, which is the shortest honest way
        // to get a bolt into the air: at standoff it has nowhere to back off to, so it winds up and
        // releases instead of repositioning for ever. It is also outside the class's 12 m acquire
        // range, so the player never swings and nothing here ever dies.
        for (int i = 0; i < _alive.Count; i++)
        {
            ref EnemySense sense = ref snapshot.AddEnemy();

            sense.Id = _alive[i];
            sense.Position = Standoff;
            sense.Velocity = Vector3.Zero;
            sense.PathDirectionToPlayer = new Vector2(0f, -1f);
            sense.HasLineOfSight = true;
        }

        _session.Tick(snapshot);
    }

    /// <summary>Where the first <typeparamref name="T"/> sits in this tick's event order.</summary>
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

    /// <summary>Eight points on a ring, clear of the origin and of each other.</summary>
    private static IReadOnlyList<Vector3> Points8()
    {
        var points = new Vector3[8];

        for (int i = 0; i < points.Length; i++)
        {
            double angle = 2d * Math.PI * i / points.Length;

            points[i] = new Vector3(
                (float)(Ring * Math.Cos(angle)),
                0f,
                (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    private static float[] Alternating(int count)
    {
        var values = new float[count];

        for (int i = 0; i < count; i++)
        {
            values[i] = i % 2 == 0 ? 0.1f : 0.9f;
        }

        return values;
    }

    /// <summary>
    /// An <see cref="IDomainEvents"/> that hands each payload to a callback as it is published, so
    /// the fixture can collect spawned ids without subscribing to anything.
    /// </summary>
    private sealed class WatchingEvents : IDomainEvents
    {
        private readonly RecordingEvents _log;

        internal WatchingEvents(RecordingEvents log)
        {
            _log = log;
        }

        public Action<object> OnPublish { get; set; }

        public void Publish<T>(in T evt)
            where T : struct
        {
            _log?.Publish(in evt);

            OnPublish?.Invoke(evt);
        }
    }
}
