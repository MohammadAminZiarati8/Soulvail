using UnityEngine;

namespace Soulvail.Game.Adapters;

/// <summary>
/// The off switch. GD §16.3 requires one: haptics are a preference, and a player who does not want
/// their phone buzzing must be able to say so and be obeyed everywhere.
/// </summary>
/// <remarks>
/// <para>
/// <b>PlayerPrefs, and only until M2-13.</b> The project's answer to persistence is <c>ISaveStore</c>
/// with versioned DTOs and migration tests, and that does not exist yet — so this one boolean uses
/// the platform key-value store rather than inventing a second save system that would have to be
/// migrated away from. When the save store lands, this class keeps its shape and changes where it
/// reads from; nothing that holds it has to change. The options screen that would expose it is
/// M8-02, so for now the only way to flip it is the key below.
/// </para>
/// <para>
/// A class rather than a bare <c>bool</c> passed around, because the toggle has to be shared: the
/// listener reads it every pulse and whatever eventually writes it — an options screen, a test —
/// writes it once, and a copied boolean would leave one of the two looking at a stale answer.
/// </para>
/// <para>
/// Neither constructor is public, and that is the point of the two factories. Reading the platform
/// store and holding a value in memory are different things with different consequences —
/// <see cref="FromPlayerPrefs"/> persists what it is told, <see cref="InMemory"/> never touches the
/// registry at all — and a test or a headless container that wants the second must not be able to
/// get the first by accident.
/// </para>
/// </remarks>
public sealed class HapticsSettings
{
    /// <summary>
    /// Where the toggle is stored. Namespaced, because <c>PlayerPrefs</c> is one flat table shared
    /// by everything the app will ever save through it.
    /// </summary>
    public const string PrefsKey = "soulvail.haptics.enabled";

    /// <summary>Whether writes go anywhere. False for <see cref="InMemory"/>.</summary>
    private readonly bool _persist;

    private bool _enabled;

    private HapticsSettings(bool enabled, bool persist)
    {
        _enabled = enabled;
        _persist = persist;
    }

    /// <summary>
    /// Whether the game may buzz at all. Default on: haptics are the cheapest game-feel win on a
    /// phone (GD §16.3), so the player who wants silence is the one who has to say so.
    /// </summary>
    /// <remarks>
    /// The write is skipped when nothing changed, so setting it to what it already is costs no
    /// registry write — and <c>Save</c> is called immediately rather than left to the next app
    /// pause, because a preference that a force-quit can lose is one the player has to set twice.
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

            if (!_persist)
            {
                return;
            }

            PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    /// <summary>The real one: reads the stored preference now, and writes every later change.</summary>
    public static HapticsSettings FromPlayerPrefs()
    {
        return new HapticsSettings(PlayerPrefs.GetInt(PrefsKey, 1) != 0, persist: true);
    }

    /// <summary>
    /// A toggle that lives and dies with the object holding it. For tests, and for anything that
    /// wants to answer the question without leaving a mark on the machine it ran on.
    /// </summary>
    public static HapticsSettings InMemory(bool enabled)
    {
        return new HapticsSettings(enabled, persist: false);
    }
}
