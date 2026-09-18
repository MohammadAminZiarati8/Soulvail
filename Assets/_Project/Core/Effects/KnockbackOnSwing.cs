using System;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

// The fifth primitive, and the first whose result is a *rule the swing did not have* rather than a
// number the player already carried. One file per pair, like every primitive before it.

/// <summary>
/// <em>"Your swing knocks enemies back 1.5 m."</em> One distance, and a thing the cone did not do.
/// </summary>
/// <remarks>
/// <para>
/// <b>A primitive rather than a <see cref="PlayerStat"/> member, even though the implementation
/// is a stat</b> (rule 6). <see cref="PlayerStat"/>'s contract is <em>"every player number that
/// exists as a <c>Stat</c> in code"</em>, addressed for <see cref="ModifyStat"/> — and a designer
/// authoring <em>"your swing knocks back"</em> should not have to know that the implementation is a
/// zero-based stat, pick the right <see cref="ModifierKind"/>, and get <see cref="ModifierKind.Flat"/>
/// rather than <see cref="ModifierKind.PercentAdd"/> right. <b>A percentage on a base of zero is
/// zero, silently and for ever</b>, which is the trap M3-12a measured on
/// <c>PlayerCombat.HealPerKill</c> and wrote into three places. One number and no kind makes the
/// wrong authoring impossible: <see cref="KnockbackOnSwingHandler"/> chooses the only kind that can
/// work. <see cref="ModifyStat"/> remains the tool for numbers the player already has; this is the
/// tool for a rule they did not.
/// </para>
/// <para>
/// <b>It stacks additively, and that falls out of the kind rather than being arranged.</b> Two nodes
/// granting 1.5 m are two <see cref="ModifierKind.Flat"/> modifiers on one stat, which is 3 m by
/// <c>Stat</c>'s own arithmetic. Nothing here counts nodes.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1).
/// </para>
/// </remarks>
public sealed class KnockbackOnSwing : IEffect
{
    /// <param name="distance">
    /// How far each enemy the swing hits is shoved, in metres. CC §5's Charge is 4 m for comparison,
    /// and a swing node is deliberately a fraction of that — the dash is a positioning tool and this
    /// is a beat of breathing room.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="distance"/> is not a finite number greater than zero. <b>Zero is refused as
    /// well as negative</b>, unlike <c>EnemyKnockbackIntent.Distance</c> where it means a hit that
    /// moved nothing: a node granting no shove at all is a node the player takes and gets nothing
    /// for, which is the mistake this door exists to catch while it is still an asset. A negative
    /// one is a pull, and nothing in the design has ever asked for one.
    /// </exception>
    public KnockbackOnSwing(float distance)
    {
        // !(distance > 0f) rather than distance <= 0f so NaN is refused too, and infinity asked
        // about separately because it passes a > 0 test (AR §18.3). ActiveSpec's spelling and
        // CooldownRules'.
        if (!(distance > 0f) || float.IsInfinity(distance))
        {
            throw new ArgumentOutOfRangeException(
                nameof(distance),
                distance,
                "KnockbackOnSwing distance must be a finite number greater than zero. Zero is a "
                    + "node that shoves nobody and a negative one is a pull.");
        }

        Distance = distance;
    }

    /// <summary>How far each enemy the swing hits is shoved, in metres.</summary>
    public float Distance { get; }
}

/// <summary>
/// What a <see cref="KnockbackOnSwing"/> means to this run: one <see cref="ModifierKind.Flat"/>
/// modifier on <c>PlayerCombat.SwingKnockback</c>, which is the number
/// <c>PlayerCombat.ResolveConeHits</c> reads once per swing.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="ModifierKind.Flat"/> is chosen here and is not authorable</b>, which is the whole
/// of rule 6 in one line. <c>SwingKnockback</c> has a base of zero and <c>Stat</c> computes
/// <c>(Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>, so every other kind multiplies
/// into zero — a node that would heal nothing, shove nobody, and report nothing anywhere. The
/// primitive carries one number precisely so this decision cannot be got wrong in an Inspector.
/// </para>
/// <para>
/// <b>The machinery it switches on already existed.</b> <c>EnemyKnockbackIntent</c> has been how
/// core shoves an enemy since M1-15 and <c>RunTicker.ApplyKnockbacks</c> already applies it; the
/// Charge has been using both all along. What this adds is a second caller, which is exactly what
/// that struct's own remarks predicted — <em>"the day something else pushes an enemy it reuses this
/// rather than growing a parallel one"</em>.
/// </para>
/// <para>
/// Allocates nothing: the <see cref="Modifier"/> is a struct handed to <c>Stat.Add</c> by
/// <see langword="in"/>.
/// </para>
/// </remarks>
public sealed class KnockbackOnSwingHandler : IEffectHandler<KnockbackOnSwing>
{
    private readonly PlayerCombat _combat;

    /// <param name="combat">
    /// The player whose swing this changes — held for its <c>SwingKnockback</c> alone, which is the
    /// same narrowness <see cref="ModifyStatHandler"/> gets from a <c>PlayerStats</c>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="combat"/> is null.</exception>
    public KnockbackOnSwingHandler(PlayerCombat combat)
    {
        _combat = combat ?? throw new ArgumentNullException(nameof(combat));
    }

    /// <summary>
    /// Lifts <c>SwingKnockback</c> off zero by <paramref name="effect"/>'s distance, on behalf of
    /// <paramref name="source"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is — the latter from
    /// <see cref="Modifier"/>, which refuses a modifier nothing could ever take back.
    /// </exception>
    public void Apply(KnockbackOnSwing effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _combat.SwingKnockback.Add(new Modifier(ModifierKind.Flat, effect.Distance, source));
    }

    /// <summary>
    /// Takes every modifier <paramref name="source"/> put on <c>SwingKnockback</c> back off it, and
    /// the swing stops shoving the moment the stack reaches zero.
    /// </summary>
    /// <remarks>
    /// A source with nothing on the stat is not an error and changes nothing, which is
    /// <c>Stat.RemoveAll</c>'s contract and what lets a caller clean up unconditionally.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    public void Remove(KnockbackOnSwing effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _combat.SwingKnockback.RemoveAll(source);
    }
}
