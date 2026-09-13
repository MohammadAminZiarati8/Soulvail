using System;
using System.Threading;
using System.Threading.Tasks;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The off switch. GD §16.3 requires one: haptics are a preference, and a player who does not want
/// their phone buzzing must be able to say so and be obeyed everywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>The class kept its shape and changed where it reads from, which is what its own comment has
/// promised since M1-20.</b> The stopgap was <c>PlayerPrefs</c>, chosen because <c>ISaveStore</c>
/// did not exist; as of M2-13b it does, and this is its first consumer (ledger row 11). The
/// <c>PlayerPrefs</c> key is deleted rather than migrated — nothing has shipped, so the population
/// that would be carried over is the owner's own dev machine, and a migration with no users is a
/// permanent line in <c>SaveMigrations</c> paying for nobody. The options screen that would expose
/// the toggle is still M8-02.
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
    /// <summary>Where writes go, or null for <see cref="InMemory"/>.</summary>
    private readonly ISaveStore _store;

    private bool _enabled;

    private HapticsSettings(bool enabled, ISaveStore store)
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
    /// The write is skipped when nothing changed, so setting this to what it already is costs no
    /// I/O — and it is written immediately rather than left to the next app pause, because a
    /// preference a force-quit can lose is one the player has to set twice.
    /// </para>
    /// <para>
    /// <b>Fire and forget, with the fault observed.</b> A setting that fails to persist must not
    /// throw out of a UI callback — the toggle the player flipped has already moved, and the worst
    /// honest outcome is that it does not survive the app. The continuation runs synchronously, so
    /// with today's synchronous store the error is logged before this setter returns.
    /// </para>
    /// </remarks>
    public bool Enabled
    {
        get => _enabled;

        set
        {
            if (_enabled == value)
            {
                return;
            }

            _enabled = value;

            if (_store is null)
            {
                return;
            }

            _store.SaveProfile(new PlayerProfile(PlayerProfile.CurrentVersion, value)).ContinueWith(
                static task => Debug.LogError(
                    "Could not save the haptics preference: " +
                    task.Exception?.GetBaseException().Message),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// The real one: starts at GD §16.3's default and writes every later change through
    /// <paramref name="store"/>.
    /// </summary>
    /// <remarks>
    /// <b>It does not read.</b> A factory that loaded here would have to block on I/O, or return
    /// before the value it promised had arrived. The profile is loaded once, at boot, by
    /// <c>BootFlow</c> — the one place in the app with a legitimate reason to await the disk — and
    /// handed over through <see cref="Apply"/>.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public static HapticsSettings FromStore(ISaveStore store)
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

    /// <summary>
    /// Adopts a profile that arrived after construction.
    /// </summary>
    /// <remarks>
    /// <b>Without writing it back.</b> The field is assigned directly rather than through
    /// <see cref="Enabled"/>, because a load that immediately re-saves is a load that can corrupt
    /// what it just read — and on a fresh install, where the caller substitutes
    /// <see cref="PlayerProfile.Default"/> for a file that does not exist, it would put a profile
    /// on disk for a player who has never changed a setting.
    /// </remarks>
    public void Apply(in PlayerProfile profile)
    {
        _enabled = profile.HapticsEnabled;
    }
}
