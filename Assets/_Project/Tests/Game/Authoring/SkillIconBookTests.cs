using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// UI-01e: what a skill looks like in the HUD's auto-cast row — the icon book, the definition's icon
/// field, the class's volley node, and the shipped assets that say which is which.
/// </summary>
/// <remarks>
/// <c>CharacterLookTests</c>' and <c>EnemyLookTests</c>' shape, one kind of content over: the book
/// refuses what names nothing and answers what it does not know with nothing, and the shipped rows
/// read the assets rather than a copy of them.
/// </remarks>
[TestFixture]
public sealed class SkillIconBookTests
{
    private const string RangerPath = "Assets/_Project/Data/Characters/Ranger.asset";
    private const string VolleyPath = "Assets/_Project/Data/Skills/Ranger/Volley.asset";
    private const string QuickDrawPath = "Assets/_Project/Data/Skills/Ranger/QuickDraw.asset";
    private const string VolleyIconPath = "Assets/_Project/Art/UI/Icons/Icon_Volley.png";
    private const string VolleyId = "skill.ranger.volley";

    private readonly List<Object> _created = new List<Object>();

    [TearDown]
    public void DestroyCreated()
    {
        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // ---- The book (rule 2) -----------------------------------------------------------------------

    [Test]
    public void Book_AnUnknownIdIsNull()
    {
        var book = new SkillIconBook(new Dictionary<ContentId, Sprite> { [new ContentId(VolleyId)] = Icon() });

        Assert.That(book.For(new ContentId("skill.test.nobody")), Is.Null);
        Assert.That(book.For(default), Is.Null, "default(ContentId) threw or answered something.");
        Assert.That(book.For(new ContentId(VolleyId)), Is.SameAs(Icon()));
    }

    [Test]
    public void Book_RefusesADefaultId()
    {
        Assert.Throws<ArgumentException>(() =>
            _ = new SkillIconBook(new Dictionary<ContentId, Sprite> { [default] = Icon() }));
        Assert.Throws<ArgumentNullException>(() => _ = new SkillIconBook(null));
    }

    [Test]
    public void Book_CopiesItsSource()
    {
        var source = new Dictionary<ContentId, Sprite> { [new ContentId(VolleyId)] = Icon() };
        var book = new SkillIconBook(source);

        source.Clear();

        Assert.That(book.For(new ContentId(VolleyId)), Is.SameAs(Icon()), "the book is a view of its source.");
        Assert.That(book.Count, Is.EqualTo(1));
    }

    // ---- The definitions (rule 2) ----------------------------------------------------------------

    [Test]
    public void Definition_CarriesItsIcon()
    {
        var definition = ScriptableObject.CreateInstance<SkillDefinition>();

        _created.Add(definition);

        Assert.That(definition.Icon, Is.Null, "a new skill ships with an icon.");

        var so = new SerializedObject(definition);

        so.FindProperty("_icon").objectReferenceValue = Icon();
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(definition.Icon, Is.SameAs(Icon()));
    }

    [Test]
    public void Look_CarriesTheVolleyNode()
    {
        var definition = ScriptableObject.CreateInstance<CharacterDefinition>();

        _created.Add(definition);

        Assert.That(definition.ToLook().VolleyNode, Is.EqualTo(default(ContentId)), "a class names a volley node by default.");

        var so = new SerializedObject(definition);

        so.FindProperty("_volleyNode").objectReferenceValue = Load<SkillDefinition>(VolleyPath);
        so.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(definition.ToLook().VolleyNode, Is.EqualTo(new ContentId(VolleyId)));
    }

    [Test]
    public void Install_RegistersTheIconBook()
    {
        var builder = new ContainerBuilder();
        var table = ScriptableObject.CreateInstance<LocalizationTable>();

        _created.Add(table);

        BootInstaller.Install(
            builder,
            new[] { Load<CharacterDefinition>(RangerPath) },
            Array.Empty<EnemyDefinition>(),
            Array.Empty<ModeDefinition>(),
            new[] { Load<SkillDefinition>(VolleyPath), Load<SkillDefinition>(QuickDrawPath) },
            Array.Empty<SkillTreeDefinition>(),
            table);

        using IObjectResolver container = builder.Build();

        var book = container.Resolve<SkillIconBook>();

        // Built from the same list as the catalog, at the root: the Volley has its art and Quick Draw
        // gained its art with UI-04c; the book answers for both.
        Assert.That(book.For(new ContentId(VolleyId)), Is.SameAs(Icon()));
        Assert.That(book.For(new ContentId("skill.ranger.quick-draw")).name, Is.EqualTo("Icon_QuickDraw"));
        Assert.That(book.Count, Is.EqualTo(2));
    }

    // ---- The shipped content (rule 9) ------------------------------------------------------------

    [Test]
    public void Shipped_TheVolleyWearsIconVolley()
    {
        Assert.That(Load<SkillDefinition>(VolleyPath).Icon, Is.SameAs(Icon()));
    }

    [Test]
    public void Shipped_TheRangerNamesItsVolleyNode()
    {
        Assert.That(Load<CharacterDefinition>(RangerPath).ToLook().VolleyNode, Is.EqualTo(new ContentId(VolleyId)),
            "the Ranger's volley is drawn as some other node, or none.");
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    private static Sprite Icon()
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(VolleyIconPath);

        Assert.That(sprite, Is.Not.Null, $"No sprite at {VolleyIconPath}.");

        return sprite;
    }

    private static T Load<T>(string path)
        where T : Object
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);

        Assert.That(asset, Is.Not.Null, $"No {typeof(T).Name} at {path}.");

        return asset;
    }
}
