using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The four screens that author a <see cref="LocKey"/> as literal label text: every one of those
/// keys is <em>claimed</em> by a serialized <see cref="TMP_Text"/> field on the presenter that owns
/// the prefab, so something resolves it before a player sees it (M3-14c rules 6, 7).
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the assertion this project can make, and <em>"no prefab draws a raw key"</em> is not
/// it.</b> That stronger claim is false by design: <c>SkillsPresenter.EmptyKey</c> and
/// <c>TreeViewPresenter.NoTreeKey</c> have been wired since M3-09b and M3-09d and <em>still</em>
/// carry their key as the prefab's authored text, deliberately — a placeholder that reads as a key
/// makes an undressed field visible in the Editor, where a blank one is indistinguishable from a
/// label nobody authored. So the checkable form is not "no key is drawn" but "every key that is
/// drawn is pointed at", which is exactly what wiring means.
/// </para>
/// <para>
/// <b>It is also the read-back M1-07 says nine hand-dressed references need.</b>
/// <c>SerializedProperty.objectReferenceValue</c> is in Traps §1's family: the assignment reports
/// success and stores <c>None</c> when the handle went fake-null, and the symptom here is a label
/// that silently keeps drawing its key — which is the exact bug M3-14c exists to remove. A row that
/// only checked the <em>code</em> would pass over nine undressed fields.
/// </para>
/// <para>
/// <b>Why it is a static walk rather than four screens driven to life.</b> Instantiating and
/// injecting all four presenters would duplicate four fixtures' worth of setup in one file, and the
/// question being asked — is the reference dressed? — is answered by the asset alone. What each
/// screen actually <em>draws</em> is pinned per screen, against the shipped <c>English.asset</c>, in
/// <c>PausePresenterTests</c>, <c>SkillsPresenterTests</c>, <c>TreeViewPresenterTests</c> and
/// <c>LevelUpPresenterTests</c>. The two halves are deliberately different questions.
/// </para>
/// <para>
/// <b>It lives beside M3-14b's two sweeps</b> because it is the same family — an
/// <c>AssetDatabase</c> walk at author time, asking a question no constructor can be handed — and
/// for M3-14b rule 12's reason: it needs <c>UnityEditor</c>, so it cannot be
/// <c>Soulvail.Tests.Core</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class StaticLabelWiringTests
{
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    /// <summary>
    /// The four prefabs that author a key as label text, and the presenter that owns each.
    /// </summary>
    /// <remarks>
    /// <b>Named rather than swept from <c>Prefabs/UI/</c>, and the difference is the point.</b> A
    /// walk of the folder would go green the day somebody adds a fifth screen with an unwired key,
    /// because the walk would find no presenter field to look for and conclude nothing was wrong.
    /// Naming the four makes a fifth screen a decision somebody has to make here.
    /// </remarks>
    private static readonly (string Prefab, string Presenter)[] Screens =
    {
        ("Assets/_Project/Prefabs/UI/Pause.prefab", "PausePresenter"),
        ("Assets/_Project/Prefabs/UI/Skills.prefab", "SkillsPresenter"),
        ("Assets/_Project/Prefabs/UI/TreeView.prefab", "TreeViewPresenter"),
        ("Assets/_Project/Prefabs/UI/LevelUp.prefab", "LevelUpPresenter"),

        // **The fifth, and it is the decision this array's remarks ask for** (M5-07a-ii).
        // CH §5.4's screen authors two of its four keys as placeholder text, and its whole argument
        // is that there is no way off it — so a key drawn raw there is a key a player is stuck
        // looking at. `ClassSelect.prefab` is deliberately still absent: it lives in the Menu scene
        // and M5-07 pinned its two keys in its own fixture, so this array is the four *run* screens
        // plus this one rather than a complete list of prefabs that author a key.
        ("Assets/_Project/Prefabs/UI/Splash.prefab", "SplashPresenter"),
    };

    [Test]
    public void EveryAuthoredKey_IsClaimedByAField()
    {
        var problems = new List<string>();

        foreach ((string prefabPath, string presenterName) in Screens)
        {
            GameObject root = LoadPrefab(prefabPath);
            Component presenter = FindPresenter(root, presenterName, prefabPath);

            HashSet<TMP_Text> claimed = ClaimedLabels(presenter);

            foreach (TMP_Text label in KeyCarryingLabels(root))
            {
                if (!claimed.Contains(label))
                {
                    problems.Add(
                        $"{prefabPath}: the label '{Path(label.transform, root.transform)}' draws " +
                        $"'{label.text}', which is a key {EnglishPath} answers, and no " +
                        $"{presenterName} field points at it — so nothing resolves it and the " +
                        "player reads the key.");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// The anti-vacuity guard, and it is named rather than assumed (M3-14c rule 7).
    /// </summary>
    /// <remarks>
    /// Every assertion in <see cref="EveryAuthoredKey_IsClaimedByAField"/> has the form <em>"nothing
    /// I found is unclaimed"</em>, which is trivially true of an empty set: a walk that stopped
    /// finding labels — a renamed prefab, a `TMP_Text` swapped for something else, a table that
    /// stopped loading — would turn the whole of M3-14c green and silent. M3-14b's
    /// <c>EverySweep_FindsTheAssetsItIsMeantTo</c>, and the same reason.
    /// </remarks>
    [Test]
    public void TheSweep_FindsAKeyOnEveryScreen()
    {
        var problems = new List<string>();

        foreach ((string prefabPath, string _) in Screens)
        {
            int found = KeyCarryingLabels(LoadPrefab(prefabPath)).Count;

            if (found == 0)
            {
                problems.Add(
                    $"{prefabPath}: the sweep found no label drawing a key {EnglishPath} answers. " +
                    "Either this screen stopped authoring its keys as placeholder text — in which " +
                    "case this array and M3-14c's argument both need revisiting — or the walk is " +
                    "not walking, and every row in this file is passing over nothing.");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("\n", problems));
    }

    /// <summary>
    /// Every <see cref="TMP_Text"/> under <paramref name="root"/> whose authored text is a key the
    /// shipped table answers.
    /// </summary>
    /// <remarks>
    /// <b><c>Has</c> rather than a <c>ui.</c> prefix match</b>, deliberately: the placeholders that
    /// are <em>not</em> keys — <c>skill.name</c>, <c>trigger.key</c>, <c>branch</c>,
    /// <c>skill.desc</c>, <c>0.0 s</c>, and the pause icon's <c>II</c> glyph — are pooled-template
    /// and art placeholders that no table will ever answer, and a prefix match would drag the first
    /// four of them in and make this fixture a ruling about templates rather than a check on
    /// wiring. The table is the authority on what counts as a key, which is also what makes this
    /// row notice a tenth one on the day it is authored.
    /// <para>
    /// <c>includeInactive: true</c>, because four of the nine sit under objects that are switched
    /// off — the Skills prompt and the two View Tree buttons — and those are precisely the ones
    /// nobody would notice by looking.
    /// </para>
    /// </remarks>
    private static List<TMP_Text> KeyCarryingLabels(GameObject root)
    {
        TableLocalizer table = Shipped();

        return root
            .GetComponentsInChildren<TMP_Text>(includeInactive: true)
            .Where(label => IsKeyTheTableAnswers(table, label.text))
            .ToList();
    }

    /// <summary>
    /// Whether <paramref name="text"/> is a <see cref="LocKey"/> at all, and one the table answers.
    /// </summary>
    /// <remarks>
    /// <b>The two questions are asked in that order, and the first one has to be asked by
    /// <see cref="LocKey"/> rather than by this file.</b> Its constructor refuses empty and
    /// whitespace-bearing strings — <c>ArgumentException</c>, not a bool — and the prefabs are full
    /// of authored text that trips it: <c>0.0 s</c> on every cooldown readout, and the pause icon's
    /// <c>II</c> glyph is only legal by luck. Reimplementing the grammar here would be a second copy
    /// of a rule that already exists, which is M3-14b rule 11's drift; catching is what asks the
    /// real one. Text that cannot be a key is not one, which is the answer this fixture wants
    /// anyway.
    /// </remarks>
    private static bool IsKeyTheTableAnswers(TableLocalizer table, string text)
    {
        LocKey key;

        try
        {
            key = new LocKey(text);
        }
        catch (System.ArgumentException)
        {
            return false;
        }

        return table.Has(key);
    }

    /// <summary>
    /// Every <see cref="TMP_Text"/> a serialized field on <paramref name="presenter"/> points at.
    /// </summary>
    /// <remarks>
    /// Through <c>SerializedObject</c> rather than reflection over the C# fields, because the
    /// question is about the <em>asset</em>: a field that exists in code and was never dressed is
    /// exactly the failure this fixture is for, and only the serialized copy knows the difference.
    /// Arrays are walked too — no presenter dresses its static labels as one today, and a later one
    /// that does should not silently fall out of the sweep.
    /// </remarks>
    private static HashSet<TMP_Text> ClaimedLabels(Component presenter)
    {
        var claimed = new HashSet<TMP_Text>();
        var serialized = new SerializedObject(presenter);

        SerializedProperty property = serialized.GetIterator();

        while (property.NextVisible(enterChildren: true))
        {
            if (property.propertyType == SerializedPropertyType.ObjectReference &&
                property.objectReferenceValue is TMP_Text label)
            {
                claimed.Add(label);
            }
        }

        return claimed;
    }

    private static GameObject LoadPrefab(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {path}.");

        return prefab;
    }

    /// <remarks>
    /// By name rather than by type so this file needs no reference to the presenters' own types,
    /// and — more usefully — so a component that failed to deserialise says so here. A
    /// <c>MonoBehaviour</c> in a file-scoped namespace loads as null with nothing reported anywhere
    /// (Traps §5), and a sweep that silently found no presenter would pass.
    /// </remarks>
    private static Component FindPresenter(GameObject root, string typeName, string path)
    {
        Component presenter = root
            .GetComponents<Component>()
            .FirstOrDefault(c => c != null && c.GetType().Name == typeName);

        Assert.That(
            presenter,
            Is.Not.Null,
            $"{path}: no {typeName} on the prefab root. If the component exists in code, it did " +
            "not deserialise onto the asset (Traps §5).");

        return presenter;
    }

    /// <summary>
    /// The real adapter over the shipped table — the authority on what counts as a key.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="TableLocalizer"/> rather than as <c>ILocalizer</c> because
    /// <c>Has</c> is deliberately <em>not</em> on the port (M3-14a rule 5: one member, and a screen
    /// asking whether a key exists would be a screen deciding what to draw from the table's
    /// contents). This fixture is one of the two callers that rule was written to allow.
    /// </remarks>
    private static TableLocalizer Shipped()
    {
        var table = AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath);

        Assert.That(table, Is.Not.Null, $"No localization table at {EnglishPath}.");

        return new TableLocalizer(table);
    }

    /// <summary>A label's path under its screen, so a red row says <em>which</em> label.</summary>
    private static string Path(Transform label, Transform root)
    {
        var parts = new List<string>();

        for (Transform current = label; current != null && current != root; current = current.parent)
        {
            parts.Add(current.name);
        }

        parts.Reverse();

        return $"{root.name}/{string.Join("/", parts)}";
    }
}
