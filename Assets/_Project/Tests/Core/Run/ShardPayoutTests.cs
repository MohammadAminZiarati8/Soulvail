using System;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Run;

/// <summary>
/// GD §14.1's three terms and the shipped <c>Descent.asset</c> numbers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row is an arithmetic assertion rather than a simulation</b>, which is what
/// <see cref="ShardPayout"/> being a pure function of two inputs buys: no run is started, nothing
/// is ticked, and no fake is needed beyond a <see cref="ModeSpec"/> with a boss roster on it.
/// </para>
/// <para>
/// <b><see cref="Descent"/> authors no archetypes, so the third term is zero in every row written
/// before M6-09a</b> and those rows still assert the two-term figures they always did.
/// <see cref="Shipped"/> is the mode with its roster, for the rows about the third term.
/// </para>
/// <para>
/// <b>And <see cref="Payout_ReadsTheAuthoredIntervalNotAFive"/> drives an interval no shipped asset
/// uses</b>, which is <c>BossSpecTests.Boss_StageRuleIsAuthoredNotHardcoded</c>'s shape one layer
/// up: a <c>stage % 5</c> that had crept into the walk would pass every other row here and fail
/// that one.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ShardPayoutTests
{
    private const string DescentId = "mode.descent";
    private const string WardenId = "boss.warden";

    /// <summary>A second boss, for the row that authors an interval nothing ships.</summary>
    private const string ArchonId = "boss.archon";

    /// <summary><c>Descent.asset</c>'s <c>_everyNStages</c>, as the shipped asset authors it.</summary>
    private const int ShippedInterval = 5;

    // ---- Rule 2: the first term -------------------------------------------------------------------

    [Test]
    public void Payout_PaysTenPerStage()
    {
        // A mode with no boss roster, so the second term cannot contribute and the first is alone.
        Assert.That(ShardPayout.For(7, Mode()), Is.EqualTo(70));

        Assert.That(ShardPayout.PerStage, Is.EqualTo(10), "GD §14.1's first coefficient.");
    }

    // ---- Rules 2 and 3: the second term -----------------------------------------------------------

    [Test]
    public void Payout_PaysFiftyPerBossStagePassed()
    {
        // Descent.asset's every 5th. Twelve stages of depth is 120; the bosses at 5 and 10 are 100.
        Assert.That(ShardPayout.BossesKilled(12, Descent()), Is.EqualTo(2));
        Assert.That(ShardPayout.For(12, Descent()), Is.EqualTo(220));

        Assert.That(ShardPayout.PerBoss, Is.EqualTo(50), "GD §14.1's second coefficient.");
    }

    // ---- Rules 3 and 4: the stage the player died on ----------------------------------------------

    [Test]
    public void Payout_DoesNotPayForTheStageDiedOn()
    {
        // The under-payment rule 4 names out loud rather than hides: a player who killed the Warden
        // on stage 5 and then died there, before walking through the door, is paid for the depth and
        // not for the boss. A stage is only *left* once its boss is cleared, so a stage the run is
        // standing in has proved nothing — and the alternative, a counter on RunState, under-pays far
        // worse and far more often (a resumed run losing every boss it ever killed).
        Assert.That(ShardPayout.BossesKilled(5, Descent()), Is.EqualTo(0));

        Assert.That(ShardPayout.For(5, Descent()), Is.EqualTo(50), "Depth only, and 50 of it.");

        // And the stage after it pays for that boss, which is the same rule from the other side.
        Assert.That(ShardPayout.BossesKilled(6, Descent()), Is.EqualTo(1));
    }

    [Test]
    public void Payout_ReadsTheAuthoredIntervalNotAFive()
    {
        // Every 3rd — a value no shipped asset uses. At stage 10 the bosses below are 3, 6 and 9, so
        // a hardcoded `stage % 5` would answer 1 here and pass every other row in this file.
        ModeSpec everyThird = Mode((ArchonId, 3));

        Assert.That(ShardPayout.BossesKilled(10, everyThird), Is.EqualTo(3));

        Assert.That(
            ShardPayout.For(10, everyThird),
            Is.EqualTo(100 + 150),
            "Ten stages of depth and three bosses, at an interval the code cannot have assumed.");
    }

    // ---- Rule 2: a pure function ------------------------------------------------------------------

    [Test]
    public void Payout_IsTheSameForTheSameInputs()
    {
        ModeSpec mode = Descent();

        int first = ShardPayout.For(17, mode);
        int second = ShardPayout.For(17, mode);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Is.EqualTo(170 + 150), "Seventeen stages, and the bosses at 5, 10 and 15.");

        // And a *different* instance authored the same way answers the same, which is the half that
        // would catch a cache keyed on the object rather than on its numbers.
        Assert.That(ShardPayout.For(17, Descent()), Is.EqualTo(first));
    }

    [Test]
    public void Payout_HoldsNoState()
    {
        // SaveMigrations' row, for SaveMigrations' reason: a static class is only not a violation of
        // AR §13 while it has nothing to mutate. A static with a setter is one nothing resets under a
        // disabled domain reload.
        FieldInfo[] fields = typeof(ShardPayout).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            Assert.That(
                field.IsLiteral || field.IsInitOnly,
                Is.True,
                $"ShardPayout.{field.Name} is mutable state. The payout is a pure function of a "
                    + "depth and a mode, and the day it needs a collaborator it becomes an injected "
                    + "object rather than a static with a field.");
        }
    }

    // ---- M6-09a rule 5: the third term ------------------------------------------------------------

    [Test]
    public void Payout_HasTheArchetypeTerm()
    {
        // Replaces Payout_HasNoArchetypeTerm, which asserted 10 here so that nobody could add the
        // term without reading why it was missing. M6-09a is the task that note named: a run that
        // died on stage 1 of the shipped Descent met its first Husk there, so all three terms pay.
        Assert.That(ShardPayout.For(1, Shipped(), Array.Empty<ContentId>()), Is.EqualTo(10 + 0 + 25));
    }

    [Test]
    public void Payout_PaysNothingForAnArchetypeAlreadyMet()
    {
        Assert.That(ShardPayout.For(1, Shipped(), new[] { Husk }), Is.EqualTo(10));
    }

    [Test]
    public void Payout_IsInclusiveOfTheDepthReached()
    {
        // The Bloater is introduced at 4, and in this mode so is a boss. Dying on stage 4 met the
        // Bloater — a body spawns on arrival — and killed no boss, which only leaving proves.
        ModeSpec mode = Mode(ShippedRoster(), (ArchonId, 4));
        var destination = new ContentId[3];

        int written = ShardPayout.NewArchetypes(4, mode, new[] { Husk, Spitter }, destination);

        Assert.That(written, Is.EqualTo(1));
        Assert.That(destination[0], Is.EqualTo(Bloater), "met on the stage died on: inclusive.");
        Assert.That(ShardPayout.BossesKilled(4, mode), Is.Zero, "not killed on the stage died on: exclusive.");
    }

    [Test]
    public void Payout_ANullSetPaysForEverything()
    {
        // Twenty stages, the Wardens at 5, 10 and 15, and all three of Descent's archetypes — the
        // generous direction GD §14.1's deferral note already chose.
        Assert.That(ShardPayout.For(20, Shipped(), alreadyMet: null), Is.EqualTo(200 + 150 + 75));
    }

    [Test]
    public void Payout_NewArchetypesWritesTheIds()
    {
        var destination = new ContentId[3];

        int written = ShardPayout.NewArchetypes(12, Shipped(), new[] { Husk }, destination);

        Assert.That(written, Is.EqualTo(2));
        Assert.That(destination[0], Is.EqualTo(Spitter), "introduction order: stage 2 first.");
        Assert.That(destination[1], Is.EqualTo(Bloater));
    }

    [Test]
    public void Payout_ThirtySpecTermIsTwentyFive()
    {
        Assert.That(ShardPayout.PerNewArchetype, Is.EqualTo(25), "GD §14.1's third coefficient.");
    }

    [Test]
    public void Payout_NewArchetypesRefusesAShortBuffer()
    {
        // A dropped id is a Shard nobody is paid and a meeting nobody records, so it throws.
        Assert.Throws<ArgumentException>(
            () => ShardPayout.NewArchetypes(12, Shipped(), null, new ContentId[2]));
    }

    // ---- Rule 8: the door -------------------------------------------------------------------------

    [TestCase(0)]
    [TestCase(-1)]
    public void Payout_RefusesAStageBelowOne(int stage)
    {
        // Zero is what default(RunSnapshot) carries, and RunSnapshot's own constructor already
        // refuses it — which is exactly why the guard here is cheap: a run cannot reach this with one.
        Assert.Throws<ArgumentOutOfRangeException>(() => ShardPayout.For(stage, Descent()));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShardPayout.BossesKilled(stage, Descent()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ShardPayout.NewArchetypes(stage, Descent(), null, new ContentId[3]));
    }

    [Test]
    public void Payout_RefusesANullMode()
    {
        Assert.Throws<ArgumentNullException>(() => ShardPayout.For(4, null));
        Assert.Throws<ArgumentNullException>(() => ShardPayout.BossesKilled(4, null));

        // The mode is checked before the depth, so a caller who got both wrong is told about the
        // missing collaborator rather than about the number.
        Assert.Throws<ArgumentNullException>(() => ShardPayout.For(0, null));
    }

    // ---- The cost -------------------------------------------------------------------------------

    [Test]
    public void Payout_AllocatesNothing()
    {
        ModeSpec mode = Descent();

        // Not decorative: this runs on the frame the player dies, which is already the busiest frame
        // a run has — a death check, an End(), a census cleared and a scope about to be torn down.
        // TryGetBossFor walks an authored array, so the whole payout is an int and a loop.
        AllocationAssert.None(() => ShardPayout.For(30, mode));
    }

    // ---- The event ------------------------------------------------------------------------------

    [Test]
    public void Event_CarriesItsOwnBreakdown()
    {
        // The three fields, copied. The breakdown rides the event because the screen that draws it
        // is a readout and must not hold a handle to the thing that computed it (M4-04's precedent).
        var awarded = new ShardsAwarded(220, 12, 2);

        Assert.That(awarded.Total, Is.EqualTo(220));
        Assert.That(awarded.DeepestStage, Is.EqualTo(12));
        Assert.That(awarded.BossesKilled, Is.EqualTo(2));

        // And the three agree with the arithmetic a caller is expected to have done, which is the
        // property M4-06's screen will render as one line and two.
        Assert.That(
            awarded.Total,
            Is.EqualTo((ShardPayout.PerStage * awarded.DeepestStage)
                + (ShardPayout.PerBoss * awarded.BossesKilled)));
    }

    /// <summary>
    /// <c>Descent.asset</c>'s boss schedule as the shipped asset authors it — one row,
    /// <c>boss.warden</c> on every 5th stage.
    /// </summary>
    /// <remarks>
    /// A fixture rather than the asset, because <c>Soulvail.Tests.Core</c> is pure C# and cannot load
    /// one. <see cref="ShippedInterval"/> is the number to change if <c>Descent.asset</c> ever
    /// re-authors it.
    /// </remarks>
    private static ModeSpec Descent() => Mode(Array.Empty<RosterEntry>(), (WardenId, ShippedInterval));

    private static readonly ContentId Husk = new ContentId("enemy.husk");
    private static readonly ContentId Spitter = new ContentId("enemy.spitter");
    private static readonly ContentId Bloater = new ContentId("enemy.bloater");

    /// <summary><c>Descent.asset</c> with its archetype roster: the Husk at 1, the Spitter at 2, the Bloater at 4.</summary>
    private static ModeSpec Shipped() => Mode(ShippedRoster(), (WardenId, ShippedInterval));

    private static RosterEntry[] ShippedRoster() => new[]
    {
        new RosterEntry(Husk, 1),
        new RosterEntry(Spitter, 2),
        new RosterEntry(Bloater, 4),
    };

    private static ModeSpec Mode(params (string Id, int Every)[] bosses) =>
        Mode(Array.Empty<RosterEntry>(), bosses);

    private static ModeSpec Mode(RosterEntry[] archetypes, params (string Id, int Every)[] bosses)
    {
        var roster = new BossRosterEntry[bosses.Length];

        for (int i = 0; i < bosses.Length; i++)
        {
            roster[i] = new BossRosterEntry(new ContentId(bosses[i].Id), bosses[i].Every);
        }

        return new ModeSpec(
            new ContentId(DescentId),
            new LocKey("mode.descent.name"),
            startingStage: 1,
            isEndless: true,
            finalStage: 0,
            Scalings.Design(),
            Scalings.Xp(),
            archetypes,
            arenas: null,
            bossRoster: roster);
    }
}
