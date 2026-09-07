using System;

namespace Soulvail.Core.Content;

/// <summary>
/// The address of a user-facing string, never the string itself:
/// <c>character.oathbound.name</c>. Core carries these in specs; the Unity-side localiser
/// resolves them from tables. See AR §11.5 and ADR-0012.
/// </summary>
/// <remarks>
/// <para>
/// A distinct type from <see cref="ContentId"/> although both wrap a string, because they are
/// not interchangeable and the compiler is the cheapest place to find that out. An id names a
/// thing; a key names a piece of text about it, and one thing has several — name, description,
/// flavour. A shared type would let a spec hand its id to the localiser and get a missing-key
/// placeholder on screen instead of a compile error.
/// </para>
/// <para>
/// Not validated against a table: at this point in the project there is no table, and even
/// when there is one, "does this key resolve" is a question about a language, asked at load
/// time by the localiser (M6). What is enforced here is only what makes a key usable as a
/// table lookup at all — non-empty, no whitespace, so a stray newline in an authored field
/// cannot become a key that mysteriously never resolves.
/// </para>
/// </remarks>
public readonly struct LocKey : IEquatable<LocKey>
{
    /// <param name="key">A non-empty string containing no whitespace.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> is null, empty, or contains whitespace.
    /// </exception>
    public LocKey(string key)
    {
        if (!IsValid(key))
        {
            throw new ArgumentException(
                $"'{key ?? "<null>"}' is not a valid localisation key. Expected a non-empty string " +
                "with no whitespace, e.g. 'character.oathbound.name'.",
                nameof(key));
        }

        Key = key;
    }

    /// <summary>The key itself. Null only for <c>default(LocKey)</c>.</summary>
    public string Key { get; }

    public static bool operator ==(LocKey left, LocKey right) => left.Equals(right);

    public static bool operator !=(LocKey left, LocKey right) => !left.Equals(right);

    /// <summary>Ordinal comparison of <see cref="Key"/>. Two defaults are equal.</summary>
    public bool Equals(LocKey other) => string.Equals(Key, other.Key, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is LocKey other && Equals(other);

    public override int GetHashCode() => Key is null ? 0 : StringComparer.Ordinal.GetHashCode(Key);

    /// <summary>The key itself; empty for <c>default(LocKey)</c>, never null.</summary>
    public override string ToString() => Key ?? string.Empty;

    private static bool IsValid(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        for (int i = 0; i < key.Length; i++)
        {
            if (char.IsWhiteSpace(key[i]))
            {
                return false;
            }
        }

        return true;
    }
}
