using System;

namespace Soulvail.Core.Content;

/// <summary>
/// The stable identity of one piece of authored content: <c>character.oathbound</c>,
/// <c>skill.oathbound.consecrate</c>. Saves, analytics and remote config all speak in these.
/// See AR §11.3 and ADR-0010.
/// </summary>
/// <remarks>
/// <para>
/// A string rather than an enum ordinal or an array index, and that is the whole point: an
/// ordinal is a promise about *position*, so inserting a skill in the middle of an enum would
/// silently repoint every id in every save ever written. Enums stay for closed sets — FSM
/// states, modifier kinds — where nothing is ever inserted from content.
/// </para>
/// <para>
/// The grammar is <c>^[a-z0-9]+(\.[a-z0-9_-]+)+$</c>: lowercase, dot-separated, at least two
/// segments, with <c>_</c> and <c>-</c> legal in every segment except the first. Validated in
/// the constructor, so an invalid id cannot be built — the check lives in one place instead of
/// at every point that reads one.
/// </para>
/// <para>
/// <c>default(ContentId)</c> is the one id that skips that check: it has a null
/// <see cref="Value"/>, because a struct always has a zeroed form and no constructor can stop
/// it. It equals no valid id, and looking it up in a <c>ContentCatalog</c> misses like any
/// other unknown id rather than throwing something else. Every member here handles it —
/// <see cref="GetHashCode"/> and <see cref="ToString"/> included — so a stray default surfaces
/// as "not found" rather than as a <see cref="NullReferenceException"/> from somewhere deeper.
/// </para>
/// </remarks>
public readonly struct ContentId : IEquatable<ContentId>
{
    /// <param name="value">The id, in the grammar above.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> does not match the grammar. Null is one of the forms that does
    /// not match, and is rejected the same way rather than as an
    /// <see cref="ArgumentNullException"/>: there is one question here — is this a well-formed
    /// id — and one answer for every way of failing it.
    /// </exception>
    public ContentId(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException(
                $"'{value ?? "<null>"}' is not a valid content id. Expected lowercase dot-separated " +
                "segments, at least two, e.g. 'character.oathbound'.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>The id itself. Null only for <c>default(ContentId)</c>.</summary>
    public string Value { get; }

    public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);

    public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);

    /// <summary>
    /// Whether <paramref name="value"/> is a well-formed content id. Null and empty are not.
    /// </summary>
    /// <remarks>
    /// Hand-written rather than a <c>Regex</c>. The grammar is three character classes and one
    /// loop, so the scanner is shorter than the explanation of why a regex would be fine here,
    /// and it avoids the two things a regex would drag in: a static cached
    /// <c>Regex</c> instance in a project that bans static state, or a fresh one — with its
    /// parse and its match allocations — on every id ever constructed. M0-11 validates a whole
    /// catalog of authored assets through this.
    /// </remarks>
    public static bool IsValid(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        int i = 0;

        // First segment: [a-z0-9]+ — no '_' or '-', so an id can never start with punctuation.
        int segmentStart = i;
        while (i < value.Length && IsHeadChar(value[i]))
        {
            i++;
        }

        if (i == segmentStart)
        {
            return false;
        }

        // Then one or more of: '.' followed by [a-z0-9_-]+.
        int segments = 1;
        while (i < value.Length)
        {
            if (value[i] != '.')
            {
                return false;
            }

            i++;
            segmentStart = i;
            while (i < value.Length && IsTailChar(value[i]))
            {
                i++;
            }

            if (i == segmentStart)
            {
                return false;
            }

            segments++;
        }

        return segments >= 2;
    }

    /// <summary>
    /// Parses <paramref name="value"/> without throwing. <paramref name="id"/> is
    /// <c>default</c> when this returns false.
    /// </summary>
    public static bool TryParse(string value, out ContentId id)
    {
        if (!IsValid(value))
        {
            id = default;
            return false;
        }

        id = new ContentId(value);
        return true;
    }

    /// <summary>Ordinal comparison of <see cref="Value"/>. Two defaults are equal.</summary>
    public bool Equals(ContentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object obj) => obj is ContentId other && Equals(other);

    public override int GetHashCode() => Value is null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

    /// <summary>
    /// The id itself, so an id drops straight into a log line or an exception message.
    /// </summary>
    /// <remarks>
    /// Empty, not null, for <c>default(ContentId)</c>. A <c>ToString</c> that can return null
    /// is a trap for every caller that concatenates its result, and the message a default id
    /// belongs in is already saying that something was not found.
    /// </remarks>
    public override string ToString() => Value ?? string.Empty;

    private static bool IsHeadChar(char c) => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

    private static bool IsTailChar(char c) => IsHeadChar(c) || c == '_' || c == '-';
}
