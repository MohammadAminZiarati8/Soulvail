using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The one holder of the live profile, and the only thing in the app that writes one (M3-09c rules
/// 3, 4 and 5).
/// </summary>
/// <remarks>
/// <para>
/// <b>What these rows are about is the copy-through</b> — that a write moves the field it was given
/// and carries every other one with it — and the shape that stops a second writer from existing.
/// The field a second writer used to erase is pinned from the other side, in
/// <c>HapticsSettingsTests.Haptics_ToggleKeepsTheHintFlag</c>, because that is the writer it
/// happened to.
/// </para>
/// <para>
/// <c>InMemorySaveStore</c> behind a <see cref="CountingStore"/>: the fake answers and counts
/// writes, and the wrapper adds the one thing it does not count — reads — because
/// <see cref="Store_StartsAtDefault"/> is a claim about the disk not being touched at all.
/// </para>
/// </remarks>
[TestFixture]
public sealed class ProfileStoreTests
{
    private InMemorySaveStore _inner;
    private CountingStore _store;
    private ProfileStore _profiles;

    [SetUp]
    public void CreateStore()
    {
        _inner = new InMemorySaveStore();
        _store = new CountingStore(_inner);
        _profiles = new ProfileStore(_store);
    }

    [Test]
    public void Construct_NullStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ProfileStore(null));
    }

    [Test]
    public void Store_StartsAtDefault()
    {
        PlayerProfile current = _profiles.Current;

        // GD §16.3's answers rather than version 0's invalid ones, so a reader that arrives before
        // BootFlow's continuation gets something true rather than something unwritten.
        Assert.That(current.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
        Assert.That(current.HapticsEnabled, Is.True);
        Assert.That(current.SeenFirstActiveHint, Is.False);
        Assert.That(current.Shards, Is.Zero, "a player who has never died has never been paid.");

        // **And it asked the disk for nothing.** A store that loaded at construction would have to
        // block on I/O or return before the value it promised had arrived — BootFlow owns the one
        // read, and hands the result over through Adopt.
        Assert.That(_store.ProfileReadCount, Is.EqualTo(0));
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(0));
    }

    [Test]
    public void Store_AdoptDoesNotWrite()
    {
        var loaded = new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: false, seenFirstActiveHint: true, shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "");

        _profiles.Adopt(loaded);

        Assert.That(_profiles.Current.HapticsEnabled, Is.False);
        Assert.That(_profiles.Current.SeenFirstActiveHint, Is.True);

        // Rule 4: a load that immediately re-saves is a load that can corrupt what it just read, and
        // on a fresh install — where BootFlow substitutes PlayerProfile.Default for a file that does
        // not exist — it would put a profile on disk for a player who has changed nothing.
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(0));
    }

    [Test]
    public void Store_SaveWritesAndUpdates()
    {
        var profile = new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: false, seenFirstActiveHint: true, shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "");

        _profiles.Save(profile);

        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "one call, not one per field.");

        // The live profile and the disk agree, which is the whole promise of there being one holder.
        Assert.That(_profiles.Current.HapticsEnabled, Is.False);
        Assert.That(_profiles.Current.SeenFirstActiveHint, Is.True);

        PlayerProfile written = _inner.LoadProfile().GetAwaiter().GetResult().Value;

        Assert.That(written.HapticsEnabled, Is.False);
        Assert.That(written.SeenFirstActiveHint, Is.True);
        Assert.That(written.Version, Is.EqualTo(PlayerProfile.CurrentVersion));
    }

    [Test]
    public void Store_CopiesThroughOneFieldAtATime()
    {
        _profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: true, seenFirstActiveHint: false,
            shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: ""));

        _profiles.Save(_profiles.Current.WithSeenFirstActiveHint(true));
        _profiles.Save(_profiles.Current.WithHaptics(false));

        // **Three features as of M4-05b**, and the third is the one that would have proved the rule
        // expensive: `ShardWriter` knows nothing about haptics or the hint, and a write that reset
        // either would do it on the frame the player died.
        _profiles.Save(_profiles.Current.WithShards(220));

        // Three writes, and none erased another's field. This is the shape the whole task is for:
        // `Current` is read, one field is moved, and the result is handed back.
        PlayerProfile written = _inner.LoadProfile().GetAwaiter().GetResult().Value;

        Assert.That(written.SeenFirstActiveHint, Is.True);
        Assert.That(written.HapticsEnabled, Is.False);
        Assert.That(written.Shards, Is.EqualTo(220));
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(3));
    }

    [Test]
    public void Store_SaveFailureIsLoggedNotThrown()
    {
        _inner.FailNextWrite();

        LogAssert.Expect(LogType.Error, new Regex("Could not save the player profile"));

        var profile = new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: true, seenFirstActiveHint: true, shards: 0, Array.Empty<ContentId>(), Array.Empty<ContentId>(), locale: "");

        // Rule 5: a profile write that fails must not take down a run, and the smallest possible
        // consequence in the project is a hint shown twice because a disk was full.
        Assert.DoesNotThrow(() => _profiles.Save(profile));

        // And `Current` stays moved. The hint the player has already seen has already been seen; a
        // store that rolled back to match the disk would disagree with them about what happened in
        // front of them.
        Assert.That(_profiles.Current.SeenFirstActiveHint, Is.True);
    }

    // ---- M6-09a: buying a class --------------------------------------------------------------------

    /// <summary>M6-09b's name for M6-09a's row: one save, and both fields moved in it.</summary>
    [Test]
    public void Store_UnlockSpendsAndOwnsInOneWrite()
    {
        _profiles.Adopt(PlayerProfile.Default.WithShards(4000).WithHaptics(false));

        _profiles.Unlock(Emberwright, ShippedCatalog());

        // GD §14.2's 3 500 off the balance and the class on the list, in one write: a purchase in
        // two saves would leave a crash between them charging for nothing.
        Assert.That(_profiles.Current.Shards, Is.EqualTo(500));
        Assert.That(_profiles.Current.UnlockedCharacterIds, Is.EqualTo(new[] { Emberwright }));
        Assert.That(_profiles.Current.HapticsEnabled, Is.False, "and nothing else moved.");
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1));
    }

    [Test]
    public void Store_UnlockRefusesWhatCanBuyRefuses()
    {
        ContentCatalog catalog = ShippedCatalog();

        _profiles.Adopt(PlayerProfile.Default.WithShards(3499));

        // Short by one, the starter, and — after a purchase — the same class twice.
        Assert.Throws<InvalidOperationException>(() => _profiles.Unlock(Emberwright, catalog));
        Assert.Throws<InvalidOperationException>(() => _profiles.Unlock(new ContentId("character.oathbound"), catalog));

        _profiles.Adopt(PlayerProfile.Default.WithShards(9000));
        _profiles.Unlock(Emberwright, catalog);

        Assert.Throws<InvalidOperationException>(() => _profiles.Unlock(Emberwright, catalog));
        Assert.That(_profiles.Current.Shards, Is.EqualTo(5500), "a refused purchase charges nothing.");
        Assert.Throws<ArgumentNullException>(() => _profiles.Unlock(Emberwright, null));
    }

    // ---- M6-09b: the purchase the class-select screen makes ------------------------------------------

    [Test]
    public void Store_UnlockRefusesWhatCannotBeBought()
    {
        _profiles.Save(PlayerProfile.Default.WithShards(3_499));

        Assert.Throws<InvalidOperationException>(() => _profiles.Unlock(Emberwright, ShippedCatalog()));

        // The disk, not only Current: a refusal wrote nothing, so the file still reads 3 499.
        PlayerProfile written = _inner.LoadProfile().GetAwaiter().GetResult().Value;

        Assert.That(written.Shards, Is.EqualTo(3_499));
        Assert.That(written.UnlockedCharacterIds, Is.Empty);
        Assert.That(_store.ProfileWriteCount, Is.EqualTo(1), "the refusal wrote.");
    }

    [Test]
    public void Store_UnlockRefusesWhatIsOwned()
    {
        _profiles.Adopt(PlayerProfile.Default.WithShards(9_000).WithUnlocked(new[] { Emberwright }));

        Assert.Throws<InvalidOperationException>(() => _profiles.Unlock(Emberwright, ShippedCatalog()));

        Assert.That(_profiles.Current.Shards, Is.EqualTo(9_000), "an owned class was charged for twice.");
        Assert.That(_store.ProfileWriteCount, Is.Zero);
    }

    [Test]
    public void Store_UnlockTouchesNothingElse()
    {
        var met = new[] { new ContentId("enemy.husk"), new ContentId("enemy.spitter") };

        _profiles.Adopt(new PlayerProfile(
            PlayerProfile.CurrentVersion, hapticsEnabled: false, seenFirstActiveHint: true,
            shards: 4_000, new[] { new ContentId("character.gravecaller") }, met, locale: "en"));

        _profiles.Unlock(Emberwright, ShippedCatalog());

        PlayerProfile after = _profiles.Current;

        Assert.That(after.HapticsEnabled, Is.False);
        Assert.That(after.SeenFirstActiveHint, Is.True);
        Assert.That(after.MetArchetypeIds, Is.EqualTo(met));
        Assert.That(after.Locale, Is.EqualTo("en"));

        // Appended, not replaced: the Gravecaller a v3 profile kept is still owned.
        Assert.That(
            after.UnlockedCharacterIds,
            Is.EqualTo(new[] { new ContentId("character.gravecaller"), Emberwright }));
    }

    private static readonly ContentId Emberwright = new ContentId("character.emberwright");

    /// <summary>The three shipped classes, converted as boot converts them.</summary>
    private static ContentCatalog ShippedCatalog()
    {
        string[] classes = { "Oathbound", "Gravecaller", "Emberwright" };
        var characters = new CharacterSpec[classes.Length];

        for (int i = 0; i < classes.Length; i++)
        {
            characters[i] = AssetDatabase
                .LoadAssetAtPath<CharacterDefinition>($"Assets/_Project/Data/Characters/{classes[i]}.asset")
                .ToSpec();
        }

        return new ContentCatalog(characters);
    }

    /// <summary>An <see cref="ISaveStore"/> that counts the profile calls its inner one does not.</summary>
    private sealed class CountingStore : ISaveStore
    {
        private readonly InMemorySaveStore _inner;

        public CountingStore(InMemorySaveStore inner)
        {
            _inner = inner;
        }

        public int ProfileReadCount { get; private set; }

        public int ProfileWriteCount => _inner.ProfileWriteCount;

        public Task<PlayerProfile?> LoadProfile()
        {
            ProfileReadCount++;
            return _inner.LoadProfile();
        }

        public Task SaveProfile(PlayerProfile profile) => _inner.SaveProfile(profile);

        public Task<RunSnapshot?> LoadRun() => _inner.LoadRun();

        public Task SaveRun(RunSnapshot run) => _inner.SaveRun(run);

        public Task ClearRun() => _inner.ClearRun();
    }
}
