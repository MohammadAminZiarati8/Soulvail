using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// <c>ModeSpec</c> and <c>RosterEntry</c>: which stages a mode has, which archetypes it may spawn
/// at each depth, and the two authoring mistakes GD §8.2 makes illegal.
/// </summary>
/// <remarks>
/// Its own fixture rather than a section of <c>ContentTests</c>, unlike the catalog row that
/// looks a mode up: this type has behaviour — a filter, a schedule and a bounds check — where the
/// other specs are records with guards. <c>ContentTests</c> owns "the catalog indexes it like
/// everything else"; this owns what a mode <em>means</em>.
/// </remarks>
[TestFixture]
public sealed class ModeSpecTests
{
    private const string DescentId = "mode.descent";
    private const string HuskId = "enemy.husk";
    private const string SpitterId = "enemy.spitter";
    private const string BloaterId = "enemy.bloater";

    /// <summary>GD §8.2's first three rows: Husk 1, Spitter 2, Bloater 4.</summary>
    private static readonly RosterEntry[] DesignRoster =
    {
        new RosterEntry(new ContentId(HuskId), 1),
        new RosterEntry(new ContentId(SpitterId), 2),
        new RosterEntry(new ContentId(BloaterId), 4),
    };

    [Test]
    public void Roster_FiltersByStage()
    {
        ModeSpec mode = Mode(DesignRoster);
        var buffer = new RosterEntry[8];

        int count = mode.RosterFor(3, buffer);

        // The Bloater is not eligible until 4, which is the whole of GD §8.2's pacing: a stage-3
        // wave is composed from a vocabulary of two.
        Assert.That(count, Is.EqualTo(2));
        Assert.That(buffer[0].SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(buffer[1].SpecId, Is.EqualTo(new ContentId(SpitterId)));

        // The boundary is inclusive: introduced *at* 4 means eligible at 4.
        Assert.That(mode.RosterFor(4, buffer), Is.EqualTo(3));

        // And stage 1 is the Husk alone, which is what a first wave has to be.
        Assert.That(mode.RosterFor(1, buffer), Is.EqualTo(1));
        Assert.That(buffer[0].SpecId, Is.EqualTo(new ContentId(HuskId)));
    }

    [Test]
    public void Roster_OrderIsRosterOrder()
    {
        // Authored out of stage order on purpose. The composer reads this list to pick what to
        // spawn, so "in roster order" has to mean the order a designer typed rather than the
        // order the stages happen to run in — otherwise the answer would silently depend on how
        // the schedule was sorted, and two authorings of the same mode would compose differently.
        ModeSpec mode = Mode(new[]
        {
            new RosterEntry(new ContentId(BloaterId), 4),
            new RosterEntry(new ContentId(HuskId), 1),
            new RosterEntry(new ContentId(SpitterId), 2),
        });

        var buffer = new RosterEntry[8];

        int count = mode.RosterFor(9, buffer);

        Assert.That(count, Is.EqualTo(3));
        Assert.That(buffer[0].SpecId, Is.EqualTo(new ContentId(BloaterId)));
        Assert.That(buffer[1].SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(buffer[2].SpecId, Is.EqualTo(new ContentId(SpitterId)));

        Assert.That(mode.Roster[0].SpecId, Is.EqualTo(new ContentId(BloaterId)),
            "And the property agrees with the fill.");
    }

    [Test]
    public void Roster_DestinationTooSmall_Throws()
    {
        ModeSpec mode = Mode(DesignRoster);
        var buffer = new RosterEntry[2];

        // Three eligible at stage 4, room for two. Refused rather than truncated: a short buffer
        // would quietly narrow the wave's vocabulary, and a director that silently stops being
        // allowed to spawn a Bloater is a balance bug with no symptom.
        Assert.Throws<ArgumentException>(() => mode.RosterFor(4, buffer));

        // Counted before anything is written, so the caller's buffer is untouched by the refusal.
        Assert.That(buffer[0].SpecId.Value, Is.Null);
        Assert.That(buffer[1].SpecId.Value, Is.Null);
    }

    [Test]
    public void Roster_ExactlySizedBuffer_Fits()
    {
        // The other side of the boundary, and the case the composer actually uses: a buffer sized
        // to the roster is never short, so the guard above must not fire on equality.
        ModeSpec mode = Mode(DesignRoster);
        var buffer = new RosterEntry[3];

        Assert.That(mode.RosterFor(4, buffer), Is.EqualTo(3));
    }

    [Test]
    public void Roster_AllocatesNothing()
    {
        ModeSpec mode = Mode(DesignRoster);

        // A heap array rather than a stackalloc, because a lambda cannot close over a ref struct
        // (Traps §7). It converts to Span<RosterEntry> at the call site inside the measured body,
        // which is the part being measured — that the fill walks the array by index and creates
        // no enumerator.
        var buffer = new RosterEntry[8];

        AllocationAssert.None(() => mode.RosterFor(4, buffer));
    }

    [Test]
    public void Roster_DuplicateId_Throws()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Mode(new[]
        {
            new RosterEntry(new ContentId(HuskId), 1),
            new RosterEntry(new ContentId(HuskId), 3),
        }));

        Assert.That(ex.Message, Does.Contain(HuskId));
    }

    [Test]
    public void Roster_TwoIntroductionsAtOneStage_Throws()
    {
        // GD §8.2: "New enemies arrive one at a time, in a wave where they're the only new
        // thing." It is a design rule before it is a data rule, and it is what lets
        // TryGetIntroduction answer with a single id instead of a list.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => Mode(new[]
        {
            new RosterEntry(new ContentId(SpitterId), 2),
            new RosterEntry(new ContentId(BloaterId), 2),
        }));

        Assert.That(ex.Message, Does.Contain(BloaterId));
    }

    [Test]
    public void Roster_DefaultEntry_Throws()
    {
        // default(RosterEntry) carries a zeroed id straight past the struct's own constructor —
        // a struct always has a zeroed form — so the check is repeated in the spec (AR §18.3).
        Assert.Throws<ArgumentException>(() => Mode(new[] { default(RosterEntry) }));
    }

    [Test]
    public void RosterEntry_Guards()
    {
        Assert.Throws<ArgumentException>(() => new RosterEntry(default, 1));

        // Stages are numbered from 1, so "eligible from the start" is spelled 1 rather than 0.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RosterEntry(new ContentId(HuskId), 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RosterEntry(new ContentId(HuskId), -1));

        var entry = new RosterEntry(new ContentId(HuskId), 4);
        Assert.That(entry.SpecId, Is.EqualTo(new ContentId(HuskId)));
        Assert.That(entry.IntroducedAtStage, Is.EqualTo(4));
    }

    [Test]
    public void Introduction_AtStage()
    {
        ModeSpec mode = Mode(DesignRoster);

        Assert.That(mode.TryGetIntroduction(2, out ContentId specId), Is.True);
        Assert.That(specId, Is.EqualTo(new ContentId(SpitterId)));

        Assert.That(mode.TryGetIntroduction(1, out specId), Is.True);
        Assert.That(specId, Is.EqualTo(new ContentId(HuskId)));
    }

    [Test]
    public void Introduction_None()
    {
        ModeSpec mode = Mode(DesignRoster);

        // Most stages introduce nothing — GD §8.2 names ten out of an endless run — so the false
        // branch is the common one rather than the error case.
        Assert.That(mode.TryGetIntroduction(3, out ContentId specId), Is.False);
        Assert.That(specId.Value, Is.Null);

        Assert.That(mode.TryGetIntroduction(99, out _), Is.False);
    }

    [Test]
    public void HasStage_Endless()
    {
        ModeSpec mode = Mode(DesignRoster);

        Assert.That(mode.IsEndless, Is.True);
        Assert.That(mode.HasStage(1), Is.True);
        Assert.That(mode.HasStage(9999), Is.True);

        // int.MaxValue rather than the authored number, so every comparison reads the same way
        // and nothing has to branch on IsEndless to ask whether a stage exists.
        Assert.That(mode.FinalStage, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void HasStage_Finite()
    {
        var mode = new ModeSpec(
            new ContentId("mode.trial"),
            new LocKey("mode.trial.name"),
            1,
            false,
            10,
            DesignRoster);

        Assert.That(mode.IsEndless, Is.False);
        Assert.That(mode.FinalStage, Is.EqualTo(10));
        Assert.That(mode.HasStage(10), Is.True);
        Assert.That(mode.HasStage(11), Is.False);
    }

    [Test]
    public void HasStage_BeforeStart()
    {
        var mode = new ModeSpec(
            new ContentId("mode.deep"),
            new LocKey("mode.deep.name"),
            3,
            true,
            0,
            DesignRoster);

        Assert.That(mode.StartingStage, Is.EqualTo(3));
        Assert.That(mode.HasStage(2), Is.False);
        Assert.That(mode.HasStage(3), Is.True);
    }

    [Test]
    public void Ctor_Guards()
    {
        Assert.Throws<ArgumentException>(
            () => new ModeSpec(default, Name(), 1, true, 0, DesignRoster));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModeSpec(Id(), Name(), 0, true, 0, DesignRoster));

        // A finite mode whose last stage is before its first has no stages at all — which would
        // otherwise be a run that starts and immediately has nowhere to be.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ModeSpec(Id(), Name(), 5, false, 4, DesignRoster));

        Assert.Throws<ArgumentNullException>(
            () => new ModeSpec(Id(), Name(), 1, true, 0, null));

        // The endless flag wins over the number beside it: an endless mode ignores finalStage
        // rather than being refused for it, because "endless" is what the designer said.
        Assert.DoesNotThrow(
            () => new ModeSpec(Id(), Name(), 5, true, 0, DesignRoster));
    }

    [Test]
    public void Ctor_CopiesRoster()
    {
        var list = new List<RosterEntry>(DesignRoster);

        ModeSpec mode = Mode(list);
        list.Clear();

        Assert.That(mode.Roster.Count, Is.EqualTo(3));
    }

    [Test]
    public void Ctor_EmptyRoster_IsLegal()
    {
        // Legal on purpose: a mode whose arena content comes entirely from its spawn plan has
        // nothing to schedule, and RosterFor already answers 0 for any stage before the first
        // introduction — so an empty roster is not a new shape for a caller to handle.
        ModeSpec mode = Mode(Array.Empty<RosterEntry>());

        Assert.That(mode.Roster, Is.Empty);
        Assert.That(mode.RosterFor(9, new RosterEntry[4]), Is.Zero);
        Assert.That(mode.TryGetIntroduction(1, out _), Is.False);
    }

    [Test]
    public void Roster_CannotBeWrittenThrough()
    {
        // The property is a wrapper, not the array. Exposed as the array it is, an
        // IReadOnlyList<T> casts straight back to RosterEntry[] and the copy above protects
        // nothing — the same guard ContentCatalog and SpawnPlan make.
        ModeSpec mode = Mode(DesignRoster);

        Assert.That(mode.Roster as RosterEntry[], Is.Null);
    }

    private static ContentId Id() => new ContentId(DescentId);

    private static LocKey Name() => new LocKey("mode.descent.name");

    private static ModeSpec Mode(IReadOnlyList<RosterEntry> roster) =>
        new ModeSpec(Id(), Name(), 1, true, 0, roster);
}
