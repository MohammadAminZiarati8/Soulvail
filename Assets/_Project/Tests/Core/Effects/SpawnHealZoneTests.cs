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
/// CC §6.4's Consecrate: the primitive, the handler that puts a zone under the caster, and the cast
/// driven by a health bar that has dropped below 60 %.
/// </summary>
/// <remarks>
/// <para>
/// <b>Against a real <c>ZoneSystem</c> and a real <c>EffectRegistry</c> throughout</b>, and against a
/// whole <c>RunSession</c> for the six rows that cannot be observed anywhere else: where in a tick a
/// zone is ticked, whether a cast reads this frame's position, and what six seconds of standing still
/// is actually worth. The rows about the zone itself are in <c>ZoneSystemTests</c>, which needs no
/// session at all.
/// </para>
/// <para>
/// <b>The class carries CC §7's 140 hit points, and that is the deliberate opposite of
/// <c>GrantShieldTests</c>' hundred-thousand sandbag.</b> That fixture holds a Spitter in firing
/// position for the length of an eight-second cooldown and would otherwise end its run mid-row; every
/// row here reads hit points as a <em>fraction</em> of 140 — the 60 % trigger, the quarter bar, the
/// heal that never happens — so a sandbag would destroy each of them. <b>Nothing in this fixture can
/// hurt the player</b>: the mode's roster is empty, no enemy ever spawns, and the only way hit points
/// move is the zone.
/// </para>
/// <para>
/// <b>The player's hit points come from the resumed snapshot, because there is no other door.</b>
/// <c>RunState.PlayerHpFraction</c> is a read and <c>RunState.Combat</c> is <c>internal</c>, so a
/// live-session row cannot damage the player except with an enemy — and an enemy would put an
/// uncontrolled second thing into every number below. A run resumed at 83 of 140 is the cleanest
/// statement of "below 60 %" there is.
/// </para>
/// <para>
/// <b>Every row builds its own <c>SkillSpec</c>, and that is not a shortcut.</b>
/// <c>Consecrate.asset</c> is M3-12c's and nothing ships here, so there is no authored node in this
/// build to cast — the seventh consecutive task whose feature waits on M3-12.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SpawnHealZoneTests
{
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";
    private const string TreeId = "tree.oathbound";

    private const string ConsecrateId = "skill.test.consecrate";
    private const string BulwarkId = "skill.test.bulwark";
    private const string PassiveId = "skill.test.passive";

    private const string ArenaOne = "arena.pillars";
    private const string ArenaTwo = "arena.tiered";

    // CC §6.4's Consecrate and CC §7's class.
    private const float Radius = 3.5f;
    private const float Duration = 6f;
    private const float Heal = 3f;
    private const float Interval = 0.5f;
    private const float Cooldown = 12f;

    private const float MaxHp = 140f;
    private const float ShieldMax = 30f;
    private const float ShieldDelay = 4f;
    private const float ShieldRefill = 15f;
    private const float HitIFrames = 0.5f;

    /// <summary>Twelve pulses of 3 — about a quarter of a 140-point bar.</summary>
    private const float WholeZone = 36f;

    /// <summary>CC §6.4's Bulwark, for the one row that needs both actives at once.</summary>
    private const float Points = 35f;

    private const int EnemyCapacity = 32;
    private const int ProjectileCapacity = 8;
    private const int DeviceCap = 28;

    private const float Frame = 1f / 60f;

    private const float Tolerance = 1e-3f;

    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 17, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private CombatBlackboard _blackboard;
    private Health _health;
    private ZoneSystem _zones;
    private EffectRegistry _registry;
    private SimulatedClock _clock;
    private SpawnHealZoneHandler _handler;

    private RunSession _session;
    private FixedRandom _random;

    /// <summary>Where the fixture reports the player each tick, and whether it walks them.</summary>
    private Vector3 _playerPosition;

    private Vector3 _stepPerTick;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _blackboard = new CombatBlackboard();
        _health = new Health(new Stat(MaxHp), null, 0f);
        _zones = new ZoneSystem(_health, _blackboard, _events);
        _registry = new EffectRegistry();
        _clock = new SimulatedClock();
        _handler = new SpawnHealZoneHandler(_zones, _clock);

        _registry.Register<SpawnHealZone>(_handler);

        _playerPosition = Vector3.Zero;
        _stepPerTick = Vector3.Zero;
    }

    // ---- The primitive --------------------------------------------------------------------------

    [Test]
    public void Effect_Guards()
    {
        // All four doors, all four ways each. NaN is the one a `<= 0` test would wave through
        // (AR §18.3), and it is the one whose failure is silent: a zone that stands there healing
        // nobody, for ever, with nothing logged.
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SpawnHealZone(bad, Duration, Heal, Interval), $"radius {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SpawnHealZone(Radius, bad, Heal, Interval), $"duration {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SpawnHealZone(Radius, Duration, bad, Interval), $"healPerPulse {bad}");
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new SpawnHealZone(Radius, Duration, Heal, bad), $"pulseInterval {bad}");
        }

        // And the door ZoneSystem's catch-up loop depends on: a six-second zone pulsing every
        // microsecond is not a fast zone, it is six million heals inside one frame.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SpawnHealZone(Radius, Duration, Heal, 1e-6f));

        // The legal one really is legal, so the row is not seventeen throws against a constructor
        // that refuses everything.
        var effect = new SpawnHealZone(Radius, Duration, Heal, Interval);

        Assert.That(effect.Radius, Is.EqualTo(Radius));
        Assert.That(effect.Duration, Is.EqualTo(Duration));
        Assert.That(effect.HealPerPulse, Is.EqualTo(Heal));
        Assert.That(effect.PulseInterval, Is.EqualTo(Interval));
    }

    // ---- The handler ----------------------------------------------------------------------------

    [Test]
    public void Handler_ApplySpawns()
    {
        var effect = new SpawnHealZone(Radius, Duration, Heal, Interval);
        var source = new object();

        _blackboard.PlayerPosition = new Vector3(2f, 0f, 7f);
        _clock.Now = 12f;

        _registry.Apply(effect, source);

        Assert.That(_zones.Count, Is.EqualTo(1));
        Assert.That(_zones.PositionAt(0), Is.EqualTo(new Vector3(2f, 0f, 7f)), "Under the caster.");
        Assert.That(_zones.RadiusAt(0), Is.EqualTo(Radius));

        ZoneSpawned spawned = _events.Single<ZoneSpawned>();

        Assert.That(spawned.Radius, Is.EqualTo(Radius));
        Assert.That(spawned.Duration, Is.EqualTo(Duration));

        // The four numbers reach the zone rather than only the two the event carries, and the clock
        // they are scheduled against is the *simulated* one: the first pulse is due at 12.5 and the
        // zone is over at 18, both measured from the clock the handler read rather than from zero.
        _health.ApplyDamage(40f, 0f);

        _zones.Tick(12.49f);

        Assert.That(_health.Current, Is.EqualTo(100f).Within(Tolerance), "Not due at 12.49.");

        _zones.Tick(12.5f);

        Assert.That(_health.Current, Is.EqualTo(103f).Within(Tolerance), "One pulse of three.");

        _zones.Tick(18f);

        Assert.That(_zones.Count, Is.Zero, "And over six seconds after it was cast.");
    }

    [Test]
    public void Handler_RemoveDoesNothing()
    {
        // **The deliberate contrast with GrantShield** (rule 9): a granted shield is state on the
        // player and has to be taken back, a zone is a thing in the world that ends when it ends.
        // Nothing holds a zone in TimedEffects, so nothing ever calls this — the row pins a
        // *contract* rather than a path, and the contract is IEffectHandler<T>.Remove's own: a source
        // holding nothing is not an error.
        var effect = new SpawnHealZone(Radius, Duration, Heal, Interval);
        var source = new object();

        _registry.Apply(effect, source);

        _events.Clear();

        Assert.That(() => _registry.Remove(effect, source), Throws.Nothing);

        Assert.That(_zones.Count, Is.EqualTo(1), "Still standing: the cast does not own its life.");
        Assert.That(_events.Count<ZoneExpired>(), Is.Zero, "And nothing was announced.");

        // A source that never placed one is the same silence, which is what would let a cleanse call
        // it unconditionally the day one exists.
        Assert.That(() => _registry.Remove(effect, new object()), Throws.Nothing);
        Assert.That(_zones.Count, Is.EqualTo(1));
    }

    [Test]
    public void Handler_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new SpawnHealZoneHandler(null, _clock));
        Assert.Throws<ArgumentNullException>(() => new SpawnHealZoneHandler(_zones, null));

        Assert.Throws<ArgumentNullException>(() => _handler.Apply(null, new object()));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(null, new object()));

        var effect = new SpawnHealZone(Radius, Duration, Heal, Interval);

        // A zone with no owner is one nothing could ever dismiss, and both doors answer the same way
        // — even Remove, which would not have read it.
        Assert.Throws<ArgumentNullException>(() => _handler.Apply(effect, null));
        Assert.Throws<ArgumentNullException>(() => _handler.Remove(effect, null));

        Assert.That(_zones.Count, Is.Zero, "And no refusal left a zone behind.");
    }

    [Test]
    public void Handler_TakesNoWallClock()
    {
        // **The spec named `IClock` for the second time in two tasks, and it is wrong the same way.**
        // IClock is one member, DateTimeOffset UtcNow, and AR §18.2 says there is no IClock in the
        // session: a zone timed off a wall clock would burn down through a level-up screen at
        // timeScale 0 and through a backgrounded app. The handler does take a clock — Apply is handed
        // no time — and what it must never take is that one.
        //
        // Reflection, and therefore a *test* rather than a RunCommand probe: a RunCommand refuses the
        // whole System.Reflection namespace before it executes anything.
        ConstructorInfo[] constructors = typeof(SpawnHealZoneHandler).GetConstructors();

        Assert.That(constructors, Has.Length.EqualTo(1), "One way in.");

        ParameterInfo[] parameters = constructors[0].GetParameters();

        var takesSimulated = false;

        for (int i = 0; i < parameters.Length; i++)
        {
            Assert.That(
                parameters[i].ParameterType,
                Is.Not.EqualTo(typeof(IClock)),
                $"'{parameters[i].Name}' is the wall clock. A zone is simulation, like a shield.");

            takesSimulated |= parameters[i].ParameterType == typeof(SimulatedClock);
        }

        Assert.That(
            takesSimulated,
            Is.True,
            "And it says which clock it does take, so the row cannot pass against a handler that "
                + "went back to remembering a `now` of its own — which for this class would be last "
                + "frame's, because ZoneSystem ticks after the caster.");
    }

    // ---- Over a live run ------------------------------------------------------------------------

    [Test]
    public void Blackboard_PositionIsFreshAtCastTime()
    {
        // **Rules 2 and 3 together, and the reason the position is on the blackboard at all.** The
        // player is walked ten metres every tick, so a zone placed from last frame's position lands a
        // whole ten metres behind the caster — a difference nothing could mistake for a rounding one.
        StartSession(Always(), cooldown: 0.5f, hp: MaxHp);

        _stepPerTick = new Vector3(10f, 0f, 0f);

        TickUntil(() => _events.Count<ZoneSpawned>() > 0);

        Vector3 firstCast = _playerPosition;

        Assert.That(
            _events.Single<ZoneSpawned>().Position,
            Is.EqualTo(firstCast),
            "Placed where the player is standing on the tick of the cast.");

        // And again on the recast, which is the one that has a real "last tick" behind it.
        TickUntil(() => _events.Count<ZoneSpawned>() > 0);

        Vector3 secondCast = _playerPosition;

        Assert.That(secondCast, Is.Not.EqualTo(firstCast), "The fixture really did walk them.");

        Assert.That(
            _events.Single<ZoneSpawned>().Position,
            Is.EqualTo(secondCast),
            "This tick's position, not last tick's — PlayerCombat fills the blackboard before the "
                + "skills step, so a cast reads the frame it is in.");

        Assert.That(
            _events.Single<ZoneSpawned>().Position,
            Is.Not.EqualTo(secondCast - _stepPerTick),
            "Ten metres behind is what a cached position would have given.");

        Assert.That(_session.State.ActiveZoneCount, Is.EqualTo(2));
    }

    [Test]
    public void Zone_TicksAfterTheTimedEffects()
    {
        // **Rule 3's half that nothing else can see, proved M3-06's way**: the tick is arranged so
        // that a grant falls due on exactly the tick a zone pulses, and the expiry has to be resolved
        // first. Ticked above TimedEffects, the two expiry mechanisms interleave and the order a
        // shield comes off in starts depending on whether a zone happened to end on the same frame.
        //
        // It is an EditMode row in Tests.Core rather than a FrameOrderTests row, deliberately: that
        // fixture is PlayMode and drives a RecordingCore fake with no RunSession in it at all, so
        // landing this there would take PlayMode from 16 to 17 and prove less.
        //
        // Pass one measures the two moments with a duration long enough that nothing expires. Pass
        // two sets the duration so the deadline falls half a frame before the pulse tick — due on
        // exactly that tick, with margin on both sides of it, because a deadline computed to land
        // *on* an accumulated float is a coin toss.
        StartSession(Always(), cooldown: 100f, hp: MaxHp, withBulwark: true, grantDuration: 1_000f);

        TickUntil(() => _events.Count<ZoneSpawned>() > 0);
        TickUntil(() => _events.Count<ShieldGranted>() > 0);

        float grantAt = _session.State.Time;

        TickUntil(() => _events.Count<ZoneHealed>() > 0);
        TickUntil(() => _events.Count<ZoneHealed>() > 0);

        float pulseAt = _session.State.Time;

        Assert.That(
            pulseAt - grantAt,
            Is.GreaterThan(Interval),
            "The fixture's own claim: the second pulse really is a whole interval past the grant.");

        StartSession(
            Always(),
            cooldown: 100f,
            hp: MaxHp,
            withBulwark: true,
            grantDuration: pulseAt - grantAt - (Frame / 2f));

        TickUntil(() => _events.Count<ZoneSpawned>() > 0);
        TickUntil(() => _events.Count<ShieldGranted>() > 0);
        TickUntil(() => _events.Count<ZoneHealed>() > 0);
        TickUntil(() => _events.Count<ZoneHealed>() > 0);

        int expiredAt = IndexOfFirst<ShieldGrantExpired>();
        int healedAt = IndexOfFirst<ZoneHealed>();

        Assert.That(
            expiredAt,
            Is.GreaterThanOrEqualTo(0),
            "The grant fell due on the pulse tick, which is the coincidence this row is built on.");

        Assert.That(
            expiredAt,
            Is.LessThan(healedAt),
            "And the timed effects were resolved before the zones, in the order of one tick's "
                + "events.");
    }

    [Test]
    public void Consecrate_CastsBelowSixtyPercent()
    {
        // CC §6.4's authored trigger, at the boundary and one point either side of it. 84 of 140 is
        // exactly 0.6 and `Below` is strict; 83 is 0.593.
        StartSession(BelowSixty(), Cooldown, hp: 84f);

        Assert.That(
            TickUntil(() => _events.Count<ZoneSpawned>() > 0),
            Is.EqualTo(-1),
            "Exactly 60 % is not below 60 %, for three thousand ticks.");

        Assert.That(_session.State.ActiveZoneCount, Is.Zero);

        StartSession(BelowSixty(), Cooldown, hp: 83f);

        Assert.That(
            TickUntil(() => _events.Count<ZoneSpawned>() > 0),
            Is.GreaterThan(0),
            "One point lower and the ground is under them.");

        Assert.That(_session.State.ActiveZoneCount, Is.EqualTo(1));
    }

    [Test]
    public void Consecrate_HealsAQuarterBarIfYouStand()
    {
        // **The authored skill, end to end, as a player would feel it**: dropped below 60 %, stood
        // still for six seconds, and got about a quarter of a bar back for it.
        StartSession(BelowSixty(), Cooldown, hp: 70f);

        TickFrames(400);

        Assert.That(
            _session.State.PlayerHp,
            Is.EqualTo(70f + WholeZone).Within(Tolerance),
            "Twelve pulses of three. Eleven would be the retirement winning the tick they share.");

        Assert.That(_events.Count<ZoneHealed>(), Is.EqualTo(12));
        Assert.That(_events.Count<ZoneExpired>(), Is.EqualTo(1), "And it is over rather than still on.");
        Assert.That(_session.State.ActiveZoneCount, Is.Zero);

        Assert.That(
            _events.Count<ZoneSpawned>(),
            Is.EqualTo(1),
            "One cast: the 12 s cooldown outlasts the 6 s zone, which is what keeps this a decision "
                + "rather than a floor the player stands on for ever.");
    }

    [Test]
    public void Consecrate_HealsNothingIfYouLeave()
    {
        // **Rules 1 and 5, and the only thing that makes this skill a decision.** The zone keeps
        // burning down where it was left, and the player who walked away gets nothing at all.
        StartSession(BelowSixty(), Cooldown, hp: 70f);

        int cast = TickUntil(() => _events.Count<ZoneSpawned>() > 0);

        Assert.That(cast, Is.GreaterThan(0), "The fixture failed to cast.");

        _playerPosition = new Vector3(20f, 0f, 0f);

        TickFrames(400);

        Assert.That(
            _session.State.PlayerHp,
            Is.EqualTo(70f).Within(Tolerance),
            "Not one point: standing in it when the pulse lands is the whole of the contract.");

        Assert.That(_events.Count<ZoneHealed>(), Is.Zero);

        Assert.That(
            _events.Count<ZoneExpired>(),
            Is.EqualTo(1),
            "And it burned down where it was left rather than following them.");
    }

    [Test]
    public void State_ExposesTheZones()
    {
        StartSession(Always(), cooldown: 0.5f, hp: MaxHp);

        _stepPerTick = new Vector3(10f, 0f, 0f);

        TickUntil(() => _events.Count<ZoneSpawned>() > 0);

        Vector3 first = _playerPosition;

        TickUntil(() => _events.Count<ZoneSpawned>() > 0);

        Vector3 second = _playerPosition;

        Assert.That(_session.State.ActiveZoneCount, Is.EqualTo(2));

        Assert.That(
            _session.State.ZoneAt(0),
            Is.EqualTo(first),
            "In the order they were placed — SkillIdAt's shape, one class over.");

        Assert.That(_session.State.ZoneAt(1), Is.EqualTo(second));

        Assert.Throws<ArgumentOutOfRangeException>(() => _session.State.ZoneAt(2));

        // **And the seal did not move to let them out** (AR §18.2). ZoneSystem has a public Spawn,
        // Tick and Clear, so a view holding the handle could put healing ground under the player,
        // advance its clock or delete one mid-fight. Asked by reflection because the compiler cannot
        // be — Soulvail.Tests.Core has no InternalsVisibleTo and deliberately never will — and asked
        // here rather than in a RunCommand probe, which refuses the whole System.Reflection namespace
        // before it executes anything.
        PropertyInfo zones = typeof(RunState).GetProperty(
            "Zones",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(zones, Is.Not.Null, "RunState.Zones is gone — this row is out of date.");
        Assert.That(zones.GetMethod.IsPublic, Is.False, "RunState.Zones became public.");

        PropertyInfo combat = typeof(RunState).GetProperty(
            "Combat",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Assert.That(combat, Is.Not.Null, "RunState.Combat is gone — this row is out of date.");

        Assert.That(
            combat.GetMethod.IsPublic,
            Is.False,
            "RunState.Combat became public. This task added two reads beside it and must not have "
                + "loosened the seal to do it.");
    }

    // ---- Fixture --------------------------------------------------------------------------------

    private static ContentId Id(string value) => new ContentId(value);

    private static TriggerSpec Below(TriggerField field, float threshold) =>
        new TriggerSpec(new[] { new TriggerClause(field, TriggerComparison.Below, threshold) });

    /// <summary>True on the first tick of any run: a full bar is below 110 %.</summary>
    private static TriggerSpec Always() => Below(TriggerField.HpFraction, 1.1f);

    /// <summary>CC §6.4's own trigger, authored.</summary>
    private static TriggerSpec BelowSixty() => Below(TriggerField.HpFraction, 0.6f);

    /// <summary>An Active whose one cast effect is a healing zone — Consecrate, without the asset.</summary>
    private static SkillSpec Consecrate(float cooldown, TriggerSpec trigger) =>
        new SkillSpec(
            Id(ConsecrateId),
            new LocKey($"{ConsecrateId}.name"),
            new LocKey($"{ConsecrateId}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(
                cooldown,
                trigger,
                new IEffect[] { new SpawnHealZone(Radius, Duration, Heal, Interval) }));

    /// <summary>CC §6.4's Bulwark, for the one row that needs a timed effect beside a zone.</summary>
    private static SkillSpec Bulwark(float cooldown, TriggerSpec trigger, float duration) =>
        new SkillSpec(
            Id(BulwarkId),
            new LocKey($"{BulwarkId}.name"),
            new LocKey($"{BulwarkId}.desc"),
            SkillKind.Active,
            Array.Empty<IEffect>(),
            new ActiveSpec(cooldown, trigger, new IEffect[] { new GrantShield(Points, duration) }));

    private static SkillSpec Passive() =>
        new SkillSpec(
            Id(PassiveId),
            new LocKey($"{PassiveId}.name"),
            new LocKey($"{PassiveId}.desc"),
            SkillKind.Passive,
            new IEffect[] { new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f) });

    private static SkillBranchSpec Branch(char letter, params ContentId[] tier) =>
        new SkillBranchSpec(
            new LocKey($"branch.{letter}"),
            new IReadOnlyList<ContentId>[] { tier });

    /// <summary>CC §7's class at the owner's retuned numbers, Aegis and all.</summary>
    private static CharacterSpec Character() => new CharacterSpec(
        Id(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        MaxHp,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 8f, 0.5f, 2.5f, 0.15f, 20f, 4f, 0.05f),
        new ShieldSpec(ShieldMax, ShieldDelay, ShieldRefill),
        HitIFrames);

    /// <summary>
    /// The mode with an <b>empty roster</b>: nothing composes, nothing spawns, nothing can hurt the
    /// player — see the fixture remarks.
    /// </summary>
    private static ModeSpec Mode() => new ModeSpec(
        Id(ModeId),
        new LocKey("mode.test.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>(),
        new[] { Id(ArenaOne), Id(ArenaTwo) });

    /// <summary>
    /// A live run owning a Consecrate — and, for one row, a Bulwark beside it — resumed at
    /// <paramref name="hp"/> of 140.
    /// </summary>
    private void StartSession(
        TriggerSpec trigger,
        float cooldown,
        float hp,
        bool withBulwark = false,
        float grantDuration = 5f)
    {
        SkillSpec consecrate = Consecrate(cooldown, trigger);
        SkillSpec bulwark = Bulwark(cooldown, trigger, grantDuration);
        SkillSpec passive = Passive();

        var nodes = new List<SkillSpec> { consecrate };

        if (withBulwark)
        {
            nodes.Add(bulwark);
        }

        var tree = new SkillTreeSpec(
            Id(TreeId),
            Id(OathboundId),
            new[]
            {
                Branch('a', consecrate.Id),
                Branch('b', bulwark.Id),
                Branch('c', passive.Id),
            });

        var catalog = new ContentCatalog(
            new[] { Character() },
            Array.Empty<EnemySpec>(),
            new[] { Mode() },
            new[] { consecrate, bulwark, passive },
            new[] { tree });

        _random = new FixedRandom(7, Alternating(8_192));

        _playerPosition = Vector3.Zero;
        _stepPerTick = Vector3.Zero;

        _session = new RunSession(
            catalog,
            _random,
            _events,
            new RecordingIntents(),
            new RunRecorder(_random, new FixedClock(Instant), _events),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);

        var taken = new ContentId[nodes.Count];

        for (int i = 0; i < nodes.Count; i++)
        {
            taken[i] = nodes[i].Id;
        }

        // Resumed rather than fresh, because that is the door RunSession.Start adds a restored Active
        // through (M3-06 rule 5) — and it is the only door there is until a tree ships. It is also
        // the only way to start a run at less than full health, which is what every trigger row here
        // needs. One level per node and nothing owed, which is the arithmetic M3-08a rule 9 refuses a
        // run for getting wrong.
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
                hp,
                0f,
                0f,
                Instant,
                nodes.Count + 1,
                0f,
                0,
                taken,
                new ContentId[SkillRunner.MaxManualSlots],
                default,
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>(),
                Array.Empty<ContentId>())));

        Assert.That(
            _session.State.OwnedActiveCount,
            Is.EqualTo(nodes.Count),
            "The fixture owns what it authored.");

        Assert.That(
            _session.State.PlayerHp,
            Is.EqualTo(hp).Within(Tolerance),
            "And it started where the row asked it to.");

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

    /// <summary>Ticks <paramref name="count"/> frames, keeping every event for the count at the end.</summary>
    private void TickFrames(int count)
    {
        for (int i = 0; i < count; i++)
        {
            TickOnce();
        }
    }

    private void TickOnce()
    {
        _playerPosition += _stepPerTick;

        var snapshot = new WorldSnapshot(EnemyCapacity)
        {
            Dt = Frame,
            PlayerPosition = _playerPosition,
            HasGate = false,
            SpawnPoints = Points8(),
        };

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
                (float)(12d * Math.Cos(angle)),
                0f,
                (float)(12d * Math.Sin(angle)));
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
}
