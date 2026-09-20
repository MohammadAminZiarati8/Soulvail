using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Support;
using TMPro;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The port, the table, the adapter — and the rule the three of them exist to make assertable:
/// AR §11.5's <em>"no raw user-facing string anywhere"</em>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Three of these rows are about the project rather than about this adapter, and they are here on
/// purpose.</b> <see cref="Menu_AndDeathOverlayDrawFromTheTable"/>,
/// <see cref="Readers_DrawNoKeyDirectly"/> and <see cref="Cells_TakeThePortOnTheirDrawCall"/> are
/// claims about six readers, four cells and two assets, and there is no
/// <c>MenuPresenterTests</c> or <c>HudPresenterTests</c> for them to live in. One home for one rule
/// beats a fixture per asset — and splitting them across the four presenter fixtures would leave the
/// rule asserted nowhere and four fragments asserted everywhere.
/// </para>
/// <para>
/// <b>The one row that does <em>not</em> live here is <c>Core_TakesNoLocalizer</c></b>, which is in
/// <c>Soulvail.Tests.Core</c>'s <c>AssemblyPurityTests</c> beside <c>Run_NoTypeTakesAClock</c>. A
/// claim about core's purity belongs in a fixture about core's purity, not in one about a Unity-side
/// adapter — and putting it there is what makes this PR four assemblies rather than three.
/// </para>
/// <para>
/// <b>What is deliberately not asserted anywhere in this task is that every shipped key has a
/// row.</b> Rule 1 makes a miss return the key, so a table written to the wrong spelling produces a
/// screen full of keys and a suite that is entirely green — which is exactly why coverage is
/// <b>M3-14b</b>'s, over every asset, rather than this task's over a table it wrote itself.
/// <see cref="TableLocalizer.Has"/> is the door it will call, and nothing here calls it for that.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TableLocalizerTests
{
    private const string MenuScenePath = "Assets/_Project/Scenes/Menu.unity";
    private const string HudPrefabPath = "Assets/_Project/Prefabs/UI/Hud.prefab";

    /// <summary>
    /// Where the two death strings live as of M4-06: <c>HudPresenter</c>'s overlay was replaced by a
    /// run-end screen on its own prefab, and the keys came with it unchanged (M4-06 rule 3).
    /// </summary>
    private const string RunEndPrefabPath = "Assets/_Project/Prefabs/UI/RunEnd.prefab";

    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string TablePath = "Assets/_Project/Data/Localisation/English.asset";

    private readonly List<Object> _created = new List<Object>();
    private readonly List<IObjectResolver> _resolvers = new List<IObjectResolver>();

    [TearDown]
    public void Cleanup()
    {
        for (int i = _resolvers.Count - 1; i >= 0; i--)
        {
            _resolvers[i].Dispose();
        }

        _resolvers.Clear();

        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // ---- The adapter (rules 1, 6) ----------------------------------------------------------------

    [Test]
    public void Get_ReturnsTheRow()
    {
        TableLocalizer localizer = Localizer("skill.oathbound.consecrate.name", "Consecrate");

        Assert.That(localizer.Get(new LocKey("skill.oathbound.consecrate.name")), Is.EqualTo("Consecrate"));
    }

    [Test]
    public void Get_MissingKeyReturnsTheKey()
    {
        TableLocalizer localizer = Localizer();

        // No throw and nothing logged: a half-translated table is M6-10's normal state, and a screen
        // showing `a.b` diagnoses itself where a screen showing nothing looks like broken layout.
        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("a.b"));
    }

    /// <summary>
    /// <c>default(LocKey)</c> answers empty, and the one character that decides it.
    /// </summary>
    /// <remarks>
    /// <b>This row only passes if the fallback is <c>key.ToString()</c> and not <c>key.Key</c>.</b>
    /// <c>LocKey.Key</c> is null for a default, <c>GetHashCode</c> answers 0 and <c>Equals</c> goes
    /// through an ordinal <c>string.Equals</c> — so the dictionary probe below is legal and simply
    /// misses, and falls through to the fallback. Returning <c>Key</c> would hand a
    /// <see langword="null"/> to a <c>TMP_Text</c> and the <c>NullReferenceException</c> would
    /// surface three screens from the unset field that caused it.
    /// </remarks>
    [Test]
    public void Get_DefaultKeyReturnsEmpty()
    {
        TableLocalizer localizer = Localizer("a.b", "words");

        Assert.That(localizer.Get(default), Is.EqualTo(string.Empty));
        Assert.That(localizer.Get(default), Is.Not.Null, "a null would reach a TMP_Text as a null.");

        // The premise, stated so this row cannot pass for the wrong reason: a default key really is
        // a legal probe rather than something the dictionary throws on.
        Assert.That(default(LocKey).Key, Is.Null);
        Assert.That(default(LocKey).ToString(), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Get_IsOrdinalAndCaseSensitive()
    {
        TableLocalizer localizer = Localizer("a.b", "words");

        // LocKey.Equals is an ordinal string comparison, so a key is an identifier and ADR-0010's
        // rule about content identity does not soften for text. The failure is visible on screen.
        Assert.That(localizer.Get(new LocKey("A.B")), Is.EqualTo("A.B"));
        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("words"));
    }

    [Test]
    public void Get_AllocatesNothing()
    {
        var pairs = new string[2_000];

        for (int i = 0; i < 1_000; i++)
        {
            pairs[i * 2] = $"key.{i}";
            pairs[(i * 2) + 1] = $"words {i}";
        }

        TableLocalizer localizer = Localizer(pairs);

        var hit = new LocKey("key.500");
        var miss = new LocKey("key.none");

        // AllocationAssert, never the raw GC API: GC.GetAllocatedBytesForCurrentThread() is inert on
        // Unity's Mono and would report "no allocation" for code that allocates freely (Traps §7).
        AllocationAssert.None(() => localizer.Get(hit));

        // **The miss path allocates nothing either, which is the less obvious half**: LocKey.ToString
        // returns the string the key already holds rather than building one, so M6-10's normal
        // half-translated state costs nothing per frame.
        AllocationAssert.None(() => localizer.Get(miss));
    }

    [Test]
    public void Has_AnswersWithoutAllocating()
    {
        TableLocalizer localizer = Localizer("a.b", "words");

        var hit = new LocKey("a.b");
        var miss = new LocKey("a.c");

        Assert.That(localizer.Has(hit), Is.True);
        Assert.That(localizer.Has(miss), Is.False);

        // M3-14b sweeps every shipped asset through this, so it is on a path that walks content.
        AllocationAssert.None(() => localizer.Has(hit));
        AllocationAssert.None(() => localizer.Has(miss));
    }

    [Test]
    public void Count_IsTheRowCount()
    {
        Assert.That(Localizer().Count, Is.Zero);
        Assert.That(Localizer("a.b", "one", "a.c", "two").Count, Is.EqualTo(2));
    }

    [Test]
    public void Construct_RefusesANullTable()
    {
        Assert.That(() => new TableLocalizer(null), Throws.ArgumentNullException);
    }

    // ---- The table (rules 2, 3) ------------------------------------------------------------------

    [Test]
    public void Table_ConvertsEveryRow()
    {
        LocalizationTable table = Table("a.b", "one", "a.c", "two", "a.d", "line\nbreak");

        IReadOnlyDictionary<LocKey, string> rows = table.ToDictionary();

        Assert.That(rows.Count, Is.EqualTo(3));
        Assert.That(rows[new LocKey("a.b")], Is.EqualTo("one"));
        Assert.That(rows[new LocKey("a.c")], Is.EqualTo("two"));

        // The text is preserved exactly, line breaks included — the field is a [TextArea] and a
        // description that wrapped deliberately would otherwise be silently reflowed.
        Assert.That(rows[new LocKey("a.d")], Is.EqualTo("line\nbreak"));
    }

    [Test]
    public void Table_DuplicateKey_NamesTheAsset()
    {
        LocalizationTable table = Table("a.b", "one", "a.b", "two");
        table.name = "English";

        var thrown = Assert.Throws<ArgumentException>(() => table.ToDictionary());

        // ContentCatalog's message and its reason: a duplicate silently picks one row and which one
        // depends on authoring order, which is the failure it has refused since M0-08.
        Assert.That(thrown.Message, Does.Contain("Duplicate key 'a.b' in 'English'"));
        Assert.That(thrown.Message, Does.Contain("must be unique"));
    }

    [Test]
    public void Table_EmptyKey_NamesTheAsset()
    {
        foreach (string bad in new[] { string.Empty, "  ", "has space" })
        {
            LocalizationTable table = Table(bad, "words");
            table.name = "English";

            var thrown = Assert.Throws<ArgumentException>(
                () => table.ToDictionary(), $"'{bad}' was accepted as a key.");

            Assert.That(thrown.Message, Does.Contain("English"), "the message must name the asset.");
        }
    }

    [Test]
    public void Table_EmptyTextIsLegal()
    {
        IReadOnlyDictionary<LocKey, string> rows = Table("a.b", string.Empty).ToDictionary();

        // An untranslated row is the normal state of every table M6-10 adds, so it has to be
        // authorable here too — the asymmetry with an empty *key*, which is a typo, is the point.
        Assert.That(rows.Count, Is.EqualTo(1));
        Assert.That(rows[new LocKey("a.b")], Is.EqualTo(string.Empty));
    }

    [Test]
    public void Table_RoundTripsThroughSerializedObject()
    {
        // The route the shipped asset was authored by, and the route the Inspector takes (Traps §5,
        // M3-02b rule 2's shape) — a table that only worked when built in code would be a table
        // nobody could edit.
        LocalizationTable table = Table("a.b", "one", "a.c", "two");

        Assert.That(table.RowCount, Is.EqualTo(2));

        IReadOnlyDictionary<LocKey, string> rows = table.ToDictionary();

        Assert.That(rows[new LocKey("a.b")], Is.EqualTo("one"));
        Assert.That(rows[new LocKey("a.c")], Is.EqualTo("two"));
    }

    [Test]
    public void Table_IsLinkedToAMonoScript()
    {
        var asset = AssetDatabase.LoadAssetAtPath<LocalizationTable>(TablePath);

        // Traps §5: a ScriptableObject in a file-scoped namespace compiles, links no MonoScript, and
        // loads as null with nothing reporting an error. This is the only row that would catch it.
        Assert.That(asset, Is.Not.Null, $"English.asset did not load as a LocalizationTable ({TablePath}).");
        Assert.That(asset.RowCount, Is.GreaterThan(0), "the shipped table is empty.");
    }

    // ---- The port (rule 5) -----------------------------------------------------------------------

    [Test]
    public void Port_HasOneMember()
    {
        Type port = typeof(ILocalizer);

        MethodInfo[] methods = port.GetMethods();

        Assert.That(methods, Has.Length.EqualTo(1), "ILocalizer grew a member.");
        Assert.That(methods[0].Name, Is.EqualTo(nameof(ILocalizer.Get)));

        ParameterInfo[] parameters = methods[0].GetParameters();

        Assert.That(parameters, Has.Length.EqualTo(1));
        Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(LocKey)));

        // **AR §6's row was corrected to match this, not the other way round** (rule 5). A
        // `params object[]` overload allocates an array on every call, and these are reached from
        // HudPresenter and AutoCastRow. M6-10 adds it when a translated sentence needs a
        // substitution *inside* it, which is a real need and not this one.
        Assert.That(
            parameters[0].IsDefined(typeof(ParamArrayAttribute), false),
            Is.False,
            "The port grew a params overload. AR §6 line 193 says one member — correct one or the "
                + "other, and say which.");

        Assert.That(port.GetProperties(), Is.Empty);
        Assert.That(port.GetEvents(), Is.Empty);
    }

    // ---- Boot (rule 4) ---------------------------------------------------------------------------

    [Test]
    public void Boot_RegistersTheLocalizer()
    {
        IObjectResolver resolver = BuildBoot(LoadShippedTable());

        var localizer = resolver.Resolve<ILocalizer>();

        Assert.That(localizer, Is.InstanceOf<TableLocalizer>(), "the port resolves to the adapter.");

        // A singleton, so every screen in the app shares one dictionary rather than converting the
        // table per scope — and so the Menu and a run cannot disagree about a word.
        Assert.That(resolver.Resolve<ILocalizer>(), Is.SameAs(localizer));

        Assert.That(((TableLocalizer)localizer).Count, Is.EqualTo(LoadShippedTable().RowCount));
    }

    [Test]
    public void Boot_MissingTable_NamesTheField()
    {
        var builder = new ContainerBuilder();

        var thrown = Assert.Throws<ArgumentNullException>(
            () => BootInstaller.Install(
                builder,
                Array.Empty<CharacterDefinition>(),
                Array.Empty<EnemyDefinition>(),
                Array.Empty<ModeDefinition>(),
                Array.Empty<SkillDefinition>(),
                Array.Empty<SkillTreeDefinition>(),
                null));

        // The *field*, not the parameter: what a reader has to go and drag something onto is a slot
        // on a prefab, and "localization is null" points at the wrong file.
        Assert.That(thrown.Message, Does.Contain("Localization field"));
        Assert.That(thrown.Message, Does.Contain("English.asset"));
    }

    [Test]
    public void Boot_ScopeCarriesTheTable()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        Component scope = prefab.GetComponent("BootScope");

        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        SerializedProperty field = new SerializedObject(scope).FindProperty("_localization");

        Assert.That(field, Is.Not.Null, "BootScope has no _localization field.");
        Assert.That(
            field.objectReferenceValue,
            Is.Not.Null,
            "BootScope.prefab ships with an empty Localization slot, so every screen would draw "
                + "its key. Drop Data/Localisation/English.asset onto it.");
    }

    /// <summary>
    /// The localizer outlives a run, which is the whole of why it is registered at the root.
    /// </summary>
    /// <remarks>
    /// <b>A child scope stands in for <c>RunScope</c>, and the stand-in is the honest part.</b> A
    /// real run would need a scene load and is PlayMode's; what this row asserts is the property
    /// that makes the Menu work — resolving through a child gives the <em>root's</em> instance, and
    /// disposing that child leaves it standing. A localizer that lived in <c>RunScope</c> is exactly
    /// how <em>"Descend"</em> would have stayed English for another three milestones (rule 4).
    /// </remarks>
    [Test]
    public void Boot_LocalizerOutlivesARun()
    {
        IObjectResolver root = BuildBoot(LoadShippedTable());

        var fromRoot = root.Resolve<ILocalizer>();

        IScopedObjectResolver run = root.CreateScope(_ => { });

        Assert.That(
            run.Resolve<ILocalizer>(),
            Is.SameAs(fromRoot),
            "a run resolved its own localizer, so the Menu and the run could disagree about a word.");

        run.Dispose();

        Assert.That(
            root.Resolve<ILocalizer>(),
            Is.SameAs(fromRoot),
            "disposing the run took the localizer with it, so the Menu would have none.");

        Assert.That(fromRoot.Get(new LocKey("ui.menu.descend")), Is.Not.Empty);
    }

    // ---- AR §11.5, which is what this whole task makes assertable (rules 8, 9, 11) ---------------

    /// <summary>
    /// The five raw English strings rule 8 names — and there were five rather than four.
    /// </summary>
    /// <remarks>
    /// <b>The ROADMAP's parking-lot line counts four</b>: <c>"Soulvail"</c> and <c>"Descend"</c> in
    /// <c>Menu.unity</c>, <c>"You died"</c> and <c>"Tap to return"</c> on <c>Hud.prefab</c>.
    /// <c>Continue</c> was added by M3-07b's resume flow and counted by neither that line nor rule 8,
    /// and AR §11.5 is unenforceable if it is exempt — so it is a key too, and the count is the
    /// owner's to correct in the ROADMAP.
    /// <para>
    /// <b>A sixth English string exists and is deliberately out of scope:</b> <c>Boot.unity</c>
    /// carries its own <c>"Soulvail"</c> splash, which nothing resolves because nothing in that scene
    /// is a presenter. It shares <c>ui.app.title</c>'s row, so wiring it later costs a field.
    /// </para>
    /// <para>
    /// <b>The two death strings moved asset at M4-06 and this row followed them</b> (rule 3). They
    /// keep their keys, and the prefab they are drawn on is <c>RunEnd.prefab</c> now rather than
    /// <c>Hud.prefab</c> — so the sweep is on that asset, with the three new run-end rows beside
    /// them. <c>Hud.prefab</c> is still read here for the one string that is deliberately <em>not</em>
    /// a key.
    /// </para>
    /// </remarks>
    [Test]
    public void Menu_AndDeathOverlayDrawFromTheTable()
    {
        IReadOnlyList<string> menu = AuthoredText(MenuScenePath);

        Assert.That(menu, Is.Not.Empty, "the scene has no TMP_Text at all, so this row tests nothing.");

        foreach (string authored in new[] { "Descend", "Soulvail", "Continue" })
        {
            Assert.That(
                menu,
                Does.Not.Contain(authored),
                $"'{authored}' is still typed into Menu.unity. AR §11.5: no raw user-facing string "
                    + "anywhere, and M6-10 must inherit no English typed into a scene.");
        }

        // **The prefab the two death strings are swept on is RunEnd.prefab as of M4-06.** The
        // overlay they were typed into left Hud.prefab with rule 2's deletion, so a row that kept
        // reading the HUD would go green on an asset that no longer has the labels at all — which is
        // a row asserting nothing rather than a row that passes.
        IReadOnlyList<string> runEnd = AuthoredText(RunEndPrefabPath);

        Assert.That(runEnd, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        foreach (string authored in new[] { "You died", "Tap to return", "Depth", "Bosses", "Soul Shards" })
        {
            Assert.That(
                runEnd,
                Does.Not.Contain(authored),
                $"'{authored}' is still typed into RunEnd.prefab.");
        }

        // And the eight have rows, so the screens that now ask for them get words rather than keys.
        TableLocalizer shipped = new TableLocalizer(LoadShippedTable());

        foreach (string key in new[]
        {
            "ui.app.title", "ui.menu.descend", "ui.menu.continue", "ui.death.title", "ui.death.hint",
            "ui.runend.depth", "ui.runend.bosses", "ui.runend.shards",
        })
        {
            Assert.That(shipped.Has(new LocKey(key)), Is.True, $"English.asset has no row for {key}.");
        }

        IReadOnlyList<string> hud = AuthoredText(HudPrefabPath);

        Assert.That(hud, Is.Not.Empty, "the prefab has no TMP_Text at all, so this row tests nothing.");

        // **The HP readout is deliberately still not among them** — "{0:0}/{1:0}" is a number format
        // rather than a sentence and survives localisation unchanged, which is the distinction the
        // parking-lot line drew and this task keeps.
        Assert.That(hud, Does.Contain("140/140"));
    }

    [Test]
    public void Readers_DrawNoKeyDirectly()
    {
        foreach (Type reader in new[]
        {
            typeof(OfferCard),
            typeof(SkillRow),
            typeof(TreeNodeView),
            typeof(ManualSkillButton),
            typeof(FirstActiveHint),
            typeof(OverflowToast),
        })
        {
            // Ledger row 9's six readers, by name. Each must be able to reach the port at all —
            // through a draw call for the four pooled cells, through [Inject] for the two
            // components RunScope owns.
            Assert.That(
                MentionsLocalizer(reader),
                Is.True,
                $"{reader.Name} does not take an ILocalizer anywhere, so it can only draw keys.");
        }
    }

    /// <summary>
    /// Rule 11: a cell is handed the port by the screen that instantiates it, not by injection.
    /// </summary>
    /// <remarks>
    /// <b>The draw call is <c>Show</c> on all four</b>, which the spec's own text does not quite say
    /// — its Tests table writes <em>"<c>Draw</c>"</em> for the slot button and rule 11 writes
    /// <em>"<c>Show</c>/<c>Draw</c>/<c>Bind</c>"</em>. <c>ManualSkillButton</c> has a <c>Bind</c> and
    /// a private <c>Draw</c>, and the one that writes the label is <c>Show</c>. <b>The method is
    /// asserted to exist before anything is asserted about it</b>, because a reflection row that
    /// looked for a method that is not there would go green on nothing at all.
    /// </remarks>
    [Test]
    public void Cells_TakeThePortOnTheirDrawCall()
    {
        foreach (Type cell in new[]
        {
            typeof(OfferCard), typeof(SkillRow), typeof(TreeNodeView), typeof(ManualSkillButton),
        })
        {
            MethodInfo show = cell.GetMethod("Show", BindingFlags.Instance | BindingFlags.Public);

            Assert.That(show, Is.Not.Null, $"{cell.Name} has no public Show — this row tests nothing.");

            var takesPort = false;

            foreach (ParameterInfo parameter in show.GetParameters())
            {
                takesPort |= parameter.ParameterType == typeof(ILocalizer);
            }

            Assert.That(takesPort, Is.True, $"{cell.Name}.Show does not take an ILocalizer.");

            // And nothing injects one individually — they are pooled templates and prefab clones,
            // which is the fact that decided M3-13a rule 2 against an injected palette as well.
            foreach (MethodInfo method in cell.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Assert.That(
                    method.IsDefined(typeof(InjectAttribute), true),
                    Is.False,
                    $"{cell.Name}.{method.Name} is an [Inject] method. A pooled cell is handed its "
                        + "dependencies by the presenter that owns the pool (rule 11).");
            }
        }
    }

    /// <summary>
    /// The rewritten rows, pinned so that <em>deleting</em> one fails rather than only renaming it.
    /// </summary>
    /// <remarks>
    /// M3-13a's <c>RewrittenRows_StillAssertWhatTheyAsserted</c>, copied for its reason: four rows
    /// that pinned a key on purpose were inverted by this task, and an inversion that quietly became
    /// a deletion would close ledger row 9 on nothing. Each entry names a fixture and a row, asserts
    /// the method exists and carries <c>[Test]</c>.
    /// </remarks>
    [Test]
    public void RewrittenRows_StillAssertWhatTheyAsserted()
    {
        (string Fixture, string Row)[] rewritten =
        {
            ("Soulvail.Tests.Game.Presentation.LevelUpPresenterTests", "Card_DrawsEnglish"),
            ("Soulvail.Tests.Game.Presentation.SkillsPresenterTests", "Card_DrawsEnglish"),
            ("Soulvail.Tests.Game.Presentation.TreeViewPresenterTests", "Tree_DrawsEnglish"),
            ("Soulvail.Tests.Game.Presentation.SkillBarPresenterTests", "Bar_LabelsAreWords"),
            ("Soulvail.Tests.Game.Presentation.FirstActiveHintTests", "Hint_ShowsOnTheFirstActive"),
        };

        foreach ((string fixture, string row) in rewritten)
        {
            Type type = typeof(TableLocalizerTests).Assembly.GetType(fixture);

            Assert.That(type, Is.Not.Null, $"{fixture} is gone.");

            MethodInfo method = type.GetMethod(
                row, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null, $"{fixture}.{row} was deleted rather than rewritten.");
            Assert.That(
                method.IsDefined(typeof(TestAttribute), false),
                Is.True,
                $"{fixture}.{row} is no longer a test.");
        }
    }

    // ---- Fixture ----------------------------------------------------------------------------------

    /// <summary>
    /// The real adapter, typed as itself rather than as the port, because <see cref="TableLocalizer.Has"/>
    /// and <see cref="TableLocalizer.Count"/> are deliberately not on <see cref="ILocalizer"/> —
    /// the port has one member (rule 5) and <c>Port_HasOneMember</c> is what keeps it that way.
    /// </summary>
    private TableLocalizer Localizer(params string[] pairs) => new TableLocalizer(Table(pairs));

    /// <summary>
    /// A table authored through <see cref="SerializedObject"/>, which is the Inspector's own route.
    /// </summary>
    private LocalizationTable Table(params string[] pairs)
    {
        var table = ScriptableObject.CreateInstance<LocalizationTable>();
        _created.Add(table);

        var serialized = new SerializedObject(table);
        SerializedProperty rows = serialized.FindProperty("_rows");

        rows.arraySize = pairs.Length / 2;

        for (int i = 0; i < rows.arraySize; i++)
        {
            SerializedProperty row = rows.GetArrayElementAtIndex(i);
            row.FindPropertyRelative("_key").stringValue = pairs[i * 2];
            row.FindPropertyRelative("_text").stringValue = pairs[(i * 2) + 1];
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        return table;
    }

    private static LocalizationTable LoadShippedTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(TablePath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {TablePath}.");

        return table;
    }

    private IObjectResolver BuildBoot(LocalizationTable table)
    {
        var builder = new ContainerBuilder();

        BootInstaller.Install(
            builder,
            Array.Empty<CharacterDefinition>(),
            Array.Empty<EnemyDefinition>(),
            Array.Empty<ModeDefinition>(),
            Array.Empty<SkillDefinition>(),
            Array.Empty<SkillTreeDefinition>(),
            table);

        IObjectResolver resolver = builder.Build();
        _resolvers.Add(resolver);

        return resolver;
    }

    /// <summary>Every <c>m_text:</c> value in an asset's YAML — what a player would actually read.</summary>
    /// <remarks>
    /// <para>
    /// Read off disk rather than out of a loaded hierarchy, because the claim is about the
    /// <em>asset</em>: a label written at runtime is fine, and a label with English baked into it is
    /// what M6-10 must not inherit. That distinction is invisible to a fixture that instantiates.
    /// </para>
    /// <para>
    /// <b>Only the <c>m_text:</c> lines, and that is a correction rather than a refinement.</b> A
    /// first draft searched the whole file and went red on <c>"Descend"</c> — which is there, as the
    /// <em>GameObject's name</em>. AR §11.5 is about what a player reads, and a well-named object is
    /// not a localisation failure; a row that said otherwise would have forced the scene to be
    /// renamed to pass.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> AuthoredText(string path)
    {
        Assert.That(System.IO.File.Exists(path), Is.True, $"No asset at {path}.");

        const string Marker = "  m_text: ";

        var drawn = new List<string>();

        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            if (line.StartsWith(Marker, StringComparison.Ordinal))
            {
                drawn.Add(line.Substring(Marker.Length).Trim());
            }
        }

        return drawn;
    }

    private static bool MentionsLocalizer(Type reader)
    {
        foreach (MethodInfo method in reader.GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(ILocalizer))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
