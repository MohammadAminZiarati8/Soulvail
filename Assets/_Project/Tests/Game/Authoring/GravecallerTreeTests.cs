using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Ai;
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
/// The twelve shipped Gravecaller nodes, their twelve effect assets and the tree that holds them —
/// plus <c>Descent.asset</c>'s Overflow block, which is what a level is worth once that tree is
/// full. <c>OathboundTreeTests</c>' shape, one class along.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row here opens a real file under <c>Data/</c></b>, for the reason
/// <c>OathboundTreeTests</c> gives: the subject is the content, so a row that built its own
/// <c>SkillDefinition</c> would be testing a second tree that happens to resemble the one the game
/// boots with. The failure mode only an asset can show is Traps §5's — a definition type that loads
/// as null off the file with nothing anywhere reporting it.
/// </para>
/// <para>
/// <b>Three of the twelve are the first content in the project aimed at
/// <see cref="StatTarget.Minions"/></b>, which is what makes this the Gravecaller's tree rather than
/// a recolour of the Oathbound's. M5-06a built the address space — the recipe, the target, the
/// count field and the verb — and shipped it reachable by no asset;
/// <see cref="MinionNodes_ChangeWhatAWightIsWorth"/> is the row that walks it end to end, from an
/// effect asset to the numbers a raised body stands up with.
/// </para>
/// <para>
/// <b>The two <c>Overflow_*</c> rows are ledger row 5(i) and are deliberately here.</b> They need
/// the shipped <c>Descent.asset</c>, and a shipped asset means <see cref="AssetDatabase"/>, which
/// <c>Soulvail.Tests.Core</c> cannot reach (M0-10). <c>LevelUpFlowTests</c> and
/// <c>TimeToKillTests</c> keep the numbers they can assert without one and meet these rows at 0.02
/// — the arrangement <c>TimeToKillTests</c> has had with <c>KeenCenser.asset</c> since M3-12c.
/// </para>
/// <para>
/// <b>Nothing about this class is playable yet and no row here claims otherwise</b> (M5-02 rule 9).
/// The menu writes <c>Characters[0]</c>, so the runs below are composed by hand from the shipped
/// catalog; the first Gravecaller run a person can start is M5-07's.
/// </para>
/// </remarks>
[TestFixture]
public sealed class GravecallerTreeTests
{
    private const string SkillDir = "Assets/_Project/Data/Skills/Gravecaller";
    private const string EffectDir = "Assets/_Project/Data/Effects/Gravecaller";

    /// <summary>
    /// <c>Gravecaller.asset</c> under <c>Data/Trees/</c>, not <c>GravecallerTree.asset</c>: a data
    /// asset's file name is the last segment of its id (CLAUDE.md), <c>tree.gravecaller</c> ends in
    /// <c>gravecaller</c>, and the folder is what says it is a tree. M3-14b renamed the Oathbound's
    /// for the same rule, and <c>ContentValidationTests.EveryFileName_MatchesTheLastSegmentOfItsId</c>
    /// sweeps both.
    /// </summary>
    private const string TreePath = "Assets/_Project/Data/Trees/Gravecaller.asset";

    private const string OathboundTreePath = "Assets/_Project/Data/Trees/Oathbound.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string WardenBossPath = "Assets/_Project/Data/Enemies/WardenBoss.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";

    private static readonly string[] EnemyPaths =
    {
        "Assets/_Project/Data/Enemies/Husk.asset",
        "Assets/_Project/Data/Enemies/Spitter.asset",
        "Assets/_Project/Data/Enemies/Bloater.asset",
        "Assets/_Project/Data/Enemies/Warden.asset",
    };

    private const string GravecallerId = "character.gravecaller";
    private const string OathboundId = "character.oathbound";
    private const string DescentId = "mode.descent";

    private const string ExhumeId = "skill.gravecaller.exhume";

    /// <summary>The twelve asset file names, in the order the tree lists them.</summary>
    private static readonly string[] NodeAssets =
    {
        "Exhume", "GraveStrength", "DeeperGraves", "KnittedBone",
        "SharpenedBone", "QuickHands", "MarrowTithe", "PaleVigour",
        "ShroudVeil", "RotFeast", "WitheringStep", "RestlessDead",
    };

    /// <summary>The same twelve as content ids — kebab-case, because camelCase does not construct.</summary>
    private static readonly string[] NodeIds =
    {
        ExhumeId, "skill.gravecaller.grave-strength",
        "skill.gravecaller.deeper-graves", "skill.gravecaller.knitted-bone",
        "skill.gravecaller.sharpened-bone", "skill.gravecaller.quick-hands",
        "skill.gravecaller.marrow-tithe", "skill.gravecaller.pale-vigour",
        "skill.gravecaller.shroud-veil", "skill.gravecaller.rot-feast",
        "skill.gravecaller.withering-step", "skill.gravecaller.restless-dead",
    };

    /// <summary>The six tier-1 nodes: what an untouched tree may offer (M3-03 rule 2).</summary>
    private static readonly string[] TierOneIds =
    {
        ExhumeId, "skill.gravecaller.grave-strength",
        "skill.gravecaller.sharpened-bone", "skill.gravecaller.quick-hands",
        "skill.gravecaller.shroud-veil", "skill.gravecaller.rot-feast",
    };

    /// <summary>The three that aim at the recipe rather than at the player (rule 5).</summary>
    private static readonly string[] MinionNodeAssets =
    {
        "GraveStrength", "KnittedBone", "RestlessDead",
    };

    /// <summary><c>Husk.asset</c>'s own numbers — what rule 5's three claims are measured against.</summary>
    private const float HuskMaxHp = 36f;
    private const float HuskContactDamage = 8f;
    private const float HuskMoveSpeed = 2f;

    /// <summary>What <c>Descent.asset</c> authors per Overflow level (rule 11).</summary>
    private const float OverflowPerLevel = 0.02f;

    /// <summary><c>Oathbound.asset</c>'s maximum hit points, for the Overflow rows' arithmetic.</summary>
    private const float OathboundMaxHp = 140f;

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

    // ---- The tree's shape (rules 2, 3, 4) --------------------------------------------------------

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
    public void Tree_NamesTheGravecaller()
    {
        // What ContentCatalog.TryGetTreeFor keys on: a run resolves its tree from the class it is
        // playing, never from a tree id anybody typed.
        Assert.That(Tree().CharacterId, Is.EqualTo(new ContentId(GravecallerId)));
        Assert.That(Tree().Id, Is.EqualTo(new ContentId("tree.gravecaller")));

        Assert.That(
            Catalog().TryGetTreeFor(new ContentId(GravecallerId), out SkillTreeSpec resolved),
            Is.True,
            "The class authored at M5-02 has had no tree for four tasks. This is where that ends.");

        Assert.That(resolved.Id, Is.EqualTo(Tree().Id));
    }

    [Test]
    public void Tree_HasNoKeystone()
    {
        // Rule 2, and M3-02a rule 8's "a partial tree is legal" for the second time: v1 ships no
        // Keystone at all, and TreeRules deliberately permits a last tier that is not one.
        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(spec.Kind, Is.Not.EqualTo(SkillKind.Keystone), $"{spec.Id} is a keystone.");
        }
    }

    [Test]
    public void Tree_AvoidsTheKeystoneNames()
    {
        // Rule 2: CH §3.2 reserves The Host, Second Death and Rot Bloom for M7-04, each a
        // build-defining node with a drawback v1 does not ship — The Host needs a PlayerStat for the
        // cap that M5-06a's table refuses, Second Death needs an on-kill trigger primitive nobody
        // has built, and Rot Bloom rewrites a Veilrot relationship the build has no meter for.
        // Taking one of their names for a lesser node makes the Keystone feel like a repeat.
        string[] reserved = { "the-host", "theHost", "second-death", "secondDeath", "rot-bloom", "rotBloom" };

        foreach (SkillSpec spec in Nodes())
        {
            foreach (string name in reserved)
            {
                Assert.That(
                    spec.Id.Value,
                    Is.Not.EqualTo("skill.gravecaller." + name),
                    $"{spec.Id} takes a keystone's name, which would make M7-04's feel like a repeat.");
            }
        }

        // And the display names, because the id is not what a player reads. `Rot` is a branch
        // heading and is deliberately not in this list; `Rot Bloom` is the node.
        string[] reservedNames = { "The Host", "Second Death", "Rot Bloom" };
        var english = ContentValidationTests.English();

        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(reservedNames, Does.Not.Contain(english.Get(spec.NameKey)), spec.Id.Value);
        }
    }

    [Test]
    public void Tree_ExhumeIsTierOneAndTheOnlyActive()
    {
        // **Rule 3's ruling, and rule 4's placement, in one row.** The M5-06 ROADMAP row named three
        // Actives — Exhume, Tether, Rot Nova. Only Exhume ships: M5-06a built the one verb it needs
        // and MinionSystem was already the system it drives, where Tether is a whole task (a link
        // with its own lifetime, break condition and view — M3-11b's size) and Rot Nova needs both a
        // radial-damage primitive nobody has built *and* a Veilrot meter nothing writes. One Active
        // in twelve against CH §4's ~25 % is the stated cost.
        //
        // Tier 1, because an Active reachable in the first two picks is what M3-04's ActiveBoost
        // exists to weight — and with one Active in the tree that boost is either useful here or
        // useful nowhere.
        AssertAt(ExhumeId, branch: 0, tier: 1);

        Assert.That(
            new TreeRules(Tree(), Catalog()).ActiveCount,
            Is.EqualTo(1),
            "Exhume, and nothing else. Tether and Rot Nova are M7-04's, with the Keystones.");
    }

    [Test]
    public void Tree_UpgradeParentIsInBranchBelow()
    {
        // Rule 4: TreeRules' cross-check satisfied rather than argued (M3-03 rule 1). And CH §4's
        // "improves a skill you already own" read literally — the parent is the one node in this
        // tree that grants a skill, so the Upgrade could not have had any other parent.
        TreeRules rules = null;

        Assert.That(() => rules = new TreeRules(Tree(), Catalog()), Throws.Nothing);

        var deeperGraves = new ContentId("skill.gravecaller.deeper-graves");

        Assert.That(rules.TryGetParent(deeperGraves, out ContentId parent), Is.True);
        Assert.That(parent, Is.EqualTo(new ContentId(ExhumeId)));

        Assert.That(
            rules.Skill(parent).Kind,
            Is.EqualTo(SkillKind.Active),
            "An Upgrade improves a skill you own, and only an Active grants one.");

        Tree().TryLocate(deeperGraves, out int childBranch, out int childTier);
        Tree().TryLocate(parent, out int parentBranch, out int parentTier);

        Assert.That(parentBranch, Is.EqualTo(childBranch), "Same branch (Legion).");
        Assert.That(parentTier, Is.LessThan(childTier), "And below it.");
    }

    // ---- Rule 6: what the tree deliberately does not carry ---------------------------------------

    [Test]
    public void Tree_CarriesNoVeilrotAndNoPact()
    {
        // **Rule 6, and the row EveryTriggerField_HasAWriter would catch from the other side.**
        // GD §13.2's corrupted nodes are M6-05's and have no shape yet; GD §10's meter is M6-04's.
        // So Rot is authored as *the shroud and what decay feeds* and carries neither — and the
        // branch key stays `tree.gravecaller.rot` so M6-04 fills it in rather than replacing it.
        //
        // Written as a whitelist of the six primitives this build ships, so a seventh appearing
        // without a decision reddens it — M3-12c's form, one primitive wider.
        Type[] shipped =
        {
            typeof(ModifyStat), typeof(GrantShield), typeof(SpawnHealZone),
            typeof(ModifySkillCooldown), typeof(KnockbackOnSwing), typeof(RaiseMinions),
        };

        foreach (SkillSpec spec in Nodes())
        {
            AssertAllOf(spec.Effects, shipped, spec.Id);

            if (spec.Active is null)
            {
                continue;
            }

            AssertAllOf(spec.Active.OnCast, shipped, spec.Id);

            foreach (TriggerClause clause in spec.Active.Trigger.Clauses)
            {
                // **The clause rule 3 refused to author.** CH §4.2's Rot Nova is "Veilrot ≥ 50 and
                // ≥ 4 enemies within 8 m", and nothing in the build writes TriggerField.Veilrot —
                // so the skill would be read every tick, always be false, and never fire, with
                // nothing anywhere saying why. Authoring it *without* the clause would ship a skill
                // under a false name. Both, so neither: M7-04's.
                Assert.That(
                    clause.Field,
                    Is.Not.EqualTo(TriggerField.Veilrot),
                    $"{spec.Id} authors a Veilrot clause and nothing writes that field (M6-04 "
                        + "does). ContentValidationTests.EveryTriggerField_HasAWriter is the "
                        + "project-wide version of this row.");
            }
        }
    }

    // ---- The ids and the files --------------------------------------------------------------------

    [Test]
    public void Tree_IdsAreKebabCaseAndConstructible()
    {
        // M3-12c ruling 1, applied before a single asset was authored rather than discovered after:
        // ContentId's grammar is ^[a-z0-9]+(\.[a-z0-9_-]+)+$, so `graveStrength` throws. The refusal
        // is asserted rather than assumed, so this row cannot pass on a grammar that quietly widened.
        Assert.That(
            ContentId.IsValid("skill.gravecaller.graveStrength"),
            Is.False,
            "If this ever passes, the grammar moved and the kebab spelling is no longer load-bearing.");

        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (SkillSpec spec in Nodes())
        {
            Assert.That(ContentId.IsValid(spec.Id.Value), Is.True);
            Assert.That(spec.Id.Value, Does.StartWith("skill.gravecaller."));
            Assert.That(ids.Add(spec.Id.Value), Is.True, $"duplicate id {spec.Id}.");
        }

        Assert.That(ids, Is.EquivalentTo(NodeIds));
    }

    [Test]
    public void Nodes_FileNamesMapToTheirIds()
    {
        // A mapping rather than a match, which is what the kebab spelling does to CLAUDE.md's
        // asset-naming rule: `GraveStrength.asset` ↔ `skill.gravecaller.grave-strength`,
        // hyphen-to-PascalCase.
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

        // The tree, whose id has no hyphen and is therefore the plain rule rather than the mapping.
        var tree = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(TreePath);

        Assert.That(tree, Is.Not.Null, $"No SkillTreeDefinition at {TreePath}.");
        Assert.That(tree.Id, Is.EqualTo("tree.gravecaller"));
        Assert.That(tree.name, Is.EqualTo("Gravecaller"));
    }

    [Test]
    public void Nodes_AreLinkedToAMonoScript()
    {
        // Traps §5, and the only row that would go red for it: a ScriptableObject declared with a
        // file-scoped namespace loads as null off any asset referencing it, with no error anywhere.
        // Twenty-five assets shipped for the first time, so this is the row that says they are real
        // — the effect assets included, because a null effect is a node that silently does nothing.
        foreach (string assetName in NodeAssets)
        {
            Assert.That(
                MonoScript.FromScriptableObject(LoadNode(assetName)),
                Is.Not.Null,
                $"{SkillDir}/{assetName}.asset is not linked to a MonoScript (Traps §5).");

            var effect = AssetDatabase.LoadAssetAtPath<EffectDefinition>(
                $"{EffectDir}/{assetName}.asset");

            Assert.That(effect, Is.Not.Null, $"No EffectDefinition at {EffectDir}/{assetName}.asset.");
            Assert.That(MonoScript.FromScriptableObject(effect), Is.Not.Null, assetName);
        }

        var tree = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(TreePath);

        Assert.That(tree, Is.Not.Null, $"No SkillTreeDefinition at {TreePath}.");
        Assert.That(MonoScript.FromScriptableObject(tree), Is.Not.Null);
    }

    // ---- Rule 1: every number is in an asset ------------------------------------------------------

    [Test]
    public void Exhume_CarriesItsAuthoredNumbers()
    {
        SkillSpec spec = Node("Exhume");

        Assert.That(spec.Kind, Is.EqualTo(SkillKind.Active));
        Assert.That(spec.Active, Is.Not.Null);
        Assert.That(spec.Active.Cooldown, Is.EqualTo(20f).Within(Tolerance));

        Assert.That(spec.Active.Trigger.Clauses, Has.Count.EqualTo(1));

        // **Asserted by name, for M3-12c's Bulwark reason.** TriggerField serialises as a raw int,
        // so the wrong ordinal is a legal asset that fires at the wrong moment for ever — and
        // MinionCount is the newest member, which makes it the one most likely to be off by one.
        Assert.That(
            spec.Active.Trigger.Clauses[0].Field,
            Is.EqualTo(TriggerField.MinionCount),
            "CH §4.2's Exhume fires when the army is thin, not when the player is.");

        Assert.That(spec.Active.Trigger.Clauses[0].Comparison, Is.EqualTo(TriggerComparison.Below));
        Assert.That(spec.Active.Trigger.Clauses[0].Threshold, Is.EqualTo(2f).Within(Tolerance));

        Assert.That(spec.Active.OnCast, Has.Count.EqualTo(1));

        var raise = (RaiseMinions)spec.Active.OnCast[0];

        Assert.That(raise.Count, Is.EqualTo(3), "CH §4.2: raise 3 Wights.");
        Assert.That(raise.Radius, Is.EqualTo(2f).Within(Tolerance));

        // An Active is the one kind that may take with no effects: its power is on cast.
        Assert.That(spec.Effects, Is.Empty);

        // **"Below 2" against a base cap of 3, which rule 7 records as wrong-by-design the day The
        // Host lands** (M5-06a's finding, M7-04's inheritance). A cap raised past 4 makes "below
        // half the cap" mean something else, and the threshold is a number rather than a fraction.
        Assert.That(
            Load<CharacterDefinition>(GravecallerPath).ToSpec().Minions.Cap,
            Is.EqualTo(3),
            "If this moves, Exhume's threshold is the thing that has to move with it.");
    }

    [Test]
    public void DeeperGraves_LowersExhumesCooldown()
    {
        var cooldown = (ModifySkillCooldown)Node("DeeperGraves").Effects[0];

        Assert.That(cooldown.Kind, Is.EqualTo(ModifierKind.PercentMult));
        Assert.That(cooldown.Value, Is.EqualTo(-0.25f).Within(Tolerance));

        // **Compared against Exhume's own asset, never against a literal.** _skillId is a serialized
        // string and M3-12b rule 3 holds a modifier for a skill the run does not own rather than
        // throwing — so a typo here is not an error anywhere: it is a pending note waiting for a
        // skill that can never arrive. Two matching literals would prove only that this file agrees
        // with itself.
        Assert.That(
            cooldown.SkillId,
            Is.EqualTo(Node("Exhume").Id),
            "Deeper Graves must name the id Exhume.asset actually carries.");

        // And that the id it names is a node of *this* tree, not merely a well-formed string.
        Assert.That(Tree().TryLocate(cooldown.SkillId, out int _, out int _), Is.True);
    }

    [Test]
    public void MinionNodes_CarryTheirNumbers()
    {
        // Rule 5: the three addresses MinionRecipeStats answers, and the only three it does.
        AssertStat("GraveStrength", PlayerStat.ContactDamage, ModifierKind.PercentAdd, 0.20f, StatTarget.Minions);
        AssertStat("KnittedBone", PlayerStat.MaxHp, ModifierKind.PercentAdd, 0.50f, StatTarget.Minions);
        AssertStat("RestlessDead", PlayerStat.MoveSpeed, ModifierKind.PercentAdd, 0.30f, StatTarget.Minions);
    }

    [Test]
    public void Tree_ThreeNodesAimAtMinions()
    {
        // **Exactly three, and which three.** The interesting half is the second: StatTarget defaults
        // to Player, so a node meant for the army that was authored without the field reads as a
        // player buff and looks completely ordinary in the Inspector. A count alone would not catch
        // a fourth node aimed here by accident either.
        var aimed = new List<string>();

        foreach (string assetName in NodeAssets)
        {
            SkillSpec spec = Node(assetName);

            foreach (IEffect effect in spec.Effects)
            {
                if (effect is ModifyStat modify && modify.Target == StatTarget.Minions)
                {
                    aimed.Add(assetName);
                }
            }
        }

        Assert.That(aimed, Is.EquivalentTo(MinionNodeAssets));
    }

    [Test]
    public void MinionNodes_ChangeWhatAWightIsWorth()
    {
        // **Rule 5's three claims, walked end to end rather than asserted as arithmetic.** The three
        // effect assets go on through a real ModifyStatHandler aimed at a real MinionRecipe, and the
        // numbers read back off a body MinionSystem actually raised — so this row fails if the
        // target routing breaks, if MinionRecipeStats stops answering an address, or if
        // MinionAgent.Initialise stops re-basing from the recipe (M5-06a rule 3's ordering).
        MinionSpec minions = Load<CharacterDefinition>(GravecallerPath).ToSpec().Minions;

        Assert.That(minions, Is.Not.Null, "Gravecaller.asset carries a MinionSpec since M5-02.");

        var recipe = new MinionRecipe(minions);
        var registry = new EffectRegistry();

        registry.Register<ModifyStat>(
            new ModifyStatHandler(new ThrowingStats(), new MinionRecipeStats(recipe)));

        foreach (string assetName in MinionNodeAssets)
        {
            registry.Apply(Node(assetName).Effects[0], this);
        }

        var events = new RecordingEvents();
        var system = new MinionSystem(minions, recipe, events, new RecordingIntents());

        // Fully qualified: this file sees both Vector3s, and core's is the one a Wight stands in.
        MinionAgent wight = system.Spawn(System.Numerics.Vector3.Zero, 0f);

        Assert.That(wight, Is.Not.Null, "an empty army raises.");

        // 8 → 9.6: four hits on a stage-1 Husk instead of five.
        Assert.That(wight.ContactDamage.Value, Is.EqualTo(9.6f).Within(1e-4f));
        Assert.That(Hits(HuskMaxHp, minions.Damage), Is.EqualTo(5), "the fixture's own premise.");
        Assert.That(Hits(HuskMaxHp, wight.ContactDamage.Value), Is.EqualTo(4));

        // 20 → 30: four Husk contacts instead of three.
        Assert.That(wight.Health.MaxHp.Value, Is.EqualTo(30f).Within(1e-4f));
        Assert.That(Hits(minions.MaxHp, HuskContactDamage), Is.EqualTo(3), "the fixture's own premise.");
        Assert.That(Hits(wight.Health.MaxHp.Value, HuskContactDamage), Is.EqualTo(4));

        // 3.0 → 3.9, against a Husk's 2 — a Wight that runs one down rather than trailing it.
        Assert.That(wight.MoveSpeed.Value, Is.EqualTo(3.9f).Within(1e-4f));
        Assert.That(wight.MoveSpeed.Value, Is.GreaterThan(HuskMoveSpeed));
    }

    [Test]
    public void StatNodes_CarryTheirNumbers()
    {
        // The six aimed at the player. Flat or PercentAdd per the tables, and the kind is the half
        // that has no symptom when it is wrong.
        AssertStat("SharpenedBone", PlayerStat.WeaponDamage, ModifierKind.PercentAdd, 0.15f, StatTarget.Player);
        AssertStat("QuickHands", PlayerStat.FireRate, ModifierKind.PercentAdd, 0.12f, StatTarget.Player);
        AssertStat("PaleVigour", PlayerStat.MaxHp, ModifierKind.Flat, 15f, StatTarget.Player);
        AssertStat("ShroudVeil", PlayerStat.MovementSkillCooldown, ModifierKind.PercentAdd, -0.20f, StatTarget.Player);
        AssertStat("RotFeast", PlayerStat.XpGain, ModifierKind.PercentAdd, 0.15f, StatTarget.Player);
        AssertStat("WitheringStep", PlayerStat.MoveSpeed, ModifierKind.Flat, 0.3f, StatTarget.Player);

        // **Flat, and it has to be.** HealPerKill's base is 0 and Stat computes
        // (Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult), so a percentage heals nothing —
        // M3-12a measured +500 % as 0. Pinned rather than trusted to the table, because the live
        // risk is a retune that reaches for a percentage.
        AssertStat("MarrowTithe", PlayerStat.HealPerKill, ModifierKind.Flat, 2f, StatTarget.Player);
    }

    [Test]
    public void Tree_IsSixStatsThreeRulesOneUpgradeAndOneActive()
    {
        // The mix, counted off the assets rather than off the spec's table. One Active in twelve
        // where the Oathbound's v1 has two — rule 3's stated cost, asserted so it is a decision
        // rather than an accident.
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

        Assert.That(actives, Is.EqualTo(1));
        Assert.That(upgrades, Is.EqualTo(1));
        Assert.That(passives, Is.EqualTo(10));
    }

    // ---- Rule 7: twenty-seven keys, and all twenty-seven resolve ----------------------------------

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

        Assert.That(keys, Has.Count.EqualTo(24));

        // The project's convention everywhere else, so a reader can predict a key from an id.
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

        Assert.That(tree.Branches[0].NameKey.Key, Is.EqualTo("tree.gravecaller.legion"));
        Assert.That(tree.Branches[1].NameKey.Key, Is.EqualTo("tree.gravecaller.grave-work"));

        // **Rule 6: the key is `rot`, not `shroud` or `decay`.** GD §10's meter is M6-04's, and
        // naming the branch after what it carries today would make that task a rename of a shipped
        // key rather than a filling-in of one.
        Assert.That(tree.Branches[2].NameKey.Key, Is.EqualTo("tree.gravecaller.rot"));
    }

    [Test]
    public void Tree_EveryKeyResolvesInEnglish()
    {
        // **The half M3-12c did not have to do.** It shipped twenty-four keys unresolved because
        // ILocalizer did not exist yet; M3-14a's ContentValidationTests.EveryLocKey_ResolvesInEnglish
        // now sweeps every authored key in the project, so an English row per key ships in this PR
        // or that row is red. This is the same claim scoped to these twenty-seven, so a red run
        // names the tree rather than the table.
        var english = ContentValidationTests.English();
        var missing = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

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

        Assert.That(
            missing,
            Is.Empty,
            "English.asset has no row for: " + string.Join(", ", missing));
    }

    // ---- The boot catalog, and two runs -----------------------------------------------------------

    [Test]
    public void Boot_RegistersTheSecondTreeAndTwelveSkills()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        var scope = prefab.GetComponent<Soulvail.Game.Composition.BootScope>();
        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        var serialized = new SerializedObject(scope);

        SerializedProperty skills = serialized.FindProperty("_skills");
        SerializedProperty trees = serialized.FindProperty("_trees");

        Assert.That(skills.arraySize, Is.EqualTo(24), "Twelve Oathbound nodes and twelve Gravecaller ones.");
        Assert.That(trees.arraySize, Is.EqualTo(2));

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

        var treeIds = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < trees.arraySize; i++)
        {
            var tree = trees.GetArrayElementAtIndex(i).objectReferenceValue as SkillTreeDefinition;

            Assert.That(tree, Is.Not.Null, $"_trees[{i}] is an empty slot.");
            treeIds.Add(tree.Id);
        }

        Assert.That(treeIds, Is.EquivalentTo(new[] { "tree.oathbound", "tree.gravecaller" }));

        // And the answer that has been false for every Gravecaller since M5-02.
        ContentCatalog catalog = Catalog();

        Assert.That(catalog.TryGetTreeFor(new ContentId(GravecallerId), out SkillTreeSpec _), Is.True);
        Assert.That(catalog.TryGetTreeFor(new ContentId(OathboundId), out SkillTreeSpec _), Is.True);
    }

    [Test]
    public void Run_AGravecallerStartsWithItsOwnTree()
    {
        RunSession session = StartRun(GravecallerId, level: 1, pending: 0);

        Assert.That(session.State.TakenNodeCount, Is.Zero);
        Assert.That(session.State.IsTreeFull, Is.False);

        // The six tier-1 nodes are available and the six tier-2 nodes are not: a branch opens both
        // of its tier-2 nodes after a single pick (M3-03 rule 2).
        foreach (string id in NodeIds)
        {
            bool expected = Array.IndexOf(TierOneIds, id) >= 0;

            Assert.That(
                session.State.IsNodeAvailable(new ContentId(id)),
                Is.EqualTo(expected),
                expected ? $"{id} is tier 1 and must be offerable" : $"{id} is tier 2 and gated");
        }

        // **And none of the Oathbound's, which is the half a shared catalog could get wrong.** Both
        // trees are in the same ContentCatalog now, and a run resolves its tree from the class it is
        // playing; a node from the wrong class showing up as available would be a card the player
        // could take and an effect aimed at a weapon they do not carry.
        Assert.That(
            session.State.IsNodeAvailable(new ContentId("skill.oathbound.keen-censer")),
            Is.False,
            "A Gravecaller was offered an Oathbound node.");
    }

    [Test]
    public void Run_TheOathboundIsUnchanged()
    {
        // **M5-02 rule 9's promise, checked from the other side.** A second class, a second tree and
        // twelve more skills in the boot catalog change nothing about the class that is actually
        // playable: the same twelve nodes, the same six offerable at level 1, and nothing of the
        // Gravecaller's reachable from an Oathbound run.
        RunSession session = StartRun(OathboundId, level: 1, pending: 0);

        SkillTreeSpec oathbound = LoadTree(OathboundTreePath);

        Assert.That(oathbound.NodeCount, Is.EqualTo(12));

        int offerable = 0;

        foreach (SkillBranchSpec branch in oathbound.Branches)
        {
            foreach (ContentId id in branch.Tier(1))
            {
                Assert.That(session.State.IsNodeAvailable(id), Is.True, id.Value);
                offerable++;
            }

            foreach (ContentId id in branch.Tier(2))
            {
                Assert.That(session.State.IsNodeAvailable(id), Is.False, id.Value);
            }
        }

        Assert.That(offerable, Is.EqualTo(6));

        foreach (string id in NodeIds)
        {
            Assert.That(
                session.State.IsNodeAvailable(new ContentId(id)),
                Is.False,
                $"An Oathbound run can reach {id}.");
        }
    }

    // ---- Ledger row 5(i): what a level is worth ---------------------------------------------------

    [Test]
    public void Overflow_ComesFromTheMode()
    {
        // **Rules 8 and 9, end to end and without a literal in sight.** The run's grant is measured
        // against what Descent.asset's own block says, so retuning the asset moves this row with it
        // — which is the whole of what row 5(i) bought. Overflow_TheShippedRunIsIdentical is the row
        // that holds the literal, so a retune is still a visible diff somewhere.
        OverflowSpec authored = Load<ModeDefinition>(DescentPath).ToSpec().Overflow;

        RunSession session = StartRun(OathboundId, level: 15, pending: 0);

        Assert.That(session.State.OverflowLevels, Is.EqualTo(14), "15 − 1 − 0 taken − 0 owed.");

        Assert.That(
            session.State.PlayerMaxHp,
            Is.EqualTo(OathboundMaxHp * (1f + (14f * authored.MaxHp))).Within(0.01f),
            "Fourteen PercentAdd modifiers under one source, pooled (ADR-0008).");

        // **The damage half is not asserted through the run, and cannot be.** RunState hands out no
        // weapon number — `Combat` is internal on AR §18.2's "a live object is never handed out",
        // and `PlayerMaxHp` exists only because the HUD draws a bar. So this row owns the half a
        // public read reaches and `LevelUpFlowTests.Overflow_ARetunedModeMovesTheGrant` owns the
        // other, over a flow built by hand; the two meet at the OverflowSpec rather than at a
        // number, which is the stronger seam.
        Assert.That(
            authored.Damage,
            Is.GreaterThan(0f),
            "A mode that grants no damage would make the sibling row above vacuous.");
    }

    [Test]
    public void Overflow_TheShippedRunIsIdentical()
    {
        // **Rule 11, and the row that makes this an ADR-0006 change rather than a balance one.**
        // Descent.asset carries 0.02 and 0.02, so every number in every shipped run is the same
        // before and after row 5(i) — ×1.28 on both stats at fourteen Overflow levels, which is what
        // RunSessionResumeTests.Resume_DerivesOverflow has asserted over a fixture mode since M3-08a
        // and this asserts over the real one.
        OverflowSpec authored = Load<ModeDefinition>(DescentPath).ToSpec().Overflow;

        Assert.That(authored.Damage, Is.EqualTo(OverflowPerLevel).Within(1e-7f));
        Assert.That(authored.MaxHp, Is.EqualTo(OverflowPerLevel).Within(1e-7f));

        RunSession session = StartRun(OathboundId, level: 15, pending: 0);

        Assert.That(session.State.PlayerMaxHp, Is.EqualTo(OathboundMaxHp * 1.28f).Within(0.01f));

        // And the control: retuning the asset is meant to move the game, so the number this row
        // pins must not be reachable by accident from a mode that grants nothing.
        Assert.That(session.State.PlayerMaxHp, Is.Not.EqualTo(OathboundMaxHp).Within(0.01f));
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    /// <summary>How many hits of <paramref name="damage"/> it takes to drop <paramref name="hp"/>.</summary>
    private static int Hits(float hp, float damage) => (int)MathF.Ceiling(hp / damage);

    private static void AssertAllOf(IReadOnlyList<IEffect> effects, Type[] shipped, ContentId id)
    {
        for (int i = 0; i < effects.Count; i++)
        {
            Assert.That(
                shipped,
                Contains.Item(effects[i].GetType()),
                $"{id} carries a {effects[i].GetType().Name}, which is not one of this build's six "
                    + "primitives. A Pact or a Veilrot effect here would be content for a mechanic "
                    + "that cannot yet be authored (rule 6).");
        }
    }

    private static void AssertStat(
        string assetName, PlayerStat stat, ModifierKind kind, float value, StatTarget target)
    {
        SkillSpec spec = Node(assetName);

        Assert.That(spec.Effects, Has.Count.EqualTo(1), $"{assetName} carries one effect.");

        var modify = (ModifyStat)spec.Effects[0];

        Assert.That(modify.Stat, Is.EqualTo(stat), assetName);
        Assert.That(modify.Kind, Is.EqualTo(kind), assetName);
        Assert.That(modify.Value, Is.EqualTo(value).Within(Tolerance), assetName);
        Assert.That(modify.Target, Is.EqualTo(target), assetName);
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

    private static SkillTreeSpec Tree() => LoadTree(TreePath);

    private static SkillTreeSpec LoadTree(string path) => Load<SkillTreeDefinition>(path).ToSpec();

    /// <summary>
    /// The shipped catalog: <b>both</b> classes, both trees and all twenty-four nodes, read off
    /// <c>Data/</c>. Both, and not the Gravecaller alone — the run rows below are as much about the
    /// two trees not leaking into each other as about either one being right.
    /// </summary>
    private static ContentCatalog Catalog()
    {
        var skills = new List<SkillSpec>(24);

        foreach (string path in ContentValidationTests.PathsOf<SkillDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            Assert.That(definition, Is.Not.Null, $"No SkillDefinition at {path}.");

            skills.Add(definition.ToSpec());
        }

        Assert.That(skills, Has.Count.EqualTo(24), "Twelve nodes a class, two classes.");

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
            },
            enemies,
            new[] { Load<ModeDefinition>(DescentPath).ToSpec() },
            skills,
            new[] { LoadTree(OathboundTreePath), Tree() },
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
    /// <paramref name="pending"/> picks owed — <c>OathboundTreeTests.StartRun</c>'s shape, which is
    /// how a fixture reaches a levelled state without simulating the kills that earned it.
    /// </summary>
    private RunSession StartRun(string characterId, int level, int pending)
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
            pending,
            Array.Empty<ContentId>(),
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

    /// <summary>
    /// A player block that refuses everything, so <see cref="MinionNodes_ChangeWhatAWightIsWorth"/>
    /// cannot pass on a node that quietly landed on the player.
    /// </summary>
    /// <remarks>
    /// <c>ModifyStatHandler</c> needs a player block it will never reach if the three assets carry
    /// <see cref="StatTarget.Minions"/> — which is exactly the claim. Standing up a real
    /// <c>PlayerStats</c> would need a whole <c>PlayerCombat</c> and would swallow the mistake.
    /// </remarks>
    private sealed class ThrowingStats : IStatBlock
    {
        public Stat Resolve(PlayerStat stat) =>
            throw new InvalidOperationException(
                $"A Minions node was aimed at the player and asked for {stat}. Its _target field "
                    + "is 0 rather than 2, which reads as an ordinary player buff in the Inspector.");

        public bool Has(PlayerStat stat) => false;
    }
}
