using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
using Soulvail.Game.Authoring;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using NVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The twelve shipped Emberwright nodes, their twelve effect assets and the tree that holds them —
/// and the four addresses and the splash sweep those nodes needed. <c>GravecallerTreeTests</c>'
/// shape, one class along.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row here opens a real file under <c>Data/</c></b>, for <c>OathboundTreeTests</c>'
/// reason: the subject is the content, and a row that built its own definition would be testing a
/// second tree that happens to resemble the one the game boots with.
/// </para>
/// <para>
/// <b>The splash rows state one correction to the spec.</b> M6-08 rule 5 says Ash and Arcana are
/// lendable to every class. Ash's Scorching Ground and Lingering Ash name the Blink pool's two
/// addresses, and rule 8 is explicit that a Charge — and a Shroudstep — has no pool, so the sweep
/// rule 8 adds refuses Ash to both other classes. Only Arcana is lendable to everyone. The rows
/// below pin the behaviour that keeps a borrowed node from throwing into a button, which is the
/// one rule 8 exists for.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EmberwrightTreeTests
{
    private const string SkillDir = "Assets/_Project/Data/Skills/Emberwright";
    private const string EffectDir = "Assets/_Project/Data/Effects/Emberwright";
    private const string RangerEffectDir = "Assets/_Project/Data/Effects/Ranger/";

    private const string TreePath = "Assets/_Project/Data/Trees/Emberwright.asset";
    private const string OathboundTreePath = "Assets/_Project/Data/Trees/Oathbound.asset";
    private const string GravecallerTreePath = "Assets/_Project/Data/Trees/Gravecaller.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string EmberwrightPath = "Assets/_Project/Data/Characters/Emberwright.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string WardenBossPath = "Assets/_Project/Data/Enemies/WardenBoss.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string PlayerStatSourcePath = "Assets/_Project/Core/Effects/PlayerStat.cs";

    private static readonly string[] EnemyPaths =
    {
        HuskPath,
        "Assets/_Project/Data/Enemies/Spitter.asset",
        "Assets/_Project/Data/Enemies/Bloater.asset",
        "Assets/_Project/Data/Enemies/Warden.asset",
    };

    private const string EmberwrightId = "character.emberwright";
    private const string OathboundId = "character.oathbound";
    private const string GravecallerId = "character.gravecaller";
    private const string DescentId = "mode.descent";

    private const string EmberfallId = "skill.emberwright.emberfall";
    private const string CinderNovaId = "skill.emberwright.cinder-nova";

    /// <summary>The branch indices, in the order the tree asset lists them.</summary>
    private const int Ember = 0;
    private const int Arcana = 1;
    private const int Ash = 2;

    /// <summary>The twelve asset file names, in the order the tree lists them.</summary>
    private static readonly string[] NodeAssets =
    {
        "EmberTouch", "StokedCoals", "QuickenedFlame", "LongBurn",
        "Emberfall", "ArcaneHaste", "DeepWell", "AshenLore",
        "CinderNova", "ScorchingGround", "LingeringAsh", "Emberheart",
    };

    /// <summary>The same twelve as content ids.</summary>
    private static readonly string[] NodeIds =
    {
        "skill.emberwright.ember-touch", "skill.emberwright.stoked-coals",
        "skill.emberwright.quickened-flame", "skill.emberwright.long-burn",
        EmberfallId, "skill.emberwright.arcane-haste",
        "skill.emberwright.deep-well", "skill.emberwright.ashen-lore",
        CinderNovaId, "skill.emberwright.scorching-ground",
        "skill.emberwright.lingering-ash", "skill.emberwright.emberheart",
    };

    /// <summary>The six tier-1 nodes: what an untouched tree may offer (M3-03 rule 2).</summary>
    private static readonly string[] TierOneIds =
    {
        "skill.emberwright.ember-touch", "skill.emberwright.stoked-coals",
        EmberfallId, "skill.emberwright.arcane-haste",
        CinderNovaId, "skill.emberwright.scorching-ground",
    };

    /// <summary><c>PlayerStat</c>'s twelve members before M6-08, by ordinal — what every shipped asset stores.</summary>
    private static readonly string[] OrdinalsBeforeM608 =
    {
        "MaxHp", "WeaponDamage", "FireRate", "MoveSpeed", "MovementSkillCooldown", "XpGain",
        "WeaponRange", "WeaponConeAngle", "ChargeDamage", "ShieldRechargeDelay", "HealPerKill",
        "ContactDamage",
    };

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;
    private const float Frame = 1f / 60f;
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

    // ---- The tree's shape (rules 2, 4) -------------------------------------------------------------

    [Test]
    public void Tree_HasTwelveNodesInThreeBranches()
    {
        SkillTreeSpec tree = Tree();

        Assert.That(tree.NodeCount, Is.EqualTo(12));
        Assert.That(tree.Branches, Has.Count.EqualTo(SkillTreeSpec.BranchCount));

        for (int b = 0; b < tree.Branches.Count; b++)
        {
            Assert.That(tree.Branches[b].TierCount, Is.EqualTo(2), $"branch {b} is two tiers deep.");
            Assert.That(tree.Branches[b].Tier(1), Has.Count.EqualTo(2));
            Assert.That(tree.Branches[b].Tier(2), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Tree_NamesTheEmberwright()
    {
        Assert.That(Tree().CharacterId, Is.EqualTo(new ContentId(EmberwrightId)));
        Assert.That(Tree().Id, Is.EqualTo(new ContentId("tree.emberwright")));

        Assert.That(Catalog().TryGetTreeFor(new ContentId(EmberwrightId), out SkillTreeSpec resolved), Is.True);
        Assert.That(resolved.Id, Is.EqualTo(Tree().Id));
    }

    [Test]
    public void Tree_HasNoKeystone()
    {
        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(spec.Kind, Is.Not.EqualTo(SkillKind.Keystone), $"{spec.Id} is a keystone.");
        }
    }

    [Test]
    public void Tree_AvoidsTheKeystoneNames()
    {
        // Rule 2: CH §3.3 reserves Wildfire, Overflow and Scorched Vail for M7-04, and each needs a
        // mechanism rather than a number. Taking a name makes the Keystone feel like a repeat.
        string[] reserved = { "wildfire", "overflow", "scorched-vail" };
        string[] reservedNames = { "Wildfire", "Overflow", "Scorched Vail" };
        var english = ContentValidationTests.English();

        foreach (SkillSpec spec in Nodes())
        {
            foreach (string name in reserved)
            {
                Assert.That(spec.Id.Value, Is.Not.EqualTo("skill.emberwright." + name), spec.Id.Value);
            }

            Assert.That(reservedNames, Does.Not.Contain(english.Get(spec.NameKey)), spec.Id.Value);
        }
    }

    [Test]
    public void Tree_HasTwoActivesAndOneUpgrade()
    {
        int upgrades = 0;
        int passives = 0;

        foreach (SkillSpec spec in Nodes())
        {
            if (spec.Kind == SkillKind.Upgrade)
            {
                upgrades++;
            }
            else if (spec.Kind == SkillKind.Passive)
            {
                passives++;
            }
        }

        Assert.That(new TreeRules(Tree(), Catalog()).ActiveCount, Is.EqualTo(2), "Emberfall and Cinder Nova.");
        Assert.That(upgrades, Is.EqualTo(1), "Deep Well.");
        Assert.That(passives, Is.EqualTo(9));
    }

    [Test]
    public void Tree_BothActivesAreTierOne()
    {
        AssertAt(EmberfallId, Arcana, 1);
        AssertAt(CinderNovaId, Ash, 1);
    }

    [Test]
    public void Tree_UpgradeParentIsInBranchBelow()
    {
        TreeRules rules = null;

        Assert.That(() => rules = new TreeRules(Tree(), Catalog()), Throws.Nothing);

        var deepWell = new ContentId("skill.emberwright.deep-well");

        Assert.That(rules.TryGetParent(deepWell, out ContentId parent), Is.True);
        Assert.That(parent, Is.EqualTo(new ContentId(EmberfallId)));
        Assert.That(rules.Skill(parent).Kind, Is.EqualTo(SkillKind.Active));

        Tree().TryLocate(deepWell, out int childBranch, out int childTier);
        Tree().TryLocate(parent, out int parentBranch, out int parentTier);

        Assert.That(parentBranch, Is.EqualTo(childBranch), "Same branch (Arcana).");
        Assert.That(parentTier, Is.LessThan(childTier), "And below it.");
    }

    // ---- The ids and the files ---------------------------------------------------------------------

    [Test]
    public void Tree_IdsAreKebabCaseAndConstructible()
    {
        Assert.That(ContentId.IsValid("skill.emberwright.emberTouch"), Is.False, "camelCase is refused.");

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(ContentId.IsValid(spec.Id.Value), Is.True);
            Assert.That(spec.Id.Value, Does.StartWith("skill.emberwright."));
            Assert.That(ids.Add(spec.Id.Value), Is.True, $"duplicate id {spec.Id}.");
        }

        Assert.That(ids, Is.EquivalentTo(NodeIds));
    }

    [Test]
    public void Nodes_FileNamesMapToTheirIds()
    {
        foreach (string assetName in NodeAssets)
        {
            SkillDefinition definition = LoadNode(assetName);

            string segment = definition.Id.Substring(definition.Id.LastIndexOf('.') + 1);
            string expected = string.Empty;

            foreach (string word in segment.Split('-'))
            {
                expected += char.ToUpperInvariant(word[0]) + word.Substring(1);
            }

            Assert.That(definition.name, Is.EqualTo(expected), $"{definition.name}.asset carries '{definition.Id}'.");
        }

        var tree = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(TreePath);

        Assert.That(tree, Is.Not.Null, $"No SkillTreeDefinition at {TreePath}.");
        Assert.That(tree.Id, Is.EqualTo("tree.emberwright"));
        Assert.That(tree.name, Is.EqualTo("Emberwright"));
    }

    [Test]
    public void Nodes_AreLinkedToAMonoScript()
    {
        // Traps §5, for twenty-five new assets and the first SpawnBurnZoneDefinition among them.
        foreach (string assetName in NodeAssets)
        {
            Assert.That(MonoScript.FromScriptableObject(LoadNode(assetName)), Is.Not.Null, assetName);

            var effect = AssetDatabase.LoadAssetAtPath<EffectDefinition>($"{EffectDir}/{assetName}.asset");

            Assert.That(effect, Is.Not.Null, $"No EffectDefinition at {EffectDir}/{assetName}.asset.");
            Assert.That(MonoScript.FromScriptableObject(effect), Is.Not.Null, assetName);
        }

        Assert.That(MonoScript.FromScriptableObject(Load<SkillTreeDefinition>(TreePath)), Is.Not.Null);
    }

    // ---- Rules 1 and 3: every number is in an asset ------------------------------------------------

    [Test]
    public void Emberfall_CarriesItsAuthoredNumbers()
    {
        AssertActive("Emberfall", 14f, TriggerField.EnemiesWithin8m, 4f, 4f, 6f);
    }

    [Test]
    public void CinderNova_CarriesItsAuthoredNumbers()
    {
        AssertActive("CinderNova", 18f, TriggerField.EnemiesWithin6m, 6f, 1.5f, 10f);
    }

    [Test]
    public void Actives_DifferOnlyInData()
    {
        // Rule 3, the argument for two: one primitive, one handler, and the whole difference is
        // numbers on two assets.
        SkillSpec emberfall = Node("Emberfall");
        SkillSpec nova = Node("CinderNova");

        IEffect a = emberfall.Active.OnCast[0];
        IEffect b = nova.Active.OnCast[0];

        Assert.That(a.GetType(), Is.EqualTo(typeof(SpawnBurnZone)));
        Assert.That(b.GetType(), Is.EqualTo(a.GetType()), "the same primitive.");

        var registry = new EffectRegistry();

        registry.Register<SpawnBurnZone>(new Ignoring());

        Assert.That(registry.CanApply(a) && registry.CanApply(b), Is.True, "one handler answers both.");

        // Five numbers — radius, duration, damage, cooldown, trigger distance — and each differs.
        var burnA = (SpawnBurnZone)a;
        var burnB = (SpawnBurnZone)b;

        Assert.That(burnA.Radius, Is.Not.EqualTo(burnB.Radius));
        Assert.That(burnA.Duration, Is.Not.EqualTo(burnB.Duration));
        Assert.That(burnA.DamagePerPulse, Is.Not.EqualTo(burnB.DamagePerPulse));
        Assert.That(emberfall.Active.Cooldown, Is.Not.EqualTo(nova.Active.Cooldown));
        Assert.That(emberfall.Active.Trigger.Clauses[0].Field, Is.Not.EqualTo(nova.Active.Trigger.Clauses[0].Field));

        // And the rest is the same shape: one clause, AtLeast 3, one cast effect, no take effects.
        foreach (SkillSpec spec in new[] { emberfall, nova })
        {
            Assert.That(spec.Active.Trigger.Clauses[0].Comparison, Is.EqualTo(TriggerComparison.AtLeast));
            Assert.That(spec.Active.Trigger.Clauses[0].Threshold, Is.EqualTo(3f).Within(Tolerance));
            Assert.That(spec.Active.OnCast, Has.Count.EqualTo(1));
            Assert.That(spec.Effects, Is.Empty);
        }
    }

    [Test]
    public void DeepWell_LowersEmberfallsCooldown()
    {
        SkillSpec deepWell = Node("DeepWell");

        Assert.That(deepWell.Kind, Is.EqualTo(SkillKind.Upgrade));

        var cooldown = (ModifySkillCooldown)deepWell.Effects[0];

        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(cooldown.Value, Is.EqualTo(-0.25f).Within(Tolerance));

        // Against Emberfall's own asset, never a literal — GravecallerTreeTests' Deeper Graves reason.
        Assert.That(cooldown.SkillId, Is.EqualTo(Node("Emberfall").Id));
        Assert.That(Tree().TryLocate(cooldown.SkillId, out int _, out int _), Is.True, "a node of this tree.");
    }

    [Test]
    public void StatNodes_CarryTheirNumbers()
    {
        AssertStat("EmberTouch", PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f);
        AssertStat("StokedCoals", PlayerStat.KindlingPerStack, ModifierKind.Flat, 0.01f);
        AssertStat("QuickenedFlame", PlayerStat.FireRate, ModifierKind.PercentAdd, 0.12f);
        AssertStat("LongBurn", PlayerStat.KindlingMaxStacks, ModifierKind.Flat, 10f);
        AssertStat("ArcaneHaste", PlayerStat.MovementSkillCooldown, ModifierKind.PercentAdd, -0.20f);
        AssertStat("AshenLore", PlayerStat.XpGain, ModifierKind.PercentAdd, 0.15f);
        AssertStat("ScorchingGround", PlayerStat.PoolDamage, ModifierKind.PercentAdd, 0.50f);
        AssertStat("LingeringAsh", PlayerStat.PoolDuration, ModifierKind.PercentAdd, 0.50f);
        AssertStat("Emberheart", PlayerStat.MaxHp, ModifierKind.Flat, 15f);
    }

    // ---- Rule 10: twenty-seven keys ----------------------------------------------------------------

    [Test]
    public void Tree_EveryNodeHasTwoKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(spec.NameKey.Key, Is.EqualTo(spec.Id.Value + ".name"));
            Assert.That(spec.DescriptionKey.Key, Is.EqualTo(spec.Id.Value + ".description"));
            Assert.That(keys.Add(spec.NameKey.Key), Is.True);
            Assert.That(keys.Add(spec.DescriptionKey.Key), Is.True);
        }

        Assert.That(keys, Has.Count.EqualTo(24));
    }

    [Test]
    public void Tree_BranchesAreKeyed()
    {
        SkillTreeSpec tree = Tree();

        Assert.That(tree.Branches[Ember].NameKey.Key, Is.EqualTo("tree.emberwright.ember"));
        Assert.That(tree.Branches[Arcana].NameKey.Key, Is.EqualTo("tree.emberwright.arcana"));
        Assert.That(tree.Branches[Ash].NameKey.Key, Is.EqualTo("tree.emberwright.ash"));
    }

    [Test]
    public void Tree_EveryKeyResolvesInEnglish()
    {
        var english = ContentValidationTests.English();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();

        foreach (SkillSpec spec in Nodes())
        {
            foreach (LocKey key in new[] { spec.NameKey, spec.DescriptionKey })
            {
                seen.Add(key.Key);

                if (!english.Has(key))
                {
                    missing.Add(key.Key);
                }
            }
        }

        foreach (SkillBranchSpec branch in Tree().Branches)
        {
            seen.Add(branch.NameKey.Key);

            if (!english.Has(branch.NameKey))
            {
                missing.Add(branch.NameKey.Key);
            }
        }

        Assert.That(seen, Has.Count.EqualTo(27), "Twelve names, twelve descriptions, three headings.");
        Assert.That(missing, Is.Empty, "English.asset has no row for: " + string.Join(", ", missing));
    }

    // ---- Rule 5: a burn is registered for every class ----------------------------------------------

    [Test]
    public void Burn_IsRegisteredForEveryClass()
    {
        var burn = new SpawnBurnZone(4f, 4f, 6f);

        foreach (string id in new[] { OathboundId, GravecallerId, EmberwrightId })
        {
            RunSession session = StartRun(id, level: 1, Array.Empty<ContentId>());

            Assert.That(Effects(session).CanApply(burn), Is.True, $"a {id} run has no SpawnBurnZone handler.");
        }
    }

    [Test]
    public void Burn_ArcanaIsBorrowableByEveryClass()
    {
        // Rule 5, as far as it is true: Arcana needs a burn (registered for everyone) and names only
        // numbers every player has, so both other classes may borrow it. Ash is not in this row —
        // see Splash_RefusesAshToAClassWithNoPool and the class remarks.
        foreach (string id in new[] { OathboundId, GravecallerId })
        {
            RunSession session = AtHalfTree(id);
            IReadOnlyList<SplashOption> options = session.State.SplashBranchesOf(new ContentId(EmberwrightId));

            Assert.That(options[Arcana].Borrowable, Is.True, $"{id} is refused Arcana.");
        }

        // And the contrast rule 5 draws: the Gravecaller's Legion and Rot are closed to an Oathbound.
        IReadOnlyList<SplashOption> gravecaller =
            AtHalfTree(OathboundId).State.SplashBranchesOf(new ContentId(GravecallerId));

        Assert.That(gravecaller[0].Borrowable, Is.False, "Legion raises the dead.");
        Assert.That(gravecaller[2].Borrowable, Is.False, "Rot's Restless Dead aims at minions.");
    }

    // ---- Rule 8: the third sweep -------------------------------------------------------------------

    [Test]
    public void Splash_RefusesEmberToAClassWithNoKindling()
    {
        RunSession session = AtHalfTree(OathboundId);

        Assert.That(session.State.IsSplashPending, Is.True, "the premise: the moment is owed.");

        SplashOption ember = session.State.SplashBranchesOf(new ContentId(EmberwrightId))[Ember];

        Assert.That(ember.Borrowable, Is.False, "an Oathbound has no Kindling for Stoked Coals to move.");
        Assert.That(ember.RefusedKey.Key, Is.EqualTo(SplashFlow.RefusedAddressKeyId));

        session.OpenSplash();

        Assert.That(session.IsSplashOpen, Is.True);
        Assert.Throws<ArgumentException>(() => session.ChooseSplash(new ContentId(EmberwrightId), Ember));
        Assert.That(session.State.HasSplashed, Is.False, "and nothing was installed.");
    }

    [Test]
    public void Splash_RefusesAshToAClassWithNoPool()
    {
        // The correction to rule 5 (class remarks): a Charge and a Shroudstep leave no pool, so
        // Scorching Ground has nothing to move on either.
        foreach (string id in new[] { OathboundId, GravecallerId })
        {
            SplashOption ash = AtHalfTree(id).State.SplashBranchesOf(new ContentId(EmberwrightId))[Ash];

            Assert.That(ash.Borrowable, Is.False, $"a {id} has no pool for Scorching Ground.");
            Assert.That(ash.RefusedKey.Key, Is.EqualTo(SplashFlow.RefusedAddressKeyId));
        }
    }

    [Test]
    public void Splash_TheRefusalNamesTheAddress()
    {
        Assert.That(
            ContentValidationTests.English().Has(new LocKey(SplashFlow.RefusedAddressKeyId)),
            Is.True,
            "the screen draws this key under a dead branch.");

        RunSession session = AtHalfTree(OathboundId);
        session.OpenSplash();

        var thrown = Assert.Throws<ArgumentException>(
            () => session.ChooseSplash(new ContentId(EmberwrightId), Ember));

        StringAssert.Contains(nameof(PlayerStat.KindlingPerStack), thrown.Message);
        StringAssert.Contains("skill.emberwright.stoked-coals", thrown.Message);
    }

    [Test]
    public void Splash_AnEmberwrightCanTakeItsOwnBranches()
    {
        // An Emberwright borrowing nothing: every one of its twelve nodes' effects applies to its own
        // run without a refusal — the four addresses are answered, and both burns land.
        RunSession session = StartRun(EmberwrightId, level: 1, Array.Empty<ContentId>());
        EffectRegistry effects = Effects(session);

        foreach (SkillSpec spec in Nodes())
        {
            foreach (IEffect effect in spec.Effects)
            {
                Assert.That(() => effects.Apply(effect, spec), Throws.Nothing, spec.Id.Value);
            }

            if (spec.Active is not null)
            {
                foreach (IEffect effect in spec.Active.OnCast)
                {
                    Assert.That(() => effects.Apply(effect, spec), Throws.Nothing, spec.Id.Value);
                }
            }
        }
    }

    [Test]
    public void OwnTree_TheShippedTreesPass()
    {
        // RS-03b rule 7: Start now asks PlayerStats.Has of every player-aimed ModifyStat in a class's
        // own tree. The three shipped trees name only numbers their own class has. Here because this
        // file's catalog is the one holding all three shipped trees, and Tests.Core cannot open them.
        foreach (string id in new[] { OathboundId, GravecallerId, EmberwrightId })
        {
            RunSession session = null;

            Assert.DoesNotThrow(() => session = StartRun(id, level: 1, Array.Empty<ContentId>()), id);
            Assert.That(session.IsRunning, Is.True, id);
        }
    }

    // ---- The four addresses, from the asset side ---------------------------------------------------

    [Test]
    public void Stats_NoPoolRadiusAddressExists()
    {
        Assert.That(Enum.GetNames(typeof(PlayerStat)), Does.Not.Contain("PoolRadius"), "no node widens a pool.");

        string source = File.ReadAllText(PlayerStatSourcePath);
        int notHere = source.IndexOf("What is still deliberately not here", StringComparison.Ordinal);

        Assert.That(notHere, Is.GreaterThan(0), "the list is gone from the enum's remarks.");

        string list = source.Substring(notHere, source.IndexOf("</para>", notHere, StringComparison.Ordinal) - notHere);

        StringAssert.Contains("ChargeSkill.PoolRadius", list, "the third pool number is named as deliberately absent.");
        StringAssert.Contains("M7-04", list, "and with the task that would claim it.");
    }

    [Test]
    public void Stats_TheEnumIsAppendedNotInserted()
    {
        // PlayerStat's own rule: every shipped asset stores `_stat` as a raw ordinal, so each one
        // must still name the member it named before M6-08 appended four. The Ranger's effects
        // (RS-03c) were written after M6-08, and three name RS-03b's VolleyEvery, so they are skipped
        // with the Emberwright's.
        for (int i = 0; i < OrdinalsBeforeM608.Length; i++)
        {
            Assert.That(((PlayerStat)i).ToString(), Is.EqualTo(OrdinalsBeforeM608[i]), $"ordinal {i} moved.");
        }

        int checkedAssets = 0;

        foreach (string path in ContentValidationTests.PathsOf<EffectDefinition>())
        {
            if (path.StartsWith(EffectDir, StringComparison.Ordinal)
                || path.StartsWith(RangerEffectDir, StringComparison.Ordinal))
            {
                continue;
            }

            var definition = AssetDatabase.LoadAssetAtPath<ModifyStatDefinition>(path);

            if (definition == null)
            {
                continue;
            }

            int ordinal = new SerializedObject(definition).FindProperty("_stat").intValue;

            Assert.That(ordinal, Is.LessThan(OrdinalsBeforeM608.Length), $"{path} stores an M6-08 ordinal.");
            Assert.That(((ModifyStat)definition.ToEffect()).Stat.ToString(), Is.EqualTo(OrdinalsBeforeM608[ordinal]), path);

            checkedAssets++;
        }

        Assert.That(checkedAssets, Is.GreaterThan(10), "the sweep found the other two trees' effects.");
    }

    [Test]
    public void Ember_BothTogetherAreTheBiggestMultiplierInTheBuild()
    {
        // Rule 6: 40 stacks at 3 % is +120 %, against the base ramp's 30 at 2 % = +60 %.
        (PlayerCombat combat, PlayerStats stats) = EmberwrightCombat();
        var handler = new ModifyStatHandler(stats);

        handler.Apply((ModifyStat)Node("StokedCoals").Effects[0], this);
        handler.Apply((ModifyStat)Node("LongBurn").Effects[0], this);

        for (int i = 0; i < 50; i++)
        {
            combat.Kindling.OnWeaponHitLanded();
        }

        Assert.That(combat.Kindling.Stacks, Is.EqualTo(40), "the cap moved to 40.");
        Assert.That(combat.Kindling.Bonus, Is.EqualTo(1.20f).Within(Tolerance), "+120 %.");

        // Above every other v1 tree's single biggest percentage, Pacts included.
        float best = 0f;

        foreach (string path in new[] { OathboundTreePath, GravecallerTreePath })
        {
            foreach (SkillBranchSpec branch in LoadTree(path).Branches)
            {
                for (int t = 1; t <= branch.TierCount; t++)
                {
                    foreach (ContentId id in branch.Tier(t))
                    {
                        SkillSpec spec = CatalogSkill(id);

                        best = MathF.Max(best, BiggestPercent(spec.Effects));

                        if (spec.Pact is not null)
                        {
                            best = MathF.Max(best, BiggestPercent(spec.Pact.Effects));
                        }
                    }
                }
            }
        }

        Assert.That(best, Is.GreaterThan(0f), "the premise: the sweep read something.");
        Assert.That(combat.Kindling.Bonus, Is.GreaterThan(best), $"another tree's best is +{best:P0}.");
    }

    [Test]
    public void Ash_BothTogetherMakeAPoolWorthFiftyFour()
    {
        // Rule 7: 4 → 6 a pulse, 3 → 4.5 s, so 9 pulses of 6.
        (PlayerCombat combat, PlayerStats stats, ZoneSystem zones, RecordingEvents events, EnemyAgent body) = Pool();
        var handler = new ModifyStatHandler(stats);

        handler.Apply((ModifyStat)Node("ScorchingGround").Effects[0], this);
        handler.Apply((ModifyStat)Node("LingeringAsh").Effects[0], this);

        float before = body.Health.Current;

        Blink(combat, zones, 0f);

        Assert.That(zones.Count, Is.EqualTo(1), "the blink left a pool.");

        for (int pulse = 1; pulse <= 12; pulse++)
        {
            zones.Tick(pulse * MovementSkillSpec.PoolPulseInterval);
        }

        Assert.That(events.Count<ZoneBurned>(), Is.EqualTo(9), "nine pulses.");
        Assert.That(before - body.Health.Current, Is.EqualTo(54f).Within(Tolerance), "9 × 6.");
    }

    [Test]
    public void Ash_ANodeMovesTheNextBlinkAndNotTheBurningOne()
    {
        (PlayerCombat combat, PlayerStats stats, ZoneSystem zones, RecordingEvents events, EnemyAgent _) = Pool();

        Blink(combat, zones, 0f);

        // Lingering Ash taken while the first pool burns.
        new ModifyStatHandler(stats).Apply((ModifyStat)Node("LingeringAsh").Effects[0], this);

        zones.Tick(3f);

        Assert.That(events.Count<ZoneExpired>(), Is.EqualTo(1), "the standing pool expired on its old clock.");

        Blink(combat, zones, 10f);

        IReadOnlyList<ZoneSpawned> spawned = events.Of<ZoneSpawned>();

        Assert.That(spawned, Has.Count.EqualTo(2));
        Assert.That(spawned[0].Duration, Is.EqualTo(3f).Within(Tolerance), "read at the first drop.");
        Assert.That(spawned[1].Duration, Is.EqualTo(4.5f).Within(Tolerance), "the next blink lasts 4.5 s.");
    }

    // ---- Rule 9 and the boot catalog ---------------------------------------------------------------

    [Test]
    public void Run_AnEmberwrightStartsWithItsOwnTree()
    {
        RunSession session = StartRun(EmberwrightId, level: 1, Array.Empty<ContentId>());

        foreach (string id in NodeIds)
        {
            bool expected = Array.IndexOf(TierOneIds, id) >= 0;

            Assert.That(session.State.IsNodeAvailable(new ContentId(id)), Is.EqualTo(expected), id);
        }

        foreach (string path in new[] { OathboundTreePath, GravecallerTreePath })
        {
            foreach (ContentId id in Ids(LoadTree(path)))
            {
                Assert.That(session.State.IsNodeAvailable(id), Is.False, $"an Emberwright was offered {id}.");
            }
        }
    }

    [Test]
    public void Run_TheOtherTwoAreUnchanged()
    {
        // Rule 9: each of the other two walks its full twelve, reaches none of the Emberwright's,
        // and its tree asset references nothing of this task's. That neither file is in `git
        // status` is checked at handover, where a shell can see it.
        foreach ((string id, string path) in new[] { (OathboundId, OathboundTreePath), (GravecallerId, GravecallerTreePath) })
        {
            SkillTreeSpec tree = LoadTree(path);
            ContentId[] all = Ids(tree);

            Assert.That(all, Has.Length.EqualTo(12), path);

            RunSession session = StartRun(id, level: 13, all);

            Assert.That(session.State.IsTreeFull, Is.True, $"{id} walked its full tree.");
            Assert.That(session.State.TakenNodeCount, Is.EqualTo(12));

            foreach (string ember in NodeIds)
            {
                Assert.That(session.State.IsNodeAvailable(new ContentId(ember)), Is.False, $"{id} can reach {ember}.");
            }

            string text = File.ReadAllText(path);

            foreach (string assetName in NodeAssets)
            {
                string guid = AssetDatabase.AssetPathToGUID($"{SkillDir}/{assetName}.asset");

                StringAssert.DoesNotContain(guid, text, $"{path} references {assetName}.");
            }
        }
    }

    [Test]
    public void Boot_RegistersTheThirdTreeAndTwelveSkills()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);
        var scope = prefab.GetComponent<Soulvail.Game.Composition.BootScope>();

        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        var serialized = new SerializedObject(scope);
        SerializedProperty skills = serialized.FindProperty("_skills");
        SerializedProperty trees = serialized.FindProperty("_trees");

        Assert.That(skills.arraySize, Is.EqualTo(45), "Thirty-six since M6-08, and RS-03c's nine.");
        Assert.That(trees.arraySize, Is.EqualTo(4));

        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < skills.arraySize; i++)
        {
            var node = skills.GetArrayElementAtIndex(i).objectReferenceValue as SkillDefinition;

            Assert.That(node, Is.Not.Null, $"_skills[{i}] is an empty slot.");
            Assert.That(ids.Add(node.Id), Is.True, $"_skills[{i}] repeats {node.Id}.");
        }

        foreach (string id in NodeIds)
        {
            Assert.That(ids, Contains.Item(id), $"{id} is authored and not in the boot catalog.");
        }

        ContentCatalog catalog = Catalog();

        foreach (string id in new[] { OathboundId, GravecallerId, EmberwrightId })
        {
            Assert.That(catalog.TryGetTreeFor(new ContentId(id), out SkillTreeSpec _), Is.True, id);
        }
    }

    [Test]
    public void Tree_TheTreelessSkipIsGone()
    {
        // M6-07a rule 9's expiry, M5-06b's precedent: the named skip and the row that dated it are
        // deleted rather than moved, and this keeps them deleted.
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (MemberInfo member in typeof(SkillTreeValidationTests).GetMembers(All))
        {
            StringAssert.DoesNotContain("Treeless", member.Name, "the M6-07a skip outlived M6-08.");
        }
    }

    // ---- Rule 11: the third curve, logged ----------------------------------------------------------

    [Test]
    public void Ttk_TheThirdCurveIsMeasurable()
    {
        // Logged, not asserted: the assertion is M8-05's and the number is M6-11's (rule 11).
        (PlayerCombat combat, PlayerStats stats) = EmberwrightCombat();
        var handler = new ModifyStatHandler(stats);

        float baseDamage = combat.Weapon.Damage.Value;

        foreach (string assetName in new[] { "EmberTouch", "StokedCoals", "QuickenedFlame", "LongBurn" })
        {
            handler.Apply((ModifyStat)Node(assetName).Effects[0], this);
        }

        // Read before the ramp rather than divided out after: Kindling is a PercentAdd pooled with
        // Ember Touch's, so the hot number is base × (1 + 0.15 + 1.20), not cold × 2.20.
        float cold = combat.Weapon.Damage.Value;

        for (int i = 0; i < 40; i++)
        {
            combat.Kindling.OnWeaponHitLanded();
        }

        float hot = combat.Weapon.Damage.Value;

        float husk = Load<EnemyDefinition>(HuskPath).ToSpec().MaxHp
            * Load<ModeDefinition>(DescentPath).ToSpec().Scaling.Hp.At(15);

        Debug.Log(
            $"[M6-08 Ttk] stage-15 Husk {husk:F1} HP · orb {baseDamage:F1} base · full Ember {cold:F1} cold "
                + $"({Hits(husk, cold)} hits), {hot:F1} at a full 40-stack ramp ({Hits(husk, hot)} hits)");

        Assert.That(hot, Is.GreaterThan(baseDamage), "the premise: the branch moved the orb.");
    }

    // ---- Fixtures ----------------------------------------------------------------------------------

    private static int Hits(float hp, float damage) => (int)MathF.Ceiling(hp / damage);

    private static float BiggestPercent(IReadOnlyList<IEffect> effects)
    {
        float best = 0f;

        foreach (IEffect effect in effects)
        {
            if (effect is ModifyStat modify && modify.Kind != ModifierKind.Flat)
            {
                best = MathF.Max(best, modify.Value);
            }
        }

        return best;
    }

    private static void AssertActive(
        string assetName, float cooldown, TriggerField field, float radius, float duration, float damage)
    {
        SkillSpec spec = Node(assetName);

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(spec.Active.Cooldown, Is.EqualTo(cooldown).Within(Tolerance));
        Assert.That(spec.Active.Trigger.Clauses, Has.Count.EqualTo(1));

        // By name, for M3-12c's Bulwark reason: TriggerField serialises as a raw int.
        Assert.That(spec.Active.Trigger.Clauses[0].Field, Is.EqualTo(field));
        Assert.That(spec.Active.Trigger.Clauses[0].Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(spec.Active.Trigger.Clauses[0].Threshold, Is.EqualTo(3f).Within(Tolerance));

        Assert.That(spec.Active.OnCast, Has.Count.EqualTo(1));

        var burn = (SpawnBurnZone)spec.Active.OnCast[0];

        Assert.That(burn.Radius, Is.EqualTo(radius).Within(Tolerance));
        Assert.That(burn.Duration, Is.EqualTo(duration).Within(Tolerance));
        Assert.That(burn.DamagePerPulse, Is.EqualTo(damage).Within(Tolerance));
        Assert.That(spec.Effects, Is.Empty);
    }

    private static void AssertStat(string assetName, PlayerStat stat, ModifierKind kind, float value)
    {
        SkillSpec spec = Node(assetName);

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Passive), assetName);
        Assert.That(spec.Effects, Has.Count.EqualTo(1), assetName);

        var modify = (ModifyStat)spec.Effects[0];

        Assert.That(modify.Stat, Is.EqualTo(stat), assetName);
        Assert.That(modify.Kind, Is.EqualTo(kind), assetName);
        Assert.That(modify.Value, Is.EqualTo(value).Within(1e-6f), assetName);
        Assert.That(modify.Target, Is.EqualTo(StatTarget.Player), assetName);
    }

    private static void AssertAt(string id, int branch, int tier)
    {
        Assert.That(Tree().TryLocate(new ContentId(id), out int b, out int t), Is.True, id);
        Assert.That(b, Is.EqualTo(branch), $"{id} branch.");
        Assert.That(t, Is.EqualTo(tier), $"{id} tier.");
    }

    /// <summary>A tree's twelve ids in an order a restore accepts: branch by branch, tier 1 first.</summary>
    private static ContentId[] Ids(SkillTreeSpec tree)
    {
        var ids = new List<ContentId>(12);

        foreach (SkillBranchSpec branch in tree.Branches)
        {
            for (int t = 1; t <= branch.TierCount; t++)
            {
                ids.AddRange(branch.Tier(t));
            }
        }

        return ids.ToArray();
    }

    /// <summary>The six tier-1 nodes of <paramref name="characterId"/>'s tree — CH §5.4's half.</summary>
    private RunSession AtHalfTree(string characterId)
    {
        string path = characterId == OathboundId ? OathboundTreePath : GravecallerTreePath;
        var half = new List<ContentId>(6);

        foreach (SkillBranchSpec branch in LoadTree(path).Branches)
        {
            half.AddRange(branch.Tier(1));
        }

        return StartRun(characterId, level: 7, half.ToArray());
    }

    private static EffectRegistry Effects(RunSession session)
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        return (EffectRegistry)typeof(RunState).GetProperty("Effects", Hidden).GetValue(session.State);
    }

    /// <summary>The shipped Emberwright's combat and address table, off its own asset.</summary>
    private static (PlayerCombat, PlayerStats) EmberwrightCombat()
    {
        var events = new RecordingEvents();
        var combat = new PlayerCombat(
            Load<CharacterDefinition>(EmberwrightPath).ToSpec(), events, new RecordingIntents(), Capacity);
        var motor = new PlayerMotor(new MovementSpec(3.4f, 0.06f, 0.08f, 720f), NVector3.UnitZ);

        return (combat, new PlayerStats(combat, motor, new LevelTracker(Load<ModeDefinition>(DescentPath).ToSpec().Xp, events)));
    }

    /// <summary>
    /// The shipped Emberwright over a zone system wired to burn, with one tough body standing where
    /// the pool will land — the body is a fixture's, so nine pulses cannot kill what they measure.
    /// </summary>
    private static (PlayerCombat, PlayerStats, ZoneSystem, RecordingEvents, EnemyAgent) Pool()
    {
        var events = new RecordingEvents();
        CharacterSpec character = Load<CharacterDefinition>(EmberwrightPath).ToSpec();
        var combat = new PlayerCombat(character, events, new RecordingIntents(), Capacity);
        var motor = new PlayerMotor(new MovementSpec(3.4f, 0.06f, 0.08f, 720f), NVector3.UnitZ);
        var stats = new PlayerStats(combat, motor, new LevelTracker(Load<ModeDefinition>(DescentPath).ToSpec().Xp, events));

        ModeSpec descent = Load<ModeDefinition>(DescentPath).ToSpec();
        var tough = new EnemySpec(
            new ContentId("enemy.husk"),
            new LocKey("enemy.husk.name"),
            1_000f,
            2f,
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

        var enemies = new EnemySystem(
            new ContentCatalog(new[] { character }, new[] { tough }, new[] { descent }),
            events,
            new FixedRandom(7),
            new DepthScaling(descent.Scaling),
            Capacity);

        var zones = new ZoneSystem(combat.Health, combat.Blackboard, events, enemies, combat);

        // A blink goes 10 m along +Z and drops the pool where it started, at the origin.
        EnemyAgent body = enemies.Spawn(new ContentId("enemy.husk"), new NVector3(1f, 0f, 0f));

        return (combat, stats, zones, events, body);
    }

    /// <summary>A press and the tick that spends it, with the zones handed down as RunSession does.</summary>
    private static void Blink(PlayerCombat combat, ZoneSystem zones, float now)
    {
        var snapshot = new WorldSnapshot(ProjectileCapacity) { Dt = Frame, PlayerPosition = NVector3.Zero };

        combat.Charge.Request(now);
        combat.Tick(Frame, now, snapshot, ReadOnlySpan<EnemyAgent>.Empty, NVector3.UnitZ, null, null, zones);
    }

    private static SkillDefinition LoadNode(string assetName)
    {
        var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>($"{SkillDir}/{assetName}.asset");

        Assert.That(definition, Is.Not.Null, $"No SkillDefinition at {SkillDir}/{assetName}.asset.");

        return definition;
    }

    private static SkillSpec Node(string assetName) => LoadNode(assetName).ToSpec();

    private static IEnumerable<SkillSpec> Nodes()
    {
        foreach (string assetName in NodeAssets)
        {
            yield return Node(assetName);
        }
    }

    private static SkillTreeSpec Tree() => LoadTree(TreePath);

    private static SkillTreeSpec LoadTree(string path) => Load<SkillTreeDefinition>(path).ToSpec();

    private static SkillSpec CatalogSkill(ContentId id)
    {
        Assert.That(Catalog().TryGetSkill(id, out SkillSpec spec), Is.True, id.Value);

        return spec;
    }

    /// <summary>The shipped catalog: all three classes, their trees and all thirty-six nodes.</summary>
    private static ContentCatalog Catalog()
    {
        var skills = new List<SkillSpec>(45);

        foreach (string path in ContentValidationTests.PathsOf<SkillDefinition>())
        {
            skills.Add(Load<SkillDefinition>(path).ToSpec());
        }

        Assert.That(skills, Has.Count.EqualTo(45), "Twelve nodes a class, three classes, and the Ranger's nine.");

        var enemies = new List<EnemySpec>(EnemyPaths.Length);

        foreach (string path in EnemyPaths)
        {
            enemies.Add(Load<EnemyDefinition>(path).ToSpec());
        }

        return new ContentCatalog(
            new[]
            {
                Load<CharacterDefinition>(OathboundPath).ToSpec(),
                Load<CharacterDefinition>(GravecallerPath).ToSpec(),
                Load<CharacterDefinition>(EmberwrightPath).ToSpec(),
            },
            enemies,
            new[] { Load<ModeDefinition>(DescentPath).ToSpec() },
            skills,
            new[] { LoadTree(OathboundTreePath), LoadTree(GravecallerTreePath), Tree() },
            new[] { Load<BossDefinition>(WardenBossPath).ToSpec() });
    }

    private static T Load<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }

    /// <summary>
    /// A run over the shipped catalog, resumed at <paramref name="level"/> with
    /// <paramref name="taken"/> already picked — <c>GravecallerTreeTests.StartRun</c>'s shape, with
    /// the take order a fixture needs to reach CH §5.4's half-tree moment without playing to it.
    /// </summary>
    private RunSession StartRun(string characterId, int level, ContentId[] taken)
    {
        var events = new RecordingEvents();
        var random = new FixedRandom(7, Alternating(4_096));
        var clock = new FixedClock(Instant);

        var session = new RunSession(
            Catalog(),
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
            new ContentId(characterId),
            random.Seed,
            1,
            new RandomState(101, 102, 103, 104, 105),
            40f,
            0f,
            120f,
            Instant,
            level,
            0f,
            0,
            taken,
            new ContentId[SkillRunner.MaxManualSlots],
            default,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>());

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(characterId),
            random.Seed,
            1,
            SpawnPlan.Empty,
            snapshot));

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

    /// <summary>A handler that does nothing, for the one row that asks only whether one answers.</summary>
    private sealed class Ignoring : IEffectHandler<SpawnBurnZone>
    {
        public void Apply(SpawnBurnZone effect, object source)
        {
        }

        public void Remove(SpawnBurnZone effect, object source)
        {
        }
    }
}
