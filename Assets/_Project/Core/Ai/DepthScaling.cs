using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;

namespace Soulvail.Core.Ai;

/// <summary>
/// Makes an enemy as tough, as dangerous and as fast as its depth says it should be: GD §12.3's
/// h(n), d(n) and s(n) applied to one agent as three modifiers from one source.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes the curves, not a <see cref="Soulvail.Core.Director.ThreatBudget"/>.</b> Scaling an
/// enemy needs h, d and s and knows nothing about how many of them a phone can draw — the device
/// cap is chosen in M2-04, where the arithmetic that prices it lives, and no
/// <c>ThreatBudget</c> is constructed in a run until then.
/// </para>
/// <para>
/// <b>Modifiers rather than a multiplier the behaviour applies.</b> This is the architecture's rule
/// — every gameplay number is a <see cref="Stat"/> with a modifier stack (ADR-0008) — and the
/// alternative has no answer for M7-02's Hasted affix (+45 % move speed) except a second
/// mechanism that the first would have to be taught about. <see cref="ModifierKind.PercentMult"/>
/// and not <see cref="ModifierKind.PercentAdd"/>, so depth *multiplies* with an Elite's 2.2×
/// rather than pooling with it (GD §8.3).
/// </para>
/// <para>
/// <b>One instance per run, from <c>RunSession.Start</c>, and its own identity is the source
/// token.</b> Every modifier it applies is removable by handing this object to
/// <see cref="Stat.RemoveAll(object)"/> — which nothing does, deliberately: a recycled agent is
/// wiped by <c>EnemyAgent.Initialise</c> with the no-argument overload instead, because a list of
/// sources to remove at the recycle point is the kind of list that ends up one entry short
/// (ledger row 2).
/// </para>
/// </remarks>
public sealed class DepthScaling
{
    private readonly ScalingSpec _scaling;

    /// <param name="scaling">The mode's curves — <see cref="ModeSpec.Scaling"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scaling"/> is null.</exception>
    public DepthScaling(ScalingSpec scaling)
    {
        _scaling = scaling ?? throw new ArgumentNullException(nameof(scaling));
    }

    /// <summary>
    /// Scales <paramref name="agent"/> to <paramref name="stage"/>: one
    /// <see cref="ModifierKind.PercentMult"/> on each of its maximum hit points, its contact
    /// damage and its move speed, then a refill so it arrives at the scaled maximum.
    /// </summary>
    /// <param name="agent">The enemy to scale. Freshly spawned in every caller today.</param>
    /// <param name="stage">The depth it is spawning at. Stages are numbered from 1.</param>
    /// <exception cref="ArgumentNullException"><paramref name="agent"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="stage"/> is below 1.</exception>
    /// <remarks>
    /// <para>
    /// <b>The refill is not optional, and its reason is <c>EnemyAgent.Initialise</c>'s</b>
    /// (AR §18.1): <c>Health.Reset</c> fills <c>Current</c> from <c>MaxHp.Value</c>, so a maximum
    /// raised afterwards leaves a stage-20 Husk standing at stage-1 hit points with a part-filled
    /// bar and nothing reporting it. The order there is base-then-reset; the order here is
    /// modifier-then-reset, for the same arithmetic.
    /// </para>
    /// <para>
    /// The three modifiers go on unconditionally, including at stage 1 where all three are +0 %.
    /// A branch would make the stack's contents depend on the depth, and something reading "how
    /// many modifiers is this Husk wearing" would then get a different answer at stage 1 than at
    /// stage 2 for a reason that is not about the enemy.
    /// </para>
    /// <para>
    /// The stage is refused by the curves rather than re-guarded here, so there is one message
    /// about what a legal stage is and it names the number the caller passed.
    /// </para>
    /// </remarks>
    public void Apply(EnemyAgent agent, int stage)
    {
        if (agent is null)
        {
            throw new ArgumentNullException(nameof(agent));
        }

        // Every curve is evaluated before anything is mutated, so a stage the curves refuse leaves
        // the agent exactly as it was rather than wearing one modifier out of three — the same
        // validate-then-assign order RunSession.Start takes with a config.
        float hp = _scaling.Hp.At(stage);
        float damage = _scaling.Damage.At(stage);
        float speed = _scaling.Speed.At(stage);

        // `multiplier − 1` because a PercentMult of 0.2 means ×1.2 (see Modifier): Stat.Value
        // applies Π(1 + PercentMult), so handing it h(n) − 1 yields exactly h(n).
        agent.Health.MaxHp.Add(new Modifier(ModifierKind.PercentMult, hp - 1f, this));
        agent.ContactDamage.Add(new Modifier(ModifierKind.PercentMult, damage - 1f, this));
        agent.MoveSpeed.Add(new Modifier(ModifierKind.PercentMult, speed - 1f, this));

        // Last, and after the maximum has moved. Health.Reset also clears i-frames and the
        // external invulnerability flag, which is correct for the spawn this is always part of and
        // is what EnemyAgent.Initialise had just done a moment earlier anyway.
        agent.Health.Reset();
    }
}
