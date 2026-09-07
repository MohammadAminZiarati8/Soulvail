using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Soulvail.Core.Content;

/// <summary>
/// Everything authored, in one place, addressed by <see cref="ContentId"/>. Built once at boot
/// from the ScriptableObject definitions and registered in <c>BootScope</c> (M0-12); core reads
/// it and never sees an asset. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Characters only, for now. Enemies, skills and modes get their own list and their own pair of
/// accessors in the milestone that introduces them — one dictionary per kind rather than one
/// dictionary of <c>object</c>, so a lookup returns the type it names and a caller cannot ask
/// for a skill and be handed a mode.
/// </para>
/// <para>
/// Immutable after construction, which is what makes it safe to share across every scope for
/// the life of the app: the input list is copied, so a caller that keeps building in its own
/// list afterwards cannot change what the catalog holds.
/// </para>
/// </remarks>
public sealed class ContentCatalog
{
    private readonly Dictionary<ContentId, CharacterSpec> _charactersById;
    private readonly ReadOnlyCollection<CharacterSpec> _characters;

    /// <param name="characters">
    /// The character specs to register. Copied; the caller's list is not retained.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="characters"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An entry is null, or two entries share an <see cref="CharacterSpec.Id"/> — thrown with
    /// the duplicated id in the message, because "one of your assets collides" is not something
    /// a person can act on.
    /// </exception>
    public ContentCatalog(IReadOnlyList<CharacterSpec> characters)
    {
        if (characters is null)
        {
            throw new ArgumentNullException(nameof(characters));
        }

        var copy = new CharacterSpec[characters.Count];
        _charactersById = new Dictionary<ContentId, CharacterSpec>(characters.Count);

        for (int i = 0; i < characters.Count; i++)
        {
            CharacterSpec spec = characters[i];
            if (spec is null)
            {
                throw new ArgumentException($"characters[{i}] is null.", nameof(characters));
            }

            if (_charactersById.ContainsKey(spec.Id))
            {
                throw new ArgumentException(
                    $"Duplicate character id '{spec.Id}'. Content ids must be unique.",
                    nameof(characters));
            }

            _charactersById.Add(spec.Id, spec);
            copy[i] = spec;
        }

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to CharacterSpec[], and then the copy above
        // protects nothing. One object, once, for the life of the app.
        _characters = Array.AsReadOnly(copy);
    }

    /// <summary>Every registered character, in the order they were supplied.</summary>
    public IReadOnlyList<CharacterSpec> Characters => _characters;

    /// <summary>The character with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No character has that id — including <c>default(ContentId)</c>, which is unknown like any
    /// other id the catalog does not hold. The message names the id, since the caller that asked
    /// is usually several layers from the code that chose it.
    /// </exception>
    public CharacterSpec Character(ContentId id)
    {
        if (!_charactersById.TryGetValue(id, out CharacterSpec spec))
        {
            throw new KeyNotFoundException($"No character with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up a character without throwing. <paramref name="spec"/> is null when this returns
    /// false.
    /// </summary>
    public bool TryGetCharacter(ContentId id, out CharacterSpec spec) =>
        _charactersById.TryGetValue(id, out spec);
}
