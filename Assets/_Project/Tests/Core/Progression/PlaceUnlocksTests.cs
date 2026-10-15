using System;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Progression;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// UI-11b: a place opens when the one before it is finished — and only that one.
/// </summary>
[TestFixture]
public sealed class PlaceUnlocksTests
{
    private static readonly ContentId A = new ContentId("mode.alpha");
    private static readonly ContentId B = new ContentId("mode.bravo");
    private static readonly ContentId C = new ContentId("mode.charlie");
    private static readonly ContentId Hidden = new ContentId("mode.hidden");

    [Test]
    public void IsUnlocked_NoUnlockedBy_True()
    {
        Assert.That(PlaceUnlocks.IsUnlocked(A, PlayerProfile.Default, Catalog()), Is.True);
    }

    [Test]
    public void IsUnlocked_PreviousFinished_True()
    {
        PlayerProfile profile = PlayerProfile.Default.WithFinished(A);

        Assert.That(PlaceUnlocks.IsUnlocked(B, profile, Catalog()), Is.True);
    }

    [Test]
    public void IsUnlocked_PreviousNotFinished_False()
    {
        Assert.That(PlaceUnlocks.IsUnlocked(B, PlayerProfile.Default, Catalog()), Is.False);
    }

    [Test]
    public void IsUnlocked_GrandparentFinishedOnly_False()
    {
        PlayerProfile profile = PlayerProfile.Default.WithFinished(A);

        Assert.That(PlaceUnlocks.IsUnlocked(C, profile, Catalog()), Is.False);
    }

    [Test]
    public void Listed_SkipsUnlisted_KeepsOrder()
    {
        var into = new ContentId[4];

        int count = PlaceUnlocks.Listed(Catalog(), into);

        Assert.That(count, Is.EqualTo(3));
        Assert.That(into[0], Is.EqualTo(A));
        Assert.That(into[1], Is.EqualTo(B));
        Assert.That(into[2], Is.EqualTo(C));
    }

    private static ContentCatalog Catalog() => new ContentCatalog(
        Array.Empty<CharacterSpec>(),
        modes: new[]
        {
            Mode(A, default, true),
            Mode(Hidden, default, false),
            Mode(B, A, true),
            Mode(C, B, true),
        });

    private static ModeSpec Mode(ContentId id, ContentId unlockedBy, bool listed) => new ModeSpec(
        id,
        new LocKey(id.Value + ".name"),
        startingStage: 1,
        isEndless: false,
        finalStage: 30,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>(),
        unlockedBy: unlockedBy,
        isListed: listed);
}
