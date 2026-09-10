using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Soulvail.Core.Combat;

/// <summary>
/// A gameplay number and everything currently changing it: a base value plus an ordered stack of
/// <see cref="Modifier"/>s, each tagged with its source. Every number the game plays with —
/// damage, fire rate, max HP, cooldown, XP gain, Veilrot gain — is one of these from the first
/// line of combat code. See AR §11.1 and ADR-0008, which is non-negotiable.
/// </summary>
/// <remarks>
/// <para>
/// The point is that the ten-odd future sources of "+damage" — tree passives, Pacts, class
/// signatures, Veilrot thresholds, Elite affixes, Ordeals, Focus, the Claiming, difficulty and
/// depth scaling — each add a modifier and remove it again, and none of them ever touches combat
/// code or knows about the others. The price is that a stat is a small object rather than a
/// float; ADR-0008 pays it deliberately.
/// </para>
/// <para>
/// Nothing here is timed. A source that expires owns its own clock and calls
/// <see cref="RemoveAll"/>; a stat has no idea what a second is, which is what keeps it out of
/// every tick.
/// </para>
/// <para>
/// Not thread-safe, and not meant to be: core runs on one thread, driven by <c>Tick</c>.
/// </para>
/// </remarks>
public sealed class Stat
{
    /// <summary>
    /// How far two values may differ and still count as the same number for
    /// <see cref="Changed"/>. Float arithmetic makes an exact comparison meaningless — removing
    /// and re-adding the same modifier can land a few ULPs away — and a stat that reports a
    /// change nobody made would have every listener rebuilding itself for nothing.
    /// </summary>
    private const float ValueTolerance = 1e-6f;

    /// <summary>Two decimals everywhere in <see cref="Describe"/>, so columns line up.</summary>
    private const string DescribeFormat = "F2";

    /// <summary>
    /// U+00D7, the multiplication sign — by code point rather than as the glyph. The glyph would
    /// put this file's encoding into the observable output: read as anything but UTF-8 it becomes
    /// two mojibake characters, and the test pinning the format would be garbled identically and
    /// still pass. The code point is ASCII in the source, so it cannot drift.
    /// </summary>
    private const char MultiplySign = (char)0x00D7;

    /// <summary>
    /// In the order they were added. Order is not arithmetic — the pooled sums and the product
    /// are both commutative — but it is the order <see cref="Describe"/> reads them out in, and
    /// the order <see cref="CopyModifiersTo"/> hands them to a debug panel.
    /// </summary>
    private readonly List<Modifier> _modifiers = new();

    private float _base;
    private float _value;

    /// <summary>
    /// Set by every mutation, cleared by the recompute in <see cref="Value"/>. Starts true
    /// because a fresh stat has never computed anything.
    /// </summary>
    private bool _isStale = true;

    /// <param name="baseValue">
    /// The unmodified number, as authored. Free to be zero or negative — a stat is arithmetic,
    /// and what a sensible range is depends entirely on what it measures.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="baseValue"/> is NaN or infinite.
    /// </exception>
    public Stat(float baseValue)
    {
        _base = Finite(baseValue, nameof(baseValue));
    }

    /// <summary>
    /// Fires after a mutation, and only when <see cref="Value"/> actually came out different —
    /// beyond <see cref="ValueTolerance"/>. A modifier that changes nothing is silent, so a
    /// listener can treat this as "recompute what you derived from me" without checking first.
    /// </summary>
    /// <remarks>
    /// Scoped to whatever object holds the stat, like everything else in this project — see
    /// ADR-0004. There is no global bus to leak into.
    /// </remarks>
    public event Action<Stat> Changed;

    /// <summary>
    /// The unmodified number. Setting it invalidates the cached <see cref="Value"/> and may
    /// raise <see cref="Changed"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is NaN or infinite.</exception>
    public float Base
    {
        get => _base;

        set
        {
            float finite = Finite(value, nameof(value));
            float before = ValueBeforeMutation();

            _base = finite;
            _isStale = true;

            RaiseIfValueChanged(before);
        }
    }

    /// <summary>
    /// The number the game uses: <c>(Base + ΣFlat) × (1 + ΣPercentAdd) × Π(1 + PercentMult)</c>,
    /// in that order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cached, and recomputed on the first read after a mutation. Reading it allocates nothing,
    /// which is what allows a hot path to read a stat per frame — or several times per frame —
    /// without a reason to copy the number into a local "for performance" and then go stale.
    /// </para>
    /// <para>
    /// Nothing clamps the result. A <see cref="ModifierKind.PercentMult"/> of −1 is a legitimate
    /// way to say "this number is now zero", and a stack can drive a value negative; where that
    /// matters — the 40 % cooldown floor of M3-06, for one — the caller clamps, because only the
    /// caller knows what the number means. Non-finite *inputs* are a different matter and are
    /// refused by <see cref="Modifier"/> and <see cref="Base"/>: a NaN would make
    /// <see cref="Changed"/> lie, since a comparison against NaN is false in both directions and
    /// the event would simply stop firing.
    /// </para>
    /// </remarks>
    public float Value
    {
        get
        {
            if (_isStale)
            {
                _value = Recompute();
                _isStale = false;
            }

            return _value;
        }
    }

    /// <summary>How many modifiers are on the stack, of all kinds.</summary>
    public int ModifierCount => _modifiers.Count;

    /// <summary>
    /// Puts a modifier on the stack. May invalidate the cache and raise <see cref="Changed"/>.
    /// </summary>
    /// <remarks>
    /// The same source may add several — a node that grants "+2 damage and +15 %" adds two, and
    /// one <see cref="RemoveAll"/> takes both off again.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// The modifier has no source. Every constructed <see cref="Modifier"/> has one, so the only
    /// way in is <c>default(Modifier)</c> — a struct always has a zeroed form, which is why the
    /// constructor's guard is not the last word. Same shape as <c>default(ContentId)</c> in
    /// M0-08: a sourceless modifier can never be removed by source, so it is refused rather than
    /// stored as a permanent change to the stat with nothing to point at.
    /// </exception>
    public void Add(in Modifier modifier)
    {
        if (modifier.Source is null)
        {
            throw new ArgumentException(
                "A modifier must carry a source, and default(Modifier) has none — it could never "
                    + "be removed. Build it with the constructor.",
                nameof(modifier));
        }

        float before = ValueBeforeMutation();

        _modifiers.Add(modifier);
        _isStale = true;

        RaiseIfValueChanged(before);
    }

    /// <summary>
    /// Takes off every modifier applied by <paramref name="source"/> — the buff ended, the node
    /// was removed, the Rot threshold was crossed back — and reports how many went.
    /// </summary>
    /// <remarks>
    /// Sources are matched by reference, never by equality: two different nodes that happen to
    /// compare equal are still two sources, and removing one must not remove the other. Survivors
    /// keep their relative order.
    /// </remarks>
    /// <returns>
    /// The number removed. Zero for a source that has nothing on this stat, which is not an
    /// error — it leaves the cache alone and raises nothing, so a caller can clean up
    /// unconditionally.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    public int RemoveAll(object source)
    {
        if (source is null)
        {
            // No stored modifier can have a null source, so this would quietly answer "nothing
            // of yours here" for what is really a caller holding the wrong reference.
            throw new ArgumentNullException(nameof(source));
        }

        float before = ValueBeforeMutation();

        // Compacted in place rather than removed one at a time: no allocation, no O(n²) shuffle,
        // and the survivors stay in the order they were added.
        int write = 0;

        for (int read = 0; read < _modifiers.Count; read++)
        {
            if (ReferenceEquals(_modifiers[read].Source, source))
            {
                continue;
            }

            _modifiers[write] = _modifiers[read];
            write++;
        }

        int removed = _modifiers.Count - write;

        if (removed > 0)
        {
            _modifiers.RemoveRange(write, removed);
            _isStale = true;

            RaiseIfValueChanged(before);
        }

        return removed;
    }

    /// <summary>
    /// Appends the current stack to <paramref name="destination"/>, in the order it was added.
    /// </summary>
    /// <remarks>
    /// Appends rather than replaces, and hands out copies rather than the list, so a debug panel
    /// can gather every stat it is showing into one buffer it owns and reuses, and nothing
    /// outside can reorder or drop a modifier the stat is still applying.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is null.</exception>
    public void CopyModifiersTo(List<Modifier> destination)
    {
        if (destination is null)
        {
            throw new ArgumentNullException(nameof(destination));
        }

        destination.AddRange(_modifiers);
    }

    /// <summary>
    /// Writes the arithmetic out in full — <c>"28.80 = (13.00 + 2.00) × 1.60 × 1.20"</c>: the
    /// result, the base, the flat sum, the pooled percentage as a factor, then one factor per
    /// <see cref="ModifierKind.PercentMult"/> in the order they were added.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shape is fixed, so the base and the flat sum are always in the same place even when
    /// they are zero and even when there are no modifiers at all
    /// (<c>"13.00 = (13.00 + 0.00) × 1.00"</c>). A reader scanning a panel of these should not
    /// have to parse a variable format to find the number they came for.
    /// </para>
    /// <para>
    /// Debug only, and allowed to allocate — formatting a float allocates a string on this
    /// runtime whatever one does. Sources are deliberately not named here; a panel that wants
    /// "+45 % (Pact)" pairs this with <see cref="CopyModifiersTo"/>, which carries them.
    /// </para>
    /// <para>
    /// Formatted with <see cref="CultureInfo.InvariantCulture"/>. The device's culture decides
    /// whether a decimal point is a comma, and this is a diagnostic, not a user-facing string —
    /// a number in a bug report should read the same wherever it was captured.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="sb"/> is null.</exception>
    public void Describe(StringBuilder sb)
    {
        if (sb is null)
        {
            throw new ArgumentNullException(nameof(sb));
        }

        Pool(out float flat, out float percentAdd);

        AppendNumber(sb, Value);
        sb.Append(" = (");
        AppendNumber(sb, _base);
        sb.Append(" + ");
        AppendNumber(sb, flat);
        sb.Append(") ").Append(MultiplySign).Append(' ');
        AppendNumber(sb, 1f + percentAdd);

        for (int i = 0; i < _modifiers.Count; i++)
        {
            Modifier modifier = _modifiers[i];

            if (modifier.Kind != ModifierKind.PercentMult)
            {
                continue;
            }

            sb.Append(' ').Append(MultiplySign).Append(' ');
            AppendNumber(sb, 1f + modifier.Value);
        }
    }

    private static void AppendNumber(StringBuilder sb, float value)
    {
        sb.Append(value.ToString(DescribeFormat, CultureInfo.InvariantCulture));
    }

    /// <remarks>
    /// Asked as two explicit questions rather than as a range comparison, for the same reason
    /// <c>MovementSpec.Positive</c> is spelled <c>!(value &gt; 0f)</c>: a comparison-based guard
    /// lets NaN through, and NaN in a stat is worse than in most places — it makes
    /// <see cref="Changed"/> stop firing rather than fire wrongly, so the symptom is a HUD that
    /// silently stops updating.
    /// </remarks>
    private static float Finite(float value, string paramName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"{paramName} must be finite.");
        }

        return value;
    }

    /// <summary>Sums the two pooled kinds in one pass over the stack.</summary>
    private void Pool(out float flat, out float percentAdd)
    {
        flat = 0f;
        percentAdd = 0f;

        for (int i = 0; i < _modifiers.Count; i++)
        {
            Modifier modifier = _modifiers[i];

            switch (modifier.Kind)
            {
                case ModifierKind.Flat:
                    flat += modifier.Value;
                    break;

                case ModifierKind.PercentAdd:
                    percentAdd += modifier.Value;
                    break;

                case ModifierKind.PercentMult:
                    // Not pooled — applied one factor at a time by the caller, after the sums.
                    break;

                default:
                    // Unreachable: the constructor refuses any other kind. It is here so that a
                    // fourth ModifierKind added without teaching this method about it fails
                    // loudly, instead of being silently dropped from every stat that has one.
                    throw new InvalidOperationException(
                        $"Unhandled modifier kind '{modifier.Kind}'.");
            }
        }
    }

    private float Recompute()
    {
        Pool(out float flat, out float percentAdd);

        float value = (_base + flat) * (1f + percentAdd);

        // Each PercentMult applied in turn, as ADR-0008 specifies, rather than pooled into one
        // product first. The two agree to well within the tolerance anyone measures at, but "in
        // turn" is the rule, and a second pass over a list this short costs nothing.
        for (int i = 0; i < _modifiers.Count; i++)
        {
            if (_modifiers[i].Kind == ModifierKind.PercentMult)
            {
                value *= 1f + _modifiers[i].Value;
            }
        }

        return value;
    }

    /// <summary>
    /// The value as it stood before a mutation, or nothing at all if no one is listening.
    /// </summary>
    /// <remarks>
    /// This is where the lazy cache and the <see cref="Changed"/> contract meet. Deciding whether
    /// the value changed means knowing what it was and what it now is, which is an eager
    /// recompute — so the laziness survives only while nothing is subscribed. That is the common
    /// case for a stat nobody is watching, and for a stat the HUD *is* watching, the recompute
    /// was going to happen anyway.
    /// </remarks>
    private float ValueBeforeMutation() => Changed is null ? 0f : Value;

    private void RaiseIfValueChanged(float before)
    {
        Action<Stat> changed = Changed;

        if (changed is null)
        {
            return;
        }

        // Negated rather than written as "differs by more than the tolerance" so that a value
        // that is somehow non-finite counts as a change: every comparison against NaN is false,
        // and the positive spelling would answer "unchanged" forever.
        if (!(MathF.Abs(Value - before) <= ValueTolerance))
        {
            changed(this);
        }
    }
}
