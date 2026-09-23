using System;
using System.Collections.Generic;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// What a dead run was worth in Soul Shards — GD §14.1, all three terms. A pure function of the
/// depth reached, the mode's authored rosters, and the archetypes this install had already met.
/// </summary>
/// <remarks>
/// <para>
/// <b>GD §14.1's formula has three terms and this ships all three as of M6-09a.</b> The formula is
/// <c>10·(deepest stage) + 50·(bosses killed) + 25·(new archetype first encountered)</c>. The third
/// term is a <em>lifetime</em> fact — <em>first</em> encountered, across every run this install has
/// ever played — so its memory lives on <c>PlayerProfile.MetArchetypeIds</c> and arrives here as
/// an argument. M4-05a shipped the first two and deferred it, and this class was named for what it
/// computes rather than for the formula so that the deferral would never read as a forgotten term.
/// <b>A null or empty set pays for everything</b>, which is the direction that deferral already
/// chose: shipping the term late over-pays a returning player rather than robbing them.
/// </para>
/// <para>
/// <b>Archetypes are walked <em>inclusive</em> of the depth reached, where bosses are exclusive,
/// and the contrast is the rule</b> (M6-09a rule 5). A boss must be <em>killed</em>, which only
/// leaving its stage proves; an archetype is merely <em>met</em>, and a body spawns on arrival. A
/// player who dies on stage 17 to the archetype introduced at stage 17 has met it.
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
/// <b><see cref="PerStage"/>, <see cref="PerBoss"/> and <see cref="PerNewArchetype"/> are <see langword="const"/>s in core, and
/// that is a knowing breach of ADR-0006</b> — the same breach <c>LevelUpFlow</c>'s Overflow 2 % is,
/// which the ROADMAP's carry-forward row 6 already owns and which records this class as its fourth
/// reader. They are GD §14.1's own numbers rather than a designer's tuning surface, and there is no
/// asset a payout belongs on: a payout is not a mode, a character or an enemy, and inventing one
/// here would be M6's Sanctum arriving early and unspecified.
/// </para>
/// <para>
/// Asked once per run, on the frame the player dies. With a null set it allocates nothing; with a
/// set, the membership probe may box an enumerator — the death tick, not a frame path (AR §14).
/// </para>
/// </remarks>
public static class ShardPayout
{
    /// <summary>GD §14.1's first coefficient: Shards per stage of depth reached.</summary>
    public const int PerStage = 10;

    /// <summary>GD §14.1's second coefficient: Shards per boss killed.</summary>
    public const int PerBoss = 50;

    /// <summary>GD §14.1's third coefficient: Shards per archetype met for the first time.</summary>
    public const int PerNewArchetype = 25;

    /// <summary>
    /// What the run is worth: <see cref="PerStage"/> per stage of depth, <see cref="PerBoss"/> per
    /// boss killed, and <see cref="PerNewArchetype"/> per archetype met for the first time.
    /// </summary>
    /// <param name="deepestStage">
    /// <c>RunState.StageIndex</c> at the moment of death. Below 1 is refused — that is the value
    /// <c>default(RunSnapshot)</c> carries and <c>RunSnapshot</c>'s own constructor already refuses,
    /// which is why the guard is cheap: a run cannot reach this with one.
    /// </param>
    /// <param name="mode">The run's mode, for its authored rosters. Null is refused.</param>
    /// <param name="alreadyMet">
    /// Every archetype this install has met before this run — <c>PlayerProfile.MetArchetypeIds</c>.
    /// <see langword="null"/> is legal and means <em>none</em>, which over-pays rather than
    /// under-pays (M6-09a rule 5).
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="mode"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deepestStage"/> is below 1.</exception>
    public static int For(
        int deepestStage, ModeSpec mode, IReadOnlyCollection<ContentId> alreadyMet = null)
    {
        Require(deepestStage, mode);

        return (PerStage * deepestStage)
            + (PerBoss * BossesKilled(deepestStage, mode))
            + (PerNewArchetype * CountNewArchetypes(deepestStage, mode, alreadyMet));
    }

    /// <summary>
    /// Which archetypes a run to <paramref name="deepestStage"/> met that
    /// <paramref name="alreadyMet"/> does not hold, in introduction order. <b>Inclusive of the
    /// depth reached</b> — see the type's remarks.
    /// </summary>
    /// <param name="deepestStage"><c>RunState.StageIndex</c> at the moment of death.</param>
    /// <param name="mode">The run's mode, for its authored archetype roster.</param>
    /// <param name="alreadyMet">What the install had met before this run; null for nothing.</param>
    /// <param name="destination">
    /// Where the ids are written. Size it to the mode's roster; a buffer too short throws rather
    /// than truncating, because a dropped id is a Shard nobody is paid and a meeting nobody records.
    /// </param>
    /// <returns>How many entries were written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="mode"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="deepestStage"/> is below 1.</exception>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too short.</exception>
    public static int NewArchetypes(
        int deepestStage,
        ModeSpec mode,
        IReadOnlyCollection<ContentId> alreadyMet,
        Span<ContentId> destination)
    {
        Require(deepestStage, mode);

        int written = 0;

        // Stage by stage rather than roster order, so the ids come out in the order the run met
        // them — which is the order ShardWriter appends them to the lifetime set in.
        for (int stage = 1; stage <= deepestStage; stage++)
        {
            if (!IsNew(stage, mode, alreadyMet, out ContentId specId))
            {
                continue;
            }

            if (written == destination.Length)
            {
                throw new ArgumentException(
                    $"destination holds {destination.Length} entries and a run to stage " +
                    $"{deepestStage} of '{mode.Id}' met more new archetypes than that. Size the " +
                    "buffer to the mode's roster.",
                    nameof(destination));
            }

            destination[written++] = specId;
        }

        return written;
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

    /// <summary><see cref="NewArchetypes"/>' count, without a buffer.</summary>
    private static int CountNewArchetypes(
        int deepestStage, ModeSpec mode, IReadOnlyCollection<ContentId> alreadyMet)
    {
        int count = 0;

        for (int stage = 1; stage <= deepestStage; stage++)
        {
            if (IsNew(stage, mode, alreadyMet, out _))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Whether <paramref name="stage"/> introduces an archetype the install had not met.</summary>
    /// <remarks>
    /// A linear probe over a collection the size of the roster — ten archetypes in GD §8.2 — asked
    /// once per introducing stage on a death, never on a frame path. Its enumerator may be boxed;
    /// that is the death tick's one allocation budget (M6-09a rule 6), not a tick's.
    /// </remarks>
    private static bool IsNew(
        int stage, ModeSpec mode, IReadOnlyCollection<ContentId> alreadyMet, out ContentId specId)
    {
        if (!mode.TryGetIntroduction(stage, out specId))
        {
            return false;
        }

        return alreadyMet is null || !Contains(alreadyMet, specId);
    }

    private static bool Contains(IReadOnlyCollection<ContentId> ids, ContentId id)
    {
        foreach (ContentId candidate in ids)
        {
            if (candidate == id)
            {
                return true;
            }
        }

        return false;
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
