using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Fakes;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// JG-04c: <see cref="RunSession"/> picks a boss's behaviour by the boss's id, and refuses a roster
/// boss it has no behaviour for.
/// </summary>
/// <remarks>
/// <para>
/// <b>A whole run, because the map is private to the session</b> and this assembly has no
/// <c>InternalsVisibleTo</c> (AR §18.2). Which behaviour drives a boss is read off what it does
/// first: Old Croaker's opening move is a hop-slam (<see cref="CroakerBehaviour.SlamMove"/>), the
/// Warden's a plain slam telegraph (move 0). A <see cref="BossBehaviour"/> with no inner behaviour
/// telegraphs nothing at all, so a row that saw no telegraph is a row whose dispatch failed.
/// </para>
/// <para>
/// The player stands just inside the boss's reach once it is up, because core only learns where a
/// body is from Unity's snapshot, and these snapshots carry none — the boss stays where it stood up.
/// Rule 3 (which boss each shipped place names) and rule 4 (what the shipped phases summon) are
/// about the assets, so their rows are in <c>ContentValidationTests</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BossDispatchTests
{
    private const string HuskId = "enemy.husk";
    private const string WardenEnemyId = "enemy.warden";
    private const string CroakerEnemyId = "enemy.croaker";
    private const string WardenId = "boss.warden";
    private const string CroakerId = "boss.croaker";
    private const string OathboundId = "character.oathbound";
    private const string ModeId = "mode.test";

    private const int HuskCost = 4;
    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    /// <summary>Comfortably outside the director's minimum spawn distance from the origin.</summary>
    private const float Ring = 10f;

    /// <summary>The boss stage every row starts on.</summary>
    private const int BossStage = 5;

    private static readonly Vector3 Door = new Vector3(0f, 0f, 18f);

    private RecordingEvents _events;

    [SetUp]
    public void SetUp()
    {
        _events = new RecordingEvents();
    }

    // ---- Rule 1 ----------------------------------------------------------------------------------

    [Test]
    public void Croaker_FightsWithCroakerBehaviour()
    {
        EnemyTelegraph first = FirstBossTelegraph(CroakerId, CroakerEnemyId);

        Assert.That(first.Move, Is.EqualTo(CroakerBehaviour.SlamMove), "Old Croaker did not open with a hop-slam.");
        Assert.That(first.Duration, Is.EqualTo(CroakerBehaviour.SlamTelegraphSeconds));
    }

    [Test]
    public void Warden_StillTheWarden()
    {
        EnemyTelegraph first = FirstBossTelegraph(WardenId, WardenEnemyId);

        Assert.That(first.Move, Is.Zero, "The Warden's slam names no move.");
        Assert.That(first.Duration, Is.EqualTo(0.9f), "The Warden's body's WindupTime.");
    }

    // ---- Rule 2 ----------------------------------------------------------------------------------

    [Test]
    public void Start_RefusesABossWithNoBehaviour()
    {
        const string Stranger = "boss.stranger";

        RunSession session = Session(Mode(Stranger), Boss(Stranger, WardenEnemyId));

        var refused = Assert.Throws<KeyNotFoundException>(() => session.Start(Config()));

        Assert.That(refused.Message, Does.Contain($"'{Stranger}'"), "The refusal does not name the id.");
        Assert.That(session.IsRunning, Is.False);
        Assert.That(_events.Count<RunStarted>(), Is.Zero, "Nothing is announced before the refusal.");
    }

    // JG-12b rule 8: inspect the attached behaviour after the real session dispatch.
    [Test]
    public void Dispatch_MotherPitcher_IsDriven()
    {
        const string Mother = "boss.mother_pitcher";
        RunSession session = Session(Mode(Mother), Boss(Mother, "enemy.mother_pitcher"));
        session.Start(Config());
        for (int i = 0; i < 1_200 && _events.Count<EnemySpawned>() == 0; i++)
        {
            session.Tick(Snapshot(Vector3.Zero));
        }

        Assert.That(_events.Count<EnemySpawned>(), Is.EqualTo(1));
        // Reflection reads the attachment without adding a public live-object escape to RunState.
        var enemies = (EnemySystem)typeof(RunState)
            .GetProperty("Enemies", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(session.State);
        EnemyAgent agent = enemies.Registry.Alive[0];
        var wrapper = agent.Behaviour as BossBehaviour;
        Assert.That(wrapper, Is.Not.Null);
        Assert.That(wrapper.Inner, Is.TypeOf<MotherPitcherBehaviour>());
        enemies.Spawn(new ContentId("enemy.pitcher_bud"), Vector3.Zero);
        session.Tick(Snapshot(Vector3.Zero));
        Assert.That(((MotherPitcherBehaviour)wrapper.Inner).IsShielded, Is.True);
        Assert.That(agent.DamageTaken.Value, Is.EqualTo(MotherPitcherBehaviour.ShieldedDamageTaken).Within(1e-5f));
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>
    /// Starts a run on a boss stage whose roster names <paramref name="bossId"/>, waits for the boss
    /// to stand up, stands the player beside it and returns the first telegraph the boss publishes.
    /// </summary>
    private EnemyTelegraph FirstBossTelegraph(string bossId, string bodyId)
    {
        RunSession session = Session(Mode(bossId), Boss(bossId, bodyId));

        session.Start(Config());

        int bossBody = 0;
        Vector3 player = Vector3.Zero;

        for (int i = 0; i < 1_200 && bossBody == 0; i++)
        {
            session.Tick(Snapshot(player));

            IReadOnlyList<EnemySpawned> spawned = _events.Of<EnemySpawned>();

            for (int s = 0; s < spawned.Count; s++)
            {
                if (spawned[s].SpecId.Value == bodyId)
                {
                    bossBody = spawned[s].Id;

                    // 1.5 m short of the body, on the side facing the origin: inside both bosses' reach.
                    player = spawned[s].Position - (Vector3.Normalize(spawned[s].Position) * 1.5f);
                }
            }
        }

        Assert.That(bossBody, Is.Not.Zero, "The fixture failed to stand the boss up.");

        for (int i = 0; i < 1_200; i++)
        {
            session.Tick(Snapshot(player));

            IReadOnlyList<EnemyTelegraph> telegraphs = _events.Of<EnemyTelegraph>();

            for (int t = 0; t < telegraphs.Count; t++)
            {
                if (telegraphs[t].Id == bossBody)
                {
                    return telegraphs[t];
                }
            }
        }

        Assert.Fail("The boss never telegraphed: no behaviour is driving it.");

        return default;
    }

    private RunSession Session(ModeSpec mode, BossSpec boss) => new RunSession(
        new ContentCatalog(
            new[] { Oathbound() },
            new[] { Husk(), Body(WardenEnemyId), Body(CroakerEnemyId),
                Body("enemy.mother_pitcher"), Bud() },
            new[] { mode },
            skills: null,
            trees: null,
            bosses: new[] { boss }),
        new FixedRandom(0, Alternating(8_192)),
        _events,
        new RecordingIntents(),
        new RunRecorder(new FixedRandom(0, Alternating(8_192)), new FixedClock(default), _events),
        Capacity,
        DeviceCap,
        ProjectileCapacity);

    private static RunConfig Config() => new RunConfig(
        new ContentId(ModeId),
        new ContentId(OathboundId),
        0,
        BossStage,
        new SpawnPlan(Array.Empty<SpawnPlan.Entry>()),
        restore: null);

    private static WorldSnapshot Snapshot(Vector3 playerPosition) =>
        new WorldSnapshot(Capacity)
        {
            Dt = 1f / 60f,
            PlayerPosition = playerPosition,
            HasGate = true,
            GatePosition = Door,
            SpawnPoints = Points(8),
        };

    /// <summary>A mode of static Husks with <paramref name="bossId"/> every fifth stage.</summary>
    private static ModeSpec Mode(string bossId)
    {
        var scaling = new ScalingSpec(
            new BudgetCurve(HuskCost, 0f, 0f),
            new WaveCurve(1, 1000, 1, 1),
            new ConcurrencyCurve(DeviceCap, 1000),
            new StatCurve(0.06f, 4f, 1, 1),
            new StatCurve(0.035f, 3f, 1, 1),
            new StatCurve(0.02f, 1.3f, 5, 0));

        return new ModeSpec(
            new ContentId(ModeId),
            new LocKey("mode.test.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            scaling,
            Scalings.Xp(),
            new[] { new RosterEntry(new ContentId(HuskId), 1) },
            arenas: null,
            bossRoster: new[] { new BossRosterEntry(new ContentId(bossId), BossStage) });
    }

    /// <summary>One phase and no summons: these rows are about who drives the boss, not the phases.</summary>
    private static BossSpec Boss(string bossId, string bodyId) => new BossSpec(
        new ContentId(bossId),
        new ContentId(bodyId),
        new[] { new BossPhaseSpec(1f) },
        1.5f);

    /// <summary><c>Warden.asset</c>'s numbers, for both bodies: these rows are about dispatch, not tuning.</summary>
    private static EnemySpec Body(string id) => new EnemySpec(
        new ContentId(id),
        new LocKey(id + ".name"),
        maxHp: 3_200f,
        moveSpeed: 1.8f,
        targetPriority: 8,
        threatCost: 40,
        xpValue: 300f,
        isElite: false,
        contactDamage: 22f,
        reach: 2.5f,
        windupTime: 0.9f,
        recoverTime: 1.2f,
        aggroRange: 40f,
        behaviour: EnemyBehaviourKind.Boss);

    /// <summary>Static, so nothing but the boss ever telegraphs at a player out of its reach.</summary>
    private static EnemySpec Husk() => new EnemySpec(
        new ContentId(HuskId),
        new LocKey("enemy.husk.name"),
        maxHp: 10f,
        moveSpeed: 2f,
        targetPriority: 1,
        threatCost: HuskCost,
        xpValue: HuskCost * 3f,
        isElite: false,
        contactDamage: 8f,
        reach: 1.2f,
        windupTime: 0.4f,
        recoverTime: 0.6f,
        aggroRange: 30f,
        behaviour: EnemyBehaviourKind.Static);

    private static EnemySpec Bud() => new EnemySpec(
        new ContentId("enemy.pitcher_bud"), new LocKey("enemy.pitcher_bud.name"),
        10f, 0f, 1, 4, 12f, false, 0f, 1f, 0.6f, 0.6f, 40f, EnemyBehaviourKind.Static);

    private static CharacterSpec Oathbound() => new CharacterSpec(
        new ContentId(OathboundId),
        new LocKey("character.oathbound.name"),
        new LocKey("character.oathbound.description"),
        100f,
        new MovementSpec(3f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));

    /// <summary><paramref name="count"/> points on a ring, clear of the origin and of each other.</summary>
    private static IReadOnlyList<Vector3> Points(int count)
    {
        var points = new Vector3[count];

        for (int i = 0; i < count; i++)
        {
            double angle = 2d * Math.PI * i / count;

            points[i] = new Vector3((float)(Ring * Math.Cos(angle)), 0f, (float)(Ring * Math.Sin(angle)));
        }

        return points;
    }

    /// <summary><paramref name="count"/> values alternating between the first and last candidate.</summary>
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
