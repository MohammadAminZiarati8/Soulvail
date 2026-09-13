using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Tests.Core.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// Ledger row 11: the toggle over an <c>ISaveStore</c> rather than over the platform registry.
/// </summary>
/// <remarks>
/// <para>
/// The rows are about <em>where the value lives and how often it is written</em>, not about
/// buzzing — <c>HapticsListenerTests</c> owns that and is untouched by this task, which is the
/// check that "the class keeps its shape and changes where it reads from" is true rather than
/// claimed.
/// </para>
/// <para>
/// <c>InMemorySaveStore</c> rather than the real adapter: what is being asserted is the number of
/// writes and what happens when one fails, and a fake that counts its calls answers both without
/// a directory to clean up.
/// </para>
/// </remarks>
[TestFixture]
public sealed class HapticsSettingsTests
{
    [Test]
    public void FromStore_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HapticsSettings.FromStore(null));
    }

    [Test]
    public void Haptics_StartsFromTheLoadedProfile()
    {
        var store = new InMemorySaveStore();
        HapticsSettings settings = HapticsSettings.FromStore(store);

        settings.Apply(new PlayerProfile(PlayerProfile.CurrentVersion, hapticsEnabled: false));

        Assert.That(settings.Enabled, Is.False);
    }

    [Test]
    public void Haptics_DefaultsOn()
    {
        var store = new InMemorySaveStore();
        HapticsSettings settings = HapticsSettings.FromStore(store);

        // What BootFlow substitutes when there is no file. GD §16.3 puts the burden on the player
        // who wants silence, so a fresh install buzzes.
        Assert.That(settings.Enabled, Is.True, "Before a profile arrives at all.");

        settings.Apply(PlayerProfile.Default);

        Assert.That(settings.Enabled, Is.True);
    }

    [Test]
    public void Haptics_ApplyDoesNotWrite()
    {
        var store = new InMemorySaveStore();
        HapticsSettings settings = HapticsSettings.FromStore(store);

        settings.Apply(new PlayerProfile(PlayerProfile.CurrentVersion, hapticsEnabled: false));

        // A load that immediately re-saves is a load that can corrupt what it just read — and on a
        // fresh install it would put a profile on disk for a player who has changed nothing.
        Assert.That(store.ProfileWriteCount, Is.EqualTo(0));
    }

    [Test]
    public void Haptics_SettingWrites()
    {
        var store = new InMemorySaveStore();
        HapticsSettings settings = HapticsSettings.FromStore(store);

        settings.Enabled = false;

        Assert.That(store.ProfileWriteCount, Is.EqualTo(1));

        PlayerProfile? stored = store.LoadProfile().GetAwaiter().GetResult();

        Assert.That(stored.HasValue, Is.True);
        Assert.That(stored.Value.HapticsEnabled, Is.False);
        Assert.That(stored.Value.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
    }

    [Test]
    public void Haptics_SettingToTheSameValueDoesNotWrite()
    {
        var store = new InMemorySaveStore();
        HapticsSettings settings = HapticsSettings.FromStore(store);

        settings.Enabled = true;

        // M1-20's rule, kept: an options screen that assigns on every draw must not write a file
        // on every draw.
        Assert.That(store.ProfileWriteCount, Is.EqualTo(0));
    }

    [Test]
    public void Haptics_FailedWriteDoesNotThrow()
    {
        var store = new InMemorySaveStore();
        HapticsSettings settings = HapticsSettings.FromStore(store);

        store.FailNextWrite();

        LogAssert.Expect(LogType.Error, new Regex("Could not save the haptics preference"));

        // The toggle the player flipped has already moved. The worst honest outcome is that it
        // does not survive the app — never an exception out of a UI callback.
        Assert.DoesNotThrow(() => settings.Enabled = false);
        Assert.That(settings.Enabled, Is.False);
    }

    [Test]
    public void Haptics_InMemoryTouchesNoStore()
    {
        HapticsSettings settings = HapticsSettings.InMemory(true);

        settings.Enabled = false;
        settings.Enabled = true;

        Assert.That(settings.Enabled, Is.True);

        // The factory's whole promise: nothing to write through, so nothing can be written. A
        // headless container or a test must not be able to leave a mark on the machine it ran on.
        FieldInfo store = typeof(HapticsSettings).GetField(
            "_store", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.That(store, Is.Not.Null, "The field this row is about was renamed.");
        Assert.That(store.GetValue(settings), Is.Null);
    }

    [Test]
    public void Haptics_HasNoPrefsKey()
    {
        Type type = typeof(HapticsSettings);
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        // Row 11 pinned rather than remembered: the PlayerPrefs key is abandoned, not migrated, so
        // the way it stays abandoned is that nothing can reach for it.
        Assert.That(type.GetField("PrefsKey", Everything), Is.Null);
        Assert.That(type.GetMethod("FromPlayerPrefs", Everything), Is.Null);
    }
}
