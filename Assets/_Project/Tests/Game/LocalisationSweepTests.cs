using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Game.Authoring;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game;

/// <summary>
/// M6-10's two sweeps: one over the code for a key nobody wrote a row for (rule 2), and a
/// pseudo-locale that makes a string nobody turned into a key visible on screen (rule 3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The per-screen rows stay, and this adds the check none of them can make.</b> Every task since
/// M3-14c ships a <c>Strings_EveryKeyThisScreenDrawsHasARow</c>. That proves the keys a screen
/// <em>has</em> resolve. It says nothing about a key some other file names, and nothing about an
/// English word typed into a view. <see cref="Sweep_EveryKeyInTheAssemblyHasAnEnglishRow"/> covers
/// the first. The pseudo-locale covers the second, and only on screen, which is why manual step 1
/// exists.
/// </para>
/// <para>
/// <b>The pseudo-locale is generated, never hand-written, and its generator lives here</b>, beside
/// the rows that fail when somebody forgets to run it. <see cref="Regenerate"/> is on the Editor's
/// menu as <c>Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale</c>, and
/// <see cref="Pseudo_IsTheGeneratorsOutput"/> fails on a hand edit as well as on a stale file.
/// </para>
/// <para>
/// <b>The debug overlay is skipped by name.</b> It is a developer readout drawn with
/// <c>OnGUI</c>, never shipped to a player's eye, and ADR-0012 is about user-facing strings. It
/// names no key today; the skip is there so the day it does is not a red row about the wrong thing.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LocalisationSweepTests
{
    /// <summary>The fallback: English, with an empty locale.</summary>
    public const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    /// <summary>The generated pseudo-locale.</summary>
    public const string PseudoPath = "Assets/_Project/Data/Localisation/Pseudo.asset";

    /// <summary>The industry's pseudo-locale tag, which Windows ships a culture for and Unity's Mono does not.</summary>
    public const string PseudoLocale = "qps-ploc";

    /// <summary>
    /// How much longer a pseudo row is than its English, in percent, before the brackets and the
    /// space. German and Finnish run 30–40 % longer than English (M6-10 rule 3).
    /// </summary>
    private const int ExpansionPercent = 35;

    /// <summary>What the padding is made of. In the default font's static atlas, which a row checks.</summary>
    private const char Pad = '·';

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    // ---- Rule 2: every key the code can name has a row --------------------------------------------

    /// <summary>
    /// The row ledger row 7 is about: no key anywhere in the code that nobody wrote a row for.
    /// </summary>
    /// <remarks>
    /// <b>Both assemblies, where the spec says one</b>, and it is strictly stronger. Every
    /// <c>ILocalizer</c> caller is in <c>Soulvail.Game</c>, but <c>SplashFlow</c> in
    /// <c>Soulvail.Core</c> names the three refusal keys that the splash screen draws. A key core
    /// names and a screen draws is still a key with no row if nobody wrote one.
    /// </remarks>
    [Test]
    public void Sweep_EveryKeyInTheAssemblyHasAnEnglishRow()
    {
        TableLocalizer english = English();
        List<NamedKey> named = KeysNamedBy(CodeTypes()).ToList();

        Assert.That(
            named.Count,
            Is.GreaterThan(50),
            "The sweep found almost nothing, so it is not reading the IL it thinks it is.");

        var problems = new List<string>();

        foreach (NamedKey key in named)
        {
            if (!english.Has(key.Key))
            {
                problems.Add(
                    $"{key.Where} names '{key.Key}' and {EnglishPath} has no row for it, so it draws "
                        + "as itself.");
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Keys the code names, resolvable in English");
    }

    /// <summary>
    /// Rule 2's own diagnosis, asserted — <c>Palette_TheSweepWouldHaveCaughtM4_06</c>'s shape: the
    /// sweep is shown finding a key that was deliberately never written, by both routes a key reaches
    /// the IL.
    /// </summary>
    [Test]
    public void Sweep_TheSweepWouldHaveCaughtAnUnwrittenKey()
    {
        TableLocalizer english = English();
        List<NamedKey> named = KeysNamedBy(new[] { typeof(UnwrittenKeys) }).ToList();

        foreach (string unwritten in new[] { UnwrittenKeys.FieldKey, UnwrittenKeys.InlineKey })
        {
            NamedKey found = named.FirstOrDefault(k => k.Key.Key == unwritten);

            Assert.That(found.Key.Key, Is.EqualTo(unwritten), $"The sweep did not see '{unwritten}'.");
            Assert.That(english.Has(found.Key), Is.False, "The fixture's premise: nobody wrote it.");
        }

        // And a key built at runtime is not a literal, so the sweep does not pretend to see it —
        // which is why the asset keys and TriggerText's keys are walked separately below.
        Assert.That(named.Any(k => k.Key.Key.StartsWith("fixture.built.", StringComparison.Ordinal)), Is.False);
    }

    /// <summary>
    /// The code's keys and the assets' keys are different sets, and between them — with
    /// <see cref="TriggerText"/>'s twenty — they account for every row English carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Neither is a subset of the other, which is what stops either row being deleted as a
    /// duplicate.</b> <c>ContentValidationTests.EveryLocKey_ResolvesInEnglish</c> sees what an asset
    /// authors (node names, class names, branch headings) and cannot see code. This sweep sees code and
    /// cannot see an asset's string field.
    /// </para>
    /// <para>
    /// <b>The other half catches a dead row.</b> A row no code, asset or trigger names is a sentence
    /// a translator would be paid for and no player would read. <c>ui.offer.rot</c> was the first:
    /// M6-10 folded it into <c>ui.offer.pact</c>, and this is the check that makes sure it went.
    /// </para>
    /// </remarks>
    [Test]
    public void Sweep_EveryAuthoredAssetKeyIsAlreadyCovered()
    {
        var code = new HashSet<LocKey>(KeysNamedBy(CodeTypes()).Select(k => k.Key));
        var assets = new HashSet<LocKey>(ContentValidationTests.EveryAuthoredKey().Select(k => k.Key));

        Assert.That(code.Except(assets), Is.Not.Empty, "every key the code names is also authored on an asset.");
        Assert.That(assets.Except(code), Is.Not.Empty, "every key an asset authors is also named in code.");

        var covered = new HashSet<LocKey>(code);
        covered.UnionWith(assets);
        covered.UnionWith(TriggerKeys());

        var orphans = new List<string>();

        foreach (LocKey key in EnglishTable().ToDictionary().Keys)
        {
            if (!covered.Contains(key))
            {
                orphans.Add(
                    $"{EnglishPath}: '{key}' is named by no code, no asset and no trigger. Delete the row, "
                        + "or name the key where it is drawn.");
            }
        }

        ContentValidationTests.AssertNoProblems(orphans, "Rows somebody reads");
    }

    /// <summary>
    /// Rule 5's sorting, kept honest: the types that still format with the invariant culture, named.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The spec counted 31 <c>InvariantCulture</c> references in <c>Soulvail.Game</c> and said the
    /// sweep decides which are sentences and which are not. These are the ones left after M6-10,
    /// and each has a reason:
    /// </para>
    /// <list type="bullet">
    /// <item><c>LocalJsonSaveStore</c> writes a file, and a file is not a player.</item>
    /// <item><c>LocalizationTable</c> <em>is</em> where a culture comes from, and the invariant
    /// culture is its answer for English and for a tag this runtime has no culture for.</item>
    /// <item><c>ServiceRow</c> writes a price, a whole number with nothing around it.</item>
    /// <item><c>SkillRow</c> writes a trigger threshold beside its phrase, deliberately. Its remarks
    /// carry the ruling and its one cost, a seconds threshold's decimal separator.</item>
    /// </list>
    /// <para>
    /// A fifth type is a diff to this list rather than a quiet regression. A type that stops using it
    /// is a diff too: the list is exact rather than a ceiling, so it cannot rot into a list of names
    /// nothing matches.
    /// </para>
    /// </remarks>
    [Test]
    public void Sweep_InvariantCultureStaysOutOfSentences()
    {
        MethodInfo invariant = typeof(CultureInfo).GetProperty(nameof(CultureInfo.InvariantCulture)).GetMethod;

        string[] callers = typeof(Palette).Assembly.GetTypes()
            .Where(type => type.DeclaringType is null && !IsDebugOnly(type))
            .Where(type => Calls(type, invariant))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            callers,
            Is.EqualTo(new[] { "LocalJsonSaveStore", "LocalizationTable", "ServiceRow", "SkillRow" }),
            "A type started or stopped formatting with the invariant culture. A number inside a sentence "
                + "a player reads goes through ILocalizer.Format (M6-10 rule 5); see this row's remarks.");
    }

    // ---- Rule 3: the pseudo-locale ----------------------------------------------------------------

    [Test]
    public void Pseudo_HasARowForEveryEnglishRow()
    {
        IReadOnlyDictionary<LocKey, string> english = EnglishTable().ToDictionary();
        LocalizationTable pseudoTable = PseudoTable();
        IReadOnlyDictionary<LocKey, string> pseudo = pseudoTable.ToDictionary();

        string regenerate = "Regenerate it: Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale.";

        Assert.That(
            english.Keys.Except(pseudo.Keys).Select(k => k.Key),
            Is.Empty,
            $"English has rows the pseudo-locale lacks. {regenerate}");

        Assert.That(
            pseudo.Keys.Except(english.Keys).Select(k => k.Key),
            Is.Empty,
            $"The pseudo-locale has rows English no longer carries. {regenerate}");

        Assert.That(pseudoTable.RowCount, Is.EqualTo(EnglishTable().RowCount));
    }

    [Test]
    public void Pseudo_IsLongerThanEnglish()
    {
        IReadOnlyDictionary<LocKey, string> pseudo = PseudoTable().ToDictionary();
        var problems = new List<string>();

        foreach (KeyValuePair<LocKey, string> row in EnglishTable().ToDictionary())
        {
            if (pseudo.TryGetValue(row.Key, out string text) && text.Length < row.Value.Length * 1.3f)
            {
                problems.Add($"'{row.Key}': {text.Length} characters against English's {row.Value.Length}.");
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Pseudo rows at least 30 % longer than English");
    }

    /// <summary>
    /// The raw-string detector's premise: a word drawn in plain English under the pseudo-locale can
    /// only have come from somewhere other than the table.
    /// </summary>
    [Test]
    public void Pseudo_IsNotEnglish()
    {
        IReadOnlyDictionary<LocKey, string> pseudo = PseudoTable().ToDictionary();
        var problems = new List<string>();

        foreach (KeyValuePair<LocKey, string> row in EnglishTable().ToDictionary())
        {
            if (pseudo.TryGetValue(row.Key, out string text) && string.Equals(text, row.Value, StringComparison.Ordinal))
            {
                problems.Add($"'{row.Key}' reads the same in both tables: '{text}'.");
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Pseudo rows that differ from English");
    }

    /// <summary>Rule 3's <em>"generated, never hand-written"</em>, asserted rather than trusted.</summary>
    [Test]
    public void Pseudo_IsTheGeneratorsOutput()
    {
        LocalizationTable pseudoTable = PseudoTable();
        IReadOnlyDictionary<LocKey, string> pseudo = pseudoTable.ToDictionary();
        var problems = new List<string>();

        Assert.That(pseudoTable.Locale, Is.EqualTo(PseudoLocale));

        foreach (KeyValuePair<LocKey, string> row in EnglishTable().ToDictionary())
        {
            string expected = Pseudolocalise(row.Value);

            if (pseudo.TryGetValue(row.Key, out string text) && !string.Equals(text, expected, StringComparison.Ordinal))
            {
                problems.Add($"'{row.Key}' is '{text}', and the generator writes '{expected}'.");
            }
        }

        ContentValidationTests.AssertNoProblems(problems, "Pseudo rows as the generator writes them");
    }

    /// <summary>
    /// Every placeholder and every rich-text tag survives, so <c>ILocalizer.Format</c> substitutes
    /// under the pseudo-locale rather than falling to its unsubstituted row.
    /// </summary>
    [Test]
    public void Pseudo_KeepsEveryPlaceholderAndTag()
    {
        IReadOnlyDictionary<LocKey, string> pseudo = PseudoTable().ToDictionary();
        var problems = new List<string>();
        var withPlaceholders = 0;

        foreach (KeyValuePair<LocKey, string> row in EnglishTable().ToDictionary())
        {
            string[] expected = Spans(row.Value).ToArray();

            withPlaceholders += expected.Length > 0 ? 1 : 0;

            if (pseudo.TryGetValue(row.Key, out string text) && !Spans(text).SequenceEqual(expected))
            {
                problems.Add($"'{row.Key}' lost or changed a placeholder: '{row.Value}' became '{text}'.");
            }
        }

        Assert.That(withPlaceholders, Is.GreaterThan(5), "The sanity half: English carries placeholders to keep.");

        ContentValidationTests.AssertNoProblems(problems, "Placeholders kept");
    }

    /// <summary>
    /// The spec's own example, and every character the generator <em>adds</em> is in the default
    /// font's static atlas — so the Editor walk reads accents rather than missing-glyph boxes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Latin-1's accents only, and that is why: <c>LiberationSans SDF</c>'s static atlas carries
    /// <c>á é î ô û ñ ç ý</c> and their capitals and nothing from Latin Extended, whose glyphs would
    /// arrive through a dynamic fallback, on a device, if at all. A pseudo-locale drawn as boxes
    /// would make manual step 1 unreadable, and a box is not the defect the walk is looking for.
    /// </para>
    /// <para>
    /// <b>What the generator adds, not what English already had.</b> The first draft checked every
    /// character and went red on <c>→</c>, which is English's own first-active hint
    /// (<em>"Pause → Skills"</em>) and outside the static atlas too. Whatever draws it in English
    /// draws it in the pseudo-locale, so it is not this row's question.
    /// </para>
    /// </remarks>
    [Test]
    public void Pseudo_TheFontDrawsEveryCharacterItAdds()
    {
        Assert.That(Pseudolocalise("Continue"), Is.EqualTo("[Çôñtîñûé ···]"), "M6-10 rule 3's example.");

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;

        Assert.That(font, Is.Not.Null, "TMP has no default font asset.");

        IReadOnlyDictionary<LocKey, string> pseudo = PseudoTable().ToDictionary();
        var missing = new SortedSet<char>();

        foreach (KeyValuePair<LocKey, string> row in EnglishTable().ToDictionary())
        {
            if (!pseudo.TryGetValue(row.Key, out string text))
            {
                continue;
            }

            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c) && row.Value.IndexOf(c) < 0 && !font.HasCharacter(c, false, false))
                {
                    missing.Add(c);
                }
            }
        }

        Assert.That(missing, Is.Empty, $"{font.name}'s static atlas has no glyph for: {string.Concat(missing)}");
    }

    // ---- The generator ------------------------------------------------------------------------------

    /// <summary>
    /// Rewrites <c>Pseudo.asset</c> from <c>English.asset</c>, creating it if it does not exist.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>One command, by the route every data asset in this project is made</b>: an instance
    /// through <c>CreateInstance</c> and every field through <see cref="SerializedObject"/>, M3-12c's
    /// recipe. No YAML is typed and no script GUID is computed. The file keeps its GUID across a
    /// regeneration, so <c>BootScope.prefab</c>'s reference to it survives.
    /// </para>
    /// <para>
    /// <b>On the Editor's menu, from a test assembly</b>, because this file is where the transform's
    /// rows live. A generator in <c>Soulvail.Editor</c> would be a second copy of
    /// <see cref="Pseudolocalise"/>, or a reference from the tests to an editor assembly they do
    /// not otherwise need.
    /// </para>
    /// </remarks>
    /// <returns>How many rows were written.</returns>
    public static int Regenerate()
    {
        LocalizationTable english = EnglishTable();
        var pseudo = AssetDatabase.LoadAssetAtPath<LocalizationTable>(PseudoPath);

        if (pseudo == null)
        {
            pseudo = ScriptableObject.CreateInstance<LocalizationTable>();
            AssetDatabase.CreateAsset(pseudo, PseudoPath);
        }

        // Read through SerializedObject rather than ToDictionary, so English's own order is kept
        // and the pseudo file diffs line for line against it.
        SerializedProperty source = new SerializedObject(english).FindProperty("_rows");

        var serialized = new SerializedObject(pseudo);
        SerializedProperty rows = serialized.FindProperty("_rows");

        serialized.FindProperty("_locale").stringValue = PseudoLocale;
        rows.arraySize = source.arraySize;

        for (int i = 0; i < source.arraySize; i++)
        {
            SerializedProperty from = source.GetArrayElementAtIndex(i);
            SerializedProperty to = rows.GetArrayElementAtIndex(i);

            to.FindPropertyRelative("_key").stringValue = from.FindPropertyRelative("_key").stringValue;
            to.FindPropertyRelative("_text").stringValue =
                Pseudolocalise(from.FindPropertyRelative("_text").stringValue ?? string.Empty);
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(pseudo);
        AssetDatabase.SaveAssets();

        return rows.arraySize;
    }

    [MenuItem("Soulvail/Localisation/Regenerate Pseudo-locale")]
    private static void RegenerateFromMenu()
    {
        Debug.Log($"{PseudoPath}: {Regenerate()} rows written from {EnglishPath}.");
    }

    /// <summary>
    /// One English row, pseudo-localised: every letter Latin-1 can accent is accented, placeholders
    /// and tags are kept whole, and the whole is bracketed and padded by about 35 %.
    /// </summary>
    /// <remarks>
    /// <c>"Continue"</c> becomes <c>"[Çôñtîñûé ···]"</c>: eight characters, three of padding, and
    /// the brackets. The brackets are what makes a clipped label visible — a label missing its
    /// <c>]</c> was cut off — and the padding is what measures whether it would fit in German.
    /// </remarks>
    public static string Pseudolocalise(string english)
    {
        if (english is null)
        {
            throw new ArgumentNullException(nameof(english));
        }

        var builder = new StringBuilder((english.Length * 2) + 4);

        builder.Append('[');

        for (int i = 0; i < english.Length; i++)
        {
            char c = english[i];

            // An escaped brace is a literal, and is not the start of a placeholder.
            if ((c == '{' || c == '}') && i + 1 < english.Length && english[i + 1] == c)
            {
                builder.Append(c).Append(c);
                i++;

                continue;
            }

            // A placeholder or a rich-text tag is copied whole: {0:0.0} is a format, <b> is markup,
            // and accenting either would break what reads it.
            if (c == '{' || c == '<')
            {
                int end = english.IndexOf(c == '{' ? '}' : '>', i);

                if (end > i)
                {
                    builder.Append(english, i, end - i + 1);
                    i = end;

                    continue;
                }
            }

            builder.Append(Accent(c));
        }

        // A ceiling in whole numbers, so a length whose 35 % is exact is not rounded up by a float.
        int pad = Math.Max(1, ((english.Length * ExpansionPercent) + 99) / 100);

        return builder.Append(' ').Append(Pad, pad).Append(']').ToString();
    }

    private static char Accent(char c) => c switch
    {
        'a' => 'á',
        'c' => 'ç',
        'e' => 'é',
        'i' => 'î',
        'n' => 'ñ',
        'o' => 'ô',
        'u' => 'û',
        'y' => 'ý',
        'A' => 'Á',
        'C' => 'Ç',
        'E' => 'É',
        'I' => 'Î',
        'N' => 'Ñ',
        'O' => 'Ô',
        'U' => 'Û',
        'Y' => 'Ý',
        _ => c,
    };

    // ---- The IL sweep, shared with TableLocalizerTests -------------------------------------------

    /// <summary>A key, and the method that names it.</summary>
    internal readonly struct NamedKey
    {
        internal NamedKey(string where, LocKey key)
        {
            Where = where;
            Key = key;
        }

        internal string Where { get; }

        internal LocKey Key { get; }
    }

    /// <summary>
    /// Every type in <c>Soulvail.Game</c> and <c>Soulvail.Core</c> that may name a player-facing
    /// key, the debug overlay excepted — see the class remarks.
    /// </summary>
    internal static IEnumerable<Type> CodeTypes() =>
        typeof(Palette).Assembly.GetTypes()
            .Concat(typeof(LocKey).Assembly.GetTypes())
            .Where(type => !IsDebugOnly(type));

    /// <summary>
    /// Every <see cref="LocKey"/> <paramref name="types"/> name, by both routes a key reaches the
    /// code: a static <see cref="LocKey"/> field, and a <c>new LocKey("…")</c> in a method body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The IL route is <c>ldstr</c> immediately followed by a call to <see cref="LocKey"/>'s
    /// constructor</b> — <c>newobj</c> for a value that is stored or passed, <c>call</c> for one
    /// built in place in a local. <c>PaletteTests.Reads</c>' technique, aimed at a string. A
    /// <c>const</c> key id is inlined as the same <c>ldstr</c>, so <c>SplashFlow</c>'s three
    /// refusals are found. A key built at runtime is not, and is not meant to be: an asset's key is
    /// <c>ContentValidationTests</c>', and <see cref="TriggerText"/>'s twenty are walked by name.
    /// </para>
    /// <para>
    /// A four-byte window that happens to follow an opcode byte inside somebody else's operand is
    /// skipped when its token resolves to nothing, rather than believed.
    /// </para>
    /// </remarks>
    internal static IEnumerable<NamedKey> KeysNamedBy(IEnumerable<Type> types)
    {
        ConstructorInfo ctor = typeof(LocKey).GetConstructor(new[] { typeof(string) });

        foreach (Type type in types)
        {
            if (type.ContainsGenericParameters)
            {
                continue;
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(LocKey) && !field.IsLiteral && field.GetValue(null) is LocKey key && key.Key is not null)
                {
                    yield return new NamedKey($"{type.FullName}.{field.Name}", key);
                }
            }

            foreach (MethodBase method in BodiesOf(type))
            {
                byte[] il = Il(method);

                if (il is null)
                {
                    continue;
                }

                for (int i = 0; i + 9 < il.Length; i++)
                {
                    // ldstr (0x72), then newobj (0x73) or call (0x28) on the very next instruction.
                    if (il[i] != 0x72 || (il[i + 5] != 0x73 && il[i + 5] != 0x28))
                    {
                        continue;
                    }

                    string literal;

                    try
                    {
                        literal = method.Module.ResolveString(BitConverter.ToInt32(il, i + 1));

                        if (method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 6)) != ctor)
                        {
                            continue;
                        }
                    }
                    catch (Exception)
                    {
                        // Not a token; the window fell inside somebody else's operand.
                        continue;
                    }

                    yield return new NamedKey($"{type.FullName}.{method.Name}", new LocKey(literal));
                }
            }
        }
    }

    /// <summary>
    /// Whether <paramref name="type"/>, or anything nested in it, calls <paramref name="target"/> —
    /// <c>ClassVeilrotTests.Calls</c>' technique, over a whole type.
    /// </summary>
    internal static bool Calls(Type type, MethodInfo target)
    {
        var types = new List<Type> { type };

        types.AddRange(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        return types.Any(candidate => BodiesOf(candidate).Any(method => Calls(method, target)));
    }

    private static bool Calls(MethodBase method, MethodInfo target)
    {
        byte[] il = Il(method);

        if (il is null)
        {
            return false;
        }

        Type[] typeArgs = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            // call (0x28) and callvirt (0x6F).
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), typeArgs, methodArgs) == target)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Not a token; the window fell inside somebody else's operand.
            }
        }

        return false;
    }

    private static IEnumerable<MethodBase> BodiesOf(Type type) =>
        type.GetMethods(Everything).Cast<MethodBase>().Concat(type.GetConstructors(Everything));

    private static byte[] Il(MethodBase method)
    {
        try
        {
            return method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The debug overlay and anything nested in it.</summary>
    private static bool IsDebugOnly(Type type)
    {
        for (Type t = type; t is not null; t = t.DeclaringType)
        {
            if (t == typeof(DebugOverlay))
            {
                return true;
            }
        }

        return false;
    }

    // ---- Fixture ------------------------------------------------------------------------------------

    /// <summary>Every trigger key, the way <c>ContentValidationTests.EveryTriggerKey_ResolvesInEnglish</c> walks them.</summary>
    private static IEnumerable<LocKey> TriggerKeys()
    {
        foreach (TriggerField field in Enum.GetValues(typeof(TriggerField)))
        {
            foreach (TriggerComparison comparison in Enum.GetValues(typeof(TriggerComparison)))
            {
                yield return TriggerText.KeyFor(field, comparison);
            }
        }
    }

    private static LocalizationTable EnglishTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No LocalizationTable at {EnglishPath}.");

        return table;
    }

    private static LocalizationTable PseudoTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(PseudoPath);

        Assert.That(
            table,
            Is.Not.Null,
            $"No LocalizationTable at {PseudoPath}. Soulvail ▸ Localisation ▸ Regenerate Pseudo-locale.");

        return table;
    }

    private static TableLocalizer English() => new TableLocalizer(EnglishTable());

    /// <summary>Every <c>{…}</c> placeholder and <c>&lt;…&gt;</c> tag, in order.</summary>
    private static IEnumerable<string> Spans(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{' && text[i] != '<')
            {
                continue;
            }

            int end = text.IndexOf(text[i] == '{' ? '}' : '>', i);

            if (end > i)
            {
                yield return text.Substring(i, end - i + 1);
                i = end;
            }
        }
    }

    /// <summary>Two keys nobody wrote a row for, one by each route — the sweep's own control.</summary>
    private static class UnwrittenKeys
    {
        internal const string FieldKey = "fixture.never-written.field";
        internal const string InlineKey = "fixture.never-written.inline";

        private static readonly LocKey Field = new LocKey(FieldKey);

        internal static string Draw(ILocalizer localizer, string suffix) =>
            localizer.Get(new LocKey(InlineKey)) + localizer.Get(Field) + localizer.Get(new LocKey("fixture.built." + suffix));
    }
}
