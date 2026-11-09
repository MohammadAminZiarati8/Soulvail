using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Progression;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

public sealed class BeastmasterTreeTests
{
    private const string Root = "Assets/_Project/";
    private static readonly string[][] Branches =
    {
        new[] { "HeavyEdge", "SwiftHands", "CleavingReturn" },
        new[] { "SharpClaws", "FeralBite", "Pounce" },
        new[] { "ThickHide", "LickWounds", "QuickSwap" },
    };

    [Test]
    public void AxeNodes_Numbers()
    {
        Modifiers("HeavyEdge", (PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.12f, StatTarget.Player));
        Modifiers("SwiftHands", (PlayerStat.FireRate, ModifierKind.PercentAdd, 0.2f, StatTarget.Player));
        Modifiers("CleavingReturn",
            (PlayerStat.AxeReturnDamage, ModifierKind.Flat, 0.5f, StatTarget.Player),
            (PlayerStat.AxeReturnSlow, ModifierKind.Flat, 0.25f, StatTarget.Player));
    }

    [Test]
    public void BeastNodes_Numbers()
    {
        Modifiers("SharpClaws", (PlayerStat.ContactDamage, ModifierKind.PercentAdd, 0.15f, StatTarget.Pet));
        Modifiers("FeralBite", (PlayerStat.PetBiteStunEvery, ModifierKind.Flat, 3f, StatTarget.Pet));

        SkillSpec node = Node("Pounce").ToSpec();
        Assert.That(node.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(node.Active.Cooldown, Is.EqualTo(8f));
        TriggerClause clause = node.Active.Trigger.Clauses.Single();
        Assert.That((clause.Field, clause.Comparison, clause.Threshold),
            Is.EqualTo((TriggerField.MarkedInPounceReach, TriggerComparison.AtLeast, 1f)));

        var definition = Load<PounceDefinition>("Data/Effects/Beastmaster/PounceCast.asset");
        var cast = (Pounce)definition.ToEffect();
        Assert.That((cast.MinReach, cast.MaxReach, cast.BiteMultiplier, cast.KnockdownSeconds, cast.LeapSeconds),
            Is.EqualTo((3f, 6f, 2f, 0.8f, 0.35f)));
        var wired = (Pounce)node.Active.OnCast.Single();
        Assert.That((wired.MinReach, wired.MaxReach, wired.BiteMultiplier, wired.KnockdownSeconds, wired.LeapSeconds),
            Is.EqualTo((cast.MinReach, cast.MaxReach, cast.BiteMultiplier, cast.KnockdownSeconds, cast.LeapSeconds)));

        var runner = new SkillRunner(new EffectRegistry(), new CombatBlackboard(), new RecordingEvents());
        runner.Add(node);
        Assert.That(runner.IsAuto(node.Id), Is.True);
    }

    [Test]
    public void WildNodes_Numbers()
    {
        Modifiers("ThickHide",
            (PlayerStat.MaxHp, ModifierKind.PercentAdd, 0.10f, StatTarget.Player),
            (PlayerStat.MaxHp, ModifierKind.PercentAdd, 0.10f, StatTarget.Pet));
        Modifiers("LickWounds",
            (PlayerStat.PetHeelRegen, ModifierKind.Flat, 0.06f, StatTarget.Pet),
            (PlayerStat.PetReturnAt, ModifierKind.Flat, -0.2f, StatTarget.Pet));
        Modifiers("QuickSwap",
            (PlayerStat.MovementSkillCooldown, ModifierKind.Flat, -1f, StatTarget.Player),
            (PlayerStat.SwapRoarSlow, ModifierKind.Flat, 0.3f, StatTarget.Pet),
            (PlayerStat.SwapBarrier, ModifierKind.Flat, 0.1f, StatTarget.Player),
            (PlayerStat.SwapBarrierShove, ModifierKind.Flat, 2f, StatTarget.Player));
    }

    [Test]
    public void QuickSwap_RaisesBarrierAndShove()
    {
        CharacterSpec beastmaster = Load<CharacterDefinition>("Data/Characters/Beastmaster.asset").ToSpec();
        Assert.That(beastmaster.MovementSkill.BarrierFraction, Is.EqualTo(0.15f));
        Assert.That(beastmaster.MovementSkill.BarrierSeconds, Is.EqualTo(3f));
        var events = new RecordingEvents();
        var combat = new PlayerCombat(beastmaster, events, new RecordingIntents(), 8);
        var stats = new PlayerStats(combat, new PlayerMotor(beastmaster.Movement, System.Numerics.Vector3.UnitZ),
            new LevelTracker(new XpCurve(20f, 12f, 1.4f), events));
        var handler = new ModifyStatHandler(stats);

        foreach (ModifyStat effect in Node("QuickSwap").ToSpec().Effects.OfType<ModifyStat>()
            .Where(effect => effect.Target == StatTarget.Player))
        {
            handler.Apply(effect, this);
        }

        Assert.That(combat.Charge.Barrier.Value, Is.EqualTo(0.25f).Within(0.0001f));
        Assert.That(combat.Charge.BarrierShove.Value, Is.EqualTo(2f));
    }

    [Test]
    public void Tree_ThreeBranches_ThreeTiers_NineNodes()
    {
        SkillTreeDefinition definition = Load<SkillTreeDefinition>("Data/Trees/Beastmaster.asset");
        SkillTreeSpec tree = definition.ToSpec();
        CharacterSpec beastmaster = Load<CharacterDefinition>("Data/Characters/Beastmaster.asset").ToSpec();
        Assert.That(tree.Id.Value, Is.EqualTo("tree.beastmaster"));
        Assert.That(tree.CharacterId, Is.EqualTo(beastmaster.Id));

        SkillDefinition[] nodes = Branches.SelectMany(branch => branch).Select(Node).ToArray();
        var catalog = new ContentCatalog(new[] { beastmaster }, skills: nodes.Select(node => node.ToSpec()).ToArray(),
            trees: new[] { tree });
        var rules = new TreeRules(tree, catalog);

        Assert.That(tree.Branches, Has.Count.EqualTo(3));
        for (int b = 0; b < Branches.Length; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];
            Assert.That(branch.TierCount, Is.EqualTo(3));
            for (int tier = 1; tier <= branch.TierCount; tier++)
            {
                var id = new ContentId(Node(Branches[b][tier - 1]).Id);
                Assert.That(branch.Tier(tier), Is.EqualTo(new[] { id }));
                Assert.That(rules.RequiredTakenInBranch(id), Is.EqualTo(tier - 1));
            }
        }

        var boot = new SerializedObject(Load<GameObject>("Prefabs/Composition/BootScope.prefab").GetComponent<BootScope>());
        Assert.That(References(boot.FindProperty("_trees")), Does.Contain(definition));
        Assert.That(References(boot.FindProperty("_skills")), Is.SupersetOf(nodes));
    }

    [Test]
    public void EveryNode_HasLocKeys_InBothTables()
    {
        SkillTreeSpec tree = Load<SkillTreeDefinition>("Data/Trees/Beastmaster.asset").ToSpec();
        CharacterSpec beastmaster = Load<CharacterDefinition>("Data/Characters/Beastmaster.asset").ToSpec();
        var keys = tree.Branches.Select(branch => branch.NameKey)
            .Append(beastmaster.NameKey)
            .Concat(Branches.SelectMany(branch => branch).Select(file => Node(file).ToSpec())
                .SelectMany(node => new[] { node.NameKey, node.DescriptionKey }));

        foreach (string locale in new[] { "English", "Pseudo" })
        {
            var table = Load<LocalizationTable>($"Data/Localisation/{locale}.asset").ToDictionary();
            foreach (LocKey key in keys)
            {
                Assert.That(table.TryGetValue(key, out string value), Is.True, $"{locale}: {key}");
                Assert.That(value, Is.Not.Empty, $"{locale}: {key}");
            }
        }
    }

    private static void Modifiers(string file, params (PlayerStat, ModifierKind, float, StatTarget)[] expected)
    {
        SkillSpec node = Node(file).ToSpec();
        Assert.That(node.Kind, Is.EqualTo(SkillKind.Passive), file);
        var actual = node.Effects.Cast<ModifyStat>().Select(effect => (effect.Stat, effect.Kind, effect.Value, effect.Target));
        Assert.That(actual, Is.EquivalentTo(expected), file);
    }

    private static Object[] References(SerializedProperty array) =>
        Enumerable.Range(0, array.arraySize).Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();

    private static SkillDefinition Node(string file) => Load<SkillDefinition>($"Data/Skills/Beastmaster/{file}.asset");

    private static T Load<T>(string path) where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(Root + path);
        Assert.That(asset, Is.Not.Null, path);
        return asset;
    }
}
