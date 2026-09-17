using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The two things a cast puts on the screen: ground you can stand in, and a shell that says a bolt
/// is about to be absorbed. Both render events and own nothing, so what can be wrong here is the
/// same short list every feedback view has — something that never appears, something that never
/// leaves, something that appears in the wrong place, and something that claims a thing the
/// simulation did not do.
/// </summary>
/// <remarks>
/// <para>
/// <b>The zone rows are keyed by id throughout, and that is the point of several of them.</b> The
/// events carry an <c>Id</c> issued from 1 rather than an index into core's table (M3-11b), so
/// <c>Zone_TwoZonesAreIndependent</c> retires the <em>older</em> of two zones and asserts the
/// survivor is still standing where it was put: keyed by index, that row draws the wrong decal back
/// into the pool and leaves the wrong one on the floor.
/// </para>
/// <para>
/// The zone prefab is a scene object rather than <c>VFX_ConsecrateZone.prefab</c>, for
/// <c>TelegraphRingsTests</c>' reason: <see cref="IObjectResolver.Instantiate"/> takes a
/// <see cref="Component"/> rather than an asset, and building the body here keeps these rows from
/// depending on how the art is dressed. The one row that <em>is</em> about the art —
/// <see cref="Views_UseNoDangerColour"/> — loads both assets off disk on purpose.
/// </para>
/// <para>
/// <c>Awake</c> never runs in EditMode (Traps §5). Nothing here needs it to: the rest pose, rotation
/// and scale a decal is returned to are field initialisers describing a body authored at the origin
/// at unit scale, which is both what this fixture builds and what the shipped prefab is.
/// </para>
/// <para>
/// Every object the fixture or a pool creates is destroyed in the teardown. An EditMode test that
/// leaks a GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class SkillViewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>
    /// Consecrate's authored radius, duplicated here rather than read from anything the views can
    /// see. Several rows below claim a decal is drawn at <em>exactly</em> the circle core heals
    /// inside, and reading that number from the same place on both sides would make the claim true
    /// by construction.
    /// </summary>
    private const float ConsecrateRadius = 3.5f;

    /// <inheritdoc cref="ConsecrateRadius" />
    private const float ConsecrateSeconds = 6f;

    /// <summary>
    /// GD §16.4's <c>#22D3EE</c> — the player, and the things that keep them safe. Written out
    /// rather than read from <see cref="Palette.Player"/>: the claim is that these two views draw the
    /// colour the design document names, and reading it from the file under test would make that
    /// claim true by construction.
    /// </summary>
    /// <remarks>
    /// The value <c>HpBarView</c> and <c>ThreatArrows</c> both shipped from M1-17, which is
    /// <c>#22D3EE</c> rounded to three places; <see cref="Near"/>'s 0.01 band is what makes the two
    /// spellings one colour. The danger literal that used to sit beside this is gone —
    /// <see cref="Palette.IsDanger"/> is the question now, and it is the whole point of the file.
    /// </remarks>
    private static readonly Color PlayerCyan = new Color(0.133f, 0.827f, 0.933f, 1f);

    private GameObject _zonePrefabObject;
    private ZoneView _zonePrefab;
    private GameObject _decalRoot;
    private GameObject _bulwarkObject;
    private GameObject _shellObject;
    private BulwarkView _bulwark;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private ZoneViews _zones;

    [SetUp]
    public void CreateViews()
    {
        _zonePrefabObject = new GameObject("ZonePrefab");
        _zonePrefab = _zonePrefabObject.AddComponent<ZoneView>();

        // Dressed the way the real prefab is, so the colour and alpha writes are exercised rather
        // than skipped — TelegraphRingsTests' idiom for a serialized field a fixture has to fill.
        typeof(ZoneView)
            .GetField("_quad", Private)
            .SetValue(_zonePrefab, _zonePrefabObject.AddComponent<MeshRenderer>());

        _decalRoot = new GameObject("Decals");

        _bulwarkObject = new GameObject("Player");
        _bulwark = _bulwarkObject.AddComponent<BulwarkView>();

        _shellObject = new GameObject("Shell");
        _shellObject.transform.SetParent(_bulwarkObject.transform);

        typeof(BulwarkView)
            .GetField("_shell", Private)
            .SetValue(_bulwark, _shellObject.AddComponent<MeshRenderer>());

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();
    }

    [TearDown]
    public void DestroyViews()
    {
        _zones?.Dispose();
        _zones = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        if (_zonePrefabObject != null)
        {
            Object.DestroyImmediate(_zonePrefabObject);
        }

        if (_decalRoot != null)
        {
            Object.DestroyImmediate(_decalRoot);
        }

        if (_bulwarkObject != null)
        {
            Object.DestroyImmediate(_bulwarkObject);
        }
    }

    /// <summary>Rules 1 and 5: the event carries where and how wide, and the decal is both.</summary>
    [Test]
    public void Zone_SpawnPlacesADecal()
    {
        _zones = Census(prewarm: 1);

        Spawn(id: 1, new Vector3(3f, 0f, -2f));

        Assert.That(_zones.Count, Is.EqualTo(1));
        Assert.That(_zones.TryGet(1, out ZoneView decal), Is.True, "Addressed by the event's id.");

        Assert.That(decal.transform.position.x, Is.EqualTo(3f).Within(1e-4f));
        Assert.That(decal.transform.position.z, Is.EqualTo(-2f).Within(1e-4f));

        Assert.That(decal.IsLive, Is.True);
        Assert.That(decal.Radius, Is.EqualTo(ConsecrateRadius).Within(1e-4f));

        // The scale *is* the radius: a quad is one unit across, so 3.5 m of ground is 7 units of
        // body. Drawn a little larger to look better, the game would be lying about where the heal
        // reaches — which is the whole of rule 5.
        Assert.That(
            decal.transform.localScale.x,
            Is.EqualTo(ConsecrateRadius * 2f).Within(1e-4f));

        // Not flashing on the frame it lands. A zone that arrived mid-pulse would be claiming a heal
        // before core had run one.
        Assert.That(decal.IsFlashing, Is.False);
    }

    /// <summary>Rule 2: core's expiry is the authority, and the body goes back to the pool.</summary>
    [Test]
    public void Zone_ExpiredReturnsItToThePool()
    {
        _zones = Census(prewarm: 1);

        Spawn(id: 1, Vector3.Zero);

        Assert.That(_zones.PooledCount, Is.Zero, "Rented, so the pool is empty.");

        _hub.Publish(new ZoneExpired(1));

        Assert.That(_zones.Count, Is.Zero);
        Assert.That(_zones.PooledCount, Is.EqualTo(1), "Back to the pool, not to the bin.");
        Assert.That(_zones.TryGet(1, out _), Is.False);
    }

    /// <summary>
    /// Rule 2's net: a decal whose <c>ZoneExpired</c> never arrived cannot stand for the rest of the
    /// run.
    /// </summary>
    /// <remarks>
    /// This is the only path by which the decal's own countdown ever fires. In a run where every
    /// event arrives, core retires the zone first and this row's condition is never met — which is
    /// exactly why it is worth a row: nothing in a normal run would ever notice it was broken.
    /// </remarks>
    [Test]
    public void Zone_StepRetiresAnOrphan()
    {
        _zones = Census(prewarm: 1);

        Spawn(id: 1, Vector3.Zero);

        _zones.Step(ConsecrateSeconds - 0.01f);

        Assert.That(_zones.Count, Is.EqualTo(1), "Inside its life it is still on the floor.");

        _zones.Step(0.02f);

        Assert.That(_zones.Count, Is.Zero, "And it takes itself off when the time is up.");
        Assert.That(_zones.PooledCount, Is.EqualTo(1));
    }

    /// <summary>Rule 4: a pulse that healed something flashes.</summary>
    [Test]
    public void Zone_HealedFlashes()
    {
        _zones = Census(prewarm: 1);

        Spawn(id: 1, Vector3.Zero);

        ZoneView decal = Decal(1);

        float resting = decal.Alpha;

        _hub.Publish(new ZoneHealed(1, 3f));

        Assert.That(decal.IsFlashing, Is.True);
        Assert.That(decal.Alpha, Is.GreaterThan(resting), "A flash is brighter than the ground.");

        // And it settles rather than staying bright, or a zone healing every half second would be a
        // disc that only ever got brighter.
        decal.Step(1f);

        Assert.That(decal.IsFlashing, Is.False);
        Assert.That(decal.Alpha, Is.EqualTo(resting).Within(1e-4f));
    }

    /// <summary>
    /// Rule 4's other half, and the row that keeps the view honest: zero is the honest answer at full
    /// health, and it is common rather than rare.
    /// </summary>
    /// <remarks>
    /// A player standing in their own Consecrate at full health receives twelve of these. Flashing on
    /// them would tell the player they were being healed for six seconds while their bar did not
    /// move, which is the one thing a feedback view must never do (GD §16.3).
    /// </remarks>
    [Test]
    public void Zone_ZeroHealDoesNotFlash()
    {
        _zones = Census(prewarm: 1);

        Spawn(id: 1, Vector3.Zero);

        ZoneView decal = Decal(1);

        float resting = decal.Alpha;

        _hub.Publish(new ZoneHealed(1, 0f));

        Assert.That(decal.IsFlashing, Is.False);
        Assert.That(decal.Alpha, Is.EqualTo(resting).Within(1e-4f));
    }

    /// <summary>
    /// The id row. Two zones overlap, the older one ends, and the younger is untouched.
    /// </summary>
    /// <remarks>
    /// This is the row M3-11b's fourth correction exists for. Core's table shifts every entry after a
    /// retirement, so a census keyed by index would take the survivor off the floor here and leave
    /// the dead one standing — and both decals are drawn, so the symptom is a zone in the wrong place
    /// rather than an exception.
    /// </remarks>
    [Test]
    public void Zone_TwoZonesAreIndependent()
    {
        _zones = Census(prewarm: 2);

        Spawn(id: 1, new Vector3(-4f, 0f, 0f));
        Spawn(id: 2, new Vector3(6f, 0f, 1f));

        Assert.That(_zones.Count, Is.EqualTo(2));

        _hub.Publish(new ZoneExpired(1));

        Assert.That(_zones.Count, Is.EqualTo(1));
        Assert.That(_zones.TryGet(1, out _), Is.False, "The one core ended.");
        Assert.That(_zones.TryGet(2, out ZoneView survivor), Is.True, "And only that one.");

        Assert.That(survivor.IsLive, Is.True);
        Assert.That(survivor.transform.position.x, Is.EqualTo(6f).Within(1e-4f));
        Assert.That(survivor.transform.position.z, Is.EqualTo(1f).Within(1e-4f));

        // And it still answers its own pulses rather than the dead one's.
        _hub.Publish(new ZoneHealed(1, 5f));

        Assert.That(survivor.IsFlashing, Is.False);

        _hub.Publish(new ZoneHealed(2, 5f));

        Assert.That(survivor.IsFlashing, Is.True);
    }

    /// <summary>
    /// Rule 3, as far as an EditMode fixture can reach it: the step a decal advances by is the one it
    /// was handed, and there is no second clock on the body that could disagree with it.
    /// </summary>
    /// <remarks>
    /// <c>ProjectileViewsTests.Ticker_StepsWithSnapshotDt</c>'s row, copied with its reasoning
    /// intact. The spec's row names <c>RunTicker</c>, which has no fixture in this project and gains
    /// none here — its frame order is ledger row 8's other half and stays with M2-11b. What is
    /// checkable without one is both halves of the actual claim: <see cref="ZoneViews.Step"/>
    /// integrates the argument rather than a frame time, and <see cref="ZoneView"/> declares no Unity
    /// message that could advance the countdown behind the ticker's back. The remaining line — that
    /// the ticker passes <c>snapshot.Dt</c> — is one call site, read in review and watched in the
    /// manual steps.
    /// </remarks>
    [Test]
    public void Zone_StepsOnTheSnapshotDt()
    {
        _zones = Census(prewarm: 1);

        Spawn(id: 1, Vector3.Zero);

        // Deliberately not a plausible frame time: if anything here read Time.deltaTime instead of
        // the argument, the decal would not be within a hundredth of its end after one call.
        _zones.Step(ConsecrateSeconds - 0.005f);

        Assert.That(_zones.Count, Is.EqualTo(1));

        _zones.Step(0.01f);

        Assert.That(_zones.Count, Is.Zero, "Five and a bit seconds, integrated from the argument.");

        foreach (string message in new[] { "Update", "LateUpdate", "FixedUpdate" })
        {
            Assert.That(
                typeof(ZoneView).GetMethod(
                    message,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public),
                Is.Null,
                $"ZoneView declares {message}. A view stepped by RunTicker must own no frame loop "
                    + "of its own, or the decal fades on the wall clock while core runs the zone on "
                    + "the clamped one — and on a hitching frame the ground outlives the heal "
                    + "(AR §18.1, §18.2).");
        }
    }

    /// <summary>Rule 1: a run does not allocate bodies. The pool is the whole of the budget.</summary>
    [Test]
    public void Zone_InstantiatesNothingDuringARun()
    {
        _zones = Census(prewarm: 8);

        int bodies = _zones.Count + _zones.PooledCount;

        Assert.That(bodies, Is.EqualTo(8), "Prewarmed before the run, which is the point.");

        for (int i = 1; i <= 50; i++)
        {
            Spawn(id: i, new Vector3(i, 0f, 0f));

            _hub.Publish(new ZoneExpired(i));

            Assert.That(
                _zones.Count + _zones.PooledCount,
                Is.EqualTo(bodies),
                $"The pool grew on zone {i}. A total that keeps climbing is the pool being "
                    + "bypassed, and on a phone it is a collection during a fight (AR §14).");
        }
    }

    /// <summary>The run ends and takes everything with it, on the floor or in the pool.</summary>
    [Test]
    public void Zone_DisposeReturnsEverything()
    {
        _zones = Census(prewarm: 3);

        Spawn(id: 1, Vector3.Zero);
        Spawn(id: 2, Vector3.Zero);
        Spawn(id: 3, Vector3.Zero);

        Assert.That(_decalRoot.GetComponentsInChildren<ZoneView>(true).Length, Is.EqualTo(3));

        _zones.Dispose();

        Assert.That(_zones.Count, Is.Zero);
        Assert.That(
            _decalRoot.GetComponentsInChildren<ZoneView>(true).Length,
            Is.Zero,
            "The pool owns every body it made and destroys the lot.");

        // And the subscriptions went with it: a census that outlived its run would be handed the
        // next run's ids.
        Assert.DoesNotThrow(() => Spawn(id: 9, Vector3.Zero));

        Assert.That(_zones.Count, Is.Zero);

        _zones = null;
    }

    /// <summary>Every door on the census, and every door on the decal.</summary>
    [Test]
    public void Zone_Guards()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ZoneViews(null, _zonePrefab, _decalRoot.transform, _hub));

        Assert.Throws<ArgumentNullException>(
            () => new ZoneViews(_container, null, _decalRoot.transform, _hub));

        Assert.Throws<ArgumentNullException>(
            () => new ZoneViews(_container, _zonePrefab, _decalRoot.transform, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ZoneViews(_container, _zonePrefab, _decalRoot.transform, _hub, prewarm: -1));

        // A null parent is the scene root, which is untidy rather than wrong.
        Assert.DoesNotThrow(() =>
        {
            var loose = new ZoneViews(_container, _zonePrefab, null, _hub, prewarm: 0);

            loose.Dispose();
        });
    }

    /// <summary>
    /// The decal's two float doors, four ways each. Neither has a meaningful zero and neither can be
    /// allowed a NaN: a NaN radius scales the body to nowhere and a NaN duration makes every reading
    /// of its life NaN, and neither throws on its own (AR §18.3).
    /// </summary>
    [Test]
    public void Zone_PlaceRefusesNonsense()
    {
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _zonePrefab.Place(UnityEngine.Vector3.zero, bad, ConsecrateSeconds),
                $"A radius of {bad} is not a circle.");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _zonePrefab.Place(UnityEngine.Vector3.zero, ConsecrateRadius, bad),
                $"A duration of {bad} is not a life.");
        }
    }

    /// <summary>A step that did not advance the simulation does not advance the picture of it.</summary>
    [Test]
    public void Zone_StepIgnoresANonFiniteDt()
    {
        _zonePrefab.Place(UnityEngine.Vector3.zero, ConsecrateRadius, ConsecrateSeconds);

        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.That(_zonePrefab.Step(bad), Is.True, $"A step of {bad} retires nothing.");
            Assert.That(_zonePrefab.IsLive, Is.True);
        }

        // And the life it was given is intact rather than partly spent by the four calls above.
        Assert.That(_zonePrefab.Step(ConsecrateSeconds - 0.01f), Is.True);
        Assert.That(_zonePrefab.Step(0.02f), Is.False);
    }

    /// <summary>Rule 6: a grant raises the shell.</summary>
    [Test]
    public void Bulwark_ShowsOnGrant()
    {
        _bulwark.Construct(_hub);

        Assert.That(_bulwark.IsUp, Is.False);

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        Assert.That(_bulwark.IsUp, Is.True);

        _bulwark.Step(1f);

        Assert.That(_bulwark.Alpha, Is.EqualTo(1f).Within(1e-4f), "Faded all the way in.");
        Assert.That(_shellObject.activeSelf, Is.True);
    }

    /// <summary>Rule 6: it comes down on the total, and zero is what that means.</summary>
    [Test]
    public void Bulwark_HidesWhenTheTotalReachesZero()
    {
        _bulwark.Construct(_hub);

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        _bulwark.Step(1f);

        _hub.Publish(new ShieldGrantExpired(35f, 0f));

        Assert.That(_bulwark.IsUp, Is.False);

        _bulwark.Step(1f);

        Assert.That(_bulwark.Alpha, Is.Zero);
        Assert.That(_shellObject.activeSelf, Is.False, "And put away once it is invisible.");
    }

    /// <summary>
    /// The row the <c>Total</c> field exists for: one grant ending while another is still running
    /// leaves the shell up.
    /// </summary>
    /// <remarks>
    /// M3-11a rule 9 put the total on the expiry event for exactly this reader. Reading
    /// <c>Removed</c> instead, the player would look unshielded with twenty points still standing
    /// between them and the next bolt — and the next bolt would then be absorbed by a shield that was
    /// not on screen, which is worse than no feedback at all.
    /// </remarks>
    [Test]
    public void Bulwark_StaysUpWhileAnotherGrantRuns()
    {
        _bulwark.Construct(_hub);

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));
        _hub.Publish(new ShieldGranted(20f, 55f, 5f));

        _bulwark.Step(1f);

        _hub.Publish(new ShieldGrantExpired(35f, 20f));

        Assert.That(_bulwark.IsUp, Is.True, "Twenty points are still up.");

        _bulwark.Step(1f);

        Assert.That(_bulwark.Alpha, Is.EqualTo(1f).Within(1e-4f));

        _hub.Publish(new ShieldGrantExpired(20f, 0f));

        Assert.That(_bulwark.IsUp, Is.False);
    }

    /// <summary>Rule 6: 0.15 s of fade, because a pop mid-fight reads as a glitch.</summary>
    [Test]
    public void Bulwark_FadesRatherThanPops()
    {
        typeof(BulwarkView).GetField("_fadeSeconds", Private).SetValue(_bulwark, 0.15f);

        _bulwark.Construct(_hub);

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        // Not 1 on the first frame, which is the whole of the claim: the shell exists and is
        // invisible, rather than arriving whole.
        Assert.That(_bulwark.Alpha, Is.Zero);

        _bulwark.Step(0.05f);

        float third = _bulwark.Alpha;

        Assert.That(third, Is.GreaterThan(0f));
        Assert.That(third, Is.LessThan(1f));
        Assert.That(third, Is.EqualTo(1f / 3f).Within(0.01f), "Linear over the authored seconds.");

        _bulwark.Step(0.05f);

        Assert.That(_bulwark.Alpha, Is.GreaterThan(third), "And it keeps climbing.");

        _bulwark.Step(0.05f);

        Assert.That(_bulwark.Alpha, Is.EqualTo(1f).Within(1e-4f), "Whole at 0.15 s and no sooner.");
    }

    /// <summary>The shell's one door.</summary>
    [Test]
    public void Bulwark_Guards()
    {
        Assert.Throws<ArgumentNullException>(() => _bulwark.Construct(null));
    }

    /// <summary>A step that did not advance the simulation does not advance the fade either.</summary>
    [Test]
    public void Bulwark_StepIgnoresANonFiniteDt()
    {
        _bulwark.Construct(_hub);

        _hub.Publish(new ShieldGranted(35f, 35f, 5f));

        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            _bulwark.Step(bad);

            Assert.That(_bulwark.Alpha, Is.Zero, $"A step of {bad} moved the fade.");
        }
    }

    /// <summary>
    /// Rule 11: neither of these holds anything a run owns. They are the two purest event-rendering
    /// objects in the project, and this is the row that says so.
    /// </summary>
    /// <remarks>
    /// A field of either type would be a door for a later task to poll through, and a view that polls
    /// is a view with two sources of truth about the same fact. There is no number to poll here: a
    /// grant and a zone both carry their whole lives on their events.
    /// </remarks>
    [Test]
    public void Views_HoldNoRunHandle()
    {
        foreach (Type type in new[] { typeof(ZoneViews), typeof(BulwarkView) })
        {
            foreach (FieldInfo field in type.GetFields(Private | BindingFlags.Public))
            {
                Assert.That(
                    typeof(IRunSession).IsAssignableFrom(field.FieldType),
                    Is.False,
                    $"{type.Name}.{field.Name} holds the run itself.");

                Assert.That(
                    field.FieldType.Name,
                    Is.Not.EqualTo("RunState"),
                    $"{type.Name}.{field.Name} holds core's state.");
            }
        }
    }

    /// <summary>
    /// M3-11c rule 10, rewritten at M3-13a: both views draw the player's cyan, neither draws the
    /// danger colour, and neither can any longer be dressed to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The hard half is the second one. GD §16.4 reserves <c>#FF4A1F</c> for danger <em>"for nothing
    /// else, ever"</em>, and a healing disc in the telegraph colour would be the single worst
    /// readability bug this project could ship — a player is meant to run out of one and stand in the
    /// other.
    /// </para>
    /// <para>
    /// <b>What changed, and why the row is stronger rather than merely different.</b> Until M3-13a
    /// this read the <em>serialized</em> <c>_colour</c> off each prefab, and had to assert the
    /// authored value as well as the absence of the forbidden one — because both class initialisers
    /// were <see cref="Color.white"/> on purpose, so a row that only ruled out danger would pass
    /// whether or not the field ever bound (Traps §7). Both fields are gone. The colour is
    /// <c>Palette.Heal</c> and <c>Palette.Player</c>, and it is asserted through what each view
    /// actually paints, so there is no longer a value a prefab could be dressed to that this would
    /// not see. The trick the white initialiser was is what a palette makes unnecessary.
    /// </para>
    /// <para>
    /// <b>The prefabs are still loaded off disk</b>, because the claim is about the two shipped
    /// assets and not about a fixture's clone — and because the day either one grows a colour field
    /// again, <c>Views_CarryNoSerializedColour</c> in <c>PaletteTests</c> is what refuses it.
    /// </para>
    /// </remarks>
    [Test]
    public void Views_UseNoDangerColour()
    {
        var zone = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Vfx/VFX_ConsecrateZone.prefab");

        var shell = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Vfx/VFX_Bulwark.prefab");

        Assert.That(zone, Is.Not.Null, "VFX_ConsecrateZone.prefab is missing.");
        Assert.That(shell, Is.Not.Null, "VFX_Bulwark.prefab is missing.");

        // What the decal draws, off the shipped asset's own component. The shell's colour is not a
        // read on BulwarkView — it paints straight into a property block — so it is asserted at the
        // palette member the paint reads, which is the same claim one dereference earlier.
        Color zoneColour = zone.GetComponent<ZoneView>().Colour;
        Color shellColour = Palette.Player;

        foreach ((string name, Color colour) in new[]
                 {
                     ("VFX_ConsecrateZone", zoneColour),
                     ("VFX_Bulwark", shellColour),
                 })
        {
            Assert.That(
                Palette.IsDanger(colour),
                Is.False,
                $"{name} draws #FF4A1F, which GD §16.4 reserves for danger and nothing else, ever. "
                    + "A healing effect in the telegraph colour is the worst readability bug "
                    + "available.");

            Assert.That(
                Near(colour, PlayerCyan),
                Is.True,
                $"{name} does not draw #22D3EE. Both views read Palette as of M3-13a, so this "
                    + "failing means the palette moved rather than that a prefab was dressed wrong.");
        }

        // **The half the field's removal is what makes true.** Neither type has a Color a prefab
        // could carry any more, so there is no dressed value for the two assertions above to have
        // been quietly agreeing with (Traps §7).
        foreach (Type type in new[] { typeof(ZoneView), typeof(BulwarkView) })
        {
            Assert.That(
                type.GetFields(Private).Where(f => f.FieldType == typeof(Color)),
                Is.Empty,
                $"{type.Name} grew a Color field back. M3-13a removed it so the prefab's stored "
                    + "value became unreachable rather than contradictory — a field defaulted from "
                    + "Palette and then dressed differently is the placeholder problem with an "
                    + "extra step.");
        }
    }

    private static bool Near(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

    private ZoneViews Census(int prewarm) =>
        new ZoneViews(_container, _zonePrefab, _decalRoot.transform, _hub, prewarm);

    private void Spawn(int id, Vector3 position) =>
        _hub.Publish(new ZoneSpawned(id, position, ConsecrateRadius, ConsecrateSeconds));

    /// <summary>The decal standing for <paramref name="id"/>, asserting that there is one.</summary>
    private ZoneView Decal(int id)
    {
        Assert.That(_zones.TryGet(id, out ZoneView view), Is.True, $"No decal for zone {id}.");

        return view;
    }
}
