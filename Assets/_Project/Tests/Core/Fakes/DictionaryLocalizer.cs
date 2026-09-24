using System;
using System.Collections.Generic;
using System.Globalization;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;

namespace Soulvail.Tests.Core.Fakes;

/// <summary>
/// An <see cref="ILocalizer"/> a fixture can author in one line, for the rows that are about what a
/// screen <em>draws</em> rather than about how a table is read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Promoted on its count rather than on a preference</b> (M3-14a). Ten fixtures gained an
/// <see cref="ILocalizer"/> argument when the port landed, and the project's own rule — written down
/// when the counting <c>IRandom</c> fake reached its second private copy — is that a fake shared by
/// three becomes a file. Seven of the ten need only <em>a</em> localizer and use the real
/// <c>TableLocalizer</c> over an empty <c>LocalizationTable</c>, which answers every key with itself;
/// the three that assert a screen reads English need rows, and this is those three's.
/// </para>
/// <para>
/// <b>It answers exactly what <c>TableLocalizer</c> answers</b> — the row, or
/// <see cref="LocKey.ToString"/> on a miss, never <see cref="LocKey.Key"/>. A fake that disagreed
/// with the adapter about the miss path would let a screen pass here and hand a
/// <see langword="null"/> to a label in a build, which is the one behaviour the adapter's own
/// remarks single out. It lives in <c>Soulvail.Tests.Core</c> beside <c>FixedClock</c> and
/// <c>AllocationAssert</c> because the port is core's and both test assemblies can then see it;
/// <c>TableLocalizer</c> itself cannot come here, being Unity-side.
/// </para>
/// </remarks>
public sealed class DictionaryLocalizer : ILocalizer
{
    private readonly Dictionary<LocKey, string> _rows = new Dictionary<LocKey, string>();

    /// <param name="pairs">
    /// Key then text, alternating. An odd count is a fixture that miscounted its own arguments and
    /// is refused rather than silently dropping the last key.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="pairs"/> has an odd length.</exception>
    public DictionaryLocalizer(params string[] pairs)
    {
        if (pairs is null)
        {
            return;
        }

        if (pairs.Length % 2 != 0)
        {
            throw new ArgumentException(
                $"Expected key/text pairs and got {pairs.Length} strings. The last key has no words.",
                nameof(pairs));
        }

        for (int i = 0; i < pairs.Length; i += 2)
        {
            _rows[new LocKey(pairs[i])] = pairs[i + 1];
        }
    }

    /// <summary>How many rows this fake answers from.</summary>
    public int Count => _rows.Count;

    /// <inheritdoc />
    public string Get(LocKey key) =>
        _rows.TryGetValue(key, out string text) ? text : key.ToString();

    /// <inheritdoc />
    /// <remarks>
    /// <c>TableLocalizer.Format</c>'s answers, in the invariant culture, which is what that adapter
    /// uses over a table with an empty locale: the row substituted, the row whole on a
    /// placeholder its arguments cannot fill, and the key's own text on a miss (M6-10).
    /// </remarks>
    public string Format(LocKey key, params object[] args)
    {
        if (!_rows.TryGetValue(key, out string row))
        {
            return key.ToString();
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, row, args ?? Array.Empty<object>());
        }
        catch (FormatException)
        {
            return row;
        }
    }
}
