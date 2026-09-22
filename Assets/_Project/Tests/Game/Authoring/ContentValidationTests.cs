using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// Every shipped asset, measured against the rules nothing else reads: ids namespaced for their
/// kind, file names that agree with those ids, and every <see cref="LocKey"/> present, distinct and
/// resolvable in <c>English.asset</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>There is no <c>ContentValidator</c> and there must not be one</b> (M3-14b rule 11). The rules
/// below are not a second implementation of anything: <c>ContentCatalog</c> refuses a duplicate id
/// <em>within a kind</em>, <c>LocalizationTable.ToDictionary</c> refuses a duplicate key, and
/// <c>TreeRules</c> cross-checks the tree a run actually boots — all at construction, over the
/// specs a catalog was handed. What no constructor can see is an asset nobody loaded, and that is
/// the whole of this fixture's subject. <b>Constructors validate specs at run time; this validates
/// assets at author time.</b>
/// </para>
/// <para>
/// <b>Every sweep is <c>AssetDatabase.FindAssets</c>, not a boot list</b> —
/// <c>EnemyDefinitionTests.AllEnemyDefinitions_ValidUniqueIds</c>' shape. An asset sitting in
/// <c>Data/</c> outside <c>BootScope</c> is content the next task to add a line to the installer
/// will ship, and finding it broken then is finding it late.
/// </para>
/// <para>
/// <b>Every failure message begins with the asset path</b> (rule 10), because the value of a sweep
/// over twenty-six assets is that it says <em>which</em> one is wrong. The rules are written as
/// collectors returning a list of problem lines rather than as a wall of <c>Assert</c> calls, for
/// two reasons: a red row names every broken asset instead of the first, and
/// <see cref="EveryFailureMessage_NamesAPath"/> can call the same collectors against a deliberately
/// broken temporary asset and check what they say.
/// </para>
/// <para>
/// <b>Two findings this task made and fixed, both named in its <em>As built</em>:</b> five keys
/// carried by shipped assets had no row in <c>English.asset</c>
/// (<c>character.oathbound.name</c>, the three <c>enemy.*.name</c>s and <c>mode.descent.name</c>),
/// and <c>Data/Trees/OathboundTree.asset</c> carried <c>tree.oathbound</c>, whose last segment is
/// <c>oathbound</c>. Both were fixed in the assets in this branch — a validation task whose first
/// run is red and whose fix is a second PR has validated nothing.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ContentValidationTests
{
    /// <summary>The one table the game ships, opened by path so a temporary one cannot stand in.</summary>
    internal const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    /// <summary>
    /// Where <see cref="EveryFailureMessage_NamesAPath"/> writes the assets it deliberately breaks.
    /// Under <c>Data/</c> on purpose: an asset somewhere the sweeps would not look would prove
    /// nothing about the sweeps.
    /// </summary>
    private const string TempFolder = "Assets/_Project/Data/__M3-14b-Temp";

    private const string TempParent = "Assets/_Project/Data";
    private const string TempLeaf = "__M3-14b-Temp";

    /// <summary>
    /// The namespace each kind's id must open with (rule 2, ADR-0010). <c>arena.</c> has no asset
    /// kind of its own yet — arena ids exist only as references inside a
    /// <see cref="ModeDefinition"/> — and is checked there rather than left unchecked.
    /// </summary>
    private const string CharacterNamespace = "character.";

    private const string EnemyNamespace = "enemy.";
    private const string ModeNamespace = "mode.";
    private const string SkillNamespace = "skill.";
    private const string TreeNamespace = "tree.";
    private const string ArenaNamespace = "arena.";

    /// <summary>
    /// The keys the authoring types initialise themselves to. A shipped asset carrying one of these
    /// is an asset nobody finished, and it would otherwise fail only on the resolution row — which
    /// says "no row for this key" rather than "nobody named this node".
    /// </summary>
    /// <remarks>
    /// <c>minion.new.name</c> is <em>not</em> on this list, and the omission is deliberate: it is
    /// the default on every class without minions, which is two of the three, and
    /// <see cref="EveryAuthoredKey"/> does not walk a class's minion block anyway (M5-02). The day
    /// something reads it, it joins the list with the sweep that reaches it.
    /// </remarks>
    /// <summary>
    /// The <see cref="TriggerField"/>s something in the build actually writes, each with its writer
    /// (M5-06a rule 8). <see cref="EveryTriggerField_HasAWriter"/> is what reads it, and that row's
    /// remarks are why this is a list rather than reflection.
    /// </summary>
    /// <remarks>
    /// <b><see cref="TriggerField.Veilrot"/> is deliberately absent</b>: GD §13's meter is a field
    /// AR §9 names and M6-04 fills, and until then a clause over it would be read every tick and
    /// always be zero. Its absence here is the whole value of the row.
    /// </remarks>
    private static readonly TriggerField[] Written =
    {
        // PlayerCombat.UpdateBlackboard — seven of the ten, in one block, every tick.
        TriggerField.HpFraction,
        TriggerField.ShieldFraction,
        TriggerField.EnemiesWithin6m,
        TriggerField.EnemiesWithin8m,
        TriggerField.EnemiesInAcquireRange,
        TriggerField.FocusRampLevel,
        TriggerField.StationaryTime,

        // PlayerCombat.UpdateBlackboard again, from the army it is handed (M5-06a rule 6).
        TriggerField.MinionCount,

        // ProjectileSystem.Tick, at the end of its own step, so a trigger reads the sky as it was
        // before this tick's arrivals were resolved (M3-06 rule 7).
        TriggerField.IncomingProjectiles,
    };

    private static readonly string[] AuthoringPlaceholders =
    {
        "character.new.name",
        "character.new.description",
        "enemy.new.name",
        "mode.new.name",
        "skill.new.name",
        "skill.new.description",
        "tree.new.branch",
    };

    /// <summary>What the project ships today, as a floor rather than as a count.</summary>
    /// <remarks>
    /// A sweep over an empty set passes, silently and for ever — the one way a fixture like this can
    /// be green while doing nothing. These floors are what say the <c>t:</c> filters still match.
    /// They are floors, not equalities, because adding content must not turn this row red; M3-12c's
    /// own suite is what pins the Oathbound's twelve and M5-06b's pins the Gravecaller's.
    /// <b>They are still raised when content ships</b>, which is what keeps them floors worth
    /// having: left at twelve after a second tree landed, the skill filter could stop matching half
    /// the project and the row would say nothing.
    /// </remarks>
    private const int ShippedCharacters = 2;

    private const int ShippedEnemies = 3;
    private const int ShippedModes = 1;
    private const int ShippedSkills = 24;
    private const int ShippedTrees = 2;
    private const int ShippedEffects = 25;
    private const int ShippedTables = 1;

    /// <summary>
    /// The bosses the project ships — GD §9.2's Warden, with the other three at M7 (M4-02).
    /// </summary>
    private const int ShippedBosses = 1;

    /// <summary>
    /// The shortest telegraph GD §9.1 rule 1 permits, in seconds. <c>WardenBehaviour</c> floors
    /// every attack at this whatever an asset says, so what this sweep catches is the
    /// <em>disagreement</em> — a boss authored faster than the rule allows, being played at the
    /// rule's speed, with nothing anywhere saying the two had parted company.
    /// </summary>
    private const float MinBossTelegraph = 0.6f;

    [TearDown]
    public void DeleteTemporaryAssets()
    {
        if (AssetDatabase.IsValidFolder(TempFolder))
        {
            AssetDatabase.DeleteAsset(TempFolder);
            AssetDatabase.Refresh();
        }
    }

    // ---- Rule 1: the four kinds M3-02b added, swept for the first time --------------------------

    [Test]
    public void AllSkills_LoadConvertAndAreUnique()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in PathsOf<SkillDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            if (definition == null)
            {
                // Traps §5's failure and the reason this row opens an asset rather than building
                // one: a definition whose script reference did not bind loads as null with nothing
                // anywhere reporting it.
                problems.Add($"{path}: did not load as a SkillDefinition.");
                continue;
            }

            SkillSpec spec;

            try
            {
                spec = definition.ToSpec();
            }
            catch (Exception exception)
            {
                problems.Add($"{path}: is not valid content — {exception.Message}");
                continue;
            }

            if (!seen.Add(spec.Id.Value))
            {
                problems.Add(
                    $"{path}: repeats the id '{spec.Id.Value}'. ContentCatalog throws on a "
                        + "duplicate, so two nodes under one id means the catalog refuses to build.");
            }
        }

        AssertNoProblems(problems, "Skill assets");
    }

    [Test]
    public void AllTrees_LoadConvertAndAreUnique()
    {
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string path in PathsOf<SkillTreeDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(path);

            if (definition == null)
            {
                problems.Add($"{path}: did not load as a SkillTreeDefinition.");
                continue;
            }

            SkillTreeSpec spec;

            try
            {
                spec = definition.ToSpec();
            }
            catch (Exception exception)
            {
                problems.Add($"{path}: is not valid content — {exception.Message}");
                continue;
            }

            if (!seen.Add(spec.Id.Value))
            {
                problems.Add($"{path}: repeats the id '{spec.Id.Value}'.");
            }
        }

        AssertNoProblems(problems, "Skill tree assets");
    }

    [Test]
    public void AllEffects_LoadAndConvert()
    {
        var problems = new List<string>();
        var withAssets = new HashSet<Type>();

        foreach (string path in PathsOf<EffectDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<EffectDefinition>(path);

            if (definition == null)
            {
                problems.Add($"{path}: did not load as an EffectDefinition.");
                continue;
            }

            withAssets.Add(definition.GetType());

            try
            {
                IEffect effect = definition.ToEffect();

                if (effect is null)
                {
                    problems.Add($"{path}: ToEffect returned null.");
                }
            }
            catch (Exception exception)
            {
                problems.Add($"{path}: is not valid content — {exception.Message}");
            }
        }

        // The other half of the row: a primitive with no asset is not an error, but it is a thing
        // the milestone should know it never authored. Reported by name rather than asserted,
        // because M3-12b shipped two primitives specifically so M3-12c could reach for them and a
        // future one may legitimately wait a milestone for its first node.
        var unused = new List<string>();

        foreach (Type type in typeof(EffectDefinition).Assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(EffectDefinition).IsAssignableFrom(type))
            {
                continue;
            }

            if (!withAssets.Contains(type))
            {
                unused.Add(type.Name);
            }
        }

        TestContext.WriteLine(
            unused.Count == 0
                ? "Every EffectDefinition subclass has at least one asset."
                : $"EffectDefinition subclasses with no asset: {string.Join(", ", unused)}.");

        AssertNoProblems(problems, "Effect assets");
    }

    [Test]
    public void AllTables_Convert()
    {
        var problems = new List<string>();

        foreach (string path in PathsOf<LocalizationTable>())
        {
            var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(path);

            if (table == null)
            {
                problems.Add($"{path}: did not load as a LocalizationTable.");
                continue;
            }

            problems.AddRange(TableProblems(path, table));
        }

        AssertNoProblems(problems, "Localisation tables");
    }

    [Test]
    public void EverySweep_FindsTheAssetsItIsMeantTo()
    {
        // The anti-vacuity row. Every assertion in this file is "nothing is wrong with the assets
        // I found", which is trivially true of an empty set — so a `t:` filter that stopped
        // matching, or a Data/ folder that moved, would turn the whole fixture green and silent.
        Assert.That(PathsOf<CharacterDefinition>(), Has.Count.AtLeast(ShippedCharacters));
        Assert.That(PathsOf<EnemyDefinition>(), Has.Count.AtLeast(ShippedEnemies));
        Assert.That(PathsOf<ModeDefinition>(), Has.Count.AtLeast(ShippedModes));
        Assert.That(PathsOf<SkillDefinition>(), Has.Count.AtLeast(ShippedSkills));
        Assert.That(PathsOf<SkillTreeDefinition>(), Has.Count.AtLeast(ShippedTrees));
        Assert.That(PathsOf<EffectDefinition>(), Has.Count.AtLeast(ShippedEffects));
        Assert.That(PathsOf<LocalizationTable>(), Has.Count.AtLeast(ShippedTables));
        Assert.That(PathsOf<BossDefinition>(), Has.Count.AtLeast(ShippedBosses));
    }

    // ---- GD §9.1 rule 1, over the assets that ship (M4-02) ---------------------------------------

    [Test]
    public void Boss_EveryAttackTelegraphsLongEnough()
    {
        // **The half of M4-02's rule 1 that only this assembly can make.** A boss's attacks
        // telegraph for the body it wears — <c>WardenBehaviour.TelegraphSeconds</c> is
        // <c>EnemySpec.WindupTime</c>, floored — and `Soulvail.Core` references no Unity assembly,
        // so `WardenBehaviourTests` can assert the floor and the shipped *numbers* but never the
        // shipped *asset*. This walks every BossDefinition on disk to the EnemyDefinition it
        // names, and asks GD §9.1 rule 1's question of it.
        var problems = new List<string>();

        foreach (string path in PathsOf<BossDefinition>())
        {
            var boss = AssetDatabase.LoadAssetAtPath<BossDefinition>(path);

            if (boss == null)
            {
                continue;
            }

            ContentId bodyId = boss.ToSpec().EnemySpecId;

            if (!TryFindEnemy(bodyId, out EnemySpec body))
            {
                problems.Add($"{path}: names the body '{bodyId}', which no EnemyDefinition holds.");

                continue;
            }

            if (body.WindupTime < MinBossTelegraph)
            {
                problems.Add(
                    $"{path}: its body '{bodyId}' telegraphs for {body.WindupTime} s, under "
                        + $"GD §9.1 rule 1's {MinBossTelegraph} s. WardenBehaviour floors it at "
                        + "the rule's number, so the asset and the fight disagree.");
            }
        }

        AssertNoProblems(problems, "Boss telegraph lengths (GD §9.1 rule 1)");
    }

    // ---- GD §15, over the modes that ship (M6-01a rule 3) ----------------------------------------

    [Test]
    public void EveryShippedMode_PricesItsEssence()
    {
        // **A shipped mode that prices nothing is a content failure, not a quiet zero.**
        // `EssenceSpec` is an optional, last `ModeSpec` argument defaulting to all zeroes, which is
        // what keeps sixty-three fixtures compiling (M6-01a rule 2) — and the cost of that default
        // is that a mode which never authored an economy is indistinguishable, to every constructor
        // in the game, from one that authored zeroes on purpose. A run on it would clear stage after
        // stage, be paid nothing, and say nothing about it.
        //
        // The two terms checked are the two something in this build actually pays: `PerElite` is
        // authored with no payer until M7-02 (rule 2) and asserting on it here would be asserting
        // about a number nothing reads. It is the same bargain M5-06b made for Overflow one
        // milestone ago.
        var problems = new List<string>();

        foreach (string path in PathsOf<ModeDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path);

            if (definition == null)
            {
                continue;
            }

            EssenceSpec essence = definition.ToSpec().Essence;

            if (essence.PerStageBase <= 0)
            {
                problems.Add(
                    $"{path}: pays {essence.PerStageBase} Essence for a stage clear. GD §15 prices "
                        + "one at 20 + 4·n, and a mode that pays nothing per stage has no economy "
                        + "at all — every price in GD §13.3 is out of reach for the whole run.");
            }

            if (essence.PerBoss <= 0)
            {
                problems.Add(
                    $"{path}: pays {essence.PerBoss} Essence for a boss. GD §15 prices one at 60, "
                        + "on top of the stage clear itself.");
            }
        }

        AssertNoProblems(problems, "Mode Essence income (GD §15)");
    }

    // ---- Rule 2: an id's namespace matches its kind ---------------------------------------------

    [Test]
    public void EveryId_IsNamespacedForItsKind()
    {
        AssertNoProblems(NamespaceProblems(), "Id namespaces (ADR-0010)");
    }

    [Test]
    public void EveryId_IsUniqueAcrossEveryKind()
    {
        // The check ContentCatalog cannot make: it indexes each kind separately, so `enemy.husk`
        // and a skill of the same name would both register and every lookup would find the one its
        // caller happened to ask. Namespacing makes a collision impossible in practice; this is what
        // says so the day two kinds share a prefix.
        var problems = new List<string>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (AuthoredId authored in EveryAuthoredId())
        {
            if (owners.TryGetValue(authored.Id, out string first))
            {
                problems.Add($"{authored.Path}: shares the id '{authored.Id}' with {first}.");
                continue;
            }

            owners.Add(authored.Id, authored.Path);
        }

        AssertNoProblems(problems, "Cross-kind id uniqueness");
    }

    // ---- Rule 3: a file name is the last segment of its id ---------------------------------------

    [Test]
    public void EveryFileName_MatchesTheLastSegmentOfItsId()
    {
        AssertNoProblems(FileNameProblems(), "Asset file names (CLAUDE.md § Asset naming)");
    }

    // ---- Rule 4: present, distinct, resolvable ---------------------------------------------------

    [Test]
    public void EveryLocKey_IsPresent()
    {
        var problems = new List<string>();

        foreach (AuthoredKey authored in EveryAuthoredKey())
        {
            if (string.IsNullOrWhiteSpace(authored.Key.Key))
            {
                problems.Add($"{authored.Path}: {authored.What} is empty.");
                continue;
            }

            foreach (string placeholder in AuthoringPlaceholders)
            {
                if (string.Equals(authored.Key.Key, placeholder, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{authored.Path}: {authored.What} is still the authoring placeholder "
                            + $"'{placeholder}' — nobody named this asset.");
                }
            }
        }

        AssertNoProblems(problems, "LocKeys present");
    }

    [Test]
    public void EveryNodeName_IsDistinct()
    {
        // Two cards reading the same word is indistinguishable, on the screen where it matters, from
        // the offer having drawn the same node twice. Names only: two nodes may legitimately share a
        // description key the day a pair of nodes says the same thing about different stats.
        var problems = new List<string>();
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (string path in PathsOf<SkillDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            if (definition == null)
            {
                continue;
            }

            string key = definition.ToSpec().NameKey.Key;

            if (owners.TryGetValue(key, out string first))
            {
                problems.Add($"{path}: shares the name key '{key}' with {first}.");
                continue;
            }

            owners.Add(key, path);
        }

        AssertNoProblems(problems, "Distinct node names");
    }

    [Test]
    public void EveryLocKey_ResolvesInEnglish()
    {
        AssertNoProblems(
            ResolutionProblems(),
            "LocKeys resolvable in English.asset — M3-14a's table, checked rather than claimed");
    }

    [Test]
    public void EveryTriggerKey_ResolvesInEnglish()
    {
        // Twenty: ten TriggerFields × two comparisons. A trigger line with no row reads as a key on
        // the Skills list, which is a screen the player uses constantly — and TriggerText's own
        // Trigger_EveryPairHasAKey proves the *table* is complete, not that the words exist.
        TableLocalizer localizer = English();
        var problems = new List<string>();
        int pairs = 0;

        foreach (TriggerField field in Enum.GetValues(typeof(TriggerField)))
        {
            foreach (TriggerComparison comparison in Enum.GetValues(typeof(TriggerComparison)))
            {
                LocKey key = TriggerText.KeyFor(field, comparison);
                pairs++;

                if (!localizer.Has(key))
                {
                    problems.Add(
                        $"{EnglishPath}: no row for '{key}' — TriggerText.KeyFor({field}, "
                            + $"{comparison}) draws as itself.");
                }
            }
        }

        Assert.That(pairs, Is.EqualTo(20), "Ten TriggerFields × two comparisons.");

        AssertNoProblems(problems, "Trigger keys resolvable");
    }

    /// <summary>
    /// M3-06's starve check, owed since M3-06 and finally taken (M5-06a rule 8): no shipped asset
    /// may author a trigger clause over a field nothing in the build writes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Over the <em>authored</em> fields rather than over the enum, and the difference is
    /// <see cref="TriggerField.Veilrot"/>.</b> That member must stay — M6-04 is what writes it, and
    /// AR §9 names it — so a check phrased over <c>Enum.GetValues</c> would be red today with the
    /// only available fix being to delete a field the design needs. Phrased over what assets
    /// actually author, it is green until somebody authors the clause that would silently never
    /// fire, which is exactly the day it should go red. It is why
    /// <see href="../../../../Docs/plan/tasks/M5-06b-gravecaller-tree-v1.md">M5-06b</see> rule 6
    /// authors Rot Nova without its Veilrot clause.
    /// </para>
    /// <para>
    /// <b><see cref="Written"/> is a hand-kept list and that is deliberate</b>, against the
    /// reflection <c>Trigger_EveryFieldReads</c> uses one assembly over. That row proves every
    /// member <em>names</em> a blackboard field; no reflection can prove anything <em>writes</em>
    /// one, because a write is a statement in a method rather than a member. So the list is written
    /// out with its writer beside each entry, and a new field arrives absent from it — which fails
    /// here the moment content authors it, with a message saying which writer is missing. The cost
    /// of the list being wrong in the safe direction is a false red; in the unsafe direction it is
    /// a field somebody added to the list without adding the write, and that is a two-line diff a
    /// reviewer is looking straight at.
    /// </para>
    /// </remarks>
    [Test]
    public void EveryTriggerField_HasAWriter()
    {
        var problems = new List<string>();
        var authored = 0;

        foreach (string path in PathsOf<SkillDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            if (definition is null)
            {
                problems.Add($"{path}: did not load as a SkillDefinition.");

                continue;
            }

            ActiveSpec active = definition.ToSpec().Active;

            if (active is null)
            {
                continue;
            }

            IReadOnlyList<TriggerClause> clauses = active.Trigger.Clauses;

            for (int i = 0; i < clauses.Count; i++)
            {
                TriggerField field = clauses[i].Field;
                authored++;

                if (Array.IndexOf(Written, field) >= 0)
                {
                    continue;
                }

                problems.Add(
                    $"{path}: authors a trigger clause over {nameof(TriggerField)}.{field}, and "
                        + "nothing in the build writes that blackboard field — so the condition is "
                        + "read every tick, is always its default, and the skill silently never "
                        + "auto-casts. Give the field a writer, or author the skill against one of "
                        + $"the {Written.Length} that have one.");
            }
        }

        Assert.That(
            authored,
            Is.GreaterThan(0),
            "Sanity: no shipped SkillDefinition authors a trigger clause at all, so this row swept "
                + "nothing. Consecrate and Bulwark each author one.");

        AssertNoProblems(problems, "Trigger fields with a writer");
    }

    // ---- Rule 10: a message names the asset path, always -----------------------------------------

    [Test]
    public void EveryFailureMessage_NamesAPath()
    {
        // The row that proves the other rows can fail, and that a red run says which file to open.
        // Two temporary assets, four collectors: an EnemyDefinition whose id is namespaced for the
        // wrong kind, whose file name matches no segment of it and whose name key has no row; and a
        // LocalizationTable with the same key twice.
        CreateTempFolder();

        var wrong = ScriptableObject.CreateInstance<EnemyDefinition>();
        Set(wrong, "_id", "skill.broken");
        Set(wrong, "_nameKey", "enemy.broken.name");
        AssetDatabase.CreateAsset(wrong, $"{TempFolder}/Wrong.asset");

        var duplicated = ScriptableObject.CreateInstance<LocalizationTable>();
        AssetDatabase.CreateAsset(duplicated, $"{TempFolder}/Duplicated.asset");
        SetTwoRowsWithOneKey(duplicated, "ui.temp.duplicated");

        AssetDatabase.SaveAssets();

        var collected = new List<string>();
        collected.AddRange(Named("namespace", NamespaceProblems()));
        collected.AddRange(Named("file name", FileNameProblems()));
        collected.AddRange(Named("resolution", ResolutionProblems()));
        collected.AddRange(
            Named(
                "table",
                TableProblems($"{TempFolder}/Duplicated.asset", duplicated)));

        Assert.That(collected, Has.Count.AtLeast(4),
            "Four collectors were given something to find and all four must have found it: "
                + string.Join(" | ", collected));

        bool namedTheBrokenOne = false;

        foreach (string problem in collected)
        {
            // "Begins with the path" literally: EnemyDefinitionTests' convention is `{path} is not
            // valid content`, and a message that led with the rule instead would make a red run over
            // twenty-six assets say what is wrong without saying where.
            Assert.That(problem, Does.StartWith("Assets/"),
                $"A problem line does not begin with an asset path: {problem}");

            namedTheBrokenOne |= problem.Contains(TempLeaf);
        }

        // Only that the broken asset is *among* the lines, not that it is the only one. A real
        // problem somewhere else in Data/ must fail its own row rather than this one — this row's
        // subject is the shape of a message, and a second failure reported twice reads as two bugs.
        Assert.That(namedTheBrokenOne, Is.True,
            "No problem line names the deliberately broken asset: " + string.Join(" | ", collected));
    }

    // ---- Rule 11: there is no validator ----------------------------------------------------------

    [Test]
    public void NoValidator_ShipsInGame()
    {
        // Production code here would be a second implementation of rules ContentCatalog,
        // LocalizationTable.ToDictionary and TreeRules already enforce, and the drift between the
        // two would be the bug — one refusing what the other allows, with no way to tell which is
        // right. This row is what stops the next task adding one "for the editor".
        var found = new List<string>();

        foreach (Type type in typeof(CharacterDefinition).Assembly.GetTypes())
        {
            if (type.Name.IndexOf("Validator", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                found.Add(type.FullName);
            }
        }

        Assert.That(found, Is.Empty,
            "Soulvail.Game ships a validator type: " + string.Join(", ", found)
                + ". Content rules live in the core constructors and in this fixture — a third "
                + "place for them is drift with no way to tell which copy is right (rule 11).");
    }

    // ---- The collectors, shared by the rows above and by EveryFailureMessage_NamesAPath ----------

    private static List<string> NamespaceProblems()
    {
        var problems = new List<string>();

        foreach (AuthoredId authored in EveryAuthoredId())
        {
            if (!authored.Id.StartsWith(authored.Namespace, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{authored.Path}: a {authored.Kind}'s id must start with "
                        + $"'{authored.Namespace}' (ADR-0010) and this one is '{authored.Id}'. "
                        + "An id that lies about its kind passes the catalog, the sweeps and the "
                        + "game.");
            }
        }

        // The referenced ids, which no asset of their own kind exists for yet. `arena.*` lives only
        // here until an ArenaDefinition exists; the roster and the cooldown target are checked in
        // the same pass because a reference with the wrong namespace fails at run time, inside a
        // run, as a KeyNotFoundException three systems from the asset that holds it.
        foreach (string path in PathsOf<ModeDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path);

            if (definition == null)
            {
                continue;
            }

            ModeSpec spec = definition.ToSpec();

            foreach (ContentId arena in spec.Arenas)
            {
                if (!arena.Value.StartsWith(ArenaNamespace, StringComparison.Ordinal))
                {
                    problems.Add($"{path}: arena reference '{arena}' is not '{ArenaNamespace}*'.");
                }
            }

            foreach (RosterEntry entry in spec.Roster)
            {
                if (!entry.SpecId.Value.StartsWith(EnemyNamespace, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"{path}: roster reference '{entry.SpecId}' is not '{EnemyNamespace}*'.");
                }
            }
        }

        foreach (string path in PathsOf<EffectDefinition>())
        {
            var definition = AssetDatabase.LoadAssetAtPath<EffectDefinition>(path);

            if (definition == null || definition.ToEffect() is not ModifySkillCooldown cooldown)
            {
                continue;
            }

            if (!cooldown.SkillId.Value.StartsWith(SkillNamespace, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{path}: addresses '{cooldown.SkillId}', which is not '{SkillNamespace}*'.");
            }
        }

        return problems;
    }

    private static List<string> FileNameProblems()
    {
        var problems = new List<string>();

        foreach (AuthoredId authored in EveryAuthoredId())
        {
            string file = Path.GetFileNameWithoutExtension(authored.Path);
            int dot = authored.Id.LastIndexOf('.');
            string segment = dot >= 0 ? authored.Id.Substring(dot + 1) : authored.Id;

            // Case-insensitive on the segment, exact on the shape: `TemperedVow.asset` answers
            // `skill.oathbound.tempered-vow` because a kebab id and a PascalCase file name are the
            // same word written twice, and `Vow.asset` does not answer it at all. The id is what
            // every spec, save file and test refers to; the file name is what a human navigates by,
            // and when they disagree every future search for a node finds the wrong asset.
            string flattened = segment.Replace("-", string.Empty).Replace("_", string.Empty);

            if (!string.Equals(file, flattened, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add(
                    $"{authored.Path}: the id '{authored.Id}' ends in '{segment}', so the file "
                        + $"should be named '{PascalOf(segment)}.asset'.");

                continue;
            }

            if (!IsPascalShaped(file))
            {
                problems.Add(
                    $"{authored.Path}: '{file}' is the right word in the wrong shape — a data "
                        + "asset's file name is PascalCase letters and digits, with the id "
                        + "carrying the separators.");
            }
        }

        return problems;
    }

    private static List<string> ResolutionProblems()
    {
        TableLocalizer localizer = English();
        var problems = new List<string>();

        foreach (AuthoredKey authored in EveryAuthoredKey())
        {
            if (string.IsNullOrWhiteSpace(authored.Key.Key))
            {
                // Owned by EveryLocKey_IsPresent; reported there rather than twice.
                continue;
            }

            if (!localizer.Has(authored.Key))
            {
                problems.Add(
                    $"{authored.Path}: {authored.What} is '{authored.Key}' and no row in "
                        + $"{EnglishPath} answers it. Rule 1 of TableLocalizer makes a missing row "
                        + "invisible — the key draws as itself, which on a screen is "
                        + "indistinguishable from a wrong key.");
            }
        }

        return problems;
    }

    private static List<string> TableProblems(string path, LocalizationTable table)
    {
        var problems = new List<string>();

        try
        {
            IReadOnlyDictionary<LocKey, string> rows = table.ToDictionary();

            if (rows.Count != table.RowCount)
            {
                problems.Add(
                    $"{path}: {table.RowCount} rows converted to {rows.Count} entries.");
            }
        }
        catch (Exception exception)
        {
            problems.Add($"{path}: is not a valid table — {exception.Message}");
        }

        return problems;
    }

    // ---- Walking the project ---------------------------------------------------------------------

    /// <summary>Every asset of <typeparamref name="T"/> in the project, by path, in a fixed order.</summary>
    /// <remarks>
    /// Sorted, because <c>FindAssets</c> makes no promise about order and a sweep whose failure
    /// message lists assets in a different order every run is one nobody can diff.
    /// </remarks>
    internal static IReadOnlyList<string> PathsOf<T>()
        where T : ScriptableObject
    {
        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");
        var paths = new List<string>(guids.Length);

        foreach (string guid in guids)
        {
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }

        paths.Sort(StringComparer.Ordinal);

        return paths;
    }

    /// <summary>
    /// The shipped <see cref="EnemySpec"/> whose id is <paramref name="id"/>, if one exists.
    /// </summary>
    /// <remarks>
    /// A linear walk over every <c>EnemyDefinition</c> on disk rather than a boot list or a
    /// catalog, which is this fixture's whole discipline (see the class remarks): a sweep that
    /// asked <c>BootScope</c> would only ever check the assets somebody remembered to drag onto it.
    /// </remarks>
    private static bool TryFindEnemy(ContentId id, out EnemySpec spec)
    {
        foreach (string path in PathsOf<EnemyDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            if (asset == null || asset.Id != id.Value)
            {
                continue;
            }

            spec = asset.ToSpec();

            return true;
        }

        spec = null;

        return false;
    }

    /// <summary>The one English table, through M3-14a's own door.</summary>
    internal static TableLocalizer English()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    /// <summary>Every id in the project, with the path that carries it and the kind it belongs to.</summary>
    private static IEnumerable<AuthoredId> EveryAuthoredId()
    {
        foreach (string path in PathsOf<CharacterDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredId(path, asset.Id, "character", CharacterNamespace);
            }
        }

        foreach (string path in PathsOf<EnemyDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredId(path, asset.Id, "enemy", EnemyNamespace);
            }
        }

        foreach (string path in PathsOf<ModeDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredId(path, asset.Id, "mode", ModeNamespace);
            }
        }

        foreach (string path in PathsOf<SkillDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredId(path, asset.Id, "skill", SkillNamespace);
            }
        }

        foreach (string path in PathsOf<SkillTreeDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredId(path, asset.Id, "tree", TreeNamespace);
            }
        }
    }

    /// <summary>Every <see cref="LocKey"/> an asset in the project carries.</summary>
    /// <remarks>
    /// Read off the converted spec rather than off the serialized field, so a key that survives
    /// authoring but not conversion is somebody else's red row rather than a silent omission here.
    /// </remarks>
    internal static IEnumerable<AuthoredKey> EveryAuthoredKey()
    {
        foreach (string path in PathsOf<CharacterDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredKey(path, "the name key", asset.ToSpec().NameKey);
            }
        }

        foreach (string path in PathsOf<EnemyDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredKey(path, "the name key", asset.ToSpec().NameKey);
            }
        }

        foreach (string path in PathsOf<ModeDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<ModeDefinition>(path);

            if (asset != null)
            {
                yield return new AuthoredKey(path, "the name key", asset.ToSpec().NameKey);
            }
        }

        foreach (string path in PathsOf<SkillDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillDefinition>(path);

            if (asset == null)
            {
                continue;
            }

            SkillSpec spec = asset.ToSpec();

            yield return new AuthoredKey(path, "the name key", spec.NameKey);
            yield return new AuthoredKey(path, "the description key", spec.DescriptionKey);
        }

        foreach (string path in PathsOf<SkillTreeDefinition>())
        {
            var asset = AssetDatabase.LoadAssetAtPath<SkillTreeDefinition>(path);

            if (asset == null)
            {
                continue;
            }

            SkillTreeSpec spec = asset.ToSpec();

            for (int b = 0; b < spec.Branches.Count; b++)
            {
                yield return new AuthoredKey(
                    path,
                    $"branch {b}'s name key",
                    spec.Branches[b].NameKey);
            }
        }
    }

    // ---- Small shared machinery -------------------------------------------------------------------

    internal static void AssertNoProblems(IReadOnlyList<string> problems, string rule)
    {
        Assert.That(
            problems,
            Is.Empty,
            $"{rule}: {problems.Count} asset(s) are wrong." + Environment.NewLine
                + string.Join(Environment.NewLine, problems));
    }

    private static IEnumerable<string> Named(string collector, IReadOnlyList<string> problems)
    {
        Assert.That(problems, Is.Not.Empty,
            $"The {collector} collector found nothing against a deliberately broken asset, so "
                + "nothing proves it can fail.");

        return problems;
    }

    private static string PascalOf(string segment)
    {
        var builder = new StringBuilder(segment.Length);
        bool upper = true;

        foreach (char c in segment)
        {
            if (c == '-' || c == '_')
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return builder.ToString();
    }

    private static bool IsPascalShaped(string file)
    {
        foreach (char c in file)
        {
            if (!char.IsLetterOrDigit(c))
            {
                return false;
            }
        }

        return file.Length > 0 && char.IsUpper(file[0]);
    }

    private static void CreateTempFolder()
    {
        if (!AssetDatabase.IsValidFolder(TempFolder))
        {
            AssetDatabase.CreateFolder(TempParent, TempLeaf);
        }
    }

    private static void Set(ScriptableObject asset, string field, string value)
    {
        var serialized = new SerializedObject(asset);
        serialized.FindProperty(field).stringValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetTwoRowsWithOneKey(LocalizationTable table, string key)
    {
        var serialized = new SerializedObject(table);
        SerializedProperty rows = serialized.FindProperty("_rows");

        rows.arraySize = 2;

        for (int i = 0; i < 2; i++)
        {
            SerializedProperty row = rows.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("_key").stringValue = key;
            row.FindPropertyRelative("_text").stringValue = $"row {i}";
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>One id, where it lives and what it should be namespaced as.</summary>
    private readonly struct AuthoredId
    {
        internal AuthoredId(string path, string id, string kind, string prefix)
        {
            Path = path;
            Id = id ?? string.Empty;
            Kind = kind;
            Namespace = prefix;
        }

        internal string Path { get; }

        internal string Id { get; }

        internal string Kind { get; }

        internal string Namespace { get; }
    }

    /// <summary>One authored key, and enough words to say which field of which asset it came from.</summary>
    internal readonly struct AuthoredKey
    {
        internal AuthoredKey(string path, string what, LocKey key)
        {
            Path = path;
            What = what;
            Key = key;
        }

        internal string Path { get; }

        internal string What { get; }

        internal LocKey Key { get; }
    }
}
