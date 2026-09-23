using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Progression;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Tests.Core.Support;

namespace Soulvail.Tests.Core.Progression;

/// <summary>
/// GD §14.2's gate (M6-09a): what a class costs, what proves it, and what a run earned — the two
/// routes, the deed nobody can do yet, and the starter that needs no entry.
/// </summary>
/// <remarks>
/// <b>The catalog here authors the shipped table by hand</b>, because <c>Soulvail.Tests.Core</c> is
/// pure C# and cannot open an asset. The two rows that read the assets themselves —
/// <c>Unlock_ThePricesAreTheDocumentsNumbers</c> and <c>Unlock_TheGravecallersDeedCannotBeDoneYet</c>
/// — are in <c>CharacterDefinitionTests</c>, M6-07c's precedent for the same reason.
/// </remarks>
[TestFixture]
public sealed class ClassUnlocksTests
{
    private static readonly ContentId Oathbound = new ContentId("character.oathbound");
    private static readonly ContentId Gravecaller = new ContentId("character.gravecaller");
    private static readonly ContentId Emberwright = new ContentId("character.emberwright");
    private static readonly ContentId Choirmother = new ContentId("boss.choirmother");
    private static readonly ContentId Warden = new ContentId("boss.warden");

    // ---- UnlockSpec --------------------------------------------------------------------------------

    [Test]
    public void Spec_UnlockCarriesItsThree()
    {
        var unlock = new UnlockSpec(3500, deedStage: 20);

        Assert.That(unlock.ShardPrice, Is.EqualTo(3500));
        Assert.That(unlock.DeedStage, Is.EqualTo(20));
        Assert.That(unlock.DeedBossId, Is.EqualTo(default(ContentId)));
        Assert.That(unlock.HasDeed, Is.True);

        Assert.That(new UnlockSpec(2000, deedBossId: Choirmother).DeedBossId, Is.EqualTo(Choirmother));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Spec_RefusesAFreePrice(int price)
    {
        // Rule 3: a free class authors null rather than a price of nothing.
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(() => new UnlockSpec(price));

        Assert.That(thrown.Message, Does.Contain("starter"));
    }

    [Test]
    public void Spec_RefusesTwoDeeds()
    {
        Assert.Throws<ArgumentException>(() => new UnlockSpec(2000, deedStage: 20, deedBossId: Choirmother));
    }

    [Test]
    public void Spec_RefusesANegativeDeedStage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new UnlockSpec(2000, deedStage: -1));
    }

    [Test]
    public void Spec_PriceOnlyHasNoDeed()
    {
        Assert.That(new UnlockSpec(2000).HasDeed, Is.False);
    }

    // ---- The gate (rule 3) ------------------------------------------------------------------------

    [Test]
    public void Unlock_TheStarterIsAlwaysPlayable()
    {
        PlayerProfile profile = PlayerProfile.Default;

        Assert.That(ClassUnlocks.IsUnlocked(Oathbound, profile, Catalog()), Is.True);
        Assert.That(profile.UnlockedCharacterIds, Is.Empty, "and not because the list names it.");
    }

    [Test]
    public void Unlock_TheOtherTwoAreNotByDefault()
    {
        Assert.That(ClassUnlocks.IsUnlocked(Gravecaller, PlayerProfile.Default, Catalog()), Is.False);
        Assert.That(ClassUnlocks.IsUnlocked(Emberwright, PlayerProfile.Default, Catalog()), Is.False);
    }

    [Test]
    public void Unlock_AnIdInTheListIsUnlocked()
    {
        PlayerProfile profile = PlayerProfile.Default.WithUnlocked(new[] { Emberwright });

        Assert.That(ClassUnlocks.IsUnlocked(Emberwright, profile, Catalog()), Is.True);
    }

    [Test]
    public void Unlock_CanBuyIsAboutTheBalance()
    {
        Assert.That(ClassUnlocks.CanBuy(Emberwright, PlayerProfile.Default.WithShards(3499), Catalog()), Is.False);
        Assert.That(ClassUnlocks.CanBuy(Emberwright, PlayerProfile.Default.WithShards(3500), Catalog()), Is.True);
    }

    [Test]
    public void Unlock_CanBuyIsFalseForWhatIsOwned()
    {
        PlayerProfile profile = PlayerProfile.Default.WithShards(9999).WithUnlocked(new[] { Gravecaller });

        Assert.That(ClassUnlocks.CanBuy(Gravecaller, profile, Catalog()), Is.False, "there is nothing to buy.");
    }

    [TestCase(0)]
    [TestCase(99999)]
    public void Unlock_CanBuyIsFalseForTheStarter(int shards)
    {
        Assert.That(ClassUnlocks.CanBuy(Oathbound, PlayerProfile.Default.WithShards(shards), Catalog()), Is.False);
    }

    [Test]
    public void Unlock_RefusesNullsAndNothing()
    {
        Assert.Throws<ArgumentNullException>(() => ClassUnlocks.IsUnlocked(Oathbound, PlayerProfile.Default, null));
        Assert.Throws<ArgumentNullException>(() => ClassUnlocks.CanBuy(Oathbound, PlayerProfile.Default, null));
        Assert.Throws<ArgumentException>(() => ClassUnlocks.IsUnlocked(default, PlayerProfile.Default, Catalog()));
        Assert.Throws<ArgumentNullException>(
            () => ClassUnlocks.Earned(20, null, PlayerProfile.Default, Catalog(), new ContentId[3]));
        Assert.Throws<ArgumentNullException>(
            () => ClassUnlocks.Earned(20, Descent(), PlayerProfile.Default, null, new ContentId[3]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ClassUnlocks.Earned(0, Descent(), PlayerProfile.Default, Catalog(), new ContentId[3]));
    }

    // ---- The deeds (rules 4 and 8) ----------------------------------------------------------------

    [Test]
    public void Unlock_TheEmberwrightsDeedIsDepth()
    {
        var destination = new ContentId[3];

        Assert.That(ClassUnlocks.Earned(19, Descent(), PlayerProfile.Default, Catalog(), destination), Is.Zero);

        int written = ClassUnlocks.Earned(20, Descent(), PlayerProfile.Default, Catalog(), destination);

        // Inclusive: dying on stage 20 reached it.
        Assert.That(written, Is.EqualTo(1));
        Assert.That(destination[0], Is.EqualTo(Emberwright));
    }

    [Test]
    public void Unlock_ADeedEarnedTwiceIsEarnedOnce()
    {
        PlayerProfile profile = PlayerProfile.Default.WithUnlocked(new[] { Emberwright });

        Assert.That(ClassUnlocks.Earned(25, Descent(), profile, Catalog(), new ContentId[3]), Is.Zero);
    }

    [Test]
    public void Unlock_ABossDeedIsKillingItAndLeaving()
    {
        // The deed route that has no shipped instance yet, driven through a mode that does author
        // the boss — so the day M7-03 authors the Choirmother, this is the behaviour it gets.
        // Exclusive, for BossesKilled' reason: a boss is only known dead once its stage is left.
        ModeSpec choir = Mode((Choirmother, 5));

        Assert.That(ClassUnlocks.Earned(5, choir, PlayerProfile.Default, Catalog(), new ContentId[3]), Is.Zero);

        var destination = new ContentId[3];

        Assert.That(ClassUnlocks.Earned(6, choir, PlayerProfile.Default, Catalog(), destination), Is.EqualTo(1));
        Assert.That(destination[0], Is.EqualTo(Gravecaller));
    }

    [Test]
    public void Unlock_EarnedRefusesAShortBuffer()
    {
        // A dropped id is a class a player earned and was never given.
        Assert.Throws<ArgumentException>(
            () => ClassUnlocks.Earned(20, Descent(), PlayerProfile.Default, Catalog(), Span<ContentId>.Empty));
    }

    // ---- Guards (rules 9 and 11, and GD §14.3) ----------------------------------------------------

    [Test]
    public void Unlocks_HoldNoState()
    {
        // Payout_HoldsNoState's row, for its reason: a static class is only not a violation of
        // AR §13 while it has nothing to mutate.
        FieldInfo[] fields = typeof(ClassUnlocks).GetFields(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);

        Assert.That(
            fields,
            Is.Empty,
            "ClassUnlocks has a field. The day it needs a collaborator it becomes an injected object.");
    }

    [Test]
    public void Payout_AllocatesNothingPerTick()
    {
        // Rule 11: the gate is dictionary probes and a walk of a short list. It is not on a frame
        // path today; M6-09b's card will ask it per draw, and this is the number it inherits.
        ContentCatalog catalog = Catalog();
        PlayerProfile profile = PlayerProfile.Default.WithShards(3000).WithUnlocked(new[] { Gravecaller });

        AllocationAssert.None(
            () =>
            {
                ClassUnlocks.IsUnlocked(Emberwright, profile, catalog);
                ClassUnlocks.CanBuy(Emberwright, profile, catalog);
            },
            iterations: 100_000);
    }

    [Test]
    public void Profile_CarriesNoNumberThatAffectsARun()
    {
        // GD §14.3's "no permanent power progression", asserted in the task that could smuggle one
        // in. Every PlayerProfile getter, and every method in Soulvail.Core outside Core.Save that
        // calls one: the only reader is ClassUnlocks, which answers yes-or-no about picking a class
        // and writes ids — nothing it returns can reach a Stat, a ThreatBudget or a Health.
        // MetArchetypeIds reaches the payout by value, through RunConfig, and never as a profile.
        MethodInfo[] getters = typeof(PlayerProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.GetMethod)
            .ToArray();

        const BindingFlags everything = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        string[] readers = typeof(PlayerProfile).Assembly.GetTypes()
            .Where(type => type.Namespace != typeof(PlayerProfile).Namespace && type != typeof(PlayerProfile))
            .SelectMany(type => type.GetMethods(everything).Cast<MethodBase>().Concat(type.GetConstructors(everything)))
            .Where(method => getters.Any(getter => Calls(method, getter)))
            .Select(method => method.DeclaringType.Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(
            readers,
            Is.EqualTo(new[] { nameof(ClassUnlocks) }),
            $"PlayerProfile is read in Soulvail.Core by: {string.Join(", ", readers)}. A profile field " +
            "that reaches a run's numbers is a permanent power layer, which GD §14.3 forbids.");

        foreach (MethodInfo method in typeof(ClassUnlocks).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            Assert.That(
                method.ReturnType == typeof(bool) || method.ReturnType == typeof(int),
                Is.True,
                $"ClassUnlocks.{method.Name} returns {method.ReturnType.Name}; the gate answers and counts, nothing else.");
        }
    }

    // ---- Fixtures ---------------------------------------------------------------------------------

    /// <summary>GD §14.2's table, authored as the three shipped assets author it.</summary>
    private static ContentCatalog Catalog() => new ContentCatalog(new[]
    {
        Class(Oathbound, unlock: null),
        Class(Gravecaller, new UnlockSpec(2000, deedBossId: Choirmother)),
        Class(Emberwright, new UnlockSpec(3500, deedStage: 20)),
    });

    /// <summary>Descent's boss schedule: the Warden every fifth stage, and no Choirmother.</summary>
    private static ModeSpec Descent() => Mode((Warden, 5));

    private static ModeSpec Mode(params (ContentId Id, int Every)[] bosses) => new ModeSpec(
        new ContentId("mode.descent"),
        new LocKey("mode.descent.name"),
        startingStage: 1,
        isEndless: true,
        finalStage: 0,
        Scalings.Design(),
        Scalings.Xp(),
        Array.Empty<RosterEntry>(),
        arenas: null,
        bossRoster: bosses.Select(boss => new BossRosterEntry(boss.Id, boss.Every)).ToArray());

    private static CharacterSpec Class(ContentId id, UnlockSpec unlock) => new CharacterSpec(
        id,
        new LocKey(id.Value + ".name"),
        new LocKey(id.Value + ".description"),
        100f,
        new MovementSpec(4f, 0.06f, 0.08f, 720f),
        new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
        new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f),
        new FocusSpec(0.4f, 1f, 1f),
        new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f),
        unlock: unlock);

    /// <summary>
    /// Whether <paramref name="method"/>'s body calls <paramref name="target"/> —
    /// <c>ClassVeilrotTests.Calls</c>' IL sweep for <c>call</c> and <c>callvirt</c>.
    /// </summary>
    private static bool Calls(MethodBase method, MethodInfo target)
    {
        byte[] il;

        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (Exception)
        {
            return false;
        }

        if (il is null)
        {
            return false;
        }

        Type[] typeArgs = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;

        for (int i = 0; i + 4 < il.Length; i++)
        {
            // call (0x28) and callvirt (0x6F).
            if (il[i] != 0x28 && il[i] != 0x6F)
            {
                continue;
            }

            try
            {
                if (method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), typeArgs, methodArgs) == target)
                {
                    return true;
                }
            }
            catch (Exception)
            {
                // Not a token; the window fell inside somebody else's operand.
            }
        }

        return false;
    }
}
