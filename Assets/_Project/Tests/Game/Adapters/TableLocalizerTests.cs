using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// <b>What is deliberately not asserted here is that every shipped key has a row.</b> A miss
/// returns the key, so a table written to the wrong spelling produces a screen full of keys and a
/// suite that is entirely green. Coverage is therefore checked somewhere else: M3-14b's
/// <c>ContentValidationTests</c> over every asset, and M6-10's <c>LocalisationSweepTests</c> over
/// the code. <see cref="TableLocalizer.Has"/> is the door both call.
/// </para>
/// <para>
/// <b>M6-10 made it many tables</b>: the pick, the two-deep fallback, <c>Format</c> and the
/// reader's culture are rows here, and the profile's locale being read at boot is
/// <c>ResumeFlowTests</c>', where <c>BootFlow</c>'s fixture lives.
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

    /// <summary>
    /// M5-07's class-select screen. Swept here for the same thing the Menu scene is swept for — no
    /// raw English typed into an asset — and the shape of what it <em>does</em> draw is
    /// <c>ClassSelectPresenterTests.Select_DrawsNoRawEnglish</c>'s.
    /// </summary>
    private const string ClassSelectPrefabPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
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

    /// <summary>
    /// Rule 9: 100 000 calls down each of the three paths, and not a byte.
    /// </summary>
    [Test]
    public void Localizer_GetAllocatesNothing()
    {
        var pairs = new string[2_000];

        for (int i = 0; i < 1_000; i++)
        {
            pairs[i * 2] = $"key.{i}";
            pairs[(i * 2) + 1] = $"words {i}";
        }

        // English with a thousand rows, and a translation carrying one of them — so key.500 is a
        // hit in the fallback only, which is the rung M6-10 added.
        var localizer = new TableLocalizer(
            new[] { Table(pairs), Localed("de", "key.1", "Wörter") }, "de");

        var hit = new LocKey("key.1");
        var fallback = new LocKey("key.500");
        var miss = new LocKey("key.none");

        Assert.That(localizer.Get(fallback), Is.EqualTo("words 500"), "the fixture's premise.");

        // AllocationAssert, never the raw GC API: GC.GetAllocatedBytesForCurrentThread() is inert on
        // Unity's Mono and would report "no allocation" for code that allocates freely (Traps §7).
        AllocationAssert.None(() => localizer.Get(hit), 100_000);
        AllocationAssert.None(() => localizer.Get(fallback), 100_000);

        // **The miss path allocates nothing either, which is the less obvious half**: LocKey.ToString
        // returns the string the key already holds rather than building one, so a half-translated
        // table costs nothing per frame.
        AllocationAssert.None(() => localizer.Get(miss), 100_000);
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
        Assert.That(() => new TableLocalizer((LocalizationTable)null), Throws.ArgumentNullException);

        // The one-table form has to be the fallback: a lone German table has nothing to fall to.
        Assert.That(() => new TableLocalizer(Localed("de", "a.b", "c")), Throws.ArgumentException);
    }

    // ---- Many tables (M6-10 rules 1, 7) ------------------------------------------------------------

    [Test]
    public void Table_CarriesItsLocale()
    {
        Assert.That(LoadShippedTable().Locale, Is.Empty, "English is the fallback and its tag is empty.");
        Assert.That(LoadPseudoTable().Locale, Is.EqualTo("qps-ploc"));

        // An asset authored before the field existed reads empty rather than null.
        Assert.That(Table().Locale, Is.Empty);
    }

    [Test]
    public void Localizer_RefusesTwoFallbacks()
    {
        LocalizationTable first = Table("a.b", "one");
        LocalizationTable second = Table("a.b", "two");

        first.name = "English";
        second.name = "EnglishAgain";

        var thrown = Assert.Throws<ArgumentException>(
            () => new TableLocalizer(new[] { first, second }, string.Empty));

        // Both assets, because "which English do we show" is a question with no answer, and the
        // reader has to open one of the two files to fix it.
        Assert.That(thrown.Message, Does.Contain("English").And.Contain("EnglishAgain"));
        Assert.That(thrown.Message, Does.Contain("fallback"));
    }

    [Test]
    public void Localizer_RefusesTwoTablesForOneLocale()
    {
        LocalizationTable first = Localed("de", "a.b", "eins");
        LocalizationTable second = Localed("DE", "a.b", "zwei");

        first.name = "German";
        second.name = "GermanToo";

        // Rule 1's argument, one language over: two German tables are the same unanswerable question,
        // and a tag's case does not make them two languages.
        var thrown = Assert.Throws<ArgumentException>(
            () => new TableLocalizer(new[] { Table(), first, second }, string.Empty));

        Assert.That(thrown.Message, Does.Contain("German").And.Contain("GermanToo"));
    }

    [Test]
    public void Localizer_RefusesNoFallback()
    {
        var thrown = Assert.Throws<ArgumentException>(
            () => new TableLocalizer(new[] { Localed("de", "a.b", "c"), Localed("fr", "a.b", "d") }, "de"));

        Assert.That(thrown.Message, Does.Contain("no fallback"));
    }

    [Test]
    public void Localizer_RefusesAnEmptyOrNullTableSet()
    {
        Assert.That(
            () => new TableLocalizer(Array.Empty<LocalizationTable>(), string.Empty),
            Throws.ArgumentException);

        var withNull = Assert.Throws<ArgumentException>(
            () => new TableLocalizer(new[] { Table(), null }, string.Empty));

        Assert.That(withNull.Message, Does.Contain("tables[1]"), "the empty slot is named by index.");

        Assert.That(
            () => new TableLocalizer((IReadOnlyList<LocalizationTable>)null, string.Empty),
            Throws.ArgumentNullException);

        Assert.That(() => new TableLocalizer(new[] { Table() }, null), Throws.ArgumentNullException);
    }

    [Test]
    public void Localizer_ReadsThePickedTable()
    {
        var localizer = new TableLocalizer(new[] { LoadShippedTable(), LoadPseudoTable() }, "qps-ploc");
        var key = new LocKey("ui.menu.descend");

        Assert.That(localizer.Locale, Is.EqualTo("qps-ploc"));
        Assert.That(localizer.Get(key), Is.EqualTo(LoadPseudoTable().ToDictionary()[key]));
        Assert.That(localizer.Get(key), Is.Not.EqualTo(LoadShippedTable().ToDictionary()[key]));
        Assert.That(localizer.Count, Is.EqualTo(LoadPseudoTable().RowCount), "Count is the table being read.");
    }

    [Test]
    public void Localizer_FallsBackARung()
    {
        var localizer = new TableLocalizer(
            new[] { Table("a.b", "english", "a.c", "only english"), Localed("qps-ploc", "a.b", "[pseudo]") },
            "qps-ploc");

        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("[pseudo]"));
        Assert.That(localizer.Get(new LocKey("a.c")), Is.EqualTo("only english"), "rule 1's middle rung.");

        // Has asks the table being read and not the chain, so a sweep over a translation finds the
        // rows the translation lacks rather than being told English has them.
        Assert.That(localizer.Has(new LocKey("a.c")), Is.False);
        Assert.That(localizer.Has(new LocKey("a.b")), Is.True);
    }

    [Test]
    public void Localizer_FallsBackToTheKey()
    {
        var localizer = new TableLocalizer(new[] { Table("a.b", "one"), Localed("qps-ploc", "a.b", "[one]") }, "qps-ploc");

        Assert.That(localizer.Get(new LocKey("x.y")), Is.EqualTo("x.y"), "ILocalizer's existing contract.");
        Assert.That(localizer.Get(default), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Localizer_AnUnknownLocaleIsTheFallback()
    {
        TableLocalizer localizer = null;

        // Rule 7: a player who picked German and then took a build that dropped it gets English,
        // rather than a boot that refuses a save because a table was renamed.
        Assert.DoesNotThrow(
            () => localizer = new TableLocalizer(new[] { Table("a.b", "english") }, "de"));

        Assert.That(localizer.Locale, Is.Empty, "the table read, not the tag asked for.");
        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("english"));

        localizer.SetLocale("fr");

        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("english"));
    }

    /// <summary>Rule 1: two deep, and there is no language-then-region step between.</summary>
    [Test]
    public void Localizer_TheChainIsTwoDeep()
    {
        var localizer = new TableLocalizer(
            new[] { Table("a.b", "english"), Localed("de", "a.b", "deutsch") }, "de-AT");

        // de-AT does not find de. A region chain is a feature nothing has asked for, and it is one
        // loop in Pick the day something does.
        Assert.That(localizer.Locale, Is.Empty);
        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("english"));

        // A tag's case is not a region: BCP-47 tags are case-insensitive.
        localizer.SetLocale("DE");

        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("deutsch"));
    }

    [Test]
    public void Localizer_SetLocaleSwitchesAndRefusesNull()
    {
        var localizer = new TableLocalizer(new[] { Table("a.b", "english"), Localed("de", "a.b", "deutsch") }, string.Empty);

        localizer.SetLocale("de");

        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("deutsch"));
        Assert.That(localizer.Culture.NumberFormat.NumberDecimalSeparator, Is.EqualTo(","));

        localizer.SetLocale(string.Empty);

        Assert.That(localizer.Get(new LocKey("a.b")), Is.EqualTo("english"));
        Assert.That(() => localizer.SetLocale(null), Throws.ArgumentNullException);
    }

    // ---- Format (M6-10 rules 4, 5) -----------------------------------------------------------------

    [Test]
    public void Format_Substitutes()
    {
        TableLocalizer localizer = Localizer("ui.classselect.locked.deed", "or reach stage {0}");

        Assert.That(
            localizer.Format(new LocKey("ui.classselect.locked.deed"), 20),
            Is.EqualTo("or reach stage 20"));
    }

    [Test]
    public void Format_UsesTheLocalesCulture()
    {
        var localizer = new TableLocalizer(
            new[] { Table("speed", "{0:0.0} m/s"), Localed("de-DE", "speed", "{0:0.0} m/s") }, "de-DE");

        // A German reader expects 3,4 — and gets it when the word falls back a rung, too.
        Assert.That(localizer.Format(new LocKey("speed"), 3.4f), Is.EqualTo("3,4 m/s"));

        var fallsBack = new TableLocalizer(new[] { Table("speed", "{0:0.0} m/s"), Localed("de-DE") }, "de-DE");

        Assert.That(fallsBack.Format(new LocKey("speed"), 3.4f), Is.EqualTo("3,4 m/s"), "the reader's culture, not the row's.");

        // And English is what every screen has drawn since M3.
        Assert.That(Localizer("speed", "{0:0.0} m/s").Format(new LocKey("speed"), 3.4f), Is.EqualTo("3.4 m/s"));
    }

    [Test]
    public void Format_FallsBackToTheCultureItKnows()
    {
        // qps-ploc is the shipped case: Windows has a culture for it and Unity's Mono does not.
        LocalizationTable pseudo = Localed("qps-ploc", "speed", "[{0:0.0} m/s]");
        TableLocalizer localizer = null;

        Assert.DoesNotThrow(() => localizer = new TableLocalizer(new[] { Table(), pseudo }, "qps-ploc"));

        Assert.That(pseudo.Culture, Is.SameAs(CultureInfo.InvariantCulture));
        Assert.That(localizer.Culture, Is.SameAs(CultureInfo.InvariantCulture));
        Assert.That(localizer.Format(new LocKey("speed"), 3.4f), Is.EqualTo("[3.4 m/s]"));
    }

    [Test]
    public void Format_AWrongPlaceholderCountDoesNotThrow()
    {
        TableLocalizer localizer = Localizer("pick", "Pick {0} of {1}", "broken", "Pick {0 of {1}");

        string written = null;

        Assert.DoesNotThrow(() => written = localizer.Format(new LocKey("pick"), 1));
        Assert.That(written, Is.EqualTo("Pick {0} of {1}"), "the unsubstituted row, not a crash.");

        Assert.That(localizer.Format(new LocKey("broken"), 1, 2), Is.EqualTo("Pick {0 of {1}"));
    }

    [Test]
    public void Format_AMissingRowStillSubstitutesNothing()
    {
        TableLocalizer localizer = Localizer("a.b", "{0} words");

        Assert.That(localizer.Format(new LocKey("x.y"), 7), Is.EqualTo("x.y"));
    }

    [Test]
    public void Format_DefaultKeyAndNullArgumentsAnswerQuietly()
    {
        TableLocalizer localizer = Localizer("a.b", "plain", "a.c", "{0} words");

        Assert.That(localizer.Format(default, 1), Is.EqualTo(string.Empty));
        Assert.That(localizer.Format(new LocKey("a.b"), null), Is.EqualTo("plain"));
        Assert.That(localizer.Format(new LocKey("a.c"), null), Is.EqualTo("{0} words"));
        Assert.That(localizer.Format(new LocKey("a.c")), Is.EqualTo("{0} words"));
    }

    /// <summary>
    /// Rule 4: the two callers that run near the frame never reach the member that allocates.
    /// </summary>
    [Test]
    public void Localizer_TheFrameAdjacentCallersDoNotFormat()
    {
        MethodInfo port = typeof(ILocalizer).GetMethod(nameof(ILocalizer.Format));
        MethodInfo adapter = typeof(TableLocalizer).GetMethod(nameof(TableLocalizer.Format));

        // The control, so this row cannot pass on a sweep that finds nothing: the class card formats.
        Assert.That(LocalisationSweepTests.Calls(typeof(ClassCard), port), Is.True, "the IL sweep is blind.");

        foreach (Type frameAdjacent in new[] { typeof(HudPresenter), typeof(AutoCastRow) })
        {
            Assert.That(
                LocalisationSweepTests.Calls(frameAdjacent, port) || LocalisationSweepTests.Calls(frameAdjacent, adapter),
                Is.False,
                $"{frameAdjacent.Name} calls Format, which allocates an array and a string per call. "
                    + "It runs near the frame; Get, a const format and TMP_Text.SetText are its tools.");
        }
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

    // ---- The port (M3-14a rule 5, M6-10 rule 4) --------------------------------------------------

    /// <summary>
    /// <c>Port_HasOneMember</c>, grown by exactly the member M6-10 named, and still no <c>params</c>
    /// overload of <c>Get</c>.
    /// </summary>
    [Test]
    public void Port_HasTwoMembers()
    {
        Type port = typeof(ILocalizer);

        MethodInfo[] methods = port.GetMethods().OrderBy(m => m.Name, StringComparer.Ordinal).ToArray();

        Assert.That(methods.Select(m => m.Name), Is.EqualTo(new[] { "Format", "Get" }), "ILocalizer grew or lost a member.");

        ParameterInfo[] format = methods[0].GetParameters();

        Assert.That(format.Select(p => p.ParameterType), Is.EqualTo(new[] { typeof(LocKey), typeof(object[]) }));
        Assert.That(format[1].IsDefined(typeof(ParamArrayAttribute), false), Is.True);

        ParameterInfo[] get = methods[1].GetParameters();

        Assert.That(get, Has.Length.EqualTo(1));
        Assert.That(get[0].ParameterType, Is.EqualTo(typeof(LocKey)));

        // **AR §6's row was corrected to match this a second time** (M6-10 rule 4). An *overload* of
        // Get carrying params is one accidental argument away from HudPresenter and AutoCastRow,
        // which run near the frame; a member with another name cannot be reached by accident.
        Assert.That(
            methods.Count(m => m.Name == nameof(ILocalizer.Get)),
            Is.EqualTo(1),
            "Get gained an overload. AR §6 says Get and Format — correct one or the other, and say which.");

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

        // And the adapter is the same object, because BootFlow resolves it to set the locale that
        // every screen then reads through the port (M6-10 rule 6).
        Assert.That(resolver.Resolve<TableLocalizer>(), Is.SameAs(localizer));

        Assert.That(((TableLocalizer)localizer).Count, Is.EqualTo(LoadShippedTable().RowCount));
    }

    /// <summary>
    /// Rule 6: the container is built before the profile answers, so it starts on the device's
    /// language — which reads English on every device V1 ships to, because no other real language
    /// has a table.
    /// </summary>
    [Test]
    public void Boot_StartsOnTheDeviceLocale()
    {
        IObjectResolver resolver = BuildBoot(LoadShippedTable(), LoadPseudoTable());

        var localizer = resolver.Resolve<TableLocalizer>();
        string device = BootInstaller.LocaleOf(Application.systemLanguage);

        // The device's tag if a table carries it, and empty — English — otherwise. No device reports
        // the pseudo-locale, so in V1 the second branch is the only one reachable.
        string expected = string.Equals(device, "qps-ploc", StringComparison.OrdinalIgnoreCase) ? device : string.Empty;

        Assert.That(localizer.Locale, Is.EqualTo(expected));

        // The table, spot-checked: German, both Chinese scripts, and a language Unity cannot name.
        Assert.That(BootInstaller.LocaleOf(SystemLanguage.German), Is.EqualTo("de"));
        Assert.That(BootInstaller.LocaleOf(SystemLanguage.ChineseSimplified), Is.EqualTo("zh-Hans"));
        Assert.That(BootInstaller.LocaleOf(SystemLanguage.ChineseTraditional), Is.EqualTo("zh-Hant"));
        Assert.That(BootInstaller.LocaleOf(SystemLanguage.Unknown), Is.Empty);

        // Every member Unity has answers something other than null, and none but Unknown is empty.
        foreach (SystemLanguage language in Enum.GetValues(typeof(SystemLanguage)))
        {
            string tag = BootInstaller.LocaleOf(language);

            Assert.That(tag, Is.Not.Null);
            Assert.That(tag.Length == 0, Is.EqualTo(language == SystemLanguage.Unknown), $"{language} maps to '{tag}'.");
        }
    }

    /// <summary>
    /// Rule 6 as a diff rather than a bug report: one type in the game calls <c>SetLocale</c>.
    /// </summary>
    [Test]
    public void Boot_OnlyBootFlowSetsTheLocale()
    {
        MethodInfo setLocale = typeof(TableLocalizer).GetMethod(nameof(TableLocalizer.SetLocale));

        string[] callers = typeof(Palette).Assembly.GetTypes()
            .Where(type => type.DeclaringType is null)
            .Where(type => LocalisationSweepTests.Calls(type, setLocale))
            .Select(type => type.Name)
            .ToArray();

        // Every label is written in Start (M3-14c rule 3), so a second caller is a screen in two
        // languages. M8-02's picker is the task that has to add one, with a redraw.
        Assert.That(callers, Is.EqualTo(new[] { nameof(BootFlow) }));
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

        // M6-10: the other languages beside it — the pseudo-locale, which is all V1 ships.
        SerializedProperty languages = new SerializedObject(scope).FindProperty("_languages");

        Assert.That(languages, Is.Not.Null, "BootScope has no _languages field.");
        Assert.That(languages.arraySize, Is.EqualTo(1));
        Assert.That(
            languages.GetArrayElementAtIndex(0).objectReferenceValue,
            Is.SameAs(LoadPseudoTable()),
            "BootScope.prefab does not carry Pseudo.asset, so profile.json's 'qps-ploc' reads English.");
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

        // **And the class-select screen, as of M5-07.** The sweep gains the prefab rather than the
        // scene: the screen is a prefab instance in Menu.unity, so its labels live in the asset and
        // a scene-only read would see none of them.
        IReadOnlyList<string> classSelect = AuthoredText(ClassSelectPrefabPath);

        Assert.That(
            classSelect,
            Is.Not.Empty,
            "the prefab has no TMP_Text at all, so this row tests nothing.");

        foreach (string authored in new[] { "Choose your class", "Back", "Oathbound", "Gravecaller" })
        {
            Assert.That(
                classSelect,
                Does.Not.Contain(authored),
                $"'{authored}' is still typed into ClassSelect.prefab.");
        }

        // And the ten have rows, so the screens that now ask for them get words rather than keys.
        TableLocalizer shipped = new TableLocalizer(LoadShippedTable());

        foreach (string key in new[]
        {
            "ui.app.title", "ui.menu.descend", "ui.menu.continue", "ui.death.title", "ui.death.hint",
            "ui.runend.depth", "ui.runend.bosses", "ui.runend.shards",
            "ui.classselect.title", "ui.classselect.back",
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
    /// <c>Port_HasTwoMembers</c> is what keeps them off it.
    /// </summary>
    private TableLocalizer Localizer(params string[] pairs) => new TableLocalizer(Table(pairs));

    /// <summary>
    /// A table authored through <see cref="SerializedObject"/>, which is the Inspector's own route.
    /// The fallback: its locale is empty.
    /// </summary>
    private LocalizationTable Table(params string[] pairs) => Localed(string.Empty, pairs);

    /// <summary><see cref="Table"/>, carrying <paramref name="locale"/>.</summary>
    private LocalizationTable Localed(string locale, params string[] pairs)
    {
        var table = ScriptableObject.CreateInstance<LocalizationTable>();
        _created.Add(table);

        table.name = string.IsNullOrEmpty(locale) ? "English" : locale;

        var serialized = new SerializedObject(table);
        SerializedProperty rows = serialized.FindProperty("_rows");

        serialized.FindProperty("_locale").stringValue = locale;

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

    private static LocalizationTable LoadPseudoTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(LocalisationSweepTests.PseudoPath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {LocalisationSweepTests.PseudoPath}.");

        return table;
    }

    private IObjectResolver BuildBoot(LocalizationTable table, params LocalizationTable[] languages)
    {
        var builder = new ContainerBuilder();

        BootInstaller.Install(
            builder,
            Array.Empty<CharacterDefinition>(),
            Array.Empty<EnemyDefinition>(),
            Array.Empty<ModeDefinition>(),
            Array.Empty<SkillDefinition>(),
            Array.Empty<SkillTreeDefinition>(),
            table,
            languages: languages);

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
