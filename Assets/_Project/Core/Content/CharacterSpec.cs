using System;

namespace Soulvail.Core.Content;

/// <summary>
/// A playable class, as authored data: who it is and the numbers it plays with. Converted once
/// at boot from a <c>CharacterDefinition</c> ScriptableObject and registered in the
/// <see cref="ContentCatalog"/>. See AR §10.1 and ADR-0006.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, and runtime state never lives here: one instance is shared by everything that
/// reads the class's numbers for a whole run, so a mutable field would be a global variable
/// with a nice name. Current HP belongs to <c>Health</c> (M1-02), not to
/// <see cref="MaxHp"/>.
/// </para>
/// <para>
/// <see cref="MaxHp"/> is a raw <see cref="float"/> for the same reason
/// <see cref="MovementSpec.Speed"/> is: M1-01 introduces the modifier stack and the runtime
/// value becomes a <c>Stat</c> seeded from this number. The authored maximum stays a plain
/// number either way — modifiers apply to the live stat, not to the data.
/// </para>
/// </remarks>
public sealed class CharacterSpec
{
    /// <param name="id">The class's stable content id, e.g. <c>character.oathbound</c>.</param>
    /// <param name="nameKey">Localisation key for the display name.</param>
    /// <param name="maxHp">Starting maximum health.</param>
    /// <param name="movement">How the class moves.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is <c>default(ContentId)</c>. A spec with no id cannot be looked
    /// up, cannot be saved, and would sit in the catalog under a key that
    /// <see cref="ContentCatalog.Character"/> reports as missing — a lie the catalog would tell
    /// forever. Rejected here, where the data is built, rather than where it is read.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="maxHp"/> is not greater than zero. A class that starts dead is a content
    /// mistake.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="movement"/> is null.</exception>
    public CharacterSpec(ContentId id, LocKey nameKey, float maxHp, MovementSpec movement)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("id must be a valid ContentId; default(ContentId) has none.", nameof(id));
        }

        // `!(maxHp > 0f)` rather than `maxHp <= 0f`, because every comparison against NaN is
        // false: the natural spelling waves NaN through, and a NaN maximum makes every health
        // fraction NaN — health bars, low-HP effects, death checks — permanently.
        if (!(maxHp > 0f))
        {
            throw new ArgumentOutOfRangeException(nameof(maxHp), maxHp, "maxHp must be greater than zero.");
        }

        Id = id;
        NameKey = nameKey;
        MaxHp = maxHp;
        Movement = movement ?? throw new ArgumentNullException(nameof(movement));
    }

    /// <summary>Stable identity, e.g. <c>character.oathbound</c>.</summary>
    public ContentId Id { get; }

    /// <summary>Localisation key for the display name — never the name itself.</summary>
    public LocKey NameKey { get; }

    /// <summary>Starting maximum health.</summary>
    public float MaxHp { get; }

    /// <summary>The class's movement numbers.</summary>
    public MovementSpec Movement { get; }
}
