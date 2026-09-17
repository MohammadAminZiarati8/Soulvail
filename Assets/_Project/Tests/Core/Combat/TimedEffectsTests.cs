using System;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Effects;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Combat;

/// <summary>
/// <c>TimedEffects</c>: that a held effect comes off at its own second and not before, that the
/// earliest deadline goes first, that a refresh is not a second note, and that the walk costs
/// nothing.
/// </summary>
/// <remarks>
/// <para>
/// Written against fixture effects and a recording handler rather than against <c>GrantShield</c>,
/// deliberately — <c>EffectRegistryTests</c>' shape and its reason. This class's whole claim is that
/// it knows nothing about any particular primitive, and a row that proved expiry using the one timed
/// primitive that exists would be proving it about Bulwark instead. <c>GrantShieldTests</c> is where
/// the shipped pair is tested.
/// </para>
/// <para>
/// <b><c>Timed_CapacityThrows</c> is the row that had to be written this way rather than the
/// obvious way.</b> Sixteen is this class's ceiling and eight is <c>GrantedShieldPool</c>'s, and they
/// do not match on purpose: one limits every timed effect in the game at once, the other limits
/// distinct sources of one mechanic. Built on <c>GrantShieldHandler</c> the row would throw out of
/// <c>Health</c> at the <em>ninth</em> hold and pass while testing the wrong refusal.
/// </para>
/// <para>
/// The frame-order rows are not here. They need a whole <c>RunSession</c> with a Spitter in the
/// arena, and they live beside the Bulwark rows that share that fixture.
/// </para>
/// </remarks>
[TestFixture]
public sealed class TimedEffectsTests
{
    private EffectRegistry _registry;
    private Recording<Marker> _handler;
    private TimedEffects _timed;

    [SetUp]
    public void SetUp()
    {
        _registry = new EffectRegistry();
        _handler = new Recording<Marker>();
        _timed = new TimedEffects(_registry);

        _registry.Register<Marker>(_handler);
    }

    // ---- Holding and expiring (rules 1, 2) -------------------------------------------------------

    [Test]
    public void Timed_HoldsAndExpires()
    {
        var effect = new Marker();
        var source = new object();

        _timed.Hold(effect, source, expiresAt: 5f);

        Assert.That(_timed.Count, Is.EqualTo(1));

        // A tenth of a second short is still held. The comparison is against an absolute deadline
        // rather than an accumulator, so this is the same claim at 30 fps and at 120.
        _timed.Tick(4.9f);

        Assert.That(_handler.Removed, Is.Empty, "Nothing comes off before its second.");
        Assert.That(_timed.Count, Is.EqualTo(1));

        _timed.Tick(5f);

        Assert.That(_handler.Removed, Has.Count.EqualTo(1), "And exactly once on the second itself.");
        Assert.That(_handler.Removed[0].Effect, Is.SameAs(effect));
        Assert.That(_handler.Removed[0].Source, Is.SameAs(source));
        Assert.That(_timed.Count, Is.Zero, "And it is no longer held.");

        // Ticking past a deadline that has already been honoured removes nothing a second time,
        // which is the half a `while` loop over a table it is also mutating could get wrong.
        _timed.Tick(600f);

        Assert.That(_handler.Removed, Has.Count.EqualTo(1));
    }

    [Test]
    public void Timed_AppliesNothing()
    {
        // Rule 1, and the whole of the split: the caster applied before this class ever heard about
        // the effect, so a Hold that also applied would double every cast in the game.
        _timed.Hold(new Marker(), new object(), expiresAt: 5f);

        Assert.That(_handler.Applied, Is.Zero, "Hold applies nothing — the caster already did.");

        _timed.Tick(9f);

        Assert.That(_handler.Applied, Is.Zero, "And neither does expiry.");
        Assert.That(_handler.Removed, Has.Count.EqualTo(1));
    }

    [Test]
    public void Timed_ExpiresOldestFirst()
    {
        // Held in the *opposite* order to the one they come off in, so the row cannot pass against
        // an implementation that simply walks the table in insertion order.
        var a = new Marker();
        var b = new Marker();

        _timed.Hold(a, "A", expiresAt: 5f);
        _timed.Hold(b, "B", expiresAt: 3f);

        _timed.Tick(6f);

        Assert.That(_handler.Removed, Has.Count.EqualTo(2), "Both were due.");
        Assert.That(_handler.Removed[0].Effect, Is.SameAs(b), "The earlier deadline goes first.");
        Assert.That(_handler.Removed[1].Effect, Is.SameAs(a));

        Assert.That(
            _timed.Count,
            Is.Zero,
            "And the table is empty rather than holding whatever the loop stepped over.");
    }

    [Test]
    public void Timed_HoldRefreshesRatherThanStacking()
    {
        // **The rule the parent spec did not state, and the one a recast depends on.**
        // GrantedShieldPool.Grant *sets* a source's contribution instead of adding to it (its
        // rule 6), so a second note against the same pair would come due on the *first* cast's clock
        // and take the refreshed shield back early — a Bulwark recast that made the player *less*
        // protected than not recasting at all.
        var effect = new Marker();
        var source = new object();

        _timed.Hold(effect, source, expiresAt: 5f);
        _timed.Hold(effect, source, expiresAt: 9f);

        Assert.That(_timed.Count, Is.EqualTo(1), "One note, not two.");

        _timed.Tick(5f);

        Assert.That(
            _handler.Removed,
            Is.Empty,
            "The first cast's deadline went with it — this is the whole of the rule.");

        _timed.Tick(9f);

        Assert.That(_handler.Removed, Has.Count.EqualTo(1), "And the refreshed one is honoured.");

        // The same effect from a *different* source is a different holding and stacks, because two
        // sources are two things — GrantedShieldPool's rule again, one layer up.
        _timed.Hold(effect, "first", expiresAt: 20f);
        _timed.Hold(effect, "second", expiresAt: 21f);

        Assert.That(_timed.Count, Is.EqualTo(2));
    }

    [Test]
    public void Timed_CapacityThrows()
    {
        // Sixteen distinct sources holding one effect — see this fixture's remarks for why this row
        // must not be built on GrantShieldHandler, whose pool refuses a *ninth* source.
        var effect = new Marker();

        for (int i = 0; i < TimedEffects.Capacity; i++)
        {
            _timed.Hold(effect, new object(), expiresAt: 5f);
        }

        Assert.That(_timed.Count, Is.EqualTo(TimedEffects.Capacity));

        var thrown = Assert.Throws<InvalidOperationException>(
            () => _timed.Hold(effect, new object(), expiresAt: 5f));

        Assert.That(
            thrown.Message,
            Does.Contain(TimedEffects.Capacity.ToString()),
            "The refusal names the number, or nobody reading the Console knows what to raise.");

        // A refusal, not a budget: the table is unchanged and the seventeenth simply did not happen.
        Assert.That(_timed.Count, Is.EqualTo(TimedEffects.Capacity));

        // And a *refresh* at capacity is still legal, which is the branch the throw must not swallow:
        // a recast when sixteen things are running is not a seventeenth effect.
        var held = new object();

        _timed.Clear();

        for (int i = 0; i < TimedEffects.Capacity - 1; i++)
        {
            _timed.Hold(effect, new object(), expiresAt: 5f);
        }

        _timed.Hold(effect, held, expiresAt: 5f);

        Assert.That(() => _timed.Hold(effect, held, expiresAt: 8f), Throws.Nothing);
        Assert.That(_timed.Count, Is.EqualTo(TimedEffects.Capacity));
    }

    // ---- Leaving early, and the run's end (rules 4, 8) -------------------------------------------

    [Test]
    public void Timed_ReleaseTakesOneBackEarly()
    {
        var effect = new Marker();
        var source = new object();

        _timed.Hold(effect, source, expiresAt: 5f);

        Assert.That(_timed.Release(effect, source), Is.True);

        Assert.That(_handler.Removed, Has.Count.EqualTo(1), "It went back through the registry.");
        Assert.That(_handler.Removed[0].Source, Is.SameAs(source));
        Assert.That(_timed.Count, Is.Zero);

        // And the deadline it no longer has cannot come round: a Release that forgot to drop the
        // entry would remove the same effect a second time, which for a shield is a second expiry
        // event for a grant that already ended.
        _timed.Tick(6f);

        Assert.That(_handler.Removed, Has.Count.EqualTo(1));
    }

    [Test]
    public void Timed_ReleaseUnknown_IsFalse()
    {
        // Stat.RemoveAll's contract, two layers up: a source holding nothing is not an error, so a
        // caller may clean up unconditionally.
        Assert.That(_timed.Release(new Marker(), new object()), Is.False);
        Assert.That(_handler.Removed, Is.Empty, "And nothing was asked of the registry.");

        // Nor does a pair half of which is held answer true. The key is the pair, not either end.
        var effect = new Marker();
        var source = new object();

        _timed.Hold(effect, source, expiresAt: 5f);

        Assert.That(_timed.Release(effect, new object()), Is.False);
        Assert.That(_timed.Release(new Marker(), source), Is.False);
        Assert.That(_timed.Count, Is.EqualTo(1), "And the real one is untouched.");
    }

    [Test]
    public void Timed_ClearForgetsWithoutRemoving()
    {
        _timed.Hold(new Marker(), "A", expiresAt: 5f);
        _timed.Hold(new Marker(), "B", expiresAt: 7f);

        _timed.Clear();

        Assert.That(_timed.Count, Is.Zero);

        Assert.That(
            _handler.Removed,
            Is.Empty,
            "Rule 8: the run is over, there is nothing left to take the effect off, and an expiry "
                + "announced here would reach a view being destroyed.");

        // And nothing is left behind to come due into the next run, which is the failure a Clear
        // that only zeroed the count would ship.
        _timed.Tick(600f);

        Assert.That(_handler.Removed, Is.Empty);
        Assert.That(_timed.Count, Is.Zero);
    }

    // ---- The per-frame cost (rule 2) -------------------------------------------------------------

    [Test]
    public void Timed_AllocatesNothing()
    {
        // Eight held and none of them due, which is what a frame of a fight with a shield up
        // actually looks like. Ledger row 4's per-frame GC.Alloc question, asked the only way an
        // Editor can ask it (Traps §7 — never hand-roll the probe).
        var effect = new Marker();

        for (int i = 0; i < 8; i++)
        {
            _timed.Hold(effect, new object(), expiresAt: 100f + i);
        }

        AllocationAssert.None(() => _timed.Tick(5f));

        Assert.That(_timed.Count, Is.EqualTo(8), "The probe measured a full table, not an empty one.");
    }

    // ---- The implied guards ----------------------------------------------------------------------

    [Test]
    public void Timed_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => new TimedEffects(null));

        var effect = new Marker();
        var source = new object();

        Assert.Throws<ArgumentNullException>(() => _timed.Hold(null, source, 1f));
        Assert.Throws<ArgumentNullException>(() => _timed.Hold(effect, null, 1f));

        // The one float door. A NaN deadline is the dangerous one and the natural spelling admits
        // it: Tick asks `now >= expiresAt`, every comparison against NaN is false, and an effect
        // that is never due is a shield that lasts the run (AR §18.3).
        Assert.Throws<ArgumentOutOfRangeException>(() => _timed.Hold(effect, source, float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _timed.Hold(effect, source, float.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _timed.Hold(effect, source, float.NegativeInfinity));

        Assert.Throws<ArgumentNullException>(() => _timed.Release(null, source));
        Assert.Throws<ArgumentNullException>(() => _timed.Release(effect, null));

        // A deadline already behind the clock is legal, not a guard: an effect whose duration is
        // shorter than a frame is authored content, and it comes off on the next tick.
        _timed.Hold(effect, source, expiresAt: -4f);

        _timed.Tick(0f);

        Assert.That(_handler.Removed, Has.Count.EqualTo(1));
    }

    /// <summary>An effect that means nothing, which is all a clock needs it to mean.</summary>
    private sealed class Marker : IEffect
    {
    }

    /// <summary>Records every pair that reached it, in the order it arrived.</summary>
    /// <remarks>
    /// A count would not do: two of these rows are about which of two effects came off
    /// <em>first</em>, and a counter answers only that both did.
    /// </remarks>
    private sealed class Recording<TEffect> : IEffectHandler<TEffect>
        where TEffect : IEffect
    {
        internal int Applied { get; private set; }

        internal List<(IEffect Effect, object Source)> Removed { get; } =
            new List<(IEffect, object)>();

        public void Apply(TEffect effect, object source)
        {
            Applied++;
        }

        public void Remove(TEffect effect, object source)
        {
            Removed.Add((effect, source));
        }
    }
}
