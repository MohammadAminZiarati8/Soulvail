using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The Ranger's nine nodes, their nine effects and the tree that holds them: the owner's three
/// passives of 2026-09-25, each taken three times. RS-03c rules 4 and 5.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three chains of three, and CH §5's tier gate is what makes them chains.</b> Rank I is a
/// Passive and ranks II and III are Upgrades whose parent is the rank below, so a level-up with a
/// one-node tier offers exactly <em>"which passive next"</em>. The rank rows walk that offer as the
/// player does — <c>OpenLevelUp</c>, then <c>ChooseOffer</c> on the rank's card — and assert on
/// every pick that the three cards are the next rank of each branch.
/// </para>
/// <para>
/// <b>The run is built from <c>BootScope.prefab</c>'s own lists</b>, so a rank that works here works
/// in the catalog the game boots, with the other three classes' trees in it. The stats are read
/// off the run's own combat and motor, which <c>RunState</c> keeps <c>internal</c> (AR §18.2), by
/// reflection — <c>BurningGroundTests</c>' route.
/// </para>
/// </remarks>
[TestFixture]
public sealed class RangerTreeTests
{
    private const string SkillDir = "Assets/_Project/Data/Skills/Ranger";
    private const string EffectDir = "Assets/_Project/Data/Effects/Ranger";
    private const string TreePath = "Assets/_Project/Data/Trees/Ranger.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";

    private const string RangerId = "character.ranger";
    private const string DescentId = "mode.descent";

    /// <summary>The branch indices, in the order the tree asset lists them.</summary>
    private const int Draw = 0;

    private const int Volley = 1;
    private const int Stride = 2;

    private static readonly string[] BranchKeys = { "tree.ranger.draw", "tree.ranger.volley", "tree.ranger.stride" };

    /// <summary>Each branch's three asset files, rank I first.</summary>
    private static readonly string[][] Files =
    {
        new[] { "QuickDraw", "QuickDraw2", "QuickDraw3" },
        new[] { "Volley", "Volley2", "Volley3" },
        new[] { "FleetFoot", "FleetFoot2", "FleetFoot3" },
    };

    /// <summary>The same nine as content ids.</summary>
    private static readonly string[][] Ids =
    {
        new[] { "skill.ranger.quick-draw", "skill.ranger.quick-draw-2", "skill.ranger.quick-draw-3" },
        new[] { "skill.ranger.volley", "skill.ranger.volley-2", "skill.ranger.volley-3" },
        new[] { "skill.ranger.fleet-foot", "skill.ranger.fleet-foot-2", "skill.ranger.fleet-foot-3" },
    };

    /// <summary>The spec's table: each branch's address and kind, and each rank's value.</summary>
    private static readonly PlayerStat[] Stats = { PlayerStat.FireRate, PlayerStat.VolleyEvery, PlayerStat.MoveSpeed };

    private static readonly ModifierKind[] Kinds = { ModifierKind.PercentAdd, ModifierKind.Flat, ModifierKind.PercentAdd };

    private static readonly float[][] Values =
    {
        new[] { 0.12f, 0.12f, 0.12f },
        new[] { 5f, -1f, -1f },
        new[] { 0.08f, 0.08f, 0.08f },
    };

    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const float Tolerance = 1e-4f;

    private static readonly DateTimeOffset Instant = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

    private readonly List<RunSession> _sessions = new List<RunSession>();

    [TearDown]
    public void EndSessions()
    {
        foreach (RunSession session in _sessions)
        {
            session.End();
        }

        _sessions.Clear();
    }

    // ---- Rule 4: the tree is the table -------------------------------------------------------------

    [Test]
    public void Tree_IsThreeChainsOfThree()
    {
        SkillTreeSpec tree = Tree();
        ContentCatalog catalog = BootCatalog();

        Assert.That(tree.Id.Value, Is.EqualTo("tree.ranger"));
        Assert.That(tree.CharacterId.Value, Is.EqualTo(RangerId));
        Assert.That(catalog.TryGetTreeFor(new ContentId(RangerId), out SkillTreeSpec booted), Is.True, "the boot catalog holds it.");
        Assert.That(booted.Id, Is.EqualTo(tree.Id));

        Assert.That(tree.NodeCount, Is.EqualTo(9), "three passives, each taken three times.");
        Assert.That(tree.Branches, Has.Count.EqualTo(SkillTreeSpec.BranchCount));

        TreeRules rules = null;

        Assert.That(() => rules = new TreeRules(tree, catalog), Throws.Nothing, "TreeRules accepts the shape.");

        for (int b = 0; b < tree.Branches.Count; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];

            Assert.That(branch.NameKey.Key, Is.EqualTo(BranchKeys[b]), $"branch {b}'s heading.");
            Assert.That(branch.TierCount, Is.EqualTo(3), $"branch {b} is three tiers deep.");

            for (int t = 1; t <= 3; t++)
            {
                string where = $"{BranchKeys[b]}, tier {t}";

                Assert.That(branch.Tier(t), Has.Count.EqualTo(1), $"{where} holds one node.");
                Assert.That(branch.Tier(t)[0].Value, Is.EqualTo(Ids[b][t - 1]), where);

                SkillSpec spec = Node(Files[b][t - 1]);

                Assert.That(spec.Id.Value, Is.EqualTo(Ids[b][t - 1]), $"{Files[b][t - 1]}.asset's id.");
                Assert.That(rules.RequiredTakenInBranch(spec.Id), Is.EqualTo(t - 1), $"{where}: CH §5's tier gate.");

                if (t == 1)
                {
                    Assert.That(spec.Kind, Is.EqualTo(SkillKind.Passive), $"{where}: rank I is a Passive.");
                    Assert.That(spec.ParentId.Value, Is.Null, $"{where}: a Passive has no parent.");
                }
                else
                {
                    Assert.That(spec.Kind, Is.EqualTo(SkillKind.Upgrade), $"{where}: ranks II and III are Upgrades.");
                    Assert.That(spec.ParentId.Value, Is.EqualTo(Ids[b][t - 2]), $"{where}: the parent is the rank below.");
                }
            }
        }
    }

    [Test]
    public void Tree_EffectsAreTheTables()
    {
        for (int b = 0; b < Files.Length; b++)
        {
            for (int r = 0; r < 3; r++)
            {
                string file = Files[b][r];
                SkillDefinition definition = LoadNode(file);
                SkillSpec spec = definition.ToSpec();

                Assert.That(spec.Effects, Has.Count.EqualTo(1), file);

                var modify = spec.Effects[0] as ModifyStat;

                Assert.That(modify, Is.Not.Null, $"{file}: a ModifyStat.");
                Assert.That(modify.Stat, Is.EqualTo(Stats[b]), $"{file}'s address.");
                Assert.That(modify.Kind, Is.EqualTo(Kinds[b]), $"{file}'s kind.");
                Assert.That(modify.Value, Is.EqualTo(Values[b][r]).Within(1e-6f), $"{file}'s value.");
                Assert.That(modify.Target, Is.EqualTo(StatTarget.Player), $"{file} aims at the player.");

                // And it is the file the table names, one effect per node under Data/Effects/Ranger/.
                UnityEngine.Object effect = new SerializedObject(definition).FindProperty("_effects").GetArrayElementAtIndex(0).objectReferenceValue;

                Assert.That(AssetDatabase.GetAssetPath(effect), Is.EqualTo($"{EffectDir}/{file}.asset"), file);
            }
        }
    }

    [Test]
    public void Tree_HasNoPact()
    {
        foreach (string[] branch in Files)
        {
            foreach (string file in branch)
            {
                SkillSpec spec = Node(file);

                Assert.That(spec.HasPact, Is.False, $"{file}: nobody has written its corrupted form.");
                Assert.That(spec.Pact, Is.Null, file);
            }
        }
    }

    // ---- Rule 5: the ranks do what the owner said ----------------------------------------------------

    [Test]
    public void Ranks_QuickDrawThreeTimesIsThirtySixPercent()
    {
        RunSession session = StartRanger(picks: 3);
        Stat fireRate = Combat(session).Weapon.FireRate;
        float bow = fireRate.Value;

        Assert.That(bow, Is.EqualTo(2.2f).Within(Tolerance), "the premise: the bow's 2.2 shots a second.");

        for (int r = 0; r < 3; r++)
        {
            Take(session, Draw, r);

            Assert.That(fireRate.Value, Is.EqualTo(bow * (1f + (0.12f * (r + 1)))).Within(Tolerance), $"after {Ids[Draw][r]}.");
        }

        Assert.That((fireRate.Value / bow) - 1f, Is.EqualTo(0.36f).Within(Tolerance), "+36 % fire rate.");
    }

    [Test]
    public void Ranks_FleetFootThreeTimesIsTwentyFourPercent()
    {
        RunSession session = StartRanger(picks: 3);
        Stat speed = Motor(session).Speed;
        float legs = speed.Value;

        Assert.That(legs, Is.EqualTo(3.2f).Within(Tolerance), "the premise: the Ranger's 3.2 m/s.");

        for (int r = 0; r < 3; r++)
        {
            Take(session, Stride, r);

            Assert.That(speed.Value, Is.EqualTo(legs * (1f + (0.08f * (r + 1)))).Within(Tolerance), $"after {Ids[Stride][r]}.");
        }

        Assert.That((speed.Value / legs) - 1f, Is.EqualTo(0.24f).Within(Tolerance), "+24 % speed.");
    }

    [Test]
    public void Ranks_VolleyCountsFiveFourThree()
    {
        RunSession session = StartRanger(picks: 3);
        Soulvail.Core.Combat.Volley volley = Combat(session).Volley;

        Assert.That(volley, Is.Not.Null, "the Ranger carries a volley.");
        Assert.That(volley.Every.Value, Is.Zero, "and fires none until a node gives it one (RS-03b).");

        int[] every = { 5, 4, 3 };

        for (int r = 0; r < 3; r++)
        {
            Take(session, Volley, r);

            Assert.That(volley.Every.Value, Is.EqualTo(every[r]).Within(Tolerance), $"after {Ids[Volley][r]}.");
        }
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    /// <summary>
    /// Opens the level-up and taps rank <paramref name="rank"/> of <paramref name="branch"/> — after
    /// asserting the offer is what a chain of one-node tiers draws: at most one rank of each branch
    /// open at a time, and the offer exactly those.
    /// </summary>
    /// <remarks>
    /// The first pick opens the screen; every later one finds its offer already up, because
    /// <c>LevelUpFlow.Choose</c> redraws at once while a pick is owed. <c>OpenLevelUp</c> is
    /// idempotent over an open offer, so it is called either way.
    /// </remarks>
    private static void Take(RunSession session, int branch, int rank)
    {
        Assert.That(session.IsLevelUpPending || session.HasOffer, Is.True, "the premise: a pick is owed.");

        var next = new List<string>(SkillTreeSpec.BranchCount);

        foreach (string[] chain in Ids)
        {
            string[] open = Array.FindAll(chain, id => session.State.IsNodeAvailable(new ContentId(id)));

            Assert.That(open.Length, Is.LessThanOrEqualTo(1), $"{string.Join(", ", open)} are open at once in one chain.");

            if (open.Length == 1)
            {
                next.Add(open[0]);
            }
        }

        session.OpenLevelUp();

        var offer = new List<string>();

        foreach (ContentId id in session.State.Offer)
        {
            offer.Add(id.Value);
        }

        Assert.That(offer, Is.EquivalentTo(next), "the level-up offers the next rank of each passive.");

        int index = offer.IndexOf(Ids[branch][rank]);

        Assert.That(index, Is.GreaterThanOrEqualTo(0), $"{Ids[branch][rank]} was not offered: {string.Join(", ", offer)}.");

        int taken = session.State.TakenNodeCount;

        session.ChooseOffer(index);

        Assert.That(session.State.TakenNodeCount, Is.EqualTo(taken + 1), $"{Ids[branch][rank]} was taken.");
    }

    private static PlayerCombat Combat(RunSession session) =>
        (PlayerCombat)typeof(RunState).GetProperty("Combat", Hidden).GetValue(session.State);

    private static PlayerMotor Motor(RunSession session) =>
        (PlayerMotor)typeof(RunState).GetProperty("Motor", Hidden).GetValue(session.State);

    private static SkillDefinition LoadNode(string file)
    {
        var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>($"{SkillDir}/{file}.asset");

        Assert.That(definition, Is.Not.Null, $"No SkillDefinition at {SkillDir}/{file}.asset.");

        return definition;
    }

    private static SkillSpec Node(string file) => LoadNode(file).ToSpec();

    private static SkillTreeSpec Tree()
    {
        var definition = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(TreePath);

        Assert.That(definition, Is.Not.Null, $"No SkillTreeDefinition at {TreePath}.");

        return definition.ToSpec();
    }

    /// <summary>The catalog the game boots: every list on <c>BootScope.prefab</c>, converted.</summary>
    private static ContentCatalog BootCatalog()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        var scope = prefab.GetComponent<BootScope>();

        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        var serialized = new SerializedObject(scope);

        return new ContentCatalog(
            Convert<CharacterDefinition, CharacterSpec>(serialized, "_characters", d => d.ToSpec()),
            Convert<EnemyDefinition, EnemySpec>(serialized, "_enemies", d => d.ToSpec()),
            Convert<ModeDefinition, ModeSpec>(serialized, "_modes", d => d.ToSpec()),
            Convert<SkillDefinition, SkillSpec>(serialized, "_skills", d => d.ToSpec()),
            Convert<SkillTreeDefinition, SkillTreeSpec>(serialized, "_trees", d => d.ToSpec()),
            Convert<BossDefinition, BossSpec>(serialized, "_bosses", d => d.ToSpec()));
    }

    private static List<TSpec> Convert<TDefinition, TSpec>(
        SerializedObject scope, string field, Func<TDefinition, TSpec> convert)
        where TDefinition : ScriptableObject
    {
        SerializedProperty list = scope.FindProperty(field);
        var specs = new List<TSpec>(list.arraySize);

        for (int i = 0; i < list.arraySize; i++)
        {
            var definition = list.GetArrayElementAtIndex(i).objectReferenceValue as TDefinition;

            Assert.That(definition, Is.Not.Null, $"{field}[{i}] is an empty slot.");

            specs.Add(convert(definition));
        }

        return specs;
    }

    /// <summary>
    /// A Ranger run over the boot catalog, resumed with nothing taken and <paramref name="picks"/>
    /// level-ups owed — <c>EmberwrightTreeTests.StartRun</c>'s shape, with the picks left to make.
    /// </summary>
    private RunSession StartRanger(int picks)
    {
        var events = new RecordingEvents();
        var random = new FixedRandom(7, Alternating(4_096));
        var clock = new FixedClock(Instant);

        var session = new RunSession(
            BootCatalog(),
            random,
            events,
            new RecordingIntents(),
            new RunRecorder(random, clock, events),
            Capacity,
            DeviceCap,
            ProjectileCapacity);

        _sessions.Add(session);

        var snapshot = new RunSnapshot(
            RunSnapshot.CurrentVersion,
            new ContentId(DescentId),
            new ContentId(RangerId),
            random.Seed,
            1,
            new RandomState(101, 102, 103, 104, 105),
            40f,
            0f,
            120f,
            Instant,
            1 + picks,
            0f,
            picks,
            Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>());

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(RangerId),
            random.Seed,
            1,
            SpawnPlan.Empty,
            snapshot));

        Assert.That(session.State.CharacterId.Value, Is.EqualTo(RangerId), "the premise: a Ranger run.");
        Assert.That(session.State.PendingLevelUps, Is.EqualTo(picks), "the premise: the picks are owed.");

        return session;
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
