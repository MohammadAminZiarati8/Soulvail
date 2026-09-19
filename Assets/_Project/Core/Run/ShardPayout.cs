using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// What a dead run was worth in Soul Shards — GD §14.1, minus the term this build cannot compute.
/// A pure function of the depth reached and the mode's authored boss roster.
/// </summary>
/// <remarks>
/// <para>
/// <b>GD §14.1's formula has three terms and this ships two, which is a ruling rather than an
/// oversight.</b> The formula is
/// <c>10·(deepest stage) + 50·(bosses killed) + 25·(new archetype first encountered)</c>. The third
/// term is a <em>lifetime</em> fact — <em>first</em> encountered, across every run this install has
/// ever played — so it belongs on <c>PlayerProfile</c> as a set of <see cref="ContentId"/>s rather
/// than on a run, and it would drag a collection into the format bump M4-05b is already the review
/// for. <b>Deferring it over-pays a returning player rather than under-paying them</b>: a profile
/// with an empty set pays 25 the first time it meets a Husk, whenever that milestone arrives, which
/// is the opposite of the Shard <em>total</em>, where not writing the number destroys it. And the
/// handover is cheap — <see cref="ModeSpec.TryGetIntroduction"/> already authors which archetype
/// arrives at which stage, so a run's contribution is another walk over <c>[1, deepestStage]</c>
/// and all it needs is the profile-side set. <b>This class is named for what it computes rather
/// than for the formula</b>, so the next reader does not think the term was forgotten.
/// </para>
/// <para>
/// <b>Static, and not a violation of the no-statics rule.</b> <c>SaveMigrations</c> is the shipped
/// precedent and its remarks carry the argument: what ADR-0002 and AR §13 ban is static
/// <em>mutable</em> state and service location — reachable-from-anywhere handles that make
/// composition a lie. This class has no fields to mutate, nothing to reset under a disabled domain
/// reload, and nothing anyone could reach a dependency through. <c>Payout_HoldsNoState</c> is what
/// keeps that true rather than remembered. <b>If it ever needs a collaborator it becomes an
/// injected object that day</b>, and the deviation gets named.
/// </para>
/// <para>
/// <b>Bosses killed is <em>derived</em> rather than counted, and the reason is that it then survives
/// a resume for free.</b> Nothing in this game counts bosses: M4-01b ruled deliberately that there
/// is no <c>BossDied</c>, and <c>EnemyDied</c> carries a <c>SpecId</c> nobody tallies. A counter
/// would have to live on <see cref="RunState"/>, hence in <c>RunSnapshot</c> to survive a
/// kill-from-recents, which is a second save-format bump in the one task that was split away from
/// the format bump. <b>The derivation is exact:</b> a stage is only left once
/// <c>SpawnDirector.IsStageComplete</c> is true, and on a boss stage that property <em>is</em>
/// <c>_bossCleared</c>, set the moment the boss stops breathing — so every boss stage strictly
/// below the current depth has had its boss killed, and <see cref="ModeSpec.TryGetBossFor"/> is
/// what says which stages those were.
/// </para>
/// <para>
/// <b>The one case that under-pays is named rather than hidden:</b> a player who kills the boss and
/// then dies on the same stage before walking through the door is paid for the depth but not for
/// that boss — at most 50 Shards, on a stage they did not finish. The alternative under-pays far
/// worse and far more often (a resumed run losing every boss it killed before the app died), and a
/// fix costs a run-format bump.
/// </para>
/// <para>
/// <b><see cref="PerStage"/> and <see cref="PerBoss"/> are <see langword="const"/>s in core, and
/// that is a knowing breach of ADR-0006</b> — the same breach <c>LevelUpFlow</c>'s Overflow 2 % is,
/// which the ROADMAP's carry-forward row 6 already owns and which records this class as its fourth
/// reader. They are GD §14.1's own numbers rather than a designer's tuning surface, and there is no
/// asset a payout belongs on: a payout is not a mode, a character or an enemy, and inventing one
/// here would be M6's Sanctum arriving early and unspecified.
/// </para>
/// <para>
/// Allocates nothing and is asked once per run, on the frame the player dies.
/// </para>
/// </remarks>
public static class ShardPayout
{
    /// <summary>GD §14.1's first coefficient: Shards per stage of depth reached.</summary>
    public const int PerStage = 10;

    /// <summary>GD §14.1's second coefficient: Shards per boss killed.</summary>
    public const int PerBoss = 50;

    /// <summary>
    /// What the run is worth: <see cref="PerStage"/> per stage of depth plus <see cref="PerBoss"/>
    /// per boss killed.
    /// </summary>
    /// <param name="deepestStage">
    /// <c>RunState.StageIndex</c> at the moment of death. Below 1 is refused — that is the value
    /// <c>default(RunSnapshot)</c> carries and <c>RunSnapshot</c>'s own constructor already refuses,
    /// which is why the guard is cheap: a run cannot reach this with one.
    /// </param>
    /// <param name="mode">The run's mode, for its authored boss roster. Null is refused.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mode"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deepestStage"/> is below 1.</exception>
    public static int For(int deepestStage, ModeSpec mode)
    {
        Require(deepestStage, mode);

        return (PerStage * deepestStage) + (PerBoss * BossesKilled(deepestStage, mode));
    }

    /// <summary>
    /// How many boss stages lie strictly below <paramref name="deepestStage"/> — which is how many
    /// bosses the run killed, for the reason in this type's remarks.
    /// </summary>
    /// <param name="deepestStage">
    /// <c>RunState.StageIndex</c> at the moment of death. Excluded from the walk: the boss stage a
    /// player died on is not a boss they killed.
    /// </param>
    /// <param name="mode">The run's mode, for its authored boss roster. Null is refused.</param>
    /// <exception cref="ArgumentNullException"><paramref name="mode"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deepestStage"/> is below 1.</exception>
    public static int BossesKilled(int deepestStage, ModeSpec mode)
    {
        Require(deepestStage, mode);

        int killed = 0;

        // Strictly below the depth, and the exclusive bound is the whole of the rule: a stage is
        // only left once its boss is cleared, so a stage the run is standing in has proved nothing.
        for (int stage = 1; stage < deepestStage; stage++)
        {
            if (mode.TryGetBossFor(stage, out _))
            {
                killed++;
            }
        }

        return killed;
    }

    private static void Require(int deepestStage, ModeSpec mode)
    {
        if (mode is null)
        {
            throw new ArgumentNullException(
                nameof(mode),
                "A payout needs the run's mode for its authored boss roster. There is no default "
                    + "mode a payout is entitled to assume (GD §4.5).");
        }

        if (deepestStage < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deepestStage),
                deepestStage,
                "Stages are numbered from 1 (GD §8.2), so a depth below 1 is a caller that read a "
                    + "defaulted snapshot rather than a run.");
        }
    }
}
