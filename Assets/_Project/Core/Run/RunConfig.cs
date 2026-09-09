using System;
using Soulvail.Core.Content;

namespace Soulvail.Core.Run;

/// <summary>
/// What a run is started with. Built by the composition root from whatever the menu chose
/// (M0-12's <c>PendingRun</c>) and handed to <c>IRunSession.Start</c>. See AR §4.1.
/// </summary>
/// <remarks>
/// <para>
/// The seed is deliberately not here. It comes from <c>IRandom.Seed</c>, which the composition
/// root creates the generator with — one source of truth, so a run can never be started with a
/// seed that disagrees with the stream it actually draws from. <see cref="RunState.Seed"/>
/// records what that generator was seeded with, for the run-end screen and for bug reports.
/// </para>
/// <para>
/// Two fields today, and a class rather than a struct because of what M2 does to it: the mode
/// (Descent, Daily, Ordeal), the depth to resume at, the modifiers a Daily carries. Growing a
/// class is a field; growing a struct passed by value is a copy that gets wider every milestone.
/// </para>
/// </remarks>
public sealed class RunConfig
{
    /// <param name="characterId">The class to play, e.g. <c>character.oathbound</c>.</param>
    /// <param name="spawnPlan">
    /// The enemies the run starts with, or <see cref="SpawnPlan.Empty"/> for none.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="characterId"/> is <c>default(ContentId)</c> — the one id no constructor
    /// can prevent, because a struct always has a zeroed form. It means nobody chose a class,
    /// which is a composition mistake rather than a missing asset, and saying so here names the
    /// real problem. Left to the catalog it would surface one layer down as "no character with
    /// id ''", pointing at content that was never at fault. Any other id is the catalog's
    /// question to answer, and it answers unknown ids with <c>KeyNotFoundException</c>.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="spawnPlan"/> is null. Required rather than optional, so that a run which
    /// starts empty says so with <see cref="SpawnPlan.Empty"/> instead of by omission — an empty
    /// arena is the one failure a playtest cannot tell apart from a bug in the spawner, and the
    /// compiler is the right thing to enumerate every call site that has to choose.
    /// </exception>
    public RunConfig(ContentId characterId, SpawnPlan spawnPlan)
    {
        if (characterId.Value is null)
        {
            throw new ArgumentException(
                "characterId must be a valid ContentId; default(ContentId) means no class was chosen.",
                nameof(characterId));
        }

        CharacterId = characterId;
        SpawnPlan = spawnPlan ?? throw new ArgumentNullException(nameof(spawnPlan));
    }

    /// <summary>The class to play. Resolved against the <see cref="ContentCatalog"/> at <c>Start</c>.</summary>
    public ContentId CharacterId { get; }

    /// <summary>
    /// The enemies to spawn as the run begins, immediately after <c>RunStarted</c>.
    /// </summary>
    /// <remarks>
    /// A run's whole starting population in M1, because there is no director yet; from M2-05 it is
    /// only what an arena has standing in it on arrival.
    /// </remarks>
    public SpawnPlan SpawnPlan { get; }
}
