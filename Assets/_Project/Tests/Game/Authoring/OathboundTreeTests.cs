using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Ports;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Authoring;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The twelve shipped Oathbound nodes, their thirteen effect assets and the tree that holds them:
/// what the assets say, read off the assets. <c>EnemyDefinitionTests</c>' shape one content kind
/// along.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row here opens a real file under <c>Data/</c>, and that is the whole point.</b>
/// <c>SkillAuthoringTests</c> builds its fixtures in memory because M3-02b shipped no content; this
/// fixture is the opposite bargain — the subject is the content, so a row that built its own
/// <c>SkillDefinition</c> would be testing a second tree that happens to resemble the one the game
/// boots with. The failure mode that only an asset can show is Traps §5's: a definition type that
/// loads as null off the file with nothing anywhere reporting it.
/// </para>
/// <para>
/// <b>Why the three <c>Run_*</c> rows live here rather than in <c>TimeToKillTests</c>.</b> They need
/// a <see cref="RunSession"/> built over the <em>shipped</em> catalog, and the shipped catalog means
/// <see cref="AssetDatabase"/>, which <c>Soulvail.Tests.Core</c> cannot reach (M0-10). This is
/// M3-12b's <c>Definition_*</c> placement problem one task on and it is answered the same way: the
/// row goes in the assembly that can open the file, and the fixture on the other side of the seam
/// keeps the numbers it can assert without one.
/// </para>
/// <para>
/// <b>Seven of the spec's twelve ids could not be constructed and were renamed before a single asset
/// was authored.</b> <see cref="ContentId"/>'s grammar is <c>^[a-z0-9]+(\.[a-z0-9_-]+)+$</c>, so
/// <c>skill.oathbound.temperedVow</c> throws — the finding M3-02b recorded and handed forward. The
/// shipped ids are kebab-case and <see cref="Tree_IdsAreKebabCaseAndConstructible"/> is the row that
/// says so. <see cref="LocKey"/> forbids only whitespace, so the twenty-four keys were legal either
/// way and were <em>not</em> touched.
/// </para>
/// </remarks>
[TestFixture]
public sealed class OathboundTreeTests
{
    private const string SkillDir = "Assets/_Project/Data/Skills/Oathbound";
    private const string EffectDir = "Assets/_Project/Data/Effects/Oathbound";
    // Renamed from OathboundTree.asset by M3-14b: CLAUDE.md's asset-naming rule wants a data
    // asset's file name to be the last segment of its id, and `tree.oathbound` ends in `oathbound`.
    // `Data/Trees/` is what says it is a tree. The GUID survived the rename, so BootScope's
    // reference never moved; only this constant and two others did.
    private const string TreePath = "Assets/_Project/Data/Trees/Oathbound.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    /// <summary>
    /// All three, not just the Husk: <c>Descent.asset</c>'s roster names every one of them, and
    /// <c>RunSession.Start</c> resolves the whole roster before it announces anything. A catalog one
    /// enemy short refuses the run with a <c>KeyNotFoundException</c> — which is that check working,
    /// and how this fixture found out it had to ship the real roster rather than a convenient subset.
    /// </summary>
    private static readonly string[] EnemyPaths =
    {
        "Assets/_Project/Data/Enemies/Husk.asset",
        "Assets/_Project/Data/Enemies/Spitter.asset",
        "Assets/_Project/Data/Enemies/Bloater.asset",
    };
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";

    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";

    /// <summary>The twelve asset file names, in the order the tree lists them.</summary>
    private static readonly string[] NodeAssets =
    {
        "TemperedVow", "SteadyBreath", "Bulwark", "Unbowed",
        "KeenCenser", "LongReach", "BroadCensure", "CrashingCensure",
        "Consecrate", "Zealotry", "Retribution", "LastingGround",
    };

    /// <summary>The same twelve as content ids — kebab-case, because camelCase does not construct.</summary>
    private static readonly string[] NodeIds =
    {
        "skill.oathbound.tempered-vow", "skill.oathbound.steady-breath",
        "skill.oathbound.bulwark", "skill.oathbound.unbowed",
        "skill.oathbound.keen-censer", "skill.oathbound.long-reach",
        "skill.oathbound.broad-censure", "skill.oathbound.crashing-censure",
        "skill.oathbound.consecrate", "skill.oathbound.zealotry",
        "skill.oathbound.retribution", "skill.oathbound.lasting-ground",
    };

    /// <summary>The six tier-1 nodes: what an untouched tree may offer (M3-03 rule 2).</summary>
    private static readonly string[] TierOneIds =
    {
        "skill.oathbound.tempered-vow", "skill.oathbound.steady-breath",
        "skill.oathbound.keen-censer", "skill.oathbound.long-reach",
        "skill.oathbound.consecrate", "skill.oathbound.zealotry",
    };

    private const int Capacity = 64;
    private const int DeviceCap = 28;
    private const int ProjectileCapacity = 8;

    private const float Tolerance = 1e-6f;

    private static readonly DateTimeOffset Instant =
        DateTimeOffset.FromUnixTimeSeconds(1_700_000_000L);

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

    // ---- Rule 10: the tree's shape ---------------------------------------------------------------

    [Test]
    public void Tree_HasTwelveNodesInThreeBranches()
    {
        SkillTreeSpec tree = Tree();

        Assert.That(tree.NodeCount, Is.EqualTo(12));
        Assert.That(tree.Branches, Has.Count.EqualTo(SkillTreeSpec.BranchCount));

        for (int b = 0; b < tree.Branches.Count; b++)
        {
            SkillBranchSpec branch = tree.Branches[b];

            Assert.That(branch.TierCount, Is.EqualTo(2), $"branch {b} is two tiers deep.");
            Assert.That(branch.NodeCount, Is.EqualTo(4), $"branch {b} holds four nodes.");
            Assert.That(branch.Tier(1), Has.Count.EqualTo(2));
            Assert.That(branch.Tier(2), Has.Count.EqualTo(2));
        }
    }

    [Test]
    public void Tree_NamesTheOathbound()
    {
        // What ContentCatalog.TryGetTreeFor keys on: a run resolves its tree from the class it is
        // playing, never from a tree id anybody typed.
        Assert.That(Tree().CharacterId, Is.EqualTo(new ContentId(OathboundId)));
        Assert.That(Tree().Id, Is.EqualTo(new ContentId("tree.oathbound")));
    }

    [Test]
    public void Tree_HasNoKeystone()
    {
        // **M3-02a rule 8's "a partial tree is legal", asserted rather than assumed.** v1 ships no
        // keystone at all, and TreeRules' placement rule deliberately permits a last tier that is
        // not one — see RequireKeystonePlacement's own remarks, which name this content.
        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(spec.Kind, Is.Not.EqualTo(SkillKind.Keystone), $"{spec.Id} is a keystone.");
        }
    }

    [Test]
    public void Tree_ConsecrateIsTierOne()
    {
        // Rule 3: CH §4's "an early pick reshapes the whole rest of the run" wants an Active the
        // player can reach on their first pick, and this is it.
        AssertAt("skill.oathbound.consecrate", branch: 2, tier: 1);
    }

    [Test]
    public void Tree_BulwarkIsTierTwo()
    {
        AssertAt("skill.oathbound.bulwark", branch: 0, tier: 2);
    }

    [Test]
    public void Tree_UpgradeParentIsInBranchBelow()
    {
        // Rule 2: TreeRules' cross-check satisfied rather than argued. An Upgrade whose parent sat
        // in another branch would let one branch wait on a pick the player may never make, and the
        // construction below is the only place that rule is ever asked.
        TreeRules rules = null;

        Assert.That(() => rules = new TreeRules(Tree(), Catalog()), Throws.Nothing);

        var lastingGround = new ContentId("skill.oathbound.lasting-ground");

        Assert.That(rules.TryGetParent(lastingGround, out ContentId parent), Is.True);
        Assert.That(parent, Is.EqualTo(new ContentId("skill.oathbound.consecrate")));

        Tree().TryLocate(lastingGround, out int childBranch, out int childTier);
        Tree().TryLocate(parent, out int parentBranch, out int parentTier);

        Assert.That(parentBranch, Is.EqualTo(childBranch), "Same branch (Judgment).");
        Assert.That(parentTier, Is.LessThan(childTier), "And below it.");
    }

    // ---- Rule 4: the twenty-four keys, and the three branch keys ----------------------------------

    [Test]
    public void Tree_EveryNodeHasTwoKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(spec.NameKey.Key, Is.Not.Null, $"{spec.Id} has no nameKey.");
            Assert.That(spec.DescriptionKey.Key, Is.Not.Null, $"{spec.Id} has no descriptionKey.");

            Assert.That(keys.Add(spec.NameKey.Key), Is.True, $"duplicate key {spec.NameKey}.");
            Assert.That(
                keys.Add(spec.DescriptionKey.Key), Is.True, $"duplicate key {spec.DescriptionKey}.");
        }

        // **Ledger row 9 made concrete.** Twenty-four keys, none of which resolves to anything
        // until M3-14a writes the English table — and they are now on four screens rather than in
        // a spec.
        Assert.That(keys, Has.Count.EqualTo(24));
    }

    [Test]
    public void Tree_KeysAreDerivedFromTheId()
    {
        // The project's convention everywhere else — `character.oathbound.name`, `enemy.husk.name` —
        // rather than the spec's `<id>` and `<id>.desc`. A name key equal to the id itself would make
        // the two strings identical, which is exactly the confusion LocKey exists as a separate type
        // to prevent (its own remarks).
        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(spec.NameKey.Key, Is.EqualTo(spec.Id.Value + ".name"));
            Assert.That(spec.DescriptionKey.Key, Is.EqualTo(spec.Id.Value + ".description"));
        }
    }

    [Test]
    public void Tree_BranchesAreKeyed()
    {
        SkillTreeSpec tree = Tree();

        Assert.That(tree.Branches[0].NameKey.Key, Is.EqualTo("tree.oathbound.oath"));
        Assert.That(tree.Branches[1].NameKey.Key, Is.EqualTo("tree.oathbound.censure"));
        Assert.That(tree.Branches[2].NameKey.Key, Is.EqualTo("tree.oathbound.judgment"));
    }

    // ---- Rules 5 and 6: what the tree deliberately does not carry ---------------------------------

    [Test]
    public void Tree_AvoidsTheKeystoneNames()
    {
        // Rule 5: CH §3.1 reserves Unbroken, Wide Censure and Martyr for M7-04, each a build-defining
        // node with a drawback v1 does not ship. *Broad* Censure is deliberately not *Wide* Censure —
        // a 90° cone is a flank-cover node and a 360° ring at 60 % damage is a different thing.
        string[] reserved = { "unbroken", "wide-censure", "wideCensure", "martyr" };

        foreach (SkillSpec spec in Nodes())
        {
            foreach (string name in reserved)
            {
                Assert.That(
                    spec.Id.Value,
                    Is.Not.EqualTo("skill.oathbound." + name),
                    $"{spec.Id} takes a keystone's name, which would make M7-04's feel like a repeat.");
            }
        }
    }

    [Test]
    public void Tree_CarriesNoPact()
    {
        // Rule 6: GD §13.2's corrupted nodes are M6-05's and have no shape yet. The honest form of
        // this row is that no node carries an effect type that does not exist in this build — so it
        // is written as a whitelist of the five primitives that do, and a sixth appearing without a
        // decision reddens it.
        Type[] shipped =
        {
            typeof(ModifyStat), typeof(GrantShield), typeof(SpawnHealZone),
            typeof(ModifySkillCooldown), typeof(KnockbackOnSwing),
        };

        foreach (SkillSpec spec in Nodes())
        {
            AssertAllOf(spec.Effects, shipped, spec.Id);

            if (spec.Active is not null)
            {
                AssertAllOf(spec.Active.OnCast, shipped, spec.Id);
            }
        }
    }

    // ---- The ruling this task had to make before it could author anything -------------------------

    [Test]
    public void Tree_IdsAreKebabCaseAndConstructible()
    {
        // **Seven of the spec's own twelve ids throw.** ContentId.IsHeadChar is [a-z0-9] and
        // IsTailChar adds only '_' and '-', so temperedVow, steadyBreath, keenCenser, longReach,
        // broadCensure, crashingCensure and lastingGround are all refused — the finding M3-02b
        // recorded and handed to M3-14b. The rename is what this row pins, and the refusal it pins
        // against is asserted rather than assumed, so the row cannot pass on a grammar that quietly
        // widened.
        Assert.That(ContentId.IsValid("skill.oathbound.temperedVow"), Is.False,
            "If this ever passes, the grammar moved and the rename is no longer load-bearing.");

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(ContentId.IsValid(spec.Id.Value), Is.True);
            Assert.That(spec.Id.Value, Does.StartWith("skill.oathbound."));
            Assert.That(ids.Add(spec.Id.Value), Is.True, $"duplicate id {spec.Id}.");
        }

        Assert.That(ids, Is.EquivalentTo(NodeIds));
    }

    [Test]
    public void Nodes_FileNamesMapToTheirIds()
    {
        // **A mapping rather than a match**, which is what the kebab rename does to CLAUDE.md's
        // asset-naming rule ("a data asset's file name matches the last segment of its ContentId").
        // `TemperedVow.asset` ↔ `skill.oathbound.tempered-vow`: hyphen-to-PascalCase, the spelling
        // M3-02b predicted M3-14b would need.
        for (int i = 0; i < NodeAssets.Length; i++)
        {
            SkillDefinition definition = LoadNode(NodeAssets[i]);

            string segment = definition.Id.Substring(definition.Id.LastIndexOf('.') + 1);
            string expected = string.Empty;

            foreach (string word in segment.Split('-'))
            {
                expected += char.ToUpperInvariant(word[0]) + word.Substring(1);
            }

            Assert.That(
                definition.name,
                Is.EqualTo(expected),
                $"{definition.name}.asset carries id '{definition.Id}'.");
        }
    }

    [Test]
    public void Nodes_AreLinkedToAMonoScript()
    {
        // Traps §5, and the only row that would go red for it: a ScriptableObject declared with a
        // file-scoped namespace loads as null off any asset referencing it, with no error anywhere.
        // With twenty-six assets shipped for the first time, this is the row that says they are real.
        foreach (string assetName in NodeAssets)
        {
            Assert.That(
                MonoScript.FromScriptableObject(LoadNode(assetName)),
                Is.Not.Null,
                $"{assetName}.asset is not linked to a MonoScript (Traps §5).");
        }

        SkillTreeDefinition tree =
            AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(TreePath);

        Assert.That(tree, Is.Not.Null, $"No SkillTreeDefinition at {TreePath}.");
        Assert.That(MonoScript.FromScriptableObject(tree), Is.Not.Null);
    }

    // ---- Rules 1 and 10: the two Actives' authored numbers ----------------------------------------

    [Test]
    public void Consecrate_CarriesItsAuthoredNumbers()
    {
        SkillSpec spec = Node("Consecrate");

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(spec.Active, Is.Not.Null);
        Assert.That(spec.Active.Cooldown, Is.EqualTo(12f).Within(Tolerance));

        Assert.That(spec.Active.Trigger.Clauses, Has.Count.EqualTo(1));
        Assert.That(spec.Active.Trigger.Clauses[0].Field, Is.EqualTo(TriggerField.HpFraction));
        Assert.That(spec.Active.Trigger.Clauses[0].Comparison, Is.EqualTo(TriggerComparison.Below));
        Assert.That(spec.Active.Trigger.Clauses[0].Threshold, Is.EqualTo(0.6f).Within(Tolerance));

        Assert.That(spec.Active.OnCast, Has.Count.EqualTo(1));

        var zone = (SpawnHealZone)spec.Active.OnCast[0];

        Assert.That(zone.Radius, Is.EqualTo(3.5f).Within(Tolerance));
        Assert.That(zone.Duration, Is.EqualTo(6f).Within(Tolerance));
        Assert.That(zone.HealPerPulse, Is.EqualTo(3f).Within(Tolerance));
        Assert.That(zone.PulseInterval, Is.EqualTo(0.5f).Within(Tolerance));

        // An Active is the one kind that may take with no effects: its power is on cast.
        Assert.That(spec.Effects, Is.Empty);
    }

    [Test]
    public void Bulwark_CarriesItsAuthoredNumbers()
    {
        SkillSpec spec = Node("Bulwark");

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(spec.Active.Cooldown, Is.EqualTo(8f).Within(Tolerance));

        Assert.That(spec.Active.Trigger.Clauses, Has.Count.EqualTo(1));

        // **Authored as EnemiesWithin8m on the first pass and caught by a probe, not by a compile.**
        // TriggerField's members are 0 HpFraction, 1 ShieldFraction, 2 EnemiesWithin6m,
        // 3 EnemiesWithin8m, 4 EnemiesInAcquireRange, 5 IncomingProjectiles — so the wrong ordinal
        // is a legal asset that fires at the wrong moment for ever. This row is the guard.
        Assert.That(
            spec.Active.Trigger.Clauses[0].Field,
            Is.EqualTo(TriggerField.IncomingProjectiles),
            "CC §6.4's Bulwark trigger: a shield that arrives before the bolt, not after it.");

        Assert.That(spec.Active.Trigger.Clauses[0].Comparison, Is.EqualTo(TriggerComparison.AtLeast));
        Assert.That(spec.Active.Trigger.Clauses[0].Threshold, Is.EqualTo(1f).Within(Tolerance));

        var grant = (GrantShield)spec.Active.OnCast[0];

        Assert.That(grant.Amount, Is.EqualTo(35f).Within(Tolerance));
        Assert.That(grant.Duration, Is.EqualTo(5f).Within(Tolerance));

        Assert.That(spec.Effects, Is.Empty);
    }

    // ---- Rule 10: the four rule nodes and the six stat nodes --------------------------------------

    [Test]
    public void RuleNodes_CarryTheirPrimitives()
    {
        var shove = (KnockbackOnSwing)Node("CrashingCensure").Effects[0];
        Assert.That(shove.Distance, Is.EqualTo(1.5f).Within(Tolerance));

        var cone = (ModifyStat)Node("BroadCensure").Effects[0];
        Assert.That(cone.Stat, Is.EqualTo(PlayerStat.WeaponConeAngle));
        Assert.That(cone.Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(cone.Value, Is.EqualTo(0.5f).Within(Tolerance));

        // **Flat, and it has to be.** HealPerKill's base is 0 and Stat computes
        // (Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult), so a percentage heals nothing —
        // M3-12a measured +500 % as 0. Pinned here rather than trusted to the spec's table, because
        // the live risk is a retune that reaches for a percentage.
        var heal = (ModifyStat)Node("Retribution").Effects[0];
        Assert.That(heal.Stat, Is.EqualTo(PlayerStat.HealPerKill));
        Assert.That(
            heal.Kind,
            Is.EqualTo(ModifierKind.Flat),
            "A percentage of a base of zero is zero, silently, for ever (M3-12a).");
        Assert.That(heal.Value, Is.EqualTo(2f).Within(Tolerance));

        var cooldown = (ModifySkillCooldown)Node("LastingGround").Effects[0];
        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(cooldown.Value, Is.EqualTo(-0.25f).Within(Tolerance));

        // **Compared against Consecrate's own asset, never against a literal.**
        // ModifySkillCooldownDefinition._skillId is a serialized *string*, and M3-12b rule 3 holds a
        // modifier for a skill the run does not own rather than throwing — deliberately, because a
        // Passive carrying one is gated by nothing. So a typo here is not an error anywhere: it is a
        // pending note waiting for a skill that can never arrive. Two matching literals would prove
        // only that this file agrees with itself.
        Assert.That(
            cooldown.SkillId,
            Is.EqualTo(Node("Consecrate").Id),
            "Lasting Ground must name the id Consecrate.asset actually carries.");
    }

    [Test]
    public void StatNodes_CarryTheirNumbers()
    {
        AssertStat("TemperedVow", PlayerStat.MaxHp, ModifierKind.Flat, 15f);
        AssertStat("SteadyBreath", PlayerStat.ShieldRechargeDelay, ModifierKind.PercentAdd, -0.25f);
        AssertStat("KeenCenser", PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f);
        AssertStat("LongReach", PlayerStat.WeaponRange, ModifierKind.Flat, 1.5f);
        AssertStat("Zealotry", PlayerStat.FireRate, ModifierKind.PercentAdd, 0.12f);

        // Unbowed is M3-05 rule 6's "+2 damage and +15 %" case: two ModifyStats from one source, not
        // one effect carrying a list — which is why there are thirteen effect assets for twelve nodes.
        SkillSpec unbowed = Node("Unbowed");

        Assert.That(unbowed.Effects, Has.Count.EqualTo(2));

        var hp = (ModifyStat)unbowed.Effects[0];
        Assert.That(hp.Stat, Is.EqualTo(PlayerStat.MaxHp));
        Assert.That(hp.Kind, Is.EqualTo(ModifierKind.PercentAdd));
        Assert.That(hp.Value, Is.EqualTo(0.10f).Within(Tolerance));

        var speed = (ModifyStat)unbowed.Effects[1];
        Assert.That(speed.Stat, Is.EqualTo(PlayerStat.MoveSpeed));
        Assert.That(speed.Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(speed.Value, Is.EqualTo(0.3f).Within(Tolerance));
    }

    [Test]
    public void Modify_ShippedAssetsAllTargetThePlayer()
    {
        // M4-01a rule 4's ripple, asserted rather than assumed. StatTarget defaults to Player and
        // the field is new, so every one of these assets keeps meaning exactly what it meant with
        // nothing in Data/ touched — but "the default is the old behaviour" is a claim about
        // deserialisation, and a claim about deserialisation is measured against the files rather
        // than reasoned about. A Self here would be a node quietly buffing whoever last cast.
        string[] guids = AssetDatabase.FindAssets(
            $"t:{nameof(ModifyStatDefinition)}",
            new[] { EffectDir });

        Assert.That(
            guids.Length,
            Is.EqualTo(9),
            "Nine of the thirteen shipped effect assets are ModifyStats. Update the number and say "
                + "which task added one.");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var definition = AssetDatabase.LoadAssetAtPath<ModifyStatDefinition>(path);

            Assert.That(definition, Is.Not.Null, $"No ModifyStatDefinition at {path}.");

            var effect = (ModifyStat)definition.ToEffect();

            Assert.That(
                effect.Target,
                Is.EqualTo(StatTarget.Player),
                $"{path} must still aim at the player. Nothing in this milestone authors Self, and "
                    + "an asset that did would be a node whose number lands on a boss.");
        }
    }

    [Test]
    public void Tree_IsSixStatsFourRulesAndTwoActives()
    {
        // The mix M3-00c's ruling asked to be counted honestly, counted off the assets rather than
        // off the table: eight passives that move a number or a rule, plus the two Actives that are
        // M3-11's whole output, plus the Upgrade that shortens one of them.
        int actives = 0;
        int upgrades = 0;
        int passives = 0;

        foreach (SkillSpec spec in Nodes())
        {
            switch (spec.Kind)
            {
                case SkillKind.Active:
                    actives++;
                    break;

                case SkillKind.Upgrade:
                    upgrades++;
                    break;

                default:
                    passives++;
                    break;
            }
        }

        Assert.That(actives, Is.EqualTo(2));
        Assert.That(upgrades, Is.EqualTo(1));
        Assert.That(passives, Is.EqualTo(9));
    }

    // ---- Rule 7: the two boot arrays stop being empty ---------------------------------------------

    [Test]
    public void Boot_RegistersTheTreeAndTwelveSkills()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        var scope = prefab.GetComponent<Soulvail.Game.Composition.BootScope>();
        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        var serialized = new SerializedObject(scope);

        SerializedProperty skills = serialized.FindProperty("_skills");
        SerializedProperty trees = serialized.FindProperty("_trees");

        // **Empty since M3-02b, and this is the task that fills them** (M3-02b rule 7). The moment
        // the milestone becomes playable is this array reading 12 instead of 0.
        Assert.That(skills.arraySize, Is.EqualTo(12));
        Assert.That(trees.arraySize, Is.EqualTo(1));

        var ids = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < skills.arraySize; i++)
        {
            var node = skills.GetArrayElementAtIndex(i).objectReferenceValue as SkillDefinition;

            Assert.That(node, Is.Not.Null, $"_skills[{i}] is an empty slot.");
            Assert.That(ids.Add(node.Id), Is.True, $"_skills[{i}] repeats {node.Id}.");
        }

        Assert.That(ids, Is.EquivalentTo(NodeIds));

        var tree = trees.GetArrayElementAtIndex(0).objectReferenceValue as SkillTreeDefinition;
        Assert.That(tree, Is.Not.Null, "_trees[0] is an empty slot.");
        Assert.That(tree.Id, Is.EqualTo("tree.oathbound"));

        // And the answer that has been false in every build ever played.
        Assert.That(
            Catalog().TryGetTreeFor(new ContentId(OathboundId), out SkillTreeSpec _),
            Is.True,
            "M3-03 rule 10's null-tree branch retires from every real build here.");
    }

    // ---- Rule 7: a run over the shipped catalog ---------------------------------------------------

    [Test]
    public void Run_StartsWithATree()
    {
        RunSession session = StartRun(level: 1, pending: 0);

        Assert.That(session.State.TakenNodeCount, Is.Zero);
        Assert.That(session.State.IsTreeFull, Is.False);
        Assert.That(session.State.IsLevelUpPending, Is.False, "Nothing owed yet.");

        // The six tier-1 nodes are available and the six tier-2 nodes are not: a branch opens both of
        // its tier-2 nodes after a single pick, so the offer stays a real draw (M3-03 rule 2).
        foreach (string id in NodeIds)
        {
            bool expected = Array.IndexOf(TierOneIds, id) >= 0;

            Assert.That(
                session.State.IsNodeAvailable(new ContentId(id)),
                Is.EqualTo(expected),
                expected ? $"{id} is tier 1 and must be offerable" : $"{id} is tier 2 and gated");
        }
    }

    [Test]
    public void Run_FirstOfferDrawsFromTheSix()
    {
        RunSession session = StartRun(level: 2, pending: 1);

        Assert.That(session.State.HasOffer, Is.False, "The draw is lazy (M3-08a rule 1).");

        ((IProgressionCommands)session).OpenLevelUp();

        IReadOnlyList<ContentId> offer = session.State.Offer;

        Assert.That(offer, Has.Count.EqualTo(3), "Three cards, from six candidates.");

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (ContentId id in offer)
        {
            Assert.That(TierOneIds, Contains.Item(id.Value), $"{id} is not a tier-1 node.");
            Assert.That(seen.Add(id.Value), Is.True, "No replacement within one offer (M3-04).");
        }
    }

    [Test]
    public void Tree_FillsAtLevelThirteen()
    {
        // Twelve nodes, so twelve picks fill the tree and the thirteenth has nothing left to buy.
        // Level 14 earns thirteen picks; none are spent yet, so the identity holds (M3-08a rule 9).
        RunSession session = StartRun(level: 14, pending: 13);

        var commands = (IProgressionCommands)session;

        for (int i = 0; i < 12; i++)
        {
            commands.OpenLevelUp();

            Assert.That(session.State.HasOffer, Is.True, $"pick {i + 1} drew nothing.");

            commands.ChooseOffer(0);
        }

        Assert.That(session.State.TakenNodeCount, Is.EqualTo(12));
        Assert.That(session.State.IsTreeFull, Is.True);

        // The thirteenth pick is spent the moment the twelfth choice re-opens: Choose clears the
        // offer and calls Open again, which finds nothing available and grants CH §5.2's Overflow
        // rather than pausing on an empty screen.
        Assert.That(session.State.HasOffer, Is.False);
        Assert.That(session.State.OverflowLevels, Is.EqualTo(1), "The thirteenth level overflowed.");
        Assert.That(session.State.PendingLevelUps, Is.Zero, "And nothing is left banked.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private static void AssertAllOf(IReadOnlyList<IEffect> effects, Type[] shipped, ContentId id)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            Assert.That(
                shipped,
                Contains.Item(effects[i].GetType()),
                $"{id} carries a {effects[i].GetType().Name}, which is not one of this build's five "
                    + "primitives. A Pact or a Veilrot effect here would be content for a mechanic "
                    + "that cannot yet be authored (rule 6).");
        }
    }

    private static void AssertStat(
        string assetName, PlayerStat stat, ModifierKind kind, float value)
    {
        SkillSpec spec = Node(assetName);

        Assert.That(spec.Effects, Has.Count.EqualTo(1), $"{assetName} carries one effect.");

        var modify = (ModifyStat)spec.Effects[0];

        Assert.That(modify.Stat, Is.EqualTo(stat), assetName);
        Assert.That(modify.Kind, Is.EqualTo(kind), assetName);
        Assert.That(modify.Value, Is.EqualTo(value).Within(Tolerance), assetName);
    }

    private static void AssertAt(string id, int branch, int tier)
    {
        Assert.That(Tree().TryLocate(new ContentId(id), out int b, out int t), Is.True, id);
        Assert.That(b, Is.EqualTo(branch), $"{id} branch.");
        Assert.That(t, Is.EqualTo(tier), $"{id} tier — CH §5's own 1-based numbering.");
    }

    private static SkillDefinition LoadNode(string assetName)
    {
        var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>(
            $"{SkillDir}/{assetName}.asset");

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

    private static SkillTreeSpec Tree()
    {
        var definition = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(TreePath);

        Assert.That(definition, Is.Not.Null, $"No SkillTreeDefinition at {TreePath}.");

        return definition.ToSpec();
    }

    /// <summary>
    /// The shipped catalog: the Oathbound, the Husk, Descent, the twelve nodes and their tree — all
    /// read off <c>Data/</c>, which is what makes this fixture worth having its own file.
    /// </summary>
    private static ContentCatalog Catalog()
    {
        var skills = new List<SkillSpec>(12);

        foreach (SkillSpec spec in Nodes())
        {
            skills.Add(spec);
        }

        var enemies = new List<EnemySpec>(EnemyPaths.Length);

        foreach (string path in EnemyPaths)
        {
            enemies.Add(Load<EnemyDefinition>(path).ToSpec());
        }

        return new ContentCatalog(
            new[] { Load<CharacterDefinition>(OathboundPath).ToSpec() },
            enemies,
            new[] { Load<ModeDefinition>(DescentPath).ToSpec() },
            skills,
            new[] { Tree() });
    }

    private static T Load<T>(string path) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }

    /// <summary>
    /// A run over the shipped catalog, resumed at <paramref name="level"/> with
    /// <paramref name="pending"/> picks owed — <c>LevelUpPresenterTests.StartRun</c>'s shape, which
    /// is how a fixture reaches a level-up without simulating the kills that earned it.
    /// </summary>
    private RunSession StartRun(int level, int pending)
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
            new ContentId(OathboundId),
            random.Seed,
            1,
            new RandomState(101, 102, 103, 104, 105),
            90f,
            6f,
            120f,
            Instant,
            level,
            0f,
            pending,
            Array.Empty<ContentId>(),
            new ContentId[SkillRunner.MaxManualSlots]);

        session.Start(new RunConfig(
            new ContentId(DescentId),
            new ContentId(OathboundId),
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
}
