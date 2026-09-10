using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// What a run is started with. Built by the composition root from whatever the menu chose
/// (M0-12's <c>PendingRun</c>) and handed to <c>IRunSession.Start</c>. See AR §4.1.
/// </summary>
/// <remarks>
/// <para>
/// <b>Five fields, reshaped once and deliberately</b> (M2-02, ledger row 6). Two of them are
/// M0-09's; the mode, the depth and the seed arrived together because M2 needs all three and
/// accreting a parameter per task across four PRs would have meant four sweeps of the same
/// nineteen call sites instead of one.
/// </para>
/// <para>
/// <b>The seed now arrives here rather than being read back out of the generator.</b> M0-09 left
/// it off on the argument that <c>IRandom.Seed</c> was the single source of truth; resume is
/// what makes that backwards, because a resumed run has to <em>state</em> the seed it is
/// continuing rather than discover whatever the container happened to build. The single-truth
/// property is kept by checking instead of by omitting: <c>RunSession.Start</c> refuses a config
/// whose seed disagrees with the generator it was given, at the one place both are visible.
/// </para>
/// <para>
/// A class rather than a struct, still, and for the reason M0-09 gave: growing a class is a
/// field, while growing a struct passed by value is a copy that gets wider every milestone. The
/// sixth field is already known — M2-14b's <c>RunSnapshot Restore</c> — and "reshape once" means
/// these five are right, not that a sixth is forbidden.
/// </para>
/// </remarks>
public sealed class RunConfig
{
    /// <param name="modeId">
    /// The mode to play, e.g. <c>mode.descent</c>. Resolved against the
    /// <see cref="ContentCatalog"/> at <c>Start</c>, which is where the run learns how deep it
    /// begins and which archetypes it is allowed to see.
    /// </param>
    /// <param name="characterId">The class to play, e.g. <c>character.oathbound</c>.</param>
    /// <param name="seed">
    /// What the run's generator was seeded with, stated by the caller. Any <see cref="int"/> is a
    /// legal seed, including zero and negatives, so there is nothing to validate here — the check
    /// that matters is the one <c>RunSession.Start</c> makes against the generator itself.
    /// </param>
    /// <param name="stageIndex">
    /// The depth this run begins at: the mode's <c>StartingStage</c> for a fresh run, the saved
    /// depth for a resumed one (M2-14b). Checked against the mode at <c>Start</c>, because
    /// whether stage 6 exists is the mode's question and this constructor cannot see one.
    /// </param>
    /// <param name="spawnPlan">
    /// The enemies the run starts with, or <see cref="SpawnPlan.Empty"/> for none.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="modeId"/> or <paramref name="characterId"/> is
    /// <c>default(ContentId)</c> — the one id no constructor can prevent, because a struct always
    /// has a zeroed form. It means nobody chose a mode or a class, which is a composition mistake
    /// rather than a missing asset, and saying so here names the real problem. Left to the
    /// catalog it would surface one layer down as "no character with id ''", pointing at content
    /// that was never at fault. Any other id is the catalog's question to answer, and it answers
    /// unknown ids with <c>KeyNotFoundException</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="stageIndex"/> is below 1. Stages are numbered from 1 (GD §8.2), so a zero
    /// is a caller that meant "the first one" and used an array index — the mistake worth
    /// catching here, where the number was chosen, rather than at the depth scaling that would
    /// quietly compute a stage-zero curve from it.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="spawnPlan"/> is null. Required rather than optional, so that a run which
    /// starts empty says so with <see cref="SpawnPlan.Empty"/> instead of by omission — an empty
    /// arena is the one failure a playtest cannot tell apart from a bug in the spawner, and the
    /// compiler is the right thing to enumerate every call site that has to choose.
    /// </exception>
    public RunConfig(
        ContentId modeId,
        ContentId characterId,
        int seed,
        int stageIndex,
        SpawnPlan spawnPlan)
    {
        if (modeId.Value is null)
        {
            throw new ArgumentException(
                "modeId must be a valid ContentId; default(ContentId) means no mode was chosen.",
                nameof(modeId));
        }

        if (characterId.Value is null)
        {
            throw new ArgumentException(
                "characterId must be a valid ContentId; default(ContentId) means no class was chosen.",
                nameof(characterId));
        }

        if (stageIndex < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stageIndex),
                stageIndex,
                "stageIndex must be at least 1. Stages are numbered from 1, not from zero.");
        }

        ModeId = modeId;
        CharacterId = characterId;
        Seed = seed;
        StageIndex = stageIndex;
        SpawnPlan = spawnPlan ?? throw new ArgumentNullException(nameof(spawnPlan));
    }

    /// <summary>The mode being played. Resolved against the <see cref="ContentCatalog"/> at <c>Start</c>.</summary>
    public ContentId ModeId { get; }

    /// <summary>The class to play. Resolved against the <see cref="ContentCatalog"/> at <c>Start</c>.</summary>
    public ContentId CharacterId { get; }

    /// <summary>
    /// What the generator was seeded with — inbound, stated by the caller.
    /// </summary>
    /// <remarks>
    /// Not "what to seed the generator with": nothing here reseeds anything. The generator is
    /// built by the composition root before this config exists, and <c>RunSession.Start</c> only
    /// checks that the two agree. Driving the generator from core would mean an
    /// <c>IRandom.Reseed</c>, which widens a port ahead of its caller (AR §6) and is half of what
    /// restoring stream state rides on — M2-13a weighs that with ledger row 1.
    /// </remarks>
    public int Seed { get; }

    /// <summary>The depth this run begins at.</summary>
    /// <remarks>
    /// A fresh run passes the mode's <c>StartingStage</c>; nothing in the composition root knows
    /// the number itself, which is GD §4.5's rule that no code may hard-code "starts at stage 1".
    /// From here it becomes <c>RunState.StageIndex</c>, which M2-10 advances.
    /// </remarks>
    public int StageIndex { get; }

    /// <summary>
    /// The enemies to spawn as the run begins, immediately after <c>RunStarted</c>.
    /// </summary>
    /// <remarks>
    /// A run's whole starting population in M1, because there is no director yet; from M2-05 it is
    /// only what an arena has standing in it on arrival.
    /// </remarks>
    public SpawnPlan SpawnPlan { get; }
}
