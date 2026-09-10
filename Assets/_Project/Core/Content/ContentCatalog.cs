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
/// Characters and enemies, for now. Skills and modes get their own list and their own pair of
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
    private readonly Dictionary<ContentId, EnemySpec> _enemiesById;
    private readonly ReadOnlyCollection<EnemySpec> _enemies;

    /// <param name="characters">
    /// The character specs to register. Copied; the caller's list is not retained.
    /// </param>
    /// <param name="enemies">
    /// The enemy archetypes to register, or null for none. Optional because "no enemies are
    /// authored yet" is a real state of this project — <c>EnemyDefinition</c> arrives in M1-07 —
    /// and because every later kind adds another list, which would otherwise leave every call
    /// site restating the ones it does not care about. The omission is not silent for long: the
    /// first <see cref="Enemy"/> lookup fails loudly, naming the id it could not find.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="characters"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// An entry is null, or two entries of one kind share an id — thrown with the duplicated id
    /// in the message, because "one of your assets collides" is not something a person can act
    /// on.
    /// </exception>
    public ContentCatalog(
        IReadOnlyList<CharacterSpec> characters,
        IReadOnlyList<EnemySpec> enemies = null)
    {
        if (characters is null)
        {
            throw new ArgumentNullException(nameof(characters));
        }

        _characters = Index(
            characters,
            spec => spec.Id,
            "character",
            nameof(characters),
            out _charactersById);

        _enemies = Index(
            enemies ?? Array.Empty<EnemySpec>(),
            spec => spec.Id,
            "enemy",
            nameof(enemies),
            out _enemiesById);
    }

    /// <summary>Every registered character, in the order they were supplied.</summary>
    public IReadOnlyList<CharacterSpec> Characters => _characters;

    /// <summary>Every registered enemy archetype, in the order they were supplied.</summary>
    public IReadOnlyList<EnemySpec> Enemies => _enemies;

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

    /// <summary>The enemy archetype with this id.</summary>
    /// <exception cref="KeyNotFoundException">
    /// No enemy has that id — including <c>default(ContentId)</c>, which is unknown like any
    /// other id the catalog does not hold. This is the exception <c>EnemySystem.Spawn</c> lets
    /// through rather than translating: a spawn plan naming an archetype nobody authored is
    /// missing content, and the message names the id.
    /// </exception>
    public EnemySpec Enemy(ContentId id)
    {
        if (!_enemiesById.TryGetValue(id, out EnemySpec spec))
        {
            throw new KeyNotFoundException($"No enemy with id '{id}' in the catalog.");
        }

        return spec;
    }

    /// <summary>
    /// Looks up an enemy archetype without throwing. <paramref name="spec"/> is null when this
    /// returns false.
    /// </summary>
    public bool TryGetEnemy(ContentId id, out EnemySpec spec) =>
        _enemiesById.TryGetValue(id, out spec);

    /// <summary>
    /// Copies <paramref name="source"/>, indexes it by id, and refuses a null entry or a
    /// duplicate id.
    /// </summary>
    /// <remarks>
    /// One method for every kind, taking the id selector as a delegate. It runs once per kind at
    /// boot, so the delegate costs nothing that matters, and it means the two lookups cannot
    /// drift — a third kind gets the same copy, the same guards and the same messages by calling
    /// it rather than by being written again.
    /// </remarks>
    private static ReadOnlyCollection<T> Index<T>(
        IReadOnlyList<T> source,
        Func<T, ContentId> idOf,
        string kind,
        string paramName,
        out Dictionary<ContentId, T> byId)
        where T : class
    {
        var copy = new T[source.Count];
        byId = new Dictionary<ContentId, T>(source.Count);

        for (int i = 0; i < source.Count; i++)
        {
            T spec = source[i];
            if (spec is null)
            {
                throw new ArgumentException($"{paramName}[{i}] is null.", paramName);
            }

            ContentId id = idOf(spec);

            if (byId.ContainsKey(id))
            {
                throw new ArgumentException(
                    $"Duplicate {kind} id '{id}'. Content ids must be unique.",
                    paramName);
            }

            byId.Add(id, spec);
            copy[i] = spec;
        }

        // Wrapped rather than handed out as the array it is: an array exposed as
        // IReadOnlyList<T> casts straight back to T[], and then the copy above protects nothing.
        // One object, once, for the life of the app.
        return Array.AsReadOnly(copy);
    }
}
