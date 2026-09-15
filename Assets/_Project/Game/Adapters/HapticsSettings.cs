using System;
using Soulvail.Core.Save;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The off switch. GD §16.3 requires one: haptics are a preference, and a player who does not want
/// their phone buzzing must be able to say so and be obeyed everywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>The class kept its shape and changed where it reads from — twice now.</b> The stopgap was
/// <c>PlayerPrefs</c>, chosen because <c>ISaveStore</c> did not exist; M2-13b moved it onto the
/// store (ledger row 11); <b>M3-09c moved it one step further, onto <see cref="ProfileStore"/></b>.
/// The <c>PlayerPrefs</c> key stays deleted rather than migrated — nothing has shipped, so the
/// population that would be carried over is the owner's own dev machine. The options screen that
/// would expose the toggle is still M8-02.
/// </para>
/// <para>
/// <b>It no longer holds a copy of the value and no longer authors a profile</b> (M3-09c rule 3).
/// Writing <c>new PlayerProfile(CurrentVersion, value)</c> was correct while the profile had one
/// field in it and became silently destructive at v2, when a haptics toggle would have reset the
/// one-time hint's flag. The value now lives in exactly one place, is written through exactly one
/// call — <c>ProfileStore.Save</c> — and this class moves one field with <c>WithHaptics</c> and
/// hands the profile back.
/// </para>
/// <para>
/// A class rather than a bare <c>bool</c> passed around, because the toggle has to be shared: the
/// listener reads it every pulse and whatever eventually writes it — an options screen, a test —
/// writes it once, and a copied boolean would leave one of the two looking at a stale answer.
/// </para>
/// <para>
/// Neither constructor is public, and that is the point of the two factories. Persisting a value
/// and holding one in memory are different things with different consequences —
/// <see cref="FromStore"/> writes every change through to disk, <see cref="InMemory"/> touches
/// nothing at all — and a test or a headless container that wants the second must not be able to
/// get the first by accident.
/// </para>
/// </remarks>
public sealed class HapticsSettings
{
    /// <summary>The profile this reads and writes through, or null for <see cref="InMemory"/>.</summary>
    private readonly ProfileStore _store;

    /// <summary>
    /// The value, and <em>only</em> for <see cref="InMemory"/>. A store-backed instance never reads
    /// this field — see <see cref="Enabled"/>.
    /// </summary>
    private bool _enabled;

    private HapticsSettings(bool enabled, ProfileStore store)
    {
        _enabled = enabled;
        _store = store;
    }

    /// <summary>
    /// Whether the game may buzz at all. Default on: haptics are the cheapest game-feel win on a
    /// phone (GD §16.3), so the player who wants silence is the one who has to say so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Read straight off the live profile</b>, so there is one holder of this fact rather than
    /// two that can drift. The in-memory factory has no profile to read, and only it uses the
    /// field.
    /// </para>
    /// <para>
    /// The write is skipped when nothing changed, so setting this to what it already is costs no
    /// I/O — and it is written immediately rather than left to the next app pause, because a
    /// preference a force-quit can lose is one the player has to set twice.
    /// </para>
    /// <para>
    /// <b>One field moved, never a struct authored</b> (M3-09c rule 3): <c>WithHaptics</c> carries
    /// every other field through untouched, which is what stops a toggle erasing a flag this class
    /// has never heard of. <c>ProfileStore.Save</c> owns the fire-and-forget write and the logged
    /// fault, so a setting that fails to persist still never throws out of a UI callback.
    /// </para>
    /// </remarks>
    public bool Enabled
    {
        get => _store is null ? _enabled : _store.Current.HapticsEnabled;

        set
        {
            if (Enabled == value)
            {
                return;
            }

            if (_store is null)
            {
                _enabled = value;
                return;
            }

            _store.Save(_store.Current.WithHaptics(value));
        }
    }

    /// <summary>
    /// The real one: a view over the live profile, writing every change back through
    /// <paramref name="store"/>.
    /// </summary>
    /// <remarks>
    /// <b>It does not touch the disk, here or later.</b> The profile is loaded once, at boot, by
    /// <c>BootFlow</c> — the one place in the app with a legitimate reason to await the disk — and
    /// handed to <c>ProfileStore.Adopt</c>. Until that lands the store answers
    /// <see cref="PlayerProfile.Default"/>, which is GD §16.3's default and the honest degradation
    /// against a future asynchronous store: the setting is at its default until the disk answers,
    /// never at a wrong stored value.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public static HapticsSettings FromStore(ProfileStore store)
    {
        if (store is null)
        {
            throw new ArgumentNullException(nameof(store));
        }

        return new HapticsSettings(PlayerProfile.Default.HapticsEnabled, store);
    }

    /// <summary>
    /// A toggle that lives and dies with the object holding it. For tests, and for anything that
    /// wants to answer the question without leaving a mark on the machine it ran on.
    /// </summary>
    public static HapticsSettings InMemory(bool enabled)
    {
        return new HapticsSettings(enabled, store: null);
    }
}
