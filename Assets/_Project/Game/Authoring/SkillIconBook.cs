using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// File-scoped, like CharacterLook.cs beside it: this type derives from nothing, least of all
// UnityEngine.Object, so Unity's script importer never has to find it and the block-namespace rule of
// Traps §5 does not apply.
namespace Soulvail.Game.Authoring;

/// <summary>
/// Skill → icon, for the HUD's auto-cast row (UI-01e): what a skill <em>looks like</em>, where the
/// catalog says what it <em>is</em>.
/// </summary>
/// <remarks>
/// <para>
/// <b>A separate object rather than a field on the catalog, because the catalog lives in core and a
/// <see cref="Sprite"/> cannot</b> — <c>Soulvail.Core</c> is <c>noEngineReferences</c> (AR §2).
/// <c>EnemyLookBook</c>'s and <c>CharacterLookBook</c>'s arrangement, one kind of content over: built
/// by <c>BootInstaller</c> from the same skill list as the catalog, keyed by each spec's id, and
/// registered at the root, where every run resolves it.
/// </para>
/// <para>
/// <b>A skill with no icon is not an error, and not an entry.</b> Every skill but the Ranger's Volley
/// ships without art today, so <see cref="For"/> answers null for it as for an id nobody knows, and
/// the row draws its placeholder. The book says which skills have a picture; the row decides what a
/// skill without one looks like.
/// </para>
/// </remarks>
public sealed class SkillIconBook
{
    private readonly Dictionary<ContentId, Sprite> _icons;

    /// <param name="icons">Each skill's icon by its id. Copied, so a later change to the source does not reach the book.</param>
    /// <exception cref="ArgumentNullException"><paramref name="icons"/> is null.</exception>
    /// <exception cref="ArgumentException">An entry is keyed by <c>default(ContentId)</c>.</exception>
    public SkillIconBook(IReadOnlyDictionary<ContentId, Sprite> icons)
    {
        if (icons is null)
        {
            throw new ArgumentNullException(nameof(icons));
        }

        _icons = new Dictionary<ContentId, Sprite>(icons.Count);

        foreach (KeyValuePair<ContentId, Sprite> entry in icons)
        {
            if (entry.Key.Value is null)
            {
                throw new ArgumentException(
                    "An icon is keyed by default(ContentId), which names no skill. Every entry must " +
                    "carry the id of the SkillDefinition it came from.",
                    nameof(icons));
            }

            // Unity's ==, so a sprite deleted from the project reads as the placeholder rather than as
            // a missing texture on a live Image.
            if (entry.Value != null)
            {
                _icons[entry.Key] = entry.Value;
            }
        }
    }

    /// <summary>How many skills have an icon.</summary>
    public int Count => _icons.Count;

    /// <summary>
    /// The skill's icon, or null for a skill with none and for an id the book does not know. Never
    /// throws, including on <c>default</c>.
    /// </summary>
    public Sprite For(ContentId skillId) =>
        skillId.Value is not null && _icons.TryGetValue(skillId, out Sprite icon) ? icon : null;
}
