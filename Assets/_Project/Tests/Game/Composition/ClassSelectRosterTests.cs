using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Composition;

/// <summary>
/// RS-05c: which classes class select shows — <c>BootScope</c>'s <i>Class Select</i> list — and the
/// Run scene's fallback class, which follows it.
/// </summary>
[TestFixture]
public sealed class ClassSelectRosterTests
{
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string JunglePath = "Assets/_Project/Data/Modes/Jungle.asset";

    private static readonly ContentId Oathbound = new ContentId("character.oathbound");
    private static readonly ContentId Gravecaller = new ContentId("character.gravecaller");
    private static readonly ContentId Ranger = new ContentId("character.ranger");

    // ---- Rule 1: the roster ------------------------------------------------------------------------

    [Test]
    public void Roster_EmptyShowsEveryClassInTheCatalogsOrder()
    {
        var roster = new ClassSelectRoster(Array.Empty<ContentId>());

        IReadOnlyList<CharacterSpec> shown = roster.ShownFrom(Catalog());

        Assert.That(roster.ShowsEveryClass, Is.True);
        Assert.That(Ids(shown), Is.EqualTo(new[] { Oathbound, Gravecaller, Ranger }));
    }

    [Test]
    public void Roster_ShowsItsClassesInItsOwnOrder()
    {
        var roster = new ClassSelectRoster(new[] { Ranger, Oathbound });

        Assert.That(roster.ShowsEveryClass, Is.False);
        Assert.That(Ids(roster.ShownFrom(Catalog())), Is.EqualTo(new[] { Ranger, Oathbound }));
    }

    [Test]
    public void Roster_SkipsAClassTheCatalogDoesNotHold()
    {
        var roster = new ClassSelectRoster(new[] { new ContentId("character.emberwright"), Ranger });

        Assert.That(Ids(roster.ShownFrom(Catalog())), Is.EqualTo(new[] { Ranger }));
    }

    [Test]
    public void Roster_RefusesAnEmptySlotOrAClassTwice()
    {
        Assert.Throws<ArgumentNullException>(() => new ClassSelectRoster(null));
        Assert.Throws<ArgumentException>(() => new ClassSelectRoster(new[] { Ranger, default }));
        Assert.Throws<ArgumentException>(() => new ClassSelectRoster(new[] { Ranger, Ranger }));
        Assert.Throws<ArgumentNullException>(() => new ClassSelectRoster(new[] { Ranger }).ShownFrom(null));
    }

    // ---- Rule 3: the shipped list ------------------------------------------------------------------

    [Test]
    public void Boot_ClassSelectShowsTheRangerAlone()
    {
        // The owner's ruling of 2026-09-28. The other three stay in Characters: hidden, not removed.
        var boot = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath).GetComponentInChildren<BootScope>(true);
        var serialized = new SerializedObject(boot);

        SerializedProperty shown = serialized.FindProperty("_classSelect");

        Assert.That(shown, Is.Not.Null, "BootScope has no Class Select list.");
        Assert.That(shown.arraySize, Is.EqualTo(1));
        Assert.That(shown.GetArrayElementAtIndex(0).objectReferenceValue, Is.EqualTo(Load<CharacterDefinition>(RangerPath)));

        SerializedProperty characters = serialized.FindProperty("_characters");

        Assert.That(characters.arraySize, Is.EqualTo(4), "a hidden class was taken out of the catalog.");
    }

    // ---- Rule 4: the Run scene's fallback ----------------------------------------------------------

    [Test]
    public void RunCharacter_FallsBackToTheFirstClassShown()
    {
        var roster = new ClassSelectRoster(new[] { Ranger });

        Assert.That(RunCharacter.Choose(new PendingRun(), Catalog(), roster), Is.EqualTo(Ranger));
        Assert.That(RunCharacter.Choose(new PendingRun(), Catalog()), Is.EqualTo(Oathbound), "no roster is the catalog's first.");
    }

    [Test]
    public void RunCharacter_APendingRunStillWins()
    {
        // A Continue of a hidden class's run resumes as that class: the roster decides what a player
        // may start, and nothing else.
        var pending = new PendingRun();
        pending.Set(new ContentId("mode.jungle"), Gravecaller, 7);

        Assert.That(
            RunCharacter.Choose(pending, Catalog(), new ClassSelectRoster(new[] { Ranger })),
            Is.EqualTo(Gravecaller));
    }

    // ---- Fixture -----------------------------------------------------------------------------------

    private static ContentCatalog Catalog() => new ContentCatalog(
        new[]
        {
            Load<CharacterDefinition>(OathboundPath).ToSpec(),
            Load<CharacterDefinition>(GravecallerPath).ToSpec(),
            Load<CharacterDefinition>(RangerPath).ToSpec(),
        },
        new[] { Load<EnemyDefinition>(HuskPath).ToSpec() },
        new[] { Load<ModeDefinition>(JunglePath).ToSpec() });

    private static ContentId[] Ids(IReadOnlyList<CharacterSpec> specs)
    {
        var ids = new ContentId[specs.Count];

        for (int i = 0; i < specs.Count; i++)
        {
            ids[i] = specs[i].Id;
        }

        return ids;
    }

    private static T Load<T>(string path)
        where T : UnityEngine.Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }
}
