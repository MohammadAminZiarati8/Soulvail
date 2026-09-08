using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// The M1-01 spec's thirteen rules, plus two rows the spec's table does not cover: the guards
/// against a non-finite value and against an undefined <see cref="ModifierKind"/>. Both are
/// decisions this task made rather than inherited — see the comments on those two tests for why
/// a stat refuses NaN at the door instead of leaving it to the caller, as rule 6 does for
/// merely negative results.
/// </summary>
/// <remarks>
/// The Oathbound's damage is the running example — base 13, +2 from a node, +15 % and +45 % from
/// two more, ×1.2 from Focus — because it is the stack the spec's own <c>Describe</c> format
/// spells out, and it exercises all three kinds at once.
/// </remarks>
[TestFixture]
public sealed class StatTests
{
    /// <summary>
    /// U+00D7, the multiplication sign the spec's format shows, by code point rather than as the
    /// glyph. Written as the glyph in both files, a mis-decoded source would garble the
    /// expectation and the output identically and this test would pass while the panel showed
    /// mojibake.
    /// </summary>
    private const char Times = (char)0x00D7;

    /// <summary>
    /// Where the allocation test parks the values it reads, so the reads cannot be optimised
    /// away and the field is not merely assigned (which is a compiler warning of its own).
    /// </summary>
    private float _sink;

    [Test]
    public void BaseOnly_ValueIsBase()
    {
        Assert.That(new Stat(13f).Value, Is.EqualTo(13f));
    }

    [Test]
    public void Flat_AddsToBase()
    {
        var stat = new Stat(13f);
        stat.Add(new Modifier(ModifierKind.Flat, 2f, new object()));

        Assert.That(stat.Value, Is.EqualTo(15f).Within(1e-4f));
        Assert.That(stat.ModifierCount, Is.EqualTo(1));
    }

    [Test]
    public void PercentAdd_SumsAdditively()
    {
        var stat = new Stat(10f);
        var node = new object();
        var pact = new object();

        stat.Add(new Modifier(ModifierKind.PercentAdd, 0.15f, node));
        stat.Add(new Modifier(ModifierKind.PercentAdd, 0.45f, pact));

        // Pooled into one ×1.60 factor. Applied as two separate factors — ×1.15 × ×1.45 — this
        // would be 16.675, which is the whole difference between PercentAdd and PercentMult.
        Assert.That(stat.Value, Is.EqualTo(16f).Within(1e-4f));
    }

    [Test]
    public void PercentMult_MultipliesEach()
    {
        var stat = new Stat(10f);
        var focus = new object();
        var phase = new object();

        stat.Add(new Modifier(ModifierKind.PercentMult, 0.2f, focus));
        stat.Add(new Modifier(ModifierKind.PercentMult, 0.5f, phase));

        // 10 × 1.2 × 1.5. Pooled the way PercentAdd is, this would be 10 × 1.7 = 17.
        Assert.That(stat.Value, Is.EqualTo(18f).Within(1e-4f));
    }

    [Test]
    public void Order_FlatThenAddThenMult()
    {
        Stat stat = Oathbound();

        // (13 + 2) × 1.60 × 1.20. The order is the rule: percentages applied to the base before
        // the flat bonus would give 13 × 1.6 × 1.2 + 2 = 26.96, and pooling the mult with the
        // adds would give (13 + 2) × 1.8 = 27. Both are close enough to look right in play and
        // wrong by more than the tolerance here.
        Assert.That(stat.Value, Is.EqualTo(28.8f).Within(1e-4f));
    }

    [Test]
    public void SetBase_Invalidates()
    {
        var stat = new Stat(10f);
        stat.Add(new Modifier(ModifierKind.PercentAdd, 0.5f, new object()));

        // Read first, deliberately: it fills the cache, so a Base setter that forgot to
        // invalidate would keep answering 15 and only this ordering would catch it.
        Assert.That(stat.Value, Is.EqualTo(15f).Within(1e-4f));

        stat.Base = 20f;

        Assert.That(stat.Base, Is.EqualTo(20f));
        Assert.That(stat.Value, Is.EqualTo(30f).Within(1e-4f));
    }

    [Test]
    public void RemoveAll_RemovesEverythingFromSource()
    {
        var stat = new Stat(10f);
        var node = new object();
        var pact = new object();

        stat.Add(new Modifier(ModifierKind.Flat, 2f, node));
        stat.Add(new Modifier(ModifierKind.PercentAdd, 0.15f, node));
        stat.Add(new Modifier(ModifierKind.Flat, 5f, pact));

        Assert.That(stat.RemoveAll(node), Is.EqualTo(2));
        Assert.That(stat.ModifierCount, Is.EqualTo(1));
        Assert.That(stat.Value, Is.EqualTo(15f).Within(1e-4f));

        // What survived is the other source's modifier, unchanged — not merely "one thing".
        var remaining = new List<Modifier>();
        stat.CopyModifiersTo(remaining);

        Assert.That(remaining.Count, Is.EqualTo(1));
        Assert.That(remaining[0].Source, Is.SameAs(pact));
        Assert.That(remaining[0].Kind, Is.EqualTo(ModifierKind.Flat));
        Assert.That(remaining[0].Value, Is.EqualTo(5f));
    }

    [Test]
    public void RemoveAll_UnknownSource_ReturnsZero_NoEvent()
    {
        var stat = new Stat(10f);
        stat.Add(new Modifier(ModifierKind.Flat, 2f, new object()));

        int fired = 0;
        stat.Changed += _ => fired++;

        Assert.That(stat.RemoveAll(new object()), Is.EqualTo(0));
        Assert.That(fired, Is.EqualTo(0));

        // Nothing of anyone else's went either.
        Assert.That(stat.ModifierCount, Is.EqualTo(1));
        Assert.That(stat.Value, Is.EqualTo(12f).Within(1e-4f));
    }

    [Test]
    public void NullSource_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new Modifier(ModifierKind.Flat, 1f, null));

        var stat = new Stat(10f);

        // Beyond the spec's row, and the reason the constructor's guard is not the last word: a
        // struct always has a zeroed form, and default(Modifier) carries a null source past it.
        // ArgumentException, not ArgumentNullException — the argument is a struct that is
        // present but unusable, and Assert.Throws matches the type exactly.
        Assert.Throws<ArgumentException>(() => stat.Add(default));

        // The other doors a null can arrive at. RemoveAll(null) would otherwise answer "nothing
        // of yours here" — no stored modifier has a null source — for what is really a caller
        // holding the wrong reference.
        Assert.Throws<ArgumentNullException>(() => stat.RemoveAll(null));
        Assert.Throws<ArgumentNullException>(() => stat.CopyModifiersTo(null));
        Assert.Throws<ArgumentNullException>(() => stat.Describe(null));

        Assert.That(stat.ModifierCount, Is.EqualTo(0), "Nothing was stored by a rejected call.");
    }

    [Test]
    public void Changed_FiresOnlyWhenValueChanges()
    {
        var stat = new Stat(10f);
        int fired = 0;
        Stat reported = null;

        stat.Changed += s =>
        {
            fired++;
            reported = s;
        };

        stat.Add(new Modifier(ModifierKind.Flat, 0f, new object()));

        // The modifier is on the stack — it is a real change to the stat's contents, and a
        // debug panel listing modifiers would want it — but the number nobody derives anything
        // from did not move, so nothing that only cares about the number is woken.
        Assert.That(fired, Is.EqualTo(0));
        Assert.That(stat.ModifierCount, Is.EqualTo(1));

        stat.Add(new Modifier(ModifierKind.Flat, 1f, new object()));

        Assert.That(fired, Is.EqualTo(1));
        Assert.That(reported, Is.SameAs(stat), "The event carries the stat that changed.");

        // Setting Base to what it already is is the same claim from the other door.
        stat.Base = 10f;
        Assert.That(fired, Is.EqualTo(1));

        stat.Base = 11f;
        Assert.That(fired, Is.EqualTo(2));
    }

    [Test]
    public void PercentMultMinusOne_YieldsZero()
    {
        var stat = new Stat(10f);
        stat.Add(new Modifier(ModifierKind.Flat, 5f, new object()));
        stat.Add(new Modifier(ModifierKind.PercentMult, -1f, new object()));

        // Rule 6: a −1 multiplier is a legitimate way to say "this number is now zero", and
        // nothing here guards against a stack going further than that. Callers clamp where the
        // meaning of the number demands it.
        Assert.That(stat.Value, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void Describe_Format()
    {
        Stat stat = Oathbound();
        var sb = new StringBuilder();

        stat.Describe(sb);

        string expected = $"28.80 = (13.00 + 2.00) {Times} 1.60 {Times} 1.20";
        Assert.That(sb.ToString(), Is.EqualTo(expected));

        // Formatted invariantly, not in the device's culture: a stat described in a bug report
        // from a phone set to German must read the same as one from this machine. The culture is
        // built by hand rather than looked up by name, because a culture lookup needs ICU data
        // that not every runtime this suite might run on carries.
        CultureInfo previous = CultureInfo.CurrentCulture;
        var commaDecimal = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        commaDecimal.NumberFormat.NumberDecimalSeparator = ",";

        try
        {
            CultureInfo.CurrentCulture = commaDecimal;

            var underCulture = new StringBuilder();
            stat.Describe(underCulture);

            Assert.That(underCulture.ToString(), Is.EqualTo(expected));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        // Appends, so a panel can gather several stats into one buffer, and the fixed shape
        // still shows the base and the flat sum when there is nothing on the stack at all.
        var bare = new StringBuilder("damage: ");
        new Stat(13f).Describe(bare);

        Assert.That(bare.ToString(), Is.EqualTo($"damage: 13.00 = (13.00 + 0.00) {Times} 1.00"));
    }

    [Test]
    public void ValueRead_AllocatesNothing()
    {
        Stat stat = Oathbound();

        // The cached path: what a hot loop reading a stat per frame actually does.
        AllocationAssert.None(() => _sink = stat.Value);

        // And the recompute path, invalidated on every one of the thousand iterations rather
        // than once, so the walk over the modifier list is what is being measured. A foreach
        // over a List<T> is allocation-free today; an "obvious" rewrite to LINQ or to an
        // IEnumerable<Modifier> field would not be, and this is what would catch it.
        AllocationAssert.None(
            () =>
            {
                stat.Base = 13f;
                _sink = stat.Value;
            },
            1_000);

        Assert.That(_sink, Is.EqualTo(28.8f).Within(1e-4f), "Sanity: the reads returned the value.");
    }

    [Test]
    public void NonFiniteValue_Throws()
    {
        // Beyond the spec's table. Rule 6 leaves negative and zero results to the caller, but a
        // non-finite one is a different animal: it does not merely produce a silly number, it
        // silences Changed. The event asks "did the value move?", every comparison against NaN
        // is false, and a stat whose value is NaN would therefore report itself as unchanged
        // forever — a HUD that quietly stops updating rather than showing something wrong.
        var source = new object();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Modifier(ModifierKind.Flat, float.NaN, source));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Modifier(ModifierKind.PercentAdd, float.PositiveInfinity, source));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Modifier(ModifierKind.PercentMult, float.NegativeInfinity, source));

        // Both of the stat's own doors, for the same reason.
        Assert.Throws<ArgumentOutOfRangeException>(() => new Stat(float.NaN));

        var stat = new Stat(10f);
        Assert.Throws<ArgumentOutOfRangeException>(() => stat.Base = float.NaN);

        // Rejected before anything moved, so a caught exception leaves a usable stat.
        Assert.That(stat.Base, Is.EqualTo(10f));
        Assert.That(stat.Value, Is.EqualTo(10f));
    }

    [Test]
    public void UndefinedKind_Throws()
    {
        // Also beyond the spec's table. An enum is not a closed set at runtime — a cast makes
        // any int one — and a kind the arithmetic does not handle would be counted by
        // ModifierCount and listed by Describe while changing nothing, which is the most
        // expensive kind of silence. Refused where it is cheapest to refuse.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Modifier((ModifierKind)7, 1f, new object()));
    }

    /// <summary>
    /// Base 13 with one of each kind on it: +2 flat, +15 % and +45 % pooled, ×1.2 on top. The
    /// spec's <c>Describe</c> example, and the stack the order rule is written against.
    /// </summary>
    private static Stat Oathbound()
    {
        var stat = new Stat(13f);
        var node = new object();
        var pact = new object();
        var focus = new object();

        stat.Add(new Modifier(ModifierKind.Flat, 2f, node));
        stat.Add(new Modifier(ModifierKind.PercentAdd, 0.15f, node));
        stat.Add(new Modifier(ModifierKind.PercentAdd, 0.45f, pact));
        stat.Add(new Modifier(ModifierKind.PercentMult, 0.2f, focus));

        return stat;
    }
}
