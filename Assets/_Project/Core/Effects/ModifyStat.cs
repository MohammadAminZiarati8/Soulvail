using System;
using Soulvail.Core.Combat;

namespace Soulvail.Core.Effects;

// The first effect primitive, and its handler. One file per pair: a primitive is the data and the
// run-side answer to it, and reading either one without the other tells you half of what it does.
// Every later primitive — OnKillTrigger, Heal, SpawnZone, ApplyStatus, ChainDamage — is this shape
// again, plus one Register line in `RunSession.Start`.

/// <summary>
/// One <see cref="Modifier"/> on one player <c>Stat</c>, as data: which number, which stack
/// position, how much. The primitive nearly every passive in the game is made of.
/// </summary>
/// <remarks>
/// <para>
/// <b>One modifier, not one node.</b> A node granting "+2 damage and +15 %" carries two of these,
/// which is <c>Stat.Add</c>'s own remark — the arithmetic already knows how to pool two
/// contributions from one source, and an effect that carried a list would be inventing a second
/// way to say what the stack says.
/// </para>
/// <para>
/// <b><see cref="ModifierKind.PercentAdd"/> is the kind a node reaches for</b>, and
/// <see cref="ModifierKind.PercentMult"/> is reserved for the loud multipliers — Focus at full
/// ramp, a boss phase, depth scaling, the Claiming — which is <c>ModifierKind</c>'s own remark and
/// GD §13.1's "additively within a family, multiplicatively across". The kind is data and the
/// author's choice; nothing is refused on it here, and M3-12 owes one line per node saying which
/// it used and why.
/// </para>
/// <para>
/// Immutable and shared across every run, like every effect (AR §10.1). The address is not
/// validated here on purpose: <see cref="PlayerStats.Resolve"/> is the one site that knows the full
/// set of live stats, and it is loud, which is <c>MovementSkillKind</c>'s argument exactly.
/// </para>
/// </remarks>
public sealed class ModifyStat : IEffect
{
    /// <param name="stat">Which player number this moves.</param>
    /// <param name="kind">
    /// Which of the three stack positions the modifier occupies. See the remarks on this class for
    /// which one a node should want.
    /// </param>
    /// <param name="value">
    /// The amount, read according to <paramref name="kind"/>: units for
    /// <see cref="ModifierKind.Flat"/>, a fraction for either percentage kind — 0.15 is +15 %.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="kind"/> is not one of the three, or <paramref name="value"/> is NaN or
    /// infinite. Both are refused at this door as well as at <see cref="Modifier"/>'s, because this
    /// is where a mistake is <em>authored</em> — a content error caught when the asset is built is
    /// a content error, and the same error caught at the moment a player picks the node is a crash
    /// in a run (AR §18.3).
    /// </exception>
    public ModifyStat(PlayerStat stat, ModifierKind kind, float value)
    {
        if (kind is not (ModifierKind.Flat or ModifierKind.PercentAdd or ModifierKind.PercentMult))
        {
            throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                "Modifier kind must be one of Flat, PercentAdd or PercentMult.");
        }

        // Asked as "is it NaN or infinite?" rather than as a range comparison, for the reason
        // AR §18.3 gives: every comparison against NaN is false, so a range check waves it
        // straight through — and a NaN that reached a Stat would silence its Changed event rather
        // than produce a wrong number.
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "ModifyStat value must be finite.");
        }

        Stat = stat;
        Kind = kind;
        Value = value;
    }

    /// <summary>Which player number this moves.</summary>
    public PlayerStat Stat { get; }

    /// <summary>Which stack position the modifier occupies.</summary>
    public ModifierKind Kind { get; }

    /// <summary>The amount, read according to <see cref="Kind"/>.</summary>
    public float Value { get; }
}

/// <summary>
/// What a <see cref="ModifyStat"/> means to this run: one <see cref="Modifier"/> added to, or a
/// source taken back off, the live <c>Stat</c> the effect addresses.
/// </summary>
/// <remarks>
/// <para>
/// The run-side half of the split. It holds this run's <see cref="PlayerStats"/> — the effect
/// holds only an address — so one shared, immutable <see cref="ModifyStat"/> is applied to whoever
/// happens to be playing.
/// </para>
/// <para>
/// <b><see cref="Remove"/> takes the source back, not the effect</b>, and that is deliberate rather
/// than a simplification. <c>Stat.RemoveAll(source)</c> is the only removal the modifier stack
/// offers, and it is the right one: a node granting "+2 damage and +15 %" is two effects from one
/// source, and taking one of them off while leaving the other would be a half-removed node. The
/// consequence to know is that removing <em>either</em> of that node's two effects removes both,
/// and that is right for V1 — nothing removes a node (respec was deleted with v0.1's meta systems,
/// CH §7), and the first timed buff (M3-06, M3-11) owns its own clock and calls this for its own
/// source, which is exactly a source being taken back in full.
/// </para>
/// <para>
/// Allocates nothing: the <see cref="Modifier"/> is a struct handed to <c>Stat.Add</c> by
/// <see langword="in"/>, and <see cref="PlayerStats.Resolve"/> is a jump table.
/// </para>
/// </remarks>
public sealed class ModifyStatHandler : IEffectHandler<ModifyStat>
{
    private readonly PlayerStats _stats;

    /// <param name="stats">Where each address lives this run.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stats"/> is null.</exception>
    public ModifyStatHandler(PlayerStats stats)
    {
        _stats = stats ?? throw new ArgumentNullException(nameof(stats));
    }

    /// <summary>
    /// Adds <paramref name="effect"/>'s modifier to the stat it addresses, tagged with
    /// <paramref name="source"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> is null, or <paramref name="source"/> is — the latter from
    /// <see cref="Modifier"/>, which refuses a modifier nothing could ever take back.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The effect addresses a stat this run has no answer for.
    /// </exception>
    public void Apply(ModifyStat effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _stats.Resolve(effect.Stat).Add(new Modifier(effect.Kind, effect.Value, source));
    }

    /// <summary>
    /// Takes every modifier <paramref name="source"/> put on the stat
    /// <paramref name="effect"/> addresses back off it — see this class's remarks for why that is
    /// more than the one effect.
    /// </summary>
    /// <remarks>
    /// A source with nothing on that stat is not an error and changes nothing, which is
    /// <c>Stat.RemoveAll</c>'s contract and what lets a caller clean up unconditionally.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="effect"/> or <paramref name="source"/> is null.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The effect addresses a stat this run has no answer for.
    /// </exception>
    public void Remove(ModifyStat effect, object source)
    {
        if (effect is null)
        {
            throw new ArgumentNullException(nameof(effect));
        }

        _stats.Resolve(effect.Stat).RemoveAll(source);
    }
}
