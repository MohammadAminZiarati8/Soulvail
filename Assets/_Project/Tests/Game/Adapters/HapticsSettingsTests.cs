using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Tests.Core.Fakes;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// Ledger row 11: the toggle over an <c>ISaveStore</c> rather than over the platform registry — and,
/// as of M3-09c, over <c>ProfileStore</c> rather than over the port directly.
/// </summary>
/// <remarks>
/// <para>
/// The rows are about <em>where the value lives and how often it is written</em>, not about
/// buzzing — <c>HapticsListenerTests</c> owns that and is untouched by this task, which is the
/// check that "the class keeps its shape and changes where it reads from" is true rather than
/// claimed.
/// </para>
/// <para>
/// <c>InMemorySaveStore</c> under a real <c>ProfileStore</c> rather than the real adapter: what is
/// being asserted is the number of writes, what is in them, and what happens when one fails — and a
/// fake that counts its calls answers all three without a directory to clean up.
/// </para>
/// <para>
/// <b><see cref="Haptics_ToggleKeepsTheHintFlag"/> is the row this fixture gained at M3-09c and the
/// reason <c>ProfileStore</c> exists.</b> Until then this class persisted with
/// <c>new PlayerProfile(CurrentVersion, value, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "")</c> — the whole struct, authored from the one field
/// it knew — which is correct for a record with one field in it and silently destructive at v2.
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
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

        profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: false, seenFirstActiveHint: false,
            shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: ""));

        Assert.That(settings.Enabled, Is.False);
    }

    [Test]
    public void Haptics_DefaultsOn()
    {
        var store = new InMemorySaveStore();
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

        // What the store holds before BootFlow's load lands, and what BootFlow substitutes when
        // there is no file. GD §16.3 puts the burden on the player who wants silence, so a fresh
        // install buzzes.
        Assert.That(settings.Enabled, Is.True, "Before a profile arrives at all.");

        profiles.Adopt(PlayerProfile.Default);

        Assert.That(settings.Enabled, Is.True);
    }

    [Test]
    public void Haptics_AdoptingAProfileDoesNotWrite()
    {
        var store = new InMemorySaveStore();
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

        profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: false, seenFirstActiveHint: false,
            shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: ""));

        // A load that immediately re-saves is a load that can corrupt what it just read — and on a
        // fresh install it would put a profile on disk for a player who has changed nothing. The
        // claim M1-20 made about `Apply` now belongs to `ProfileStore.Adopt`; this row is what says
        // routing the setting through the store did not quietly lose it.
        Assert.That(store.ProfileWriteCount, Is.EqualTo(0));
        Assert.That(settings.Enabled, Is.False, "and the value still arrived.");
    }

    [Test]
    public void Haptics_SettingWrites()
    {
        var store = new InMemorySaveStore();
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

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
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

        settings.Enabled = true;

        // M1-20's rule, kept: an options screen that assigns on every draw must not write a file
        // on every draw.
        Assert.That(store.ProfileWriteCount, Is.EqualTo(0));
    }

    /// <summary>
    /// The row that would have caught M3-09c rule 3, and the reason <c>ProfileStore</c> is the
    /// task's real subject rather than the hint is.
    /// </summary>
    /// <remarks>
    /// Against the code that shipped at M1-20 this goes red on its last assertion: the setter wrote
    /// <c>new PlayerProfile(CurrentVersion, value, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "")</c>, so the persisted flag came back at the
    /// constructor's default and a player who had already dismissed CC §6.3's one-time callout would
    /// be shown it again the next time they turned haptics off.
    /// </remarks>
    [Test]
    public void Haptics_ToggleKeepsTheHintFlag()
    {
        var store = new InMemorySaveStore();
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

        profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: true, seenFirstActiveHint: true,
            shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: ""));

        settings.Enabled = false;

        PlayerProfile stored = store.LoadProfile().GetAwaiter().GetResult().Value;

        Assert.That(store.ProfileWriteCount, Is.EqualTo(1), "one write, from one writer.");
        Assert.That(stored.HapticsEnabled, Is.False, "the field this class knows about moved.");

        // And the one it does not know about did not. A writer that knows one field must never
        // author the whole DTO.
        Assert.That(
            stored.SeenFirstActiveHint,
            Is.True,
            "toggling haptics erased the hint flag — the exact bug rule 3 is about.");

        // The live profile agrees with the disk, so the next feature to write reads the truth.
        Assert.That(profiles.Current.SeenFirstActiveHint, Is.True);
        Assert.That(profiles.Current.HapticsEnabled, Is.False);
    }

    [Test]
    public void Haptics_FailedWriteDoesNotThrow()
    {
        var store = new InMemorySaveStore();
        var profiles = new ProfileStore(store);
        HapticsSettings settings = HapticsSettings.FromStore(profiles);

        store.FailNextWrite();

        // The message moved with the write: `ProfileStore.Save` owns the fire-and-forget and the
        // logged fault now, because it is the one writer of a profile (rule 5).
        LogAssert.Expect(LogType.Error, new Regex("Could not save the player profile"));

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

    [Test]
    public void Haptics_AuthorsNoProfile()
    {
        Type type = typeof(HapticsSettings);
        const BindingFlags Everything =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        // Rule 3 pinned at this class's own doors. `Apply` is gone — the profile arrives at
        // `ProfileStore.Adopt` now — and nothing here holds an `ISaveStore`, so the only way this
        // class can persist anything is the one call that copies a field onto the live profile.
        Assert.That(type.GetMethod("Apply", Everything), Is.Null);

        foreach (FieldInfo field in type.GetFields(Everything))
        {
            Assert.That(
                typeof(Soulvail.Core.Ports.ISaveStore).IsAssignableFrom(field.FieldType),
                Is.False,
                $"HapticsSettings.{field.Name} reaches the save store directly, which is how a " +
                "writer that knows one field ends up authoring the whole profile again.");
        }
    }
}
