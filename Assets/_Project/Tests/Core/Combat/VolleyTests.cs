using System;
using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// RS-03b rules 1–8: the volley. After every <em>n</em> shots the next is a fan of arrows, each
/// dealing more; <em>n</em> is a <see cref="Stat"/> a node moves; the fan is one tell and several
/// shots; the run fires them all on one tick; and a tree may name the volley's numbers only on a class
/// that has them.
/// </summary>
/// <remarks>
/// <para>
/// <b>A bow built in code, not the Ranger</b>, for <c>HoldFireTests</c>' reason: RS-03c authors the
/// class. Two shots a second, the arrow loosed halfway through the draw, a 13-damage arrow, and a Husk
/// that cannot die standing 8 m ahead. The volley is the Ranger's: three arrows, 30°, ×1.5.
/// </para>
/// <para>
/// <b>Three halves, by what each rule can reach.</b> Rules 1–5 tick a <see cref="PlayerCombat"/> by
/// hand in <c>RunSession.Tick</c>'s order and take every shot the tick it is offered, as the run does.
/// Rules 6 and 7 drive a real <see cref="RunSession"/>, because the take loop and the sweep at
/// <c>Start</c> are that class's. Rule 8 drives a <see cref="SplashFlow"/> directly, as
/// <c>SplashFlowTests</c> does. <c>Queue_CapacityHoldsTheLargestVolley</c> reads
/// <c>BootInstaller.ProjectileCapacity</c> and <c>OwnTree_TheShippedTreesPass</c> reads the shipped
/// assets, so both live in <c>Tests.Game</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class VolleyTests
{
    private const string BowmanId = "character.bowman";
    private const string SwordsmanId = "character.swordsman";
    private const string HuskId = "enemy.husk";
    private const string DescentId = "mode.descent";

    /// <summary>The node that gives the bow its volley: <c>VolleyEvery</c> Flat +1.</summary>
    private const string VolleyNodeId = "skill.bowman.volley";

    private const string BowSteadyId = "skill.bowman.steady";
    private const string BowKeenId = "skill.bowman.keen";
    private const string SwordEdgeId = "skill.swordsman.edge";
    private const string SwordGuardId = "skill.swordsman.guard";
    private const string SwordReachId = "skill.swordsman.reach";

    private const float ShotDamage = 13f;
    private const float ShotsPerSecond = 2f;
    private const float Interval = 1f / ShotsPerSecond;
    private const float ShotRange = 12f;
    private const float ShotSpeed = 40f;
    private const float ShotRadius = 0.5f;
    private const float DamageFrame = 0.5f;
    private const float FullCircle = 360f;

    /// <summary>The Ranger's volley (RS-03c): three arrows, a 30° fan, ×1.5 each.</summary>
    private const int VolleyArrows = 3;

    private const float FanDeg = 30f;
    private const float Multiplier = 1.5f;

    /// <summary>Inside the bow's reach, straight ahead of a player at the origin.</summary>
    private const float TargetRange = 8f;

    private const float Frame = 1f / 120f;

    /// <summary>A shot every 60 ticks; this is headroom for one, on top of however many a row asks for.</summary>
    private const int TicksPerShot = 120;

    private const int EnemyCapacity = 8;
    private const int DeviceCap = 8;
    private const int ProjectileCapacity = 32;
    private const int Seed = 7;

    private const float Tolerance = 1e-3f;

    private static readonly Vector2 Push = new(1f, 0f);
    private static readonly Vector3 Running = new(3f, 0f, 0f);

    private static readonly DateTimeOffset Instant = new(2026, 9, 25, 10, 0, 0, TimeSpan.Zero);

    private RecordingEvents _events;
    private RecordingIntents _intents;
    private EnemySystem _enemies;
    private WorldSnapshot _snapshot;
    private EnemySense _husk;

    private float _now;

    /// <summary>Every damage frame that loosed anything, as the shots it loosed, in the order taken.</summary>
    private List<Projectile[]> _frames;

    /// <summary>When each <see cref="PlayerAttacked"/> went out, on the fixture's clock.</summary>
    private List<float> _swingStarts;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
        _intents = new RecordingIntents();

        var catalog = new ContentCatalog(
            new[] { Bowman(BowmanId, Spec()) }, new[] { Husk() }, new[] { Descent() });

        _enemies = new EnemySystem(
            catalog, _events, new FixedRandom(Seed), new DepthScaling(Scalings.Design()), EnemyCapacity);

        EnemyAgent husk = _enemies.Spawn(new ContentId(HuskId), new Vector3(0f, 0f, TargetRange));

        _husk = new EnemySense
        {
            Id = husk.Id,
            Position = husk.Position,
            Velocity = Vector3.Zero,
            PathDirectionToPlayer = Vector2.Zero,
            HasLineOfSight = true,
        };

        _snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
        _now = 0f;
        _frames = new List<Projectile[]>();
        _swingStarts = new List<float>();
    }

    // ---- Rule 1: the spec refuses what cannot fly --------------------------------------------------

    [Test]
    public void Spec_RefusesArrowsFanAndDamageOutOfRange()
    {
        foreach (int arrows in new[] { 1, 0, -3, VolleySpec.MaxArrows + 1 })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => new VolleySpec(arrows, FanDeg, Multiplier));

            Assert.That(thrown.ParamName, Is.EqualTo("arrows"), $"arrows {arrows}.");
        }

        foreach (float fan in new[] { 0f, -10f, 180f, 270f, float.NaN, float.PositiveInfinity })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => new VolleySpec(VolleyArrows, fan, Multiplier));

            Assert.That(thrown.ParamName, Is.EqualTo("fanAngleDeg"), $"fan {fan}.");
        }

        foreach (float damage in new[] { 0.99f, 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => new VolleySpec(VolleyArrows, FanDeg, damage));

            Assert.That(thrown.ParamName, Is.EqualTo("damageMultiplier"), $"damage {damage}.");
        }

        // And the edges that fly: two arrows, the queue's seven, a hair of fan, a volley at ×1.
        Assert.DoesNotThrow(() => new VolleySpec(2, 0.5f, 1f));
        Assert.DoesNotThrow(() => new VolleySpec(VolleySpec.MaxArrows, 179f, 3f));
    }

    // ---- Rule 2: the count is a floored Every, and 0 is no volley ----------------------------------

    [Test]
    public void Every_ZeroMeansNoVolley()
    {
        PlayerCombat bow = Combat();

        Assert.That(bow.Volley.Every.Base, Is.Zero, "the premise: no volley until a node gives one.");

        RunShots(bow, 20);

        Assert.That(Arrows(), Is.EqualTo(Repeat(1, 20)), "twenty ordinary shots.");
        Assert.That(bow.Volley.ShotsTowardNext, Is.Zero, "and the count held where it was.");
        Assert.That(bow.Volley.IsReady, Is.False);
        Assert.That(_events.Count<VolleyReady>(), Is.Zero);
    }

    [Test]
    public void Every_IsFloored()
    {
        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 4.9f);

        RunShots(bow, 5);

        Assert.That(Arrows(), Is.EqualTo(new[] { 1, 1, 1, 1, VolleyArrows }), "4.9 counts as 4.");
    }

    // ---- Rule 3: the counter -----------------------------------------------------------------------

    [Test]
    public void Counter_FiveOrdinaryThenAVolley()
    {
        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 5f);

        RunShots(bow, 12);

        Assert.That(
            Arrows(),
            Is.EqualTo(new[] { 1, 1, 1, 1, 1, VolleyArrows, 1, 1, 1, 1, 1, VolleyArrows }),
            "read literally: five ordinary shots, then the volley — shots 6 and 12.");

        // One VolleyReady each way per cycle: ready on the fifth, spent on the sixth.
        IReadOnlyList<VolleyReady> ready = _events.Of<VolleyReady>();

        Assert.That(ready.Count, Is.EqualTo(4));
        Assert.That(ready[0].IsReady && !ready[1].IsReady && ready[2].IsReady && !ready[3].IsReady, Is.True);
    }

    [Test]
    public void Counter_RanksCountDown()
    {
        // The Ranger's Volley, Volley II and Volley III (RS-03c): +5, then −1, then −1.
        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 5f);
        RunShots(bow, 6);

        Give(bow, PlayerStat.VolleyEvery, -1f);
        RunShots(bow, 5);

        Give(bow, PlayerStat.VolleyEvery, -1f);
        RunShots(bow, 4);

        Assert.That(
            Arrows(),
            Is.EqualTo(new[]
            {
                1, 1, 1, 1, 1, VolleyArrows,
                1, 1, 1, 1, VolleyArrows,
                1, 1, 1, VolleyArrows,
            }),
            "cycles of five, four and three ordinary shots.");
    }

    [Test]
    public void Counter_ALowerEveryReadiesAtOnce()
    {
        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 5f);
        RunShots(bow, 4);

        Assert.That(bow.Volley.ShotsTowardNext, Is.EqualTo(4), "the premise: four shots toward five.");
        Assert.That(bow.Volley.IsReady, Is.False);
        Assert.That(_events.Count<VolleyReady>(), Is.Zero);

        // A rank taken mid-count: 5 → 3, with no shot in between.
        Give(bow, PlayerStat.VolleyEvery, -2f);

        Assert.That(bow.Volley.IsReady, Is.True, "ready the moment the node is taken.");
        Assert.That(_events.Count<VolleyReady>(), Is.EqualTo(1), "announced once.");
        Assert.That(_events.Single<VolleyReady>().IsReady, Is.True);
        Assert.That(bow.Volley.ShotsTowardNext, Is.EqualTo(4), "the count is only a shot's to move.");

        RunShots(bow, 1);

        Assert.That(_frames[4], Has.Length.EqualTo(VolleyArrows), "and the next shot is the volley.");
        Assert.That(bow.Volley.ShotsTowardNext, Is.Zero);
    }

    [Test]
    public void Counter_ADroppedShotCountsNothing()
    {
        // RS-03a's hold: a draw the player moves out of is dropped, and no arrow leaves.
        PlayerCombat bow = Combat(firesWhileMoving: false);

        Give(bow, PlayerStat.VolleyEvery, 5f);
        RunShots(bow, 1);

        Assert.That(bow.Volley.ShotsTowardNext, Is.EqualTo(1), "the premise: one shot has left.");

        RunUntilSwingStarts(bow);

        Assert.That(bow.Weapon.IsSwinging, Is.True, "Sanity: drawing.");

        Step(bow, Push, Running);

        Assert.That(bow.Weapon.IsSwinging, Is.False, "Sanity: the draw is dropped.");

        for (int i = 0; i < 2 * TicksPerShot; i++)
        {
            Step(bow, Push, Running);
        }

        Assert.That(_frames, Has.Count.EqualTo(1), "Sanity: nothing left on the move.");
        Assert.That(bow.Volley.ShotsTowardNext, Is.EqualTo(1), "the dropped draw counted nothing.");

        // And the next shot that does leave counts, so the row above is about the drop.
        RunShots(bow, 1);

        Assert.That(bow.Volley.ShotsTowardNext, Is.EqualTo(2));
    }

    // ---- Rule 4: the fan ---------------------------------------------------------------------------

    [Test]
    public void Fan_ThreeArrowsThirtyDegrees()
    {
        // At chest height, so "the same height" is a number the fan has to keep rather than a zero.
        _husk.Position = new Vector3(0f, 1.2f, TargetRange);

        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 1f);
        RunShots(bow, 2);

        Projectile ordinary = _frames[0][0];
        Projectile[] fan = _frames[1];

        Assert.That(fan, Has.Length.EqualTo(VolleyArrows), "the second shot is the volley.");

        float[] expected = { -15f, 0f, 15f };

        for (int i = 0; i < fan.Length; i++)
        {
            Projectile arrow = fan[i];

            Assert.That(Yaw(arrow), Is.EqualTo(expected[i]).Within(Tolerance), $"arrow {i}'s heading.");
            Assert.That(DistanceXZ(arrow), Is.EqualTo(TargetRange).Within(Tolerance), $"arrow {i} flies 8 m.");
            Assert.That(arrow.Target.Y, Is.EqualTo(ordinary.Target.Y).Within(1e-5f), $"arrow {i}'s height.");
            Assert.That(arrow.Origin, Is.EqualTo(ordinary.Origin), $"arrow {i} leaves where the shot does.");
            Assert.That(arrow.Speed, Is.EqualTo(ShotSpeed), $"arrow {i} at the weapon's speed.");
            Assert.That(arrow.Radius, Is.EqualTo(ShotRadius), $"arrow {i} at the weapon's radius.");
        }

        // The middle arrow goes where the ordinary shot went.
        Assert.That(Vector3.Distance(fan[1].Target, ordinary.Target), Is.LessThan(1e-4f));
    }

    [Test]
    public void Fan_EachArrowCarriesTheMultiplier()
    {
        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 1f);
        RunShots(bow, 2);

        Assert.That(_frames[0][0].Damage, Is.EqualTo(ShotDamage).Within(Tolerance), "an ordinary shot deals 13.");

        foreach (Projectile arrow in _frames[1])
        {
            Assert.That(arrow.Damage, Is.EqualTo(19.5f).Within(Tolerance), "13 × 1.5, each.");
        }
    }

    [Test]
    public void Fan_ArrowsAreClampedAndFloored()
    {
        PlayerCombat bow = Combat();
        var node = new object();

        Give(bow, PlayerStat.VolleyEvery, 1f);
        Give(bow, PlayerStat.VolleyArrows, 96f, node);

        Assert.That(bow.Volley.Arrows.Value, Is.EqualTo(99f), "Sanity: 3 + 96.");
        Assert.That(bow.Volley.ArrowCount, Is.EqualTo(VolleySpec.MaxArrows), "99 is clamped to 7.");

        RunShots(bow, 2);

        Assert.That(_frames[1], Has.Length.EqualTo(VolleySpec.MaxArrows), "and seven leave.");

        bow.Volley.Arrows.RemoveAll(node);
        Give(bow, PlayerStat.VolleyArrows, -1.3f);

        Assert.That(bow.Volley.Arrows.Value, Is.EqualTo(1.7f).Within(Tolerance), "Sanity: 3 − 1.3.");
        Assert.That(bow.Volley.ArrowCount, Is.EqualTo(2), "1.7 is floored, and a fan is never under 2.");

        RunShots(bow, 2);

        Assert.That(_frames[3], Has.Length.EqualTo(2));
    }

    // ---- Rule 5: one swing, one draw, one tell -----------------------------------------------------

    [Test]
    public void Volley_IsOneTell()
    {
        PlayerCombat bow = Combat();

        Give(bow, PlayerStat.VolleyEvery, 1f);
        RunShots(bow, 1);

        Assert.That(bow.Volley.IsReady, Is.True, "Sanity: the next shot is the volley.");

        int before = _events.Count<PlayerAttacked>();

        RunShots(bow, 1);

        Assert.That(_events.Count<PlayerAttacked>() - before, Is.EqualTo(1), "one PlayerAttacked for the volley.");
        Assert.That(_frames[1], Has.Length.EqualTo(VolleyArrows), "and three shots from its damage frame.");

        // And the fire rate treats it as one shot: the next draw starts one interval after its own.
        RunShots(bow, 1);

        Assert.That(_swingStarts, Has.Count.GreaterThanOrEqualTo(3));
        Assert.That(_swingStarts[2] - _swingStarts[1], Is.EqualTo(Interval).Within(Frame));
        Assert.That(_swingStarts[1] - _swingStarts[0], Is.EqualTo(Interval).Within(Frame));
    }

    // ---- Rule 6: shots queue, and the run fires every one on its tick ------------------------------

    [Test]
    public void Queue_EveryShotIsFiredOnItsTick()
    {
        RunSession session = Session(Catalog(new[] { Bowman(BowmanId, Spec()) }, new[] { BowTree() }));

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(BowmanId),
            Seed,
            1,
            new SpawnPlan(new[] { new SpawnPlan.Entry(new ContentId(HuskId), new Vector3(0f, 0f, TargetRange)) }),
            Resumed(BowmanId, VolleyNodeId)));

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = Frame };
        int volleyTicks = 0;

        for (int i = 0; i < 3 * TicksPerShot && _events.Count<ProjectileFired>() < 1 + VolleyArrows; i++)
        {
            int before = _events.Count<ProjectileFired>();

            session.Tick(snapshot);

            int fired = _events.Count<ProjectileFired>() - before;

            if (fired > 1)
            {
                volleyTicks++;

                Assert.That(fired, Is.EqualTo(VolleyArrows), "the whole fan on the tick it was loosed.");
            }
        }

        IReadOnlyList<ProjectileFired> shots = _events.Of<ProjectileFired>();

        Assert.That(shots, Has.Count.EqualTo(1 + VolleyArrows), "one ordinary shot, then the volley.");
        Assert.That(volleyTicks, Is.EqualTo(1));

        float[] expected = { -15f, 0f, 15f };

        for (int i = 0; i < VolleyArrows; i++)
        {
            ProjectileFired arrow = shots[1 + i];

            Assert.That(arrow.Id, Is.EqualTo(shots[1].Id + i), "consecutive ids.");
            Assert.That(arrow.SpecId, Is.EqualTo(new ContentId(BowmanId)));

            float yaw = MathF.Atan2(arrow.Target.X - arrow.Origin.X, arrow.Target.Z - arrow.Origin.Z) * (180f / MathF.PI);

            Assert.That(yaw, Is.EqualTo(expected[i]).Within(Tolerance), "in aim order.");
        }
    }

    // ---- Rule 7: a class's own tree names only the numbers it has ----------------------------------

    [Test]
    public void OwnTree_AMissingAddressIsRefusedAtStart()
    {
        // A swordsman — no volley — whose own tree opens on the bow's Volley node.
        RunSession session = Session(Catalog(
            new[] { Bowman(SwordsmanId, volley: null) },
            new[] { SwordTree(VolleyNodeId) }));

        var thrown = Assert.Throws<ArgumentException>(() => session.Start(FreshConfig(SwordsmanId)));

        StringAssert.Contains(VolleyNodeId, thrown.Message, "the node is named.");
        StringAssert.Contains(nameof(PlayerStat.VolleyEvery), thrown.Message, "and the stat.");

        Assert.That(session.IsRunning, Is.False);
        Assert.That(_events.Count<RunStarted>(), Is.Zero, "nothing about the run was announced.");

        // The refusal is about the class, not the node: the same node on a class with a volley starts.
        RunSession bow = Session(Catalog(new[] { Bowman(BowmanId, Spec()) }, new[] { BowTree() }));

        Assert.DoesNotThrow(() => bow.Start(FreshConfig(BowmanId)));
        Assert.That(bow.IsRunning, Is.True);
    }

    // ---- Rule 8: a class without a volley, and a borrowed Volley branch ----------------------------

    [Test]
    public void Splash_ABorrowedVolleyBranchIsRefused()
    {
        ContentCatalog catalog = Catalog(
            new[] { Bowman(SwordsmanId, volley: null), Bowman(BowmanId, Spec()) },
            new[] { SwordTree(SwordEdgeId), BowTree() });

        var sword = new PlayerCombat(Bowman(SwordsmanId, volley: null), _events, _intents, EnemyCapacity);
        PlayerStats stats = Stats(sword);

        var effects = new EffectRegistry();
        effects.Register<ModifyStat>(new ModifyStatHandler(stats));

        var tree = new SkillTree(new TreeRules(SwordTree(SwordEdgeId), catalog), effects, _events);
        var flow = new SplashFlow(tree, catalog, effects, new ContentId(SwordsmanId), _events, stats);

        IReadOnlyList<SplashOption> branches = flow.BranchesOf(new ContentId(BowmanId));

        Assert.That(branches[0].Borrowable, Is.False, "a swordsman has no volley for the Volley node to move.");
        Assert.That(branches[0].RefusedKey, Is.EqualTo(new LocKey(SplashFlow.RefusedAddressKeyId)));

        // The control: the bow's other branches name only numbers every player has.
        Assert.That(branches[1].Borrowable, Is.True);
        Assert.That(branches[2].Borrowable, Is.True);
    }

    // ---- Fixtures: the combat half -----------------------------------------------------------------

    /// <summary>One frame of the player's fight, and the run taking every shot it offered.</summary>
    private void Step(PlayerCombat player, Vector2 stick, Vector3 velocity)
    {
        _now += Frame;

        _snapshot.MoveInput = stick;
        _snapshot.PlayerVelocity = velocity;
        _snapshot.EnemyCount = 0;
        _snapshot.AddEnemy() = _husk;

        _enemies.Ingest(_snapshot);

        int attacked = _events.Count<PlayerAttacked>();

        player.Tick(Frame, _now, _snapshot, _enemies.Registry.Alive, Vector3.UnitZ);

        if (_events.Count<PlayerAttacked>() > attacked)
        {
            _swingStarts.Add(_now);
        }

        var taken = new List<Projectile>();

        while (player.TryTakeShot(out Projectile shot))
        {
            taken.Add(shot);
        }

        if (taken.Count > 0)
        {
            _frames.Add(taken.ToArray());
        }
    }

    /// <summary>Stands still until <paramref name="count"/> more damage frames have loosed something.</summary>
    private void RunShots(PlayerCombat player, int count)
    {
        int target = _frames.Count + count;

        for (int i = 0; i < (count + 1) * TicksPerShot && _frames.Count < target; i++)
        {
            Step(player, Vector2.Zero, Vector3.Zero);
        }

        if (_frames.Count < target)
        {
            throw new InvalidOperationException($"{target - _frames.Count} shot(s) short.");
        }
    }

    private void RunUntilSwingStarts(PlayerCombat player)
    {
        int before = _swingStarts.Count;

        for (int i = 0; i < TicksPerShot && _swingStarts.Count == before; i++)
        {
            Step(player, Vector2.Zero, Vector3.Zero);
        }

        if (_swingStarts.Count == before)
        {
            throw new InvalidOperationException($"No swing within {TicksPerShot} still ticks.");
        }
    }

    /// <summary>How many shots each damage frame loosed, in order.</summary>
    private int[] Arrows()
    {
        var arrows = new int[_frames.Count];

        for (int i = 0; i < arrows.Length; i++)
        {
            arrows[i] = _frames[i].Length;
        }

        return arrows;
    }

    private static int[] Repeat(int value, int count)
    {
        var values = new int[count];

        Array.Fill(values, value);

        return values;
    }

    /// <summary>A shot's heading as yaw in degrees: 0 is +Z, positive turns towards +X.</summary>
    private static float Yaw(in Projectile shot) =>
        MathF.Atan2(shot.Target.X - shot.Origin.X, shot.Target.Z - shot.Origin.Z) * (180f / MathF.PI);

    private static float DistanceXZ(in Projectile shot)
    {
        float dx = shot.Target.X - shot.Origin.X;
        float dz = shot.Target.Z - shot.Origin.Z;

        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private PlayerCombat Combat(bool firesWhileMoving = true) =>
        new(Bowman(BowmanId, Spec(), firesWhileMoving), _events, _intents, EnemyCapacity);

    private PlayerStats Stats(PlayerCombat combat) => new(
        combat,
        new PlayerMotor(new MovementSpec(3f, 0.06f, 0.08f, 720f), Vector3.UnitZ),
        new LevelTracker(Scalings.Xp(), _events));

    /// <summary>A node's worth of <paramref name="stat"/>, through the address a tree would use.</summary>
    private void Give(PlayerCombat combat, PlayerStat stat, float flat, object source = null) =>
        new ModifyStatHandler(Stats(combat)).Apply(
            new ModifyStat(stat, ModifierKind.Flat, flat), source ?? new object());

    private static VolleySpec Spec() => new(VolleyArrows, FanDeg, Multiplier);

    // ---- Fixtures: the run half --------------------------------------------------------------------

    private RunSession Session(ContentCatalog catalog)
    {
        var random = new FixedRandom(Seed);

        return new RunSession(
            catalog,
            random,
            _events,
            _intents,
            new RunRecorder(random, new FixedClock(Instant), _events),
            EnemyCapacity,
            DeviceCap,
            ProjectileCapacity);
    }

    private static ContentCatalog Catalog(CharacterSpec[] characters, SkillTreeSpec[] trees) =>
        new(characters, new[] { Husk() }, new[] { Descent() }, Skills(), trees);

    private static RunConfig FreshConfig(string characterId) => new(
        new ContentId(DescentId), new ContentId(characterId), Seed, 1, SpawnPlan.Empty, restore: null);

    /// <summary>A save at level 2 with <paramref name="taken"/> picked: the volley without the kills.</summary>
    private static RunSnapshot Resumed(string characterId, string taken) => new(
        RunSnapshot.CurrentVersion,
        new ContentId(DescentId),
        new ContentId(characterId),
        Seed,
        1,
        new RandomState(0, 0, 0, 0, 0),
        100f,
        0f,
        0f,
        Instant,
        2,
        0f,
        0,
        new[] { new ContentId(taken) },
        new ContentId[SkillRunner.MaxManualSlots],
        default,
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>());

    /// <summary>The bow's tree: the Volley node, and two branches every player could borrow.</summary>
    private static SkillTreeSpec BowTree() => new(
        new ContentId("tree.bowman"),
        new ContentId(BowmanId),
        new[] { Branch("tree.bowman.a", VolleyNodeId), Branch("tree.bowman.b", BowSteadyId), Branch("tree.bowman.c", BowKeenId) });

    /// <summary>A swordsman's tree, opening on <paramref name="first"/>.</summary>
    private static SkillTreeSpec SwordTree(string first) => new(
        new ContentId("tree.swordsman"),
        new ContentId(SwordsmanId),
        new[] { Branch("tree.swordsman.a", first), Branch("tree.swordsman.b", SwordGuardId), Branch("tree.swordsman.c", SwordReachId) });

    private static SkillBranchSpec Branch(string nameKey, string node) =>
        new(new LocKey(nameKey), new IReadOnlyList<ContentId>[] { new[] { new ContentId(node) } });

    private static IReadOnlyList<SkillSpec> Skills() => new[]
    {
        Node(VolleyNodeId, new ModifyStat(PlayerStat.VolleyEvery, ModifierKind.Flat, 1f)),
        Node(BowSteadyId, new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f)),
        Node(BowKeenId, new ModifyStat(PlayerStat.FireRate, ModifierKind.PercentAdd, 0.05f)),
        Node(SwordEdgeId, new ModifyStat(PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.05f)),
        Node(SwordGuardId, new ModifyStat(PlayerStat.MaxHp, ModifierKind.Flat, 10f)),
        Node(SwordReachId, new ModifyStat(PlayerStat.WeaponRange, ModifierKind.Flat, 1f)),
    };

    private static SkillSpec Node(string id, IEffect effect) => new(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.desc"),
        SkillKind.Passive,
        new[] { effect });

    /// <summary>
    /// A bow on a class with a roll, and <paramref name="volley"/> when it has one: the shape RS-03c
    /// will author, with round numbers. No Aegis, and the Focus ramp off so the cadence stays two.
    /// </summary>
    private static CharacterSpec Bowman(string id, VolleySpec volley, bool firesWhileMoving = true) => new(
        new ContentId(id),
        new LocKey($"{id}.name"),
        new LocKey($"{id}.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(ShotRange, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(
            WeaponKind.Projectile, ShotDamage, ShotsPerSecond, ShotRange, FullCircle, DamageFrame,
            ShotSpeed, ShotRadius, firesWhileMoving: firesWhileMoving),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 6f, 0.3f, 3f, 0.15f, 0f, 0f, 0.05f),
        null,
        0.5f,
        volley: volley);

    /// <summary>A Husk that stands still and cannot be killed by anything these rows do.</summary>
    private static EnemySpec Husk() => new(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        1000f,
        3.5f,
        1,
        threatCost: 4,
        xpValue: 12f,
        isElite: false,
        8f,
        1.2f,
        0.4f,
        0.6f,
        aggroRange: 30f,
        EnemyBehaviourKind.Static);

    /// <summary>
    /// Descent with an empty roster, so a run composes nothing: what these rows spawn comes from a
    /// <see cref="SpawnPlan"/>.
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
