using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// File-scoped, like SkillIconBook.cs beside it: this type derives from nothing, least of all
// UnityEngine.Object, so the block-namespace rule of Traps §5 does not apply.
namespace Soulvail.Game.Authoring;

/// <summary>
/// Place → picture, for the Places tab's cards (UI-11h-b): what a place <em>looks like</em>, where the
/// catalog says what it <em>is</em>.
/// </summary>
/// <remarks>
/// <c>SkillIconBook</c>'s arrangement over modes: a <see cref="Sprite"/> cannot live in the core
/// catalog (AR §2), so <c>BootInstaller</c> builds this from the same mode list, keyed by each spec's
/// id, and registers it at the root. A place with no picture is not an entry and the book answers null
/// for it; the card decides what a place without one looks like.
/// </remarks>
public sealed class PlaceLookBook
{
    private readonly Dictionary<ContentId, Sprite> _pictures;

    /// <param name="pictures">Each place's picture by its id. Copied, so a later change to the source does not reach the book.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pictures"/> is null.</exception>
    /// <exception cref="ArgumentException">An entry is keyed by <c>default(ContentId)</c>.</exception>
    public PlaceLookBook(IReadOnlyDictionary<ContentId, Sprite> pictures)
    {
        if (pictures is null)
        {
            throw new ArgumentNullException(nameof(pictures));
        }

        _pictures = new Dictionary<ContentId, Sprite>(pictures.Count);

        foreach (KeyValuePair<ContentId, Sprite> entry in pictures)
        {
            if (entry.Key.Value is null)
            {
                throw new ArgumentException(
                    "A picture is keyed by default(ContentId), which names no place. Every entry must " +
                    "carry the id of the ModeDefinition it came from.",
                    nameof(pictures));
            }

            // Unity's ==, so a sprite deleted from the project reads as no picture.
            if (entry.Value != null)
            {
                _pictures[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>
    /// The place's picture, or null for a place with none and for an id the book does not know. Never
    /// throws, including on <c>default</c>.
    /// </summary>
    public Sprite For(ContentId place) =>
        place.Value is not null && _pictures.TryGetValue(place, out Sprite picture) ? picture : null;
}
