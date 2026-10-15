using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Save;

namespace Soulvail.Core.Progression;

/// <summary>
/// Which places an install may enter: every place opens when the one before it is finished
/// (UI-11b, the owner's ruling of 2026-10-15). Pure functions over a profile and the catalog.
/// </summary>
/// <remarks>
/// <para>
/// <b>Static for <see cref="ClassUnlocks"/>' reason and with its caveat</b>: no fields, nothing to
/// reset under a disabled domain reload; if it ever needs a collaborator it becomes an injected
/// object that day.
/// </para>
/// <para>
/// <b>Only the direct predecessor is asked.</b> A finished grandparent opens nothing: C opened by B
/// stays locked until B itself is finished.
/// </para>
/// </remarks>
public static class PlaceUnlocks
{
    /// <summary>
    /// Whether <paramref name="modeId"/> may be entered. True for a mode with no
    /// <see cref="ModeSpec.UnlockedBy"/>, and for one whose predecessor is in
    /// <see cref="PlayerProfile.FinishedModeIds"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="modeId"/> is <c>default(ContentId)</c>.</exception>
    /// <exception cref="KeyNotFoundException">The catalog holds no such mode.</exception>
    public static bool IsUnlocked(ContentId modeId, in PlayerProfile profile, ContentCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(
                nameof(catalog), "Whether a place is open is a question about what it authors.");
        }

        if (modeId.Value is null)
        {
            throw new ArgumentException(
                "modeId is default(ContentId), which names no mode.", nameof(modeId));
        }

        ContentId previous = catalog.Mode(modeId).UnlockedBy;

        return previous.Value is null || IsFinished(previous, profile);
    }

    /// <summary>Whether <paramref name="modeId"/>'s final stage has been cleared on this install.</summary>
    public static bool IsFinished(ContentId modeId, in PlayerProfile profile)
    {
        IReadOnlyList<ContentId> finished = profile.FinishedModeIds;

        for (int i = 0; i < finished.Count; i++)
        {
            if (finished[i] == modeId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The listed modes in catalog order, written into <paramref name="into"/>; returns the count.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> or <paramref name="into"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="into"/> is too short.</exception>
    public static int Listed(ContentCatalog catalog, ContentId[] into)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        IReadOnlyList<ModeSpec> modes = catalog.Modes;
        int written = 0;

        for (int i = 0; i < modes.Count; i++)
        {
            if (!modes[i].IsListed)
            {
                continue;
            }

            if (written == into.Length)
            {
                throw new ArgumentException(
                    $"into holds {into.Length} entries and the catalog lists more modes than that.",
                    nameof(into));
            }

            into[written++] = modes[i].Id;
        }

        return written;
    }
}
