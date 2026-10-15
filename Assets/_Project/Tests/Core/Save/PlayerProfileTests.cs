using System;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Save;

namespace Soulvail.Tests.Core.Save;

/// <summary>
/// UI-11a: the places a profile remembers — finished, best stage, and the last picks.
/// </summary>
[TestFixture]
public sealed class PlayerProfileTests
{
    private static readonly ContentId Jungle = new ContentId("mode.jungle");
    private static readonly ContentId Desert = new ContentId("mode.desert");
    private static readonly ContentId Ranger = new ContentId("character.ranger");

    [Test]
    public void WithFinished_Twice_AddsOnce()
    {
        PlayerProfile profile = PlayerProfile.Default.WithFinished(Jungle).WithFinished(Jungle);

        Assert.That(profile.FinishedModeIds, Is.EqualTo(new[] { Jungle }));
    }

    [Test]
    public void WithFinished_KeepsOrderAndEverythingElse()
    {
        PlayerProfile start = PlayerProfile.Default.WithShards(40).WithHaptics(false);

        PlayerProfile profile = start.WithFinished(Jungle).WithFinished(Desert);

        Assert.That(profile.FinishedModeIds, Is.EqualTo(new[] { Jungle, Desert }));
        Assert.That(profile.Shards, Is.EqualTo(40));
        Assert.That(profile.HapticsEnabled, Is.False);
    }

    [Test]
    public void WithBestStage_Lower_KeepsHigher()
    {
        PlayerProfile profile = PlayerProfile.Default.WithBestStage(Jungle, 9);

        Assert.That(profile.WithBestStage(Jungle, 4).BestStage(Jungle), Is.EqualTo(9));
        Assert.That(profile.WithBestStage(Jungle, 9).BestStage(Jungle), Is.EqualTo(9));
        Assert.That(profile.WithBestStage(Jungle, 11).BestStage(Jungle), Is.EqualTo(11));
        Assert.That(profile.WithBestStage(Jungle, 11).BestStages, Has.Count.EqualTo(1), "raised in place.");
    }

    [Test]
    public void WithBestStage_NewMode_AddsRow()
    {
        PlayerProfile profile = PlayerProfile.Default.WithBestStage(Jungle, 9).WithBestStage(Desert, 2);

        Assert.That(profile.BestStages, Has.Count.EqualTo(2));
        Assert.That(profile.BestStage(Jungle), Is.EqualTo(9));
        Assert.That(profile.BestStage(Desert), Is.EqualTo(2));
        Assert.That(profile.BestStage(new ContentId("mode.hell")), Is.Zero, "never played.");
    }

    [Test]
    public void WithSelection_ReplacesBoth()
    {
        PlayerProfile picked = PlayerProfile.Default.WithSelection(Ranger, Jungle);

        Assert.That(picked.SelectedCharacterId, Is.EqualTo(Ranger));
        Assert.That(picked.SelectedModeId, Is.EqualTo(Jungle));

        PlayerProfile cleared = picked.WithSelection(default, default);

        Assert.That(cleared.SelectedCharacterId, Is.EqualTo(default(ContentId)), "default means none picked.");
        Assert.That(cleared.SelectedModeId, Is.EqualTo(default(ContentId)));
    }

    [Test]
    public void Default_HasNoPlacesAndNoPicks()
    {
        PlayerProfile profile = PlayerProfile.Default;

        Assert.That(profile.FinishedModeIds, Is.Not.Null.And.Empty);
        Assert.That(profile.BestStages, Is.Not.Null.And.Empty);
        Assert.That(profile.SelectedCharacterId, Is.EqualTo(default(ContentId)));
        Assert.That(profile.SelectedModeId, Is.EqualTo(default(ContentId)));
    }

    [Test]
    public void Ctor_DuplicateFinished_Throws()
    {
        Assert.Throws<ArgumentException>(() => Build(new[] { Jungle, Jungle }, Array.Empty<ModeBest>()));
    }

    [Test]
    public void Ctor_DuplicateBestRow_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Build(Array.Empty<ContentId>(), new[] { new ModeBest(Jungle, 2), new ModeBest(Jungle, 3) }));
    }

    [Test]
    public void Ctor_BestStageZero_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => Build(Array.Empty<ContentId>(), new[] { new ModeBest(Jungle, 0) }));
        Assert.Throws<ArgumentException>(
            () => Build(Array.Empty<ContentId>(), new[] { default(ModeBest) }));
    }

    [Test]
    public void Ctor_NullLists_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Build(null, Array.Empty<ModeBest>()));
        Assert.Throws<ArgumentNullException>(() => Build(Array.Empty<ContentId>(), null));
    }

    private static PlayerProfile Build(
        System.Collections.Generic.IReadOnlyList<ContentId> finished,
        System.Collections.Generic.IReadOnlyList<ModeBest> bests)
    {
        return new PlayerProfile(
            PlayerProfile.CurrentVersion,
            hapticsEnabled: true,
            seenFirstActiveHint: false,
            shards: 0,
            Array.Empty<ContentId>(),
            Array.Empty<ContentId>(),
            locale: string.Empty,
            finished,
            bests,
            default,
            default);
    }
}
