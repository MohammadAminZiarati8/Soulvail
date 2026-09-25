using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// File-scoped, unlike CharacterDefinition beside it: neither type here derives from
// UnityEngine.Object, so Unity's script importer never has to find them and the block-namespace
// rule of Traps §5 does not apply. EnemyLook.cs's split, for the same reason.
namespace Soulvail.Game.Authoring;

/// <summary>
/// What one class looks like in a run: the body it wears. Game side only — a model is not core's
/// business (AR §3), and a <c>CharacterSpec</c> names none. RS-02b.
/// </summary>
/// <remarks>
/// <para>
/// <b>A prefab, where <see cref="EnemyLook"/> is a tint and a scale.</b> Enemies share one pooled
/// body and are told apart by per-instance properties (GD §11.3); a class is one body standing for
/// the whole run, raised once, so it can be a model of its own with its own controller and its own
/// animator view.
/// </para>
/// <para>
/// <b><c>default(CharacterLook)</c> is the default look, and that is the difference from
/// <see cref="EnemyLook"/> worth knowing.</b> An enemy's struct default is an invisible body, which
/// is why its book returns a named default on a miss. Here a null body already means "the run's
/// default body" — <c>RunScope</c>'s <c>_defaultBody</c> — so a miss can return the struct default
/// and mean exactly that.
/// </para>
/// </remarks>
public readonly struct CharacterLook
{
    /// <param name="body">
    /// The body prefab a run of this class wears, raised under the player at the start of the run.
    /// Null is not an error: it is the run's default body.
    /// </param>
    public CharacterLook(GameObject body)
    {
        Body = body;
    }

    /// <summary>The body prefab, or null for the run's default body.</summary>
    /// <remarks>
    /// Read with Unity's <c>==</c>, never <c>is null</c>: a prefab deleted from the project is a live
    /// reference that only the engine's operator calls null.
    /// </remarks>
    public GameObject Body { get; }
}

/// <summary>
/// Class id → its look, built once at boot from the same definitions the <c>ContentCatalog</c> is
/// built from. The body half of a <c>CharacterDefinition</c>, kept out of core.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="EnemyLookBook"/>'s shape and its rules (RS-02b rule 1), for its reason: the catalog
/// lives in core and a <see cref="GameObject"/> cannot (AR §2), so what a class <em>is</em> goes to
/// core and what it <em>looks like</em> stays here.
/// </para>
/// <para>
/// Registered as a root singleton by <c>BootInstaller</c> and resolved by <c>RunScope</c>, which is
/// the only reader: a look is read once, when a run raises its body.
/// </para>
/// </remarks>
public sealed class CharacterLookBook
{
    private readonly Dictionary<ContentId, CharacterLook> _looks;

    /// <param name="looks">
    /// Every authored look, keyed by class id. Copied; the caller's dictionary is not retained.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="looks"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Two entries share an id, or an entry is keyed by <c>default(ContentId)</c>. The catalog's own
    /// rule, repeated because this is a second index over the same assets and a duplicate would
    /// otherwise keep whichever one the source enumerated last, in silence.
    /// </exception>
    public CharacterLookBook(IReadOnlyDictionary<ContentId, CharacterLook> looks)
    {
        if (looks is null)
        {
            throw new ArgumentNullException(nameof(looks));
        }

        _looks = new Dictionary<ContentId, CharacterLook>(looks.Count);

        foreach (KeyValuePair<ContentId, CharacterLook> entry in looks)
        {
            if (entry.Key.Value is null)
            {
                throw new ArgumentException(
                    "A look is keyed by default(ContentId), which names no class. Every entry " +
                    "must carry the id of the CharacterDefinition it came from.",
                    nameof(looks));
            }

            if (_looks.ContainsKey(entry.Key))
            {
                throw new ArgumentException(
                    $"Two looks are keyed '{entry.Key.Value}'. One class has one look; a duplicate " +
                    "means two definitions share an id, which ContentCatalog refuses outright.",
                    nameof(looks));
            }

            _looks.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// The look authored for <paramref name="characterId"/>, or the default look — a null
    /// <see cref="CharacterLook.Body"/> — for an id nobody authored one for.
    /// </summary>
    /// <remarks>
    /// Never throws, including on <c>default(ContentId)</c>, for <see cref="EnemyLookBook.For"/>'s
    /// reason: a missing body is not worth ending a run over, and the default body is legible as the
    /// player even when it is the wrong one.
    /// </remarks>
    public CharacterLook For(ContentId characterId)
        => _looks.TryGetValue(characterId, out CharacterLook look) ? look : default;
}
