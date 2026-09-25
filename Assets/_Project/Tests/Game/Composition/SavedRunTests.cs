using System;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Composition;

namespace Soulvail.Tests.Game.Composition;

/// <summary>
/// What the disk said at launch, and the bargain it makes with anyone who asks before it has been
/// told: presence, the throw, and forgetting.
/// </summary>
/// <remarks>
/// A fixture of its own rather than rows in <c>InstallerTests</c>, which is about wiring: this
/// class has behaviour — a value that is there or is not, and a read that refuses rather than
/// defaults — and it is the half of the resume path that needs no container at all.
/// </remarks>
[TestFixture]
public sealed class SavedRunTests
{
    private static readonly DateTimeOffset Instant =
        new DateTimeOffset(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public void Saved_IsAbsentUntilSet()
    {
        var saved = new SavedRun();

        Assert.That(saved.IsPresent, Is.False, "A fresh install has no run to continue.");
    }

    [Test]
    public void Saved_ReadBeforeSet_Throws()
    {
        var saved = new SavedRun();

        // `PendingRun`'s bargain and for its reason: handing back `default(RunSnapshot)` would
        // give the Menu a version-0, stage-0 run that reads as real until something three layers
        // away refuses it, by which point nothing can say who asked.
        InvalidOperationException thrown =
            Assert.Throws<InvalidOperationException>(() => _ = saved.Value);

        Assert.That(
            thrown.Message,
            Does.Contain(nameof(SavedRun.IsPresent)),
            "The message has to name the property the caller should have checked, or it only says "
                + "that something went wrong.");
    }

    [Test]
    public void Saved_SetThenPresent()
    {
        var saved = new SavedRun();
        RunSnapshot snapshot = Snapshot(stage: 9);

        saved.Set(snapshot);

        Assert.That(saved.IsPresent, Is.True);

        // Field by field rather than by equality: `RunSnapshot` is a struct with no `Equals`
        // override, so `Is.EqualTo` would compare member-wise through reflection and pass for the
        // wrong reasons on a type that later gets one.
        Assert.That(saved.Value.StageIndex, Is.EqualTo(9));
        Assert.That(saved.Value.Seed, Is.EqualTo(snapshot.Seed));
        Assert.That(saved.Value.ModeId, Is.EqualTo(snapshot.ModeId));
        Assert.That(saved.Value.PlayerHp, Is.EqualTo(snapshot.PlayerHp));
    }

    [Test]
    public void Saved_SetReplaces()
    {
        var saved = new SavedRun();

        saved.Set(Snapshot(stage: 2));
        saved.Set(Snapshot(stage: 11));

        Assert.That(saved.Value.StageIndex, Is.EqualTo(11));
    }

    [Test]
    public void Saved_ClearForgets()
    {
        var saved = new SavedRun();

        saved.Set(Snapshot(stage: 4));
        saved.Clear();

        Assert.That(saved.IsPresent, Is.False);
        Assert.Throws<InvalidOperationException>(() => _ = saved.Value);
    }

    [Test]
    public void Saved_ClearOnAnAbsentRun_IsANoOp()
    {
        var saved = new SavedRun();

        Assert.DoesNotThrow(() => saved.Clear());
        Assert.That(saved.IsPresent, Is.False);
    }

    private static RunSnapshot Snapshot(int stage) => new RunSnapshot(
        RunSnapshot.CurrentVersion,
        new ContentId("mode.descent"),
        new ContentId("character.oathbound"),
        seed: 4_242,
        stageIndex: stage,
        random: new RandomState(1, 2, 3, 4, 5),
        playerHp: 62f,
        playerShield: 9f,
        runTime: 412.5f,
        writtenAt: Instant,
        level: 1,
        xp: 0f,
        pendingLevelUps: 0,
        takenNodeIds: Array.Empty<ContentId>(),
        manualSkillIds: new ContentId[SkillRunner.MaxManualSlots],
        default,
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>(),
        Array.Empty<ContentId>());
}
