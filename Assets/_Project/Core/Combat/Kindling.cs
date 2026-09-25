using System;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;

namespace Soulvail.Core.Combat;

/// <summary>
/// CH §3.3's Kindling: consecutive weapon hits with nothing touching you, as a modifier on weapon
/// damage. The only thing in the game that pays for a perfect stretch rather than for a build.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two edges and nothing on a clock</b> (M6-07a rules 3, 5 and 7). A landed weapon hit adds a
/// stack and damage that arrived drops them all; both are called by <c>PlayerCombat</c>, which is
/// the one object that sees a swing resolve and a hit land. Between the two edges nothing runs, so a
/// run that lands no hits raises nothing.
/// </para>
/// <para>
/// <b>One <see cref="ModifierKind.PercentAdd"/> under this object as its source</b> (rule 3,
/// ADR-0008). Pooled additively, so a full ramp is ×1.60 rather than 1.02³⁰, and
/// <see cref="Stat.RemoveAll(object)"/> takes the whole ramp back without knowing what else was
/// added since. <c>FocusTracker</c>'s arrangement on the fire rate, one stat over.
/// </para>
/// <para>
/// <b>A node that moves <see cref="PerStack"/> is felt on the next hit, not the instant it is
/// taken.</b> The modifier is rewritten when the count moves and never when a stat does, so the
/// stacks standing at a pick keep what they were worth until the next one lands — at most one
/// 0.67 s interval for a class firing 1.5 times a second, and M6-08's to revisit if it ever reads.
/// </para>
/// <para>
/// <b>Nothing here allocates</b> (rule 11): an <see cref="int"/>, a <see cref="float"/>, and one
/// <see cref="Modifier"/> struct written into the stat's pool at most once per landed hit.
/// </para>
/// </remarks>
public sealed class Kindling
{
    private readonly Stat _damage;
    private readonly IDomainEvents _events;

    /// <param name="spec">The class's authored block. Null is refused — build one or do not.</param>
    /// <param name="damage">
    /// <c>Weapon.Damage</c>, the <see cref="Stat"/> the ramp rides. Handed the stat rather than the
    /// weapon, for <c>RisePassive</c>'s reason: the one number this touches is the one it is given.
    /// </param>
    /// <param name="events">Where <see cref="KindlingChanged"/> goes.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public Kindling(KindlingSpec spec, Stat damage, IDomainEvents events)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        _damage = damage ?? throw new ArgumentNullException(nameof(damage));
        _events = events ?? throw new ArgumentNullException(nameof(events));

        PerStack = new Stat(spec.PerStack);
        MaxStacks = new Stat(spec.MaxStacks);
    }

    /// <summary>How many stacks are standing, in <c>[0, MaxStacks.Value]</c>.</summary>
    public int Stacks { get; private set; }

    /// <summary>
    /// What one stack is worth, live. Where M6-08's Ember branch puts <em>"+1 % a stack"</em> —
    /// rule 3.
    /// </summary>
    public Stat PerStack { get; }

    /// <summary>How many count, live. Where Ember puts <em>"+10 stacks"</em>.</summary>
    /// <remarks>
    /// <b>Two <see cref="Stat"/>s rather than two floats, and it is <c>RisePassive.Chance</c>'s
    /// arrangement rather than a new one</b>: an authored number a node is going to move is a
    /// <see cref="Stat"/> seeded from the spec, always (ADR-0008). Both are read through a clamp at
    /// the point of use rather than clamped in the stat, because a <see cref="Stat"/> clamps nothing —
    /// a negative per-stack is a signature that punishes a good stretch and a cap below zero is one
    /// that never starts, so both are floored where they are read.
    /// <b>Neither is addressable until M6-08</b>, which is M3-12a's own sequence: the stat exists here
    /// because the number is authored, and the <c>PlayerStat</c> member arrives with the node that
    /// names it.
    /// </remarks>
    public Stat MaxStacks { get; }

    /// <summary>
    /// What the stacks are worth right now, as a fraction — the value of the one modifier this has on
    /// the damage stat, or 0 when it has none (rule 3).
    /// </summary>
    public float Bonus { get; private set; }

    /// <summary>
    /// One more of the class's weapon hits landed. Adds a stack and rewrites the modifier — rule 5.
    /// </summary>
    /// <remarks>
    /// <b>A full ramp saturates silently</b> (rule 4): a thirty-first hit adds nothing, publishes
    /// nothing and is not an error — it is the ordinary state of a good stretch.
    /// </remarks>
    public void OnWeaponHitLanded()
    {
        int cap = Cap();

        // Min rather than a plain increment, so a cap a stat has moved below the standing count
        // pulls the count down to it on the next hit rather than leaving it stranded above.
        int next = Stacks < cap ? Stacks + 1 : cap;

        if (next == Stacks)
        {
            return;
        }

        Stacks = next;

        Apply();

        _events.Publish(new KindlingChanged(Stacks, cap, Bonus));
    }

    /// <summary>
    /// Damage reached the player. Drops every stack — rule 7.
    /// </summary>
    /// <remarks>
    /// <b>The caller decides what "reached" means</b>, and <c>PlayerCombat.ApplyDamage</c> calls this
    /// only when something was applied: a hit the i-frames ate never gets here, because CC §5's
    /// i-frames exist to make a dodge worth something. Silent when there was nothing to lose, so a
    /// player hit at zero stacks is not news.
    /// </remarks>
    public void OnPlayerDamaged()
    {
        if (Stacks == 0)
        {
            return;
        }

        Stacks = 0;

        Apply();

        _events.Publish(new KindlingChanged(0, Cap(), Bonus));
    }

    /// <summary>Back to cold, silently. For the end of a run — <c>PlayerCombat.Reset</c>'s.</summary>
    /// <remarks>
    /// Publishes nothing: the end of a run is not news (<c>EssenceWallet.Restore</c>'s rule, and
    /// <c>FocusTracker.Reset</c>'s). Takes its modifier off where <c>Weapon.Reset</c> does not, for
    /// Focus's reason — this is the source of it, so it is the one thing entitled to remove it.
    /// </remarks>
    public void Reset()
    {
        Stacks = 0;
        Bonus = 0f;

        _damage.RemoveAll(this);
    }

    /// <summary>
    /// The live cap as a count: <c>max(0, floor(MaxStacks.Value))</c> — rule 4.
    /// </summary>
    /// <remarks>
    /// Spelled as the negated <c>&gt; 0</c> so NaN floors to zero with everything else; +∞ and
    /// anything past <see cref="int.MaxValue"/> saturate there rather than overflowing a cast.
    /// </remarks>
    private int Cap()
    {
        float value = MaxStacks.Value;

        if (!(value > 0f))
        {
            return 0;
        }

        if (value >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return (int)MathF.Floor(value);
    }

    /// <summary>
    /// Rule 3: one <see cref="ModifierKind.PercentAdd"/> of <c>Stacks × max(0, PerStack)</c>, or none.
    /// </summary>
    /// <remarks>
    /// Removed and re-added rather than edited, because a <see cref="Modifier"/> is a readonly
    /// struct — <c>FocusTracker.ApplyModifier</c>'s shape. A per-stack a stat has driven negative,
    /// NaN or infinite reads as zero, so the signature can never punish the stretch it rewards.
    /// </remarks>
    private void Apply()
    {
        _damage.RemoveAll(this);

        float perStack = PerStack.Value;

        if (!(perStack > 0f) || float.IsInfinity(perStack))
        {
            perStack = 0f;
        }

        float bonus = Stacks * perStack;

        Bonus = bonus > 0f && !float.IsInfinity(bonus) ? bonus : 0f;

        if (Bonus > 0f)
        {
            _damage.Add(new Modifier(ModifierKind.PercentAdd, Bonus, this));
        }
    }
}
