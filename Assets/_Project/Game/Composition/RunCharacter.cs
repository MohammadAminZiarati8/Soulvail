using System;
using Soulvail.Core.Content;

namespace Soulvail.Game.Composition;

/// <summary>
/// The one choice of which class a run plays (RS-02b rule 3). <c>RunTicker</c> starts the run with
/// it and <c>RunScope</c> raises the body with it, so the class started and the body worn cannot
/// disagree.
/// </summary>
/// <remarks>
/// Static, but not state: a function of its two arguments, <c>BootInstaller</c>'s bargain.
/// </remarks>
public static class RunCharacter
{
    /// <summary>
    /// The pending run's class, or the catalog's first when no run is pending.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fallback is the bargain <c>RunInstaller</c> makes with the seed (M0-12 rule 6), for the
    /// same workflow — pressing Play with the Run scene already open is how this game is iterated
    /// on, and throwing there would break the fastest loop in development. Silent rather than
    /// warning, unlike the seed: "you got the first class" is visible on screen the moment the run
    /// starts.
    /// </para>
    /// <para>
    /// <b>Both callers ask before <c>RunTicker.Start</c> clears the pending run</b>: <c>RunScope</c>
    /// in its build callback, while the scene is still loading, and the ticker just before the
    /// clear. After it, this would answer the catalog's first class for every run.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// No run is pending and the catalog holds no characters.
    /// </exception>
    public static ContentId Choose(PendingRun pending, ContentCatalog catalog)
    {
        if (pending is null)
        {
            throw new ArgumentNullException(nameof(pending));
        }

        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (pending.IsSet)
        {
            return pending.CharacterId;
        }

        if (catalog.Characters.Count == 0)
        {
            throw new InvalidOperationException(
                "No run is pending and the content catalog is empty, so there is no class to " +
                "play. Add a CharacterDefinition to BootScope's character list.");
        }

        return catalog.Characters[0].Id;
    }
}
