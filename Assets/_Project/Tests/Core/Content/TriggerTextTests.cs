using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;

namespace Soulvail.Tests.Core.Content;

/// <summary>
/// The vocabulary CC §6.3's trigger line is written from: one key per <c>(field, comparison)</c>
/// pair, and the kind of number that goes beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Both tables are walked over their enums rather than spot-checked</b>, which is the whole
/// reason they are worth testing at all: the failure this file is written against is a
/// <c>TriggerField</c> added to the enum and not to the tables that describe it, and a fixture
/// listing nine names by hand would have to be edited by the same person who forgot.
/// <c>Stats_ResolveEveryMember</c>'s shape (M3-05), one module over.
/// </para>
/// <para>
/// <b>Nothing here asserts English.</b> <c>ILocalizer</c> has no adapter until M6-10, so what is
/// checkable now is that the keys exist, are distinct, and are shaped the way a table lookup needs
/// (ADR-0012, ledger row 9). Whether <em>"Player HP below 60 %"</em> is readable in GD §13.1's two
/// seconds is M3-15's question and cannot be asked here.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TriggerTextTests
{
    /// <summary>Nine fields times two comparisons — the cross product this file walks.</summary>
    private const int PairCount = 18;

    [Test]
    public void Trigger_EveryPairHasAKey()
    {
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (TriggerField field in Enum.GetValues(typeof(TriggerField)))
        {
            foreach (TriggerComparison comparison in Enum.GetValues(typeof(TriggerComparison)))
            {
                LocKey key = default;

                Assert.That(
                    () => key = TriggerText.KeyFor(field, comparison),
                    Throws.Nothing,
                    $"No key for ({field}, {comparison}) — a TriggerField was added to the enum "
                        + "and not to KeyFor's table.");

                Assert.That(
                    key,
                    Is.Not.EqualTo(default(LocKey)),
                    $"({field}, {comparison}) resolved to default(LocKey), whose Key is null.");

                // **Distinct across the whole cross product, which is the half that catches a
                // copy-paste.** A pair that borrowed another pair's key would pass every "has a
                // key" assertion and put the wrong sentence on the screen — the one failure mode a
                // table of eighteen near-identical lines actually has.
                bool taken = seen.TryGetValue(key.Key, out string owner);

                Assert.That(
                    taken,
                    Is.False,
                    $"({field}, {comparison}) and {owner} share the key '{key.Key}'.");

                seen[key.Key] = $"({field}, {comparison})";
            }
        }

        Assert.That(
            seen.Count,
            Is.EqualTo(PairCount),
            "Nine fields and two comparisons is eighteen keys. If the enum grew, this number grows "
                + "with it — and so does the table.");
    }

    [Test]
    public void Trigger_KeyShape()
    {
        // The shape M6-10 resolves: the module, the field in camelCase, the comparison. A key per
        // field with the comparison bolted on separately was the alternative and it is wrong for
        // the reason word order is — "below" lands in different places in different languages.
        Assert.That(
            TriggerText.KeyFor(TriggerField.HpFraction, TriggerComparison.Below).Key,
            Is.EqualTo("trigger.hpFraction.below"));

        Assert.That(
            TriggerText.KeyFor(TriggerField.IncomingProjectiles, TriggerComparison.AtLeast).Key,
            Is.EqualTo("trigger.incomingProjectiles.atLeast"));

        // And every one of them survives LocKey's own door, which is what makes them usable as a
        // table lookup: non-empty and free of whitespace, so a stray space in the table cannot
        // become a key that mysteriously never resolves.
        foreach (TriggerField field in Enum.GetValues(typeof(TriggerField)))
        {
            foreach (TriggerComparison comparison in Enum.GetValues(typeof(TriggerComparison)))
            {
                string key = TriggerText.KeyFor(field, comparison).Key;

                Assert.That(key, Does.StartWith("trigger."), $"({field}, {comparison}) is off-module.");
                Assert.That(key, Does.Not.Contain(" "), $"({field}, {comparison}) holds a space.");
            }
        }
    }

    [Test]
    public void Trigger_EveryFieldHasAUnit()
    {
        foreach (TriggerField field in Enum.GetValues(typeof(TriggerField)))
        {
            Assert.That(
                () => TriggerText.UnitOf(field),
                Throws.Nothing,
                $"No unit for {field} — a TriggerField was added to the enum and not to UnitOf.");
        }

        // The four the spec names by hand, which is what stops the walk above passing against a
        // table that answers Fraction for everything.
        Assert.That(TriggerText.UnitOf(TriggerField.HpFraction), Is.EqualTo(TriggerUnit.Fraction));
        Assert.That(TriggerText.UnitOf(TriggerField.EnemiesWithin6m), Is.EqualTo(TriggerUnit.Count));
        Assert.That(TriggerText.UnitOf(TriggerField.Veilrot), Is.EqualTo(TriggerUnit.Points));
        Assert.That(TriggerText.UnitOf(TriggerField.StationaryTime), Is.EqualTo(TriggerUnit.Seconds));

        // **And FocusRampLevel, which the spec's rule 2 puts in Count and CombatBlackboard declares
        // a float in [0, 1].** Pinned here rather than left implicit so that the day someone moves
        // it to Fraction — which is what a 0.6 threshold drawn as "1" will eventually cost — they
        // are moving a row that says why rather than discovering a silent disagreement. See
        // TriggerText.UnitOf's remarks and the M3-09b As built.
        Assert.That(
            TriggerText.UnitOf(TriggerField.FocusRampLevel),
            Is.EqualTo(TriggerUnit.Count),
            "FocusRampLevel is Count because M3-09b rule 2 says so, not because it reads like one.");
    }

    [Test]
    public void Trigger_UnknownField_Throws()
    {
        // Loud rather than a placeholder key, for PlayerStats.Resolve's reason (M3-05 rule 4): a
        // silent fallback is a trigger line describing the wrong condition, and a player who acts
        // on it has been lied to by the one screen whose job is to explain.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TriggerText.KeyFor((TriggerField)99, TriggerComparison.Below));

        Assert.Throws<ArgumentOutOfRangeException>(() => TriggerText.UnitOf((TriggerField)99));

        // The other half of the pair, and it names the other argument — a caller who cast an int
        // into the comparison is told about the comparison rather than about the field they got
        // right.
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => TriggerText.KeyFor(TriggerField.HpFraction, (TriggerComparison)7));

        Assert.That(thrown.ParamName, Is.EqualTo("comparison"));
    }
}
