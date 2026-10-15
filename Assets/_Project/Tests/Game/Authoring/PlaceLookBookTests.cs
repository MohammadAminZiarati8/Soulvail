using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Authoring;
using UnityEditor;
using UnityEngine;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>UI-11h-b rule 8: the book that says which picture a place wears.</summary>
[TestFixture]
public sealed class PlaceLookBookTests
{
    private const string JungleArt = "Assets/_Project/Art/UI/Home/PlaceCard_Jungle.png";
    private const string SoonArt = "Assets/_Project/Art/UI/Home/PlaceCard_Soon.png";

    private static readonly ContentId Jungle = new ContentId("mode.jungle");
    private static readonly ContentId Elsewhere = new ContentId("mode.elsewhere");

    [Test]
    public void Book_ReturnsPicture_NullForUnknown()
    {
        var jungle = AssetDatabase.LoadAssetAtPath<Sprite>(JungleArt);
        var book = new PlaceLookBook(new Dictionary<ContentId, Sprite> { [Jungle] = jungle });

        Assert.That(jungle, Is.Not.Null, "PlaceCard_Jungle is not a sprite.");
        Assert.That(book.For(Jungle), Is.SameAs(jungle));
        Assert.That(book.For(Elsewhere), Is.Null, "an id it was not given has a picture.");
        Assert.That(book.For(default), Is.Null, "default(ContentId) threw or answered.");
    }

    [Test]
    public void Book_Guards()
    {
        var jungle = AssetDatabase.LoadAssetAtPath<Sprite>(JungleArt);
        var soon = AssetDatabase.LoadAssetAtPath<Sprite>(SoonArt);

        Assert.Throws<ArgumentNullException>(() => new PlaceLookBook(null));
        Assert.Throws<ArgumentException>(() => new PlaceLookBook(new Dictionary<ContentId, Sprite> { [default] = jungle }));

        var source = new Dictionary<ContentId, Sprite> { [Jungle] = jungle };
        var book = new PlaceLookBook(source);

        source[Jungle] = soon;
        source[Elsewhere] = soon;

        Assert.That(book.For(Jungle), Is.SameAs(jungle), "the book followed its source.");
        Assert.That(book.For(Elsewhere), Is.Null, "the book followed its source.");
    }
}
