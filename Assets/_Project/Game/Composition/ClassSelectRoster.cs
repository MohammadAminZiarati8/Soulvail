using System;
using System.Collections.Generic;
using Soulvail.Core.Content;

namespace Soulvail.Game.Composition;

/// <summary>
/// Which classes class select shows, in order (RS-05c) — <c>BootScope</c>'s <i>Class Select</i>
/// list. Empty shows every class the catalog holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a player may start a run as, and nothing else.</b> Every class stays in the catalog, so a
/// saved run of a hidden class still resumes and its branches can still be borrowed at the splash
/// (the owner's ruling of 2026-09-28: <i>"disable means just not show the others"</i>).
/// </para>
/// <para>
/// <b>Beside the catalog rather than a flag on each class</b>: which classes a build offers is a
/// decision about the build, like <c>BootScope</c>'s <i>Modes</i> order choosing the first place, and
/// the class assets stay what every class test reads.
/// </para>
/// </remarks>
public sealed class ClassSelectRoster
{
    private readonly ContentId[] _ids;

    /// <param name="ids">The classes to show, in order. Copied. Empty means every class.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ids"/> is null.</exception>
    /// <exception cref="ArgumentException">An id is <c>default</c>, or listed twice.</exception>
    public ClassSelectRoster(IReadOnlyList<ContentId> ids)
    {
        if (ids is null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        _ids = new ContentId[ids.Count];

        var seen = new HashSet<ContentId>();

        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i].Value is null)
            {
                throw new ArgumentException(
                    $"Class select's entry {i} names no class. An empty slot in BootScope's Class "
                        + "Select list is the likely cause: fill it or remove it.",
                    nameof(ids));
            }

            if (!seen.Add(ids[i]))
            {
                throw new ArgumentException(
                    $"Class select lists '{ids[i]}' twice, which would draw it on two cards.",
                    nameof(ids));
            }

            _ids[i] = ids[i];
        }
    }

    /// <summary>Whether the list is empty, so every class the catalog holds is shown.</summary>
    public bool ShowsEveryClass => _ids.Length == 0;

    /// <summary>
    /// The classes to show, in order: the list's, or the catalog's own order when the list is empty.
    /// A listed class the catalog does not hold is skipped.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> is null.</exception>
    public IReadOnlyList<CharacterSpec> ShownFrom(ContentCatalog catalog)
    {
        if (catalog is null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (ShowsEveryClass)
        {
            return catalog.Characters;
        }

        var shown = new List<CharacterSpec>(_ids.Length);

        foreach (ContentId id in _ids)
        {
            if (catalog.TryGetCharacter(id, out CharacterSpec spec))
            {
                shown.Add(spec);
            }
        }

        return shown;
    }
}
