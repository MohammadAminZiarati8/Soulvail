using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// The third class, number by number, each against the document it comes from or the ruling that
/// moved it — <c>GravecallerTests</c>' shape, one class on. M6-07a rules 1, 2 and 8–10.
/// </summary>
/// <remarks>
/// <para>
/// <b>One number is not CH §3.3's, and the rows say which.</b> The Cinder Orb deals 17 rather than
/// 30: thirty kills a stage-1 Husk in two hits, under GD §6.2's three-to-five, before a run has
/// started. <see cref="Orb_KillsAStageOneHuskInThreeHits"/> is the ruling and
/// <see cref="Orb_AtTheDocumentedThirtyWouldBreakTheBand"/> is its control, so a row that passes is a
/// row that measured something rather than one that restated a constant.
/// </para>
/// <para>
/// <b>Everything here reads the shipped assets</b>, because <c>Soulvail.Tests.Core</c> cannot
/// (M0-10). The arithmetic of the ramp itself is <c>KindlingTests</c>', which writes 17 out and
/// meets this fixture at the number.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EmberwrightTests
{
    private const string EmberwrightPath = "Assets/_Project/Data/Characters/Emberwright.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string HuskPath = "Assets/_Project/Data/Enemies/Husk.asset";
    private const string SpitterPath = "Assets/_Project/Data/Enemies/Spitter.asset";
    private const string DescentPath = "Assets/_Project/Data/Modes/Descent.asset";
    private const string BootScopePath = "Assets/_Project/Prefabs/Composition/BootScope.prefab";
    private const string ClassSelectPath = "Assets/_Project/Prefabs/UI/ClassSelect.prefab";
    private const string EnglishPath = "Assets/_Project/Data/Localisation/English.asset";

    private const string EmberwrightId = "character.emberwright";
    private const string GravecallerId = "character.gravecaller";
    private const string OathboundId = "character.oathbound";

    /// <summary>What CH §3.3's table publishes for the orb, and M6-07a rule 2's control.</summary>
    private const float DocumentedOrbDamage = 30f;

    /// <summary>
    /// The last stage at which the documented 30 is still two hits on the shipped curve —
    /// h(12) = 1.66, so 59.76 HP. h(13) = 1.72 and 61.92, which is three.
    /// </summary>
    private const int LastStageUnderTheBandAtThirty = 12;

    private const int BandLow = 3;
    private const int BandHigh = 5;

    private const int EnemyCapacity = 8;
    private const int ProjectileCapacity = 8;
    private const int Seed = 11;

    /// <summary><c>GravecallerTests</c>' tolerance, for its reason.</summary>
    private const float Tolerance = 1e-6f;

    private readonly List<Object> _spawned = new List<Object>();

    [TearDown]
    public void DestroySpawned()
    {
        foreach (Object spawned in _spawned)
        {
            if (spawned != null)
            {
                Object.DestroyImmediate(spawned);
            }
        }

        _spawned.Clear();
    }

    // ---- Rule 1: the block is optional, and null on both shipped classes --------------------------

    [Test]
    public void Kindling_IsNullOnBothShippedClasses()
    {
        Assert.That(Character(OathboundPath).Kindling, Is.Null);
        Assert.That(Character(GravecallerPath).Kindling, Is.Null);

        // And neither file was rewritten to say so. M4-01a's measured finding: Unity does not
        // re-serialise an asset because the script that reads it grew a field, so neither carries a
        // Kindling line at all — and the day one does, it must read 0. Asked of the file rather than
        // of `git status`, for GravecallerTests.Minions_AreNullOnTheOathbound's reason: a
        // re-serialised asset is correct content, and a row that reddened for it would be reporting a
        // diff rather than a defect.
        foreach (string path in new[] { OathboundPath, GravecallerPath })
        {
            foreach (string line in File.ReadAllLines(path))
            {
                string trimmed = line.Trim();

                if (trimmed.StartsWith("_kindlingMaxStacks:", StringComparison.Ordinal))
                {
                    Assert.That(trimmed, Is.EqualTo("_kindlingMaxStacks: 0"),
                        $"{path} carries Kindling, which is the Emberwright's signature alone.");
                }
            }
        }
    }

    [Test]
    public void Kindling_IsTheAuthoredBlock()
    {
        KindlingSpec kindling = Emberwright().Kindling;

        Assert.That(kindling, Is.Not.Null, "CH §3.3's signature is the class.");
        Assert.That(kindling.PerStack, Is.EqualTo(0.02f).Within(Tolerance), "CH §3.3: +2 % a hit.");
        Assert.That(kindling.MaxStacks, Is.EqualTo(30), "CH §3.3's +60 %, divided by the 2 %.");
        Assert.That(kindling.MaxBonus, Is.EqualTo(0.60f).Within(1e-5f));
    }

    // ---- Rule 2: the class ----------------------------------------------------------------------------

    [Test]
    public void Emberwright_ConvertsAndIsInTheCatalog()
    {
        CharacterSpec spec = Emberwright();

        Assert.That(spec.Id.Value, Is.EqualTo(EmberwrightId));
        Assert.That(spec.NameKey.Key, Is.EqualTo("character.emberwright.name"));
        Assert.That(spec.DescriptionKey.Key, Is.EqualTo("character.emberwright.description"));

        ContentCatalog catalog = BootCatalog();

        Assert.That(catalog.Characters.Count, Is.EqualTo(3), "CH §3's whole roster boots.");

        foreach (string id in new[] { OathboundId, GravecallerId, EmberwrightId })
        {
            Assert.That(catalog.Character(new ContentId(id)).Id.Value, Is.EqualTo(id));
        }
    }

    [Test]
    public void Emberwright_IsSeventyHitPointsAndNoShield()
    {
        CharacterSpec spec = Emberwright();

        Assert.That(spec.MaxHp, Is.EqualTo(70f).Within(Tolerance), "CH §3.3: 70 HP.");
        Assert.That(spec.Shield, Is.Null, "CH §3.1: the Aegis is the only regeneration in the game.");
        Assert.That(spec.Minions, Is.Null, "CH §3.3 raises nothing.");
        Assert.That(spec.HitIFrames, Is.EqualTo(0.5f).Within(Tolerance), "M5-02 rule 4's decision.");
    }

    [Test]
    public void Emberwright_SpeedIsTheTopOfTheRuledBand()
    {
        float speed = Emberwright().Movement.Speed;

        Assert.That(speed, Is.EqualTo(3.4f).Within(Tolerance), "M5-02 rule 1's ceiling: the wizard is the fastest.");
        Assert.That(speed, Is.GreaterThan(Character(OathboundPath).Movement.Speed));
        Assert.That(speed, Is.GreaterThan(Character(GravecallerPath).Movement.Speed));
    }

    [Test]
    public void Emberwright_TargetingAndFocusMatchTheOathbound()
    {
        // M5-02 rule 4's decision, pinned for the third class: CC §3 is one targeting system for
        // every class, and CC §2.5 publishes one set of handling numbers.
        CharacterSpec oathbound = Character(OathboundPath);

        foreach (CharacterSpec other in new[] { Emberwright(), Character(GravecallerPath) })
        {
            Assert.That(other.Targeting.AcquireRange, Is.EqualTo(oathbound.Targeting.AcquireRange).Within(Tolerance));
            Assert.That(other.Targeting.DistanceWeight, Is.EqualTo(oathbound.Targeting.DistanceWeight).Within(Tolerance));
            Assert.That(other.Targeting.EliteBonus, Is.EqualTo(oathbound.Targeting.EliteBonus).Within(Tolerance));
            Assert.That(other.Targeting.FinisherBonus, Is.EqualTo(oathbound.Targeting.FinisherBonus).Within(Tolerance));
            Assert.That(other.Targeting.Hysteresis, Is.EqualTo(oathbound.Targeting.Hysteresis).Within(Tolerance));
            Assert.That(other.Targeting.Cadence, Is.EqualTo(oathbound.Targeting.Cadence).Within(Tolerance));

            Assert.That(other.Focus.Delay, Is.EqualTo(oathbound.Focus.Delay).Within(Tolerance));
            Assert.That(other.Focus.RampTime, Is.EqualTo(oathbound.Focus.RampTime).Within(Tolerance));
            Assert.That(other.Focus.MaxMultiplier, Is.EqualTo(oathbound.Focus.MaxMultiplier).Within(Tolerance));

            Assert.That(other.Movement.AccelTime, Is.EqualTo(oathbound.Movement.AccelTime).Within(Tolerance));
            Assert.That(other.Movement.DecelTime, Is.EqualTo(oathbound.Movement.DecelTime).Within(Tolerance));
            Assert.That(other.Movement.TurnSpeedDeg, Is.EqualTo(oathbound.Movement.TurnSpeedDeg).Within(Tolerance));
        }
    }

    // ---- Rule 2: the weapon, and the ruling -------------------------------------------------------------

    [Test]
    public void Orb_IsTheRuledWeapon()
    {
        CharacterSpec spec = Emberwright();
        WeaponSpec orb = spec.Weapon;

        Assert.That(orb.Kind, Is.EqualTo(WeaponKind.Projectile), "CH §3.3.");
        Assert.That(orb.Damage, Is.EqualTo(17f).Within(Tolerance), "Ours — CH §3.3 says 30.");
        Assert.That(orb.SwingsPerSecond, Is.EqualTo(1.5f).Within(Tolerance), "CH §3.3.");
        Assert.That(orb.Range, Is.EqualTo(12f).Within(Tolerance), "Ours: the acquire range.");
        Assert.That(orb.Range, Is.EqualTo(spec.Targeting.AcquireRange).Within(Tolerance),
            "M5-02 rule 3: a ranged class fires at everything it can acquire.");
        Assert.That(orb.ConeAngleDeg, Is.EqualTo(360f).Within(Tolerance), "WeaponSpec's one spelling for a projectile.");
        Assert.That(orb.DamageFrame, Is.EqualTo(0.15f).Within(Tolerance), "Ours: a release, 100 ms in.");
        Assert.That(orb.ShotSpeed, Is.EqualTo(25f).Within(Tolerance), "CH §3.3's slow 25 m/s.");
        Assert.That(orb.ShotRadius, Is.EqualTo(3f).Within(Tolerance), "CH §3.3's 3 m detonation.");
        Assert.That(orb.ShotSpread, Is.Zero, "Reserved (M5-01).");
    }

    [Test]
    public void Orb_KillsAStageOneHuskInThreeHits()
    {
        int hits = Hits(HuskHpAt(1), Emberwright().Weapon.Damage);

        Assert.That(hits, Is.EqualTo(3), "ceil(36 / 17), with no tree, no Overflow and no Kindling.");
        Assert.That(hits, Is.InRange(BandLow, BandHigh), "GD §6.2's three to five, at the opening.");
    }

    [Test]
    public void Orb_AtTheDocumentedThirtyWouldBreakTheBand()
    {
        // The control that proves the ruling moved something that was actually wrong: 17 was chosen
        // because it lands in the band, so of course it does. This is CH §3.3's own number.
        Assert.That(Hits(HuskHpAt(1), DocumentedOrbDamage), Is.EqualTo(2), "ceil(36 / 30).");
        Assert.That(Hits(HuskHpAt(1), DocumentedOrbDamage), Is.LessThan(BandLow));

        // And it is not a stage-1 accident: the shipped curve keeps it under the band for twelve
        // stages, which is a third of a played run spent at two hits.
        Assert.That(Hits(HuskHpAt(LastStageUnderTheBandAtThirty), DocumentedOrbDamage), Is.EqualTo(2),
            $"h({LastStageUnderTheBandAtThirty}) × 36 against 30.");

        Assert.That(Hits(HuskHpAt(LastStageUnderTheBandAtThirty + 1), DocumentedOrbDamage), Is.EqualTo(3),
            "the boundary: the band is reached one stage later.");
    }

    [Test]
    public void Orb_IsTheSlowestKillerAndTheWidestBlast()
    {
        float husk = HuskHpAt(1);

        WeaponSpec orb = Emberwright().Weapon;
        WeaponSpec censer = Character(OathboundPath).Weapon;
        WeaponSpec bolt = Character(GravecallerPath).Weapon;

        // Hits over rate: the seconds of fire a stage-1 Husk costs. Comparisons rather than literals,
        // because the claim is the ordering CH §3.3 describes — "enormous, slow, unforgiving".
        float orbSeconds = Hits(husk, orb.Damage) / orb.SwingsPerSecond;

        Assert.That(orbSeconds, Is.EqualTo(2f).Within(1e-4f), "3 hits at 1.5 a second.");
        Assert.That(orbSeconds, Is.GreaterThan(Hits(husk, censer.Damage) / censer.SwingsPerSecond));
        Assert.That(orbSeconds, Is.GreaterThan(Hits(husk, bolt.Damage) / bolt.SwingsPerSecond));

        EnemySpec spitter = Enemy(SpitterPath);

        Assert.That(orb.ShotRadius, Is.GreaterThan(bolt.ShotRadius), "wider than the Bone Bolt.");
        Assert.That(orb.ShotRadius, Is.GreaterThan(spitter.Projectile.Radius), "wider than a Spitter's.");
    }

    [Test]
    public void Orb_CatchesEverythingInsideThreeMetres()
    {
        CharacterSpec spec = Emberwright();
        var events = new RecordingEvents();

        EnemySystem enemies = Enemies(events);
        var player = new PlayerCombat(spec, events, new RecordingIntents(), EnemyCapacity);
        var projectiles = new ProjectileSystem(events, ProjectileCapacity);

        var impact = new Vector3(0f, 0f, 10f);

        // Four Husks on a 2.5 m ring around the impact — all inside 3 m, none on top of it.
        for (int i = 0; i < 4; i++)
        {
            float angle = i * MathF.PI / 2f;

            enemies.Spawn(
                new ContentId("enemy.husk"),
                impact + new Vector3(2.5f * MathF.Cos(angle), 0f, 2.5f * MathF.Sin(angle)));
        }

        projectiles.Fire(Orb(spec, impact), 0f);
        projectiles.Tick(5f, Vector3.Zero, player, enemies);

        IReadOnlyList<EnemyDamaged> damaged = events.Of<EnemyDamaged>();

        Assert.That(damaged.Count, Is.EqualTo(4), "LandOnEnemies reaches everything in the radius.");

        foreach (EnemyDamaged hit in damaged)
        {
            Assert.That(hit.Amount, Is.EqualTo(17f).Within(Tolerance), "the full orb, to each.");
        }
    }

    [Test]
    public void Orb_TheLeadStillDecidesHowManyItCatches()
    {
        // A column of five Husks 1.4 m apart, walking across the line of fire at their shipped speed.
        // A led orb lands centred on the one it was aimed at; an unled one lands where it was, which
        // the column has walked off by a metre — so the 3 m blast forgives the aim and still catches
        // fewer. That difference is what CC §3.7's lead is for on a weapon this wide.
        float speed = Enemy(HuskPath).MoveSpeed;
        var velocity = new Vector3(speed, 0f, 0f);
        var target = new Vector3(0f, 0f, 12f);

        Vector3 led = ProjectileLead.Solve(Vector3.Zero, target, velocity, Emberwright().Weapon.ShotSpeed);

        int ledCaught = CaughtFromAColumn(led, velocity);
        int unledCaught = CaughtFromAColumn(target, velocity);

        Assert.That(ledCaught, Is.EqualTo(5), "the led orb lands on the column's centre.");
        Assert.That(ledCaught, Is.GreaterThan(unledCaught), "the lead is not pointless at 3 m.");
    }

    // ---- Rule 8, and M6-07b: the Blink and its fire ---------------------------------------------------

    [Test]
    public void Emberwright_MovementIsABlink()
    {
        MovementSkillSpec blink = Emberwright().MovementSkill;

        Assert.That(blink.Kind, Is.EqualTo(MovementSkillKind.Blink), "CH §3.3.");
        Assert.That(blink.Distance, Is.EqualTo(10f).Within(Tolerance), "CH §3.3: 10 m.");
        Assert.That(blink.Duration, Is.EqualTo(0.05f).Within(Tolerance), "CH §3.3's instant, the Shroudstep's 0.05.");
        Assert.That(blink.Cooldown, Is.EqualTo(2f).Within(Tolerance), "CH §3.3: 2.0 s.");
        Assert.That(blink.Damage, Is.Zero, "a blink hits nothing on the way.");
        Assert.That(blink.Knockback, Is.Zero, "…and shoves nothing.");
        Assert.That(blink.DecoyDuration, Is.Zero, "a Blink leaves fire, not a corpse.");
        Assert.That(blink.IFrameTrail, Is.EqualTo(0.05f).Within(Tolerance), "CC §5's touch-latency trail.");
        Assert.That(blink.InputBuffer, Is.EqualTo(0.15f).Within(Tolerance), "CC §5's input buffer.");
    }

    [Test]
    public void Blink_TheShippedOneLeavesFireAndNothingElse()
    {
        // M6-07a's Blink_LeavesNothingYet, rewritten the day the fire arrived: the shipped asset,
        // blinked through a zone system wired the way RunSession wires one.
        CharacterSpec spec = Emberwright();
        var events = new RecordingEvents();
        var intents = new RecordingIntents();

        EnemySystem enemies = Enemies(events);
        var player = new PlayerCombat(spec, events, intents, EnemyCapacity);
        var lures = new LureSystem(events);
        var zones = new ZoneSystem(player.Health, player.Blackboard, events, enemies, player);

        EnemyAgent husk = enemies.Spawn(new ContentId("enemy.husk"), new Vector3(0f, 0f, 3f));
        float hp = husk.Health.Current;

        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = 1f / 60f };

        player.Charge.Request(0f);
        player.Tick(snapshot.Dt, snapshot.Dt, snapshot, enemies.Registry.Alive, Vector3.UnitZ, lures, null, zones);

        // The Charge's clock, unchanged: one start, one intent carrying the Blink's own 10 m and
        // 0.05 s, and the i-frames raised on Health.
        Assert.That(events.Count<ChargeStarted>(), Is.EqualTo(1));
        Assert.That(intents.LastCharge.Distance, Is.EqualTo(10f).Within(Tolerance));
        Assert.That(intents.LastCharge.Duration, Is.EqualTo(0.05f).Within(Tolerance));
        Assert.That(player.Health.IsInvulnerable, Is.True);

        player.ResolveChargeHits(new[] { husk.Id }, snapshot.Dt, enemies);

        Assert.That(husk.Health.Current, Is.EqualTo(hp), "no damage on the way — the fire is what burns.");
        Assert.That(lures.Count, Is.Zero, "no decoy — that is the Shroudstep's payload.");
        Assert.That(events.Count<DecoySpawned>(), Is.Zero);

        ZoneSpawned pool = events.Single<ZoneSpawned>();

        Assert.That(pool.Position, Is.EqualTo(Vector3.Zero), "where the blink left.");
        Assert.That(pool.Radius, Is.EqualTo(3f).Within(Tolerance));
        Assert.That(pool.Duration, Is.EqualTo(3f).Within(Tolerance));
        Assert.That(zones.SideAt(0), Is.EqualTo(ZoneSide.BurnsEnemies));
    }

    [Test]
    public void Emberwright_CarriesThePoolNumbers()
    {
        MovementSkillSpec blink = Emberwright().MovementSkill;

        // M6-07b rule 5. CH §3.3 gives the pool no numbers, so all three are ours.
        Assert.That(blink.PoolRadius, Is.EqualTo(3f).Within(Tolerance), "Ours: the orb's blast, one distance the class reads by.");
        Assert.That(blink.PoolDuration, Is.EqualTo(3f).Within(Tolerance), "Ours: against a 2.0 s cooldown, so two overlap for a second.");
        Assert.That(blink.PoolDamagePerPulse, Is.EqualTo(4f).Within(Tolerance), "Ours: six pulses of 4 is about one orb.");
        Assert.That(MovementSkillSpec.PoolPulseInterval, Is.EqualTo(0.5f), "Consecrate's beat, and a const.");

        Assert.That(blink.PoolRadius, Is.EqualTo(Emberwright().Weapon.ShotRadius).Within(Tolerance),
            "The pool and the orb's blast are one distance — a retune of one is a retune of both.");
    }

    [Test]
    public void Pool_IsWorthAboutOneOrb()
    {
        CharacterSpec spec = Emberwright();
        MovementSkillSpec blink = spec.MovementSkill;

        float pulses = blink.PoolDuration / MovementSkillSpec.PoolPulseInterval;
        float pool = pulses * blink.PoolDamagePerPulse;
        float orb = spec.Weapon.Damage;

        string both = $"Emberwright.asset's pool is {pool} over its life ({pulses} pulses of "
            + $"{blink.PoolDamagePerPulse}) and Emberwright.asset's orb is {orb}. M6-07b rule 5: a "
            + "blink is worth a shot, not a second weapon — retune one, and decide the other.";

        Assert.That(pool, Is.EqualTo(24f).Within(Tolerance), both);
        Assert.That(orb, Is.EqualTo(17f).Within(Tolerance), both);
        Assert.That(pool, Is.GreaterThan(orb), both);
        Assert.That(pool, Is.LessThan(2f * orb), both);
    }

    [Test]
    public void View_IsNotEdited()
    {
        // M6-07b rule 9: the pool is drawn by the decal that already exists, in the colour it already
        // uses. Pinned so the limit is a decision — a healing circle and a burning one are the same
        // cyan until M7's art pass gives the fire a texture.
        string[] fields = Array.ConvertAll(
            typeof(ZoneSpawned).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public),
            f => f.Name);

        Assert.That(fields, Is.EquivalentTo(new[] { "Id", "Position", "Radius", "Duration" }),
            "ZoneSpawned gained a field. Rule 9 refused one until something reads it.");

        foreach (Type view in new[] { typeof(Soulvail.Game.Views.ZoneView), typeof(Soulvail.Game.Views.ZoneViews) })
        {
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.DeclaredOnly;

            foreach (System.Reflection.FieldInfo field in view.GetFields(all))
            {
                Assert.That(field.FieldType, Is.Not.EqualTo(typeof(ZoneSide)), $"{view.Name}.{field.Name}");
            }

            foreach (System.Reflection.MethodInfo method in view.GetMethods(all))
            {
                foreach (System.Reflection.ParameterInfo parameter in method.GetParameters())
                {
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(ZoneSide)), $"{view.Name}.{method.Name}");
                    Assert.That(parameter.ParameterType, Is.Not.EqualTo(typeof(ZoneBurned)), $"{view.Name}.{method.Name}");
                }
            }
        }

        var go = new GameObject("ZoneView");
        _spawned.Add(go);

        var decal = go.AddComponent<Soulvail.Game.Views.ZoneView>();

        Assert.That(decal.Colour, Is.EqualTo(Palette.Heal), "one colour, GD §16.4's cyan — no second one.");
    }

    [Test]
    public void Spec_ABlinkStillRefusesADecoyDuration()
    {
        var thrown = Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new MovementSkillSpec(MovementSkillKind.Blink, 10f, 0.05f, 2f, 0.15f, 0f, 0f, 0.05f, 3f, 3f, 3f, 4f));

        Assert.That(thrown.ParamName, Is.EqualTo("decoyDuration"),
            "MovementSkillSpec.Decoy's line, unchanged by a third member: only a Shroudstep leaves one.");
    }

    // ---- Rule 9: the third card, with no prefab edit ------------------------------------------------

    [Test]
    public void Class_TheThirdCardIsBoundWithNoPrefabEdit()
    {
        string before = File.ReadAllText(ClassSelectPath);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClassSelectPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {ClassSelectPath}.");

        GameObject screen = Object.Instantiate(prefab);
        _spawned.Add(screen);

        var presenter = screen.GetComponent<ClassSelectPresenter>();

        Assert.That(presenter, Is.Not.Null, "ClassSelectPresenter did not load off its own prefab (Traps §5).");

        presenter.Construct(
            new PendingRun(),
            BootCatalog(),
            new SceneLoader(),
            new TableLocalizer(ScriptableObject.CreateInstance<LocalizationTable>()));

        presenter.Open();

        var cards = (ClassCard[])typeof(ClassSelectPresenter)
            .GetField("_cards", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .GetValue(presenter);

        Assert.That(cards.Length, Is.EqualTo(3), "the prefab authors CH §3's roster of three.");

        foreach (ClassCard card in cards)
        {
            Assert.That(card.IsShown, Is.True, "every card is bound: the catalog holds three classes.");
        }

        Assert.That(cards[2].CharacterId.Value, Is.EqualTo(EmberwrightId),
            "ClassCard.Clear's own prediction: the Emberwright's arrival is a card being filled.");

        Assert.That(File.ReadAllText(ClassSelectPath), Is.EqualTo(before), "and the prefab on disk did not move.");
    }

    // ---- Traps §5 and rule 10 -------------------------------------------------------------------------

    [Test]
    public void Assets_AreLinkedToAMonoScript()
    {
        var asset = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(EmberwrightPath);

        Assert.That(asset, Is.Not.Null, $"{EmberwrightPath} did not load as a CharacterDefinition.");

        MonoScript script = MonoScript.FromScriptableObject(asset);

        Assert.That(script, Is.Not.Null, $"{EmberwrightPath}: m_Script does not resolve (Traps §5).");
        Assert.That(script.GetClass(), Is.EqualTo(typeof(CharacterDefinition)));
    }

    [Test]
    public void Keys_ResolveInEnglish()
    {
        CharacterSpec spec = Emberwright();
        var english = new TableLocalizer(AssetDatabase.LoadAssetAtPath<LocalizationTable>(EnglishPath));

        foreach (LocKey key in new[] { spec.NameKey, spec.DescriptionKey })
        {
            Assert.That(english.Has(key), Is.True, $"English.asset has no row for {key}.");
            Assert.That(english.Get(key), Is.Not.Empty, $"{key} resolves to an empty string.");
        }
    }

    // ---- Fixtures -------------------------------------------------------------------------------------

    /// <summary>Whole hits, because a thing at one hit point is still standing.</summary>
    private static int Hits(float hp, float damagePerHit) =>
        (int)Math.Ceiling(hp / damagePerHit);

    private static CharacterSpec Emberwright() => Character(EmberwrightPath);

    private static CharacterSpec Character(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition.ToSpec();
    }

    private static EnemySpec Enemy(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<EnemyDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No EnemyDefinition at {path}.");

        return definition.ToSpec();
    }

    private static ModeSpec Descent()
    {
        var definition = AssetDatabase.LoadAssetAtPath<ModeDefinition>(DescentPath);

        Assert.That(definition, Is.Not.Null, $"No ModeDefinition at {DescentPath}.");

        return definition.ToSpec();
    }

    /// <summary>
    /// A Husk's maximum hit points at <paramref name="stage"/>, through the shipped
    /// <see cref="DepthScaling"/> and <c>Descent.asset</c>'s own curve — <c>TimeToKillTests</c>'
    /// <c>ScaledHuskHp</c>, reading the file rather than a copy of it.
    /// </summary>
    private static float HuskHpAt(int stage)
    {
        var registry = new EnemyRegistry(EnemyCapacity);
        EnemyAgent husk = registry.Spawn(Enemy(HuskPath), Vector3.Zero);

        new DepthScaling(Descent().Scaling).Apply(husk, stage);

        return husk.Health.MaxHp.Value;
    }

    private static EnemySystem Enemies(RecordingEvents events) => new EnemySystem(
        new ContentCatalog(new[] { Emberwright() }, new[] { Enemy(HuskPath) }),
        events,
        new FixedRandom(Seed),
        new DepthScaling(Descent().Scaling),
        EnemyCapacity);

    /// <summary>The shipped orb, fired from the origin at <paramref name="target"/>.</summary>
    private static Projectile Orb(CharacterSpec spec, Vector3 target) => new Projectile(
        spec.Id, 0, Vector3.Zero, target,
        spec.Weapon.ShotSpeed, spec.Weapon.ShotRadius, spec.Weapon.Damage, ShotSide.AtEnemies);

    /// <summary>
    /// How many of a walking column of five, 1.4 m apart so the ends sit 2.8 m from the centre and
    /// inside the blast, an orb aimed at <paramref name="aim"/> catches. The
    /// column's centre is 12 m up the line of fire when the orb leaves, and every body walks at
    /// <paramref name="velocity"/> for exactly the orb's flight before it lands.
    /// </summary>
    private int CaughtFromAColumn(Vector3 aim, Vector3 velocity)
    {
        CharacterSpec spec = Emberwright();
        var events = new RecordingEvents();

        EnemySystem enemies = Enemies(events);
        var player = new PlayerCombat(spec, events, new RecordingIntents(), EnemyCapacity);
        var projectiles = new ProjectileSystem(events, ProjectileCapacity);
        var snapshot = new WorldSnapshot(EnemyCapacity) { Dt = 1f / 60f };

        float flight = aim.Length() / spec.Weapon.ShotSpeed;
        var agents = new List<EnemyAgent>();

        for (int i = -2; i <= 2; i++)
        {
            agents.Add(enemies.Spawn(new ContentId("enemy.husk"), new Vector3(i * 1.4f, 0f, 12f)));
        }

        projectiles.Fire(Orb(spec, aim), 0f);

        // Where the body reports them when the orb arrives: the positions the landing tests against.
        foreach (EnemyAgent agent in agents)
        {
            snapshot.AddEnemy() = new EnemySense
            {
                Id = agent.Id,
                Position = agent.Position + (velocity * flight),
                Velocity = velocity,
                PathDirectionToPlayer = System.Numerics.Vector2.Zero,
                HasLineOfSight = true,
            };
        }

        enemies.Ingest(snapshot);
        projectiles.Tick(flight + 0.001f, Vector3.Zero, player, enemies);

        return events.Count<EnemyDamaged>();
    }

    /// <summary>A catalog built from <c>BootScope.prefab</c>'s own <c>_characters</c> list.</summary>
    private static ContentCatalog BootCatalog()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BootScopePath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {BootScopePath}.");

        var scope = prefab.GetComponent<BootScope>();

        Assert.That(scope, Is.Not.Null, "BootScope did not load off its own prefab (Traps §5).");

        SerializedProperty characters = new SerializedObject(scope).FindProperty("_characters");
        var specs = new List<CharacterSpec>(characters.arraySize);

        for (int i = 0; i < characters.arraySize; i++)
        {
            var definition = characters.GetArrayElementAtIndex(i).objectReferenceValue as CharacterDefinition;

            Assert.That(definition, Is.Not.Null, $"_characters[{i}] is an empty slot.");

            specs.Add(definition.ToSpec());
        }

        return new ContentCatalog(specs);
    }
}
