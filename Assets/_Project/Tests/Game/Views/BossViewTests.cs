using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Authoring;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Support;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Vector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The census that turns M4-02's five hazard events and M4-01b's two beat events into things on
/// screen. Until this task nothing in <c>Soulvail.Game</c> subscribed to any of them, so the fight
/// was real in core and invisible: a ring took 22 hit points off the player with nothing on the
/// floor, which is what the owner's playtest found and what these rows exist to keep fixed.
/// </summary>
/// <remarks>
/// <para>
/// The prefabs are scene objects rather than the shipped <c>VFX_*</c> assets, for
/// <c>TelegraphRingsTests</c>' reason: <see cref="IObjectResolver.Instantiate"/> takes a
/// <see cref="Component"/> rather than an asset, and building the bodies here keeps the fixture from
/// depending on how the art is dressed. <see cref="Prefabs_AreDressed"/> is the one row that does
/// read the assets, and it is the row that would catch Traps §5's block-namespace trap.
/// </para>
/// <para>
/// Rings and cracks are read back through the parent they are pooled under, which is
/// <c>TelegraphRingsTests</c>' idiom. <b>Shells are not</b>, and that is rule 4 rather than an
/// exception: a beat is parented to the boss's body while it is worn, so an active child of the
/// decal root is exactly what a shell in service is <em>not</em>.
/// </para>
/// <para>
/// <c>Awake</c> never runs in EditMode (Traps §5). Nothing here needs it to: the rest pose a body is
/// returned to is a field initialiser describing an object authored at the origin at unit scale,
/// which is what this fixture builds and what the three shipped prefabs are.
/// </para>
/// <para>
/// Every object the fixture or a pool creates is destroyed in the teardown. An EditMode test that
/// leaks a GameObject leaks it into every test that runs after it.
/// </para>
/// </remarks>
[TestFixture]
public sealed class BossViewTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string ArenaFolder = "Assets/_Project/Prefabs/Arenas";
    private const string PillarsPath = "Assets/_Project/Prefabs/Arenas/Arena_Pillars.prefab";
    private const string ShockwavePath = "Assets/_Project/Prefabs/Vfx/VFX_Shockwave.prefab";
    private const string FissurePath = "Assets/_Project/Prefabs/Vfx/VFX_Fissure.prefab";
    private const string BeatPath = "Assets/_Project/Prefabs/Vfx/VFX_BossBeat.prefab";

    /// <summary>
    /// The Warden's shipped ring, duplicated here rather than read through <c>WardenBehaviour</c>.
    /// </summary>
    /// <remarks>
    /// The claim several rows below make is that a view is driven by <em>the event</em> and by
    /// nothing else — reading the constant from the same place on both sides would make that true by
    /// construction, which is exactly the mistake M4-03's spec warns against.
    /// </remarks>
    private const float RingSpeed = 8f;

    /// <inheritdoc cref="RingSpeed" />
    private const float RingCeiling = 7f;

    /// <summary>What <c>MaxRadius / Speed</c> comes to at the two above: 0.875 s.</summary>
    private const float RingLifetime = RingCeiling / RingSpeed;

    /// <inheritdoc cref="RingSpeed" />
    private const float CrackRadius = 2.5f;

    /// <inheritdoc cref="RingSpeed" />
    private const float CrackArm = 0.9f;

    /// <summary><c>FissureView</c>'s own collapse, which is a prefab number rather than core's.</summary>
    private const float CollapseSeconds = 0.35f;

    /// <inheritdoc cref="CollapseSeconds" />
    private const float FireFlare = 1.35f;

    /// <summary>The Warden's authored beat, from <c>WardenBoss.asset</c>.</summary>
    private const float BeatSeconds = 1.5f;

    private static readonly ContentId Husk = new ContentId("enemy.husk");
    private static readonly ContentId TestArena = new ContentId("arena.test");

    private readonly List<GameObject> _spawned = new List<GameObject>();

    private ShockwaveView _shockwavePrefab;
    private FissureView _fissurePrefab;
    private BossBeatView _beatPrefab;
    private EnemyView _enemyPrefab;
    private GameObject _parentObject;
    private IObjectResolver _container;
    private DomainEventHub _hub;
    private EnemyViews _enemies;
    private ArenaPool _arenas;
    private BossViews _views;

    /// <summary>Every arena prefab, so a new one is covered by being added — <c>ArenaViewTests</c>' source.</summary>
    private static IEnumerable<string> ArenaPaths()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ArenaFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ArenaView>() != null)
            {
                yield return path;
            }
        }
    }

    [SetUp]
    public void CreateCensus()
    {
        _shockwavePrefab = Decal<ShockwaveView>("ShockwavePrefab", "_quad");
        _fissurePrefab = Decal<FissureView>("FissurePrefab", "_quad");
        _beatPrefab = Decal<BossBeatView>("BeatPrefab", "_shell");

        _enemyPrefab = Keep(new GameObject("EnemyPrefab")).AddComponent<EnemyView>();

        _parentObject = Keep(new GameObject("Decals"));

        _container = new ContainerBuilder().Build();
        _hub = new DomainEventHub();

        _enemies = new EnemyViews(
            _container,
            _enemyPrefab,
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 1);
    }

    [TearDown]
    public void DestroyCensus()
    {
        _views?.Dispose();
        _views = null;

        _arenas?.Dispose();
        _arenas = null;

        _enemies?.Dispose();
        _enemies = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        for (int i = 0; i < _spawned.Count; i++)
        {
            if (_spawned[i] != null)
            {
                Object.DestroyImmediate(_spawned[i]);
            }
        }

        _spawned.Clear();
    }

    // ---- The ring --------------------------------------------------------------------------------

    [Test]
    public void Shock_BindsToTheEventsOrigin()
    {
        _views = Census();

        Emit(1, new Vector3(4f, 0f, -6f));

        Assert.That(_views.ShockwaveCount, Is.EqualTo(1));

        Assert.That(_views.TryGetShockwave(1, out ShockwaveView ring), Is.True);

        // Where the boss *stood*, not near it: M4-02 rule 2 anchors the ring to the slam point, and
        // a ring drawn approximately would make "I walked out of it" a statement about a circle
        // nobody could see.
        Assert.That(ring.transform.position.x, Is.EqualTo(4f).Within(1e-4f));
        Assert.That(ring.transform.position.z, Is.EqualTo(-6f).Within(1e-4f));

        Assert.That(ring.IsLive, Is.True);

        // From nothing. A ring that appeared at its ceiling would have already passed the player.
        Assert.That(ring.Radius, Is.Zero);
        Assert.That(ring.transform.localScale.x, Is.Zero);

        // Both numbers came off the event and neither is a constant in Soulvail.Game.
        Assert.That(ring.Speed, Is.EqualTo(RingSpeed).Within(1e-4f));
        Assert.That(ring.MaxRadius, Is.EqualTo(RingCeiling).Within(1e-4f));
    }

    [Test]
    public void Shock_ExpandsAtTheEventsSpeed()
    {
        _views = Census();

        Emit(1, Vector3.Zero);

        _views.TryGetShockwave(1, out ShockwaveView ring);

        _views.Step(0.25f);

        // 8 m/s for a quarter of a second is two metres, and the drawn diameter is twice that. This
        // is the row that would fail if a view ever re-declared WardenBehaviour's constants instead
        // of reading the event: any other speed lands somewhere other than 2.
        Assert.That(ring.Radius, Is.EqualTo(2f).Within(1e-3f));
        Assert.That(ring.transform.localScale.x, Is.EqualTo(4f).Within(1e-3f));
    }

    [Test]
    public void Shock_RetiresOnItsOwnCountdown()
    {
        _views = Census();

        Emit(1, Vector3.Zero);

        // Rule 7: no ShockwavePassed is ever published here, and the ring must still go home. The
        // lifetime is derived from the event — 7 m at 8 m/s is 0.875 s — rather than authored, so a
        // view that had invented its own would retire at the wrong moment.
        _views.Step(RingLifetime - 0.01f);

        Assert.That(_views.ShockwaveCount, Is.EqualTo(1), "Inside its life the ring is still travelling.");
        Assert.That(_views.PooledShockwaves, Is.Zero);

        _views.Step(0.02f);

        Assert.That(_views.ShockwaveCount, Is.Zero, "And it takes itself off when it reaches the ceiling.");
        Assert.That(_views.PooledShockwaves, Is.EqualTo(1), "Back to the pool, not to the bin.");
    }

    [Test]
    public void Shock_PassedTakesItBackFirst()
    {
        _views = Census();

        Emit(1, Vector3.Zero);

        _views.Step(0.2f);

        _hub.Publish(new ShockwavePassed(1));

        // The event is the rule and the countdown is only the net, so a ring core has retired goes
        // home immediately rather than finishing the 0.875 s on its own.
        Assert.That(_views.ShockwaveCount, Is.Zero);
        Assert.That(_views.PooledShockwaves, Is.EqualTo(1));

        // And a second retirement for the same id is not a bug: Step's net and the event can both
        // reach for one ring on one frame.
        Assert.That(() => _hub.Publish(new ShockwavePassed(1)), Throws.Nothing);
    }

    [Test]
    public void Shock_IsDangerColoured()
    {
        _views = Census();

        Emit(1, Vector3.Zero);

        _views.TryGetShockwave(1, out ShockwaveView ring);

        // GD §16.4 reserves saturated red-orange for danger and nothing else, and a ring that takes
        // 22 hit points off the player is danger. Read from the one place the palette lives.
        Assert.That(Palette.IsDanger(ring.Colour), Is.True);
    }

    // ---- The crack -------------------------------------------------------------------------------

    [Test]
    public void Fissure_ArmUsesTheDangerColour()
    {
        _views = Census();

        Arm(1, new Vector3(2f, 0f, 2f));

        Assert.That(_views.TryGetFissure(1, out FissureView crack), Is.True);

        Assert.That(crack.IsArming, Is.True);

        // Rule 2, deliberately: #FF4A1F is reserved for danger "and nothing else, ever", and a
        // circle that is about to bite is the thing the reservation is for. This is the first task
        // since M3-13b to reach for the member on purpose rather than argue around it.
        Assert.That(Palette.IsDanger(crack.Colour), Is.True);

        Assert.That(
            crack.Colour.r, Is.EqualTo(Palette.Danger.r).Within(1e-3f));
        Assert.That(
            crack.Colour.g, Is.EqualTo(Palette.Danger.g).Within(1e-3f));
        Assert.That(
            crack.Colour.b, Is.EqualTo(Palette.Danger.b).Within(1e-3f));
    }

    [Test]
    public void Fissure_ArmAndFireDifferByShape()
    {
        _views = Census();

        Arm(1, Vector3.Zero);
        Arm(2, new Vector3(20f, 0f, 0f));

        _views.TryGetFissure(1, out FissureView arming);
        _views.TryGetFissure(2, out FissureView fired);

        // Both half way through the same arm, so anything that differs below is the *state* rather
        // than the clock: one is told to bite and the other is not.
        _views.Step(CrackArm * 0.5f);

        _hub.Publish(new FissureFired(2));

        Assert.That(arming.IsArming, Is.True);
        Assert.That(fired.HasFired, Is.True);

        // Rule 3. The arm is a disc opening towards the circle core will test; the bite is past it
        // and shutting. The two are never the same size, and the difference is not a brightness:
        // 1.25 m against 3.375 m at this instant.
        Assert.That(arming.DrawnRadius, Is.EqualTo(CrackRadius * 0.5f).Within(1e-3f));
        Assert.That(fired.DrawnRadius, Is.EqualTo(CrackRadius * FireFlare).Within(1e-3f));

        Assert.That(
            fired.transform.localScale.x,
            Is.Not.EqualTo(arming.transform.localScale.x).Within(0.1f),
            "A fissure that only got brighter is the failure mode rule 3 names by name.");

        // And they move in opposite directions, which is the half a still frame cannot show: the
        // arm keeps growing, the bite keeps shutting. A test on size alone would pass for a fired
        // state that merely started larger.
        float armingBefore = arming.DrawnRadius;
        float firedBefore = fired.DrawnRadius;

        _views.Step(CollapseSeconds * 0.25f);

        Assert.That(arming.DrawnRadius, Is.GreaterThan(armingBefore));
        Assert.That(fired.DrawnRadius, Is.LessThan(firedBefore));
    }

    [Test]
    public void Fissure_ArmsForTheEventsWindow()
    {
        _views = Census();

        Arm(1, Vector3.Zero, arm: 1.4f);

        _views.TryGetFissure(1, out FissureView crack);

        Assert.That(crack.ArmSeconds, Is.EqualTo(1.4f).Within(1e-4f));
        Assert.That(crack.Radius, Is.EqualTo(CrackRadius).Within(1e-4f));

        // Half of the event's window, not half of WardenBehaviour's 0.6 s floor or of the 0.9 s the
        // shipped body authors. GD §9.1 rule 1's minimum is a clamp in core, so the number a
        // telegraph must be drawn on is the one that arrived.
        _views.Step(0.7f);

        Assert.That(crack.IsArming, Is.True);
        Assert.That(crack.DrawnRadius, Is.EqualTo(CrackRadius * 0.5f).Within(1e-3f));
    }

    [Test]
    public void Fissure_FiresItselfIfNothingDoes()
    {
        _views = Census();

        Arm(1, Vector3.Zero);

        // No FissureFired ever. The arm is over, which is the one thing a crack whose events never
        // arrived can safely be assumed to have done — ZoneView's net, one hazard over.
        _views.Step(CrackArm + 0.01f);

        _views.TryGetFissure(1, out FissureView crack);

        Assert.That(crack.HasFired, Is.True, "An arm that runs out bites rather than sitting there.");
        Assert.That(_views.FissureCount, Is.EqualTo(1), "And it is still on the floor, collapsing.");

        _views.Step(CollapseSeconds + 0.01f);

        Assert.That(_views.FissureCount, Is.Zero);
        Assert.That(_views.PooledFissures, Is.EqualTo(2), "The whole prewarm is back in the pool.");
    }

    [Test]
    public void Fissure_ClosedTakesItBackFirst()
    {
        _views = Census();

        Arm(1, Vector3.Zero);

        _hub.Publish(new FissureFired(1));
        _hub.Publish(new FissureClosed(1));

        Assert.That(_views.FissureCount, Is.Zero);
        Assert.That(_views.PooledFissures, Is.EqualTo(2), "The whole prewarm is back in the pool.");
    }

    [Test]
    public void Fissure_ComesBackClean()
    {
        _views = Census();

        Arm(1, new Vector3(9f, 0f, 9f));

        _hub.Publish(new FissureFired(1));

        _views.TryGetFissure(1, out FissureView first);

        _hub.Publish(new FissureClosed(1));

        // In the pool: standing for nothing, at no size, invisible, and back where it was made.
        Assert.That(first.IsLive, Is.False);
        Assert.That(first.HasFired, Is.False);
        Assert.That(first.DrawnRadius, Is.Zero);
        Assert.That(first.Alpha, Is.Zero);
        Assert.That(first.transform.localScale, Is.EqualTo(UnityEngine.Vector3.one));

        Arm(2, new Vector3(-3f, 0f, 1f));

        _views.TryGetFissure(2, out FissureView second);

        Assert.That(second, Is.SameAs(first), "The same body is rented again rather than a second made.");

        // The state is the field a previous life leaves set that would be silent: a body returned
        // still fired would draw the next arming crack as a collapsing flare — a telegraph running
        // backwards (AR §18.4).
        Assert.That(second.IsArming, Is.True);
        Assert.That(second.DrawnRadius, Is.Zero);
        Assert.That(second.transform.position.x, Is.EqualTo(-3f).Within(1e-4f));
    }

    // ---- The beat --------------------------------------------------------------------------------

    [Test]
    public void Beat_IsDrawnOnTheBody()
    {
        _views = Census();

        EnemyView body = Spawn(7);

        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        Assert.That(_views.TryGetBeat(7, out BossBeatView shell), Is.True);

        // Rule 4: the shell hangs on the agent. Not on a canvas, not at the world origin, and not
        // as a full-screen flash — a screen-wide effect every phase change is GD §11.3's fill-rate
        // warning and says nothing about *which* thing is not taking damage.
        Assert.That(shell.Body, Is.SameAs(body.transform));
        Assert.That(shell.transform.parent, Is.SameAs(body.transform));
        Assert.That(shell.transform.localPosition, Is.EqualTo(UnityEngine.Vector3.zero));

        Assert.That(
            shell.GetComponentInParent<Canvas>(),
            Is.Null,
            "A beat raised on a canvas is the full-screen effect rule 4 forbids.");

        // And it is drawn in neither of the two reserved colours: the boss being safe is not the
        // player being in danger, and it is not the player either (GD §16.4).
        Assert.That(Palette.IsDanger(shell.Colour), Is.False);
        Assert.That(shell.Colour, Is.EqualTo(Palette.Neutral));
    }

    [Test]
    public void Beat_EndsOnTheEvent()
    {
        _views = Census();

        Spawn(7);

        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        _views.Step(0.3f);

        Assert.That(_views.BeatCount, Is.EqualTo(1));

        _hub.Publish(new BossBeatEnded(7));

        Assert.That(_views.BeatCount, Is.Zero, "The shell is gone the moment the boss can be hurt again.");
        Assert.That(_views.PooledBeats, Is.EqualTo(1), "Back to the pool, not to the bin.");

        Assert.That(_views.TryGetBeat(7, out _), Is.False);
    }

    [Test]
    public void Beat_ComesOffTheBodyWhenItIsReturned()
    {
        _views = Census();

        EnemyView body = Spawn(7);

        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        _views.TryGetBeat(7, out BossBeatView shell);

        _hub.Publish(new BossBeatEnded(7));

        // The pooling rule the other two views do not have: a shell left hanging off a corpse would
        // be scaled by an archetype look it has nothing to do with and destroyed with a body the
        // pool does not own (AR §18.4).
        Assert.That(shell.transform.parent, Is.Not.SameAs(body.transform));
        Assert.That(shell.transform.parent, Is.SameAs(_parentObject.transform));
        Assert.That(shell.IsLive, Is.False);
        Assert.That(shell.Alpha, Is.Zero);
        Assert.That(shell.transform.localScale, Is.EqualTo(UnityEngine.Vector3.one));
    }

    [Test]
    public void Beat_RetiresOnItsOwnCountdown()
    {
        _views = Census();

        Spawn(7);

        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        // No BossBeatEnded ever. A shell that outlived its beat would say a boss was untouchable
        // while it was being killed, which is worse than not drawing one at all.
        _views.Step(BeatSeconds - 0.01f);

        Assert.That(_views.BeatCount, Is.EqualTo(1));

        _views.Step(0.02f);

        Assert.That(_views.BeatCount, Is.Zero);
        Assert.That(_views.PooledBeats, Is.EqualTo(1));
    }

    [Test]
    public void Beat_OnABossWithNoBodyIsDropped()
    {
        _views = Census();

        // Nothing spawned under this id. Not a state a run reaches — EnemySpawned builds the body
        // before any behaviour can publish — and the alternative to dropping it is a shell at the
        // world origin saying something there cannot be hurt.
        Assert.That(() => _hub.Publish(new BossBeatStarted(99, BeatSeconds)), Throws.Nothing);

        Assert.That(_views.BeatCount, Is.Zero);
        Assert.That(_views.PooledBeats, Is.EqualTo(1), "And the body it did not use is still in the pool.");
    }

    // ---- The rules that are about the code rather than the fight ---------------------------------

    [Test]
    public void Views_CarryNoSerializedColour()
    {
        // M3-13a's rule, which closed ledger row 6. Views_CarryNoSerializedColour there is scoped by
        // name, so these three carry their own guard rather than joining that array — and the reason
        // is not tidiness: a colour read back off a dressed prefab agrees with itself whatever it is
        // (Traps §7), so a field is unfalsifiable where Palette is one line that can fail.
        foreach (Type view in new[] { typeof(ShockwaveView), typeof(FissureView), typeof(BossBeatView) })
        {
            foreach (FieldInfo field in view.GetFields(Private | BindingFlags.Public))
            {
                bool serialized =
                    field.IsPublic || field.GetCustomAttribute<SerializeField>() != null;

                bool isColour =
                    field.FieldType == typeof(Color) || field.FieldType == typeof(Color32);

                Assert.That(
                    serialized && isColour,
                    Is.False,
                    $"{view.Name} serializes a Color in {field.Name}. Every colour comes from "
                        + "Palette (M3-13a, ledger row 6).");
            }
        }
    }

    [Test]
    public void Views_StepOnTheSnapshotDt()
    {
        _views = Census();

        Emit(1, Vector3.Zero);
        Arm(1, new Vector3(30f, 0f, 0f));
        Spawn(7);
        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        _views.TryGetShockwave(1, out ShockwaveView ring);
        _views.TryGetFissure(1, out FissureView crack);

        // Deliberately not a plausible frame time: 0.3 s is a third of the 0.9 s arm and 2.4 m of
        // an 8 m/s ring, and anything reading Time.deltaTime instead of the argument lands on
        // neither.
        _views.Step(0.3f);

        Assert.That(ring.Radius, Is.EqualTo(2.4f).Within(1e-3f));
        Assert.That(crack.DrawnRadius, Is.EqualTo(CrackRadius / 3f).Within(1e-3f));

        // Rule 5's other half, and the one a value check cannot reach: a view stepped by RunTicker
        // must own no frame loop of its own, or it runs on the wall clock while core runs the hazard
        // on the clamped one — and then the ring is somewhere other than the edge that bites
        // (AR §18.1, §18.2).
        foreach (Type view in new[] { typeof(ShockwaveView), typeof(FissureView), typeof(BossBeatView) })
        {
            foreach (string message in new[] { "Update", "LateUpdate", "FixedUpdate" })
            {
                Assert.That(
                    view.GetMethod(message, Private | BindingFlags.Public),
                    Is.Null,
                    $"{view.Name} declares {message}.");
            }
        }
    }

    [Test]
    public void Views_LieFlatAndHoldNoCamera()
    {
        _views = Census();

        Emit(1, new Vector3(4f, 0f, 4f));
        Arm(1, new Vector3(-4f, 0f, -4f));

        _views.TryGetShockwave(1, out ShockwaveView ring);
        _views.TryGetFissure(1, out FissureView crack);

        // A quad's face is its local −Z, so a decal lying on the floor has that pointing at the sky.
        foreach (Component decal in new Component[] { ring, crack })
        {
            Assert.That(
                UnityEngine.Vector3.Dot(-decal.transform.forward, UnityEngine.Vector3.up),
                Is.EqualTo(1f).Within(1e-3f),
                $"{decal.GetType().Name} must lie on the arena floor, not stand up in it.");
        }

        // And none of the three can billboard, which is why rather than a promise that they do not:
        // the camera is fixed at 57° (GD §5.1), so there is nothing to turn towards.
        foreach (Type view in new[] { typeof(ShockwaveView), typeof(FissureView), typeof(BossBeatView) })
        {
            foreach (FieldInfo field in view.GetFields(
                Private | BindingFlags.Public | BindingFlags.Static))
            {
                Assert.That(
                    typeof(Camera).IsAssignableFrom(field.FieldType),
                    Is.False,
                    $"{view.Name} holds a Camera in {field.Name}.");
            }
        }
    }

    [Test]
    public void Pool_ReturnsEverything()
    {
        _views = Census(shockwaves: 4, fissures: 8, beats: 1);

        // A full fight's worth: core's own capacities, which is what the pools are prewarmed to.
        for (int i = 1; i <= 4; i++)
        {
            Emit(i, new Vector3(i * 20f, 0f, 0f));
        }

        for (int i = 1; i <= 8; i++)
        {
            Arm(i, new Vector3(0f, 0f, i * 20f));
        }

        Spawn(7);
        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        Assert.That(_views.ShockwaveCount, Is.EqualTo(4));
        Assert.That(_views.FissureCount, Is.EqualTo(8));
        Assert.That(_views.BeatCount, Is.EqualTo(1));

        Assert.That(_views.PooledShockwaves, Is.Zero, "The whole prewarm is in service.");
        Assert.That(_views.PooledFissures, Is.Zero);
        Assert.That(_views.PooledBeats, Is.Zero);

        // The fight ends the way core ends one: every hazard retired by its own event, and the beat
        // by the boss becoming hittable again.
        for (int i = 1; i <= 4; i++)
        {
            _hub.Publish(new ShockwavePassed(i));
        }

        for (int i = 1; i <= 8; i++)
        {
            _hub.Publish(new FissureClosed(i));
        }

        _hub.Publish(new BossBeatEnded(7));

        Assert.That(_views.ShockwaveCount, Is.Zero);
        Assert.That(_views.FissureCount, Is.Zero);
        Assert.That(_views.BeatCount, Is.Zero);

        // Every instance is back, and there are no more of them than there were: a total that keeps
        // climbing is the pool being bypassed, which nothing else in a run would report (AR §14).
        Assert.That(_views.PooledShockwaves, Is.EqualTo(4));
        Assert.That(_views.PooledFissures, Is.EqualTo(8));
        Assert.That(_views.PooledBeats, Is.EqualTo(1));
    }

    [Test]
    public void Prefabs_AreDressed()
    {
        // The row Traps §5's block-namespace trap would fail: a MonoBehaviour in a file-scoped
        // namespace loads as null off every asset that references it, with nothing reported
        // anywhere. Loading the component through the asset is what catches that, and the second
        // half catches a renderer field nobody dragged — which draws nothing and reports nothing.
        AssertDressed<ShockwaveView>(ShockwavePath, v => v.IsDrawable);
        AssertDressed<FissureView>(FissurePath, v => v.IsDrawable);
        AssertDressed<BossBeatView>(BeatPath, v => v.IsDrawable);
    }

    // ---- The arena's own hazard (rule 6) ---------------------------------------------------------

    [Test]
    public void Arena_HazardIsOnOneArenaOnly()
    {
        var carrying = new List<string>();

        foreach (string path in ArenaPaths())
        {
            ArenaView arena = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<ArenaView>();

            if (arena.HazardCount > 0)
            {
                carrying.Add(path);
            }
        }

        // Rule 6: one arena gets it and the other does not, so the contract is proved without
        // claiming content that does not exist — M2-11a rule 11's own bargain, which shipped two
        // arenas rather than twelve.
        Assert.That(
            carrying,
            Is.EquivalentTo(new[] { PillarsPath }),
            "Exactly one shipped arena carries a hazard, and it is the pillared one.");
    }

    [Test]
    public void Hazard_IsToppledByARingThatReachesIt()
    {
        ArenaView arena = RaiseTestArena(hazardAt: new UnityEngine.Vector3(3f, 0f, 0f));

        _views = Census();

        Assert.That(arena.IsToppled(0), Is.False, "It is standing when the fight starts.");

        Emit(1, Vector3.Zero);

        // The front edge is at 1.6 m after a fifth of a second and the brazier is at 3 m, so the
        // wave has not got there yet.
        _views.Step(0.2f);

        Assert.That(arena.IsToppled(0), Is.False, "A ring that has not reached it leaves it alone.");

        _views.Step(0.2f);

        // 3.2 m: the edge crossed it on this step. Rule 6's "a thing in the room the boss can set
        // off", and the reason the test is a crossing rather than a containment.
        Assert.That(arena.IsToppled(0), Is.True);
    }

    [Test]
    public void Hazard_SurvivesARingThatFallsShort()
    {
        ArenaView arena = RaiseTestArena(hazardAt: new UnityEngine.Vector3(30f, 0f, 0f));

        _views = Census();

        Emit(1, Vector3.Zero);

        // Thirty metres away and the ring's ceiling is seven. The whole of the wave's life passes
        // and the brazier is untouched — a hazard that fell over because a slam happened *somewhere*
        // would be the arena reacting to the boss's intent rather than to its ring.
        _views.Step(RingLifetime + 0.01f);

        Assert.That(_views.ShockwaveCount, Is.Zero, "The ring is over.");
        Assert.That(arena.IsToppled(0), Is.False);
    }

    [Test]
    public void Hazard_StandsBackUpWhenTheArenaIsSealed()
    {
        ArenaView arena = RaiseTestArena(hazardAt: new UnityEngine.Vector3(3f, 0f, 0f));

        _views = Census();

        Emit(1, Vector3.Zero);
        _views.Step(0.5f);

        Assert.That(arena.IsToppled(0), Is.True);

        // The same instance is raised again at every stage boundary (M2-11a), so a brazier knocked
        // over in a boss fight would still be lying down five stages later.
        _hub.Publish(new StageArrived(2, TestArena));

        Assert.That(arena.IsToppled(0), Is.False);

        // And the count is the authored list's rather than the standing ones', so
        // Arena_HazardIsOnOneArenaOnly cannot depend on when it was asked.
        Assert.That(arena.HazardCount, Is.EqualTo(1));
    }

    [Test]
    public void Hazard_ARunWithNoArenaIsFine()
    {
        // The undressed Run scene — the fastest iteration loop in the project, which every optional
        // field on RunScope exists to protect. A hazard is arena content, so no arena is no hazard
        // rather than a throw on the first slam of a boss fight.
        _views = Census();

        Assert.That(() => Emit(1, Vector3.Zero), Throws.Nothing);
        Assert.That(() => _views.Step(0.2f), Throws.Nothing);

        Assert.That(_views.ShockwaveCount, Is.EqualTo(1));
    }

    // ---- Doors, disposal and the per-frame cost --------------------------------------------------

    [Test]
    public void Step_AllocatesNothing()
    {
        _views = Census(shockwaves: 4, fissures: 8, beats: 1);

        for (int i = 1; i <= 4; i++)
        {
            Emit(i, new Vector3(i * 20f, 0f, 0f));
        }

        for (int i = 1; i <= 8; i++)
        {
            Arm(i, new Vector3(0f, 0f, i * 20f));
        }

        Spawn(7);
        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        // A step small enough that everything is still live after the whole measured run: 10 001
        // iterations is 0.1 s, well inside the 0.875 s ring. A step that expired them would spend
        // the measurement walking three empty dictionaries and would touch none of the writes this
        // row is about. AllocationAssert, never a hand-rolled probe (Traps §7).
        AllocationAssert.None(() => _views.Step(1e-5f));

        Assert.That(_views.ShockwaveCount, Is.EqualTo(4), "Everything survived the measurement.");
        Assert.That(_views.FissureCount, Is.EqualTo(8));
        Assert.That(_views.BeatCount, Is.EqualTo(1));
    }

    [Test]
    public void Step_NonFiniteDt_ChangesNothing()
    {
        _views = Census();

        Emit(1, Vector3.Zero);
        Arm(1, new Vector3(30f, 0f, 0f));

        _views.Step(0.2f);

        _views.TryGetShockwave(1, out ShockwaveView ring);
        _views.TryGetFissure(1, out FissureView crack);

        float radius = ring.Radius;
        float drawn = crack.DrawnRadius;

        // Refused rather than thrown on: a bad frame time is the run's problem to report, and a
        // hazard's job is to not become a permanent NaN because of it — which no later comparison
        // would be true about and nothing would log.
        _views.Step(float.NaN);
        _views.Step(float.PositiveInfinity);
        _views.Step(-0.5f);

        Assert.That(ring.Radius, Is.EqualTo(radius).Within(1e-6f));
        Assert.That(crack.DrawnRadius, Is.EqualTo(drawn).Within(1e-6f));

        Assert.That(_views.ShockwaveCount, Is.EqualTo(1), "And neither was silently retired.");
        Assert.That(_views.FissureCount, Is.EqualTo(1));
    }

    [Test]
    public void Bind_InvalidArgument_Throws()
    {
        // Every float door gets a non-finite row (AR §18.3). None of these has a meaningful zero
        // either: a ring that does not travel never reaches anybody, a ring with no ceiling never
        // ends, a crack of no size is nowhere to stand off, an arm of no length is a telegraph
        // nobody could read, and a beat of no length is a phase change with nothing in it.
        foreach (float bad in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwavePrefab.Bind(UnityEngine.Vector3.zero, bad, RingCeiling),
                $"A speed of {bad} must be refused at the door.");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _shockwavePrefab.Bind(UnityEngine.Vector3.zero, RingSpeed, bad),
                $"A ceiling of {bad} must be refused at the door.");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissurePrefab.Arm(UnityEngine.Vector3.zero, bad, CrackArm),
                $"A radius of {bad} must be refused at the door.");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _fissurePrefab.Arm(UnityEngine.Vector3.zero, CrackRadius, bad),
                $"An arm of {bad} must be refused at the door.");

            Assert.Throws<ArgumentOutOfRangeException>(
                () => _beatPrefab.Bind(_parentObject.transform, bad),
                $"A beat of {bad} must be refused at the door.");
        }

        Assert.Throws<ArgumentNullException>(
            () => _beatPrefab.Bind(null, BeatSeconds),
            "A shell with nothing to hang on is the census asking for a beat on a boss with no view.");
    }

    [Test]
    public void Dispose_UnsubscribesAndDestroys()
    {
        _views = Census();

        Emit(1, Vector3.Zero);
        Arm(1, new Vector3(30f, 0f, 0f));
        Spawn(7);
        _hub.Publish(new BossBeatStarted(7, BeatSeconds));

        _views.Dispose();

        Assert.That(_views.ShockwaveCount, Is.Zero, "Disposing drops the census with the bodies it held.");
        Assert.That(_views.FissureCount, Is.Zero);
        Assert.That(_views.BeatCount, Is.Zero);

        // The subscriptions are gone, so a slam published into a run this object has outlived
        // reaches nothing — and cannot ask a disposed pool for a body.
        Assert.That(() => Emit(2, Vector3.Zero), Throws.Nothing);
        Assert.That(() => Arm(2, Vector3.Zero), Throws.Nothing);
        Assert.That(() => _hub.Publish(new BossBeatStarted(7, BeatSeconds)), Throws.Nothing);

        Assert.That(_views.ShockwaveCount, Is.Zero);

        // Idempotent, like every other census' — a double dispose is a disposal-order question
        // nobody should have to answer.
        Assert.That(() => _views.Dispose(), Throws.Nothing);
    }

    [Test]
    public void Constructor_NullDependency_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new BossViews(null, _shockwavePrefab, _fissurePrefab, _beatPrefab, null, _hub, _enemies, null));

        Assert.Throws<ArgumentNullException>(
            () => new BossViews(_container, null, _fissurePrefab, _beatPrefab, null, _hub, _enemies, null));

        Assert.Throws<ArgumentNullException>(
            () => new BossViews(_container, _shockwavePrefab, null, _beatPrefab, null, _hub, _enemies, null));

        Assert.Throws<ArgumentNullException>(
            () => new BossViews(_container, _shockwavePrefab, _fissurePrefab, null, null, _hub, _enemies, null));

        Assert.Throws<ArgumentNullException>(
            () => new BossViews(_container, _shockwavePrefab, _fissurePrefab, _beatPrefab, null, null, _enemies, null));

        Assert.Throws<ArgumentNullException>(
            () => new BossViews(_container, _shockwavePrefab, _fissurePrefab, _beatPrefab, null, _hub, null, null));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BossViews(
                _container, _shockwavePrefab, _fissurePrefab, _beatPrefab, null, _hub, _enemies, null,
                shockwavePrewarm: -1));

        // The arena pool is the one argument that may be missing — see the class remarks on
        // BossViews and Hazard_ARunWithNoArenaIsFine.
        Assert.That(
            () => new BossViews(
                _container, _shockwavePrefab, _fissurePrefab, _beatPrefab, null, _hub, _enemies, null)
                .Dispose(),
            Throws.Nothing);
    }

    // ---- Fixture ---------------------------------------------------------------------------------

    /// <summary>A census over the fixture's prefabs, parent, hub, container and enemy population.</summary>
    private BossViews Census(int shockwaves = 1, int fissures = 2, int beats = 1) =>
        new BossViews(
            _container,
            _shockwavePrefab,
            _fissurePrefab,
            _beatPrefab,
            _parentObject.transform,
            _hub,
            _enemies,
            _arenas,
            shockwaves,
            fissures,
            beats);

    /// <summary>Publishes a slam's ring the way <c>ShockwaveSystem</c> does.</summary>
    private void Emit(int id, Vector3 origin) =>
        _hub.Publish(new ShockwaveEmitted(id, origin, RingSpeed, RingCeiling));

    /// <summary>Publishes a crack opening the way <c>FissureSystem</c> does.</summary>
    private void Arm(int id, Vector3 at, float arm = CrackArm) =>
        _hub.Publish(new FissureArmed(id, at, CrackRadius, arm));

    /// <summary>Puts a body in the arena the way <c>EnemySystem</c> does, and hands it back.</summary>
    private EnemyView Spawn(int id)
    {
        _hub.Publish(new EnemySpawned(id, Husk, Vector3.Zero));

        Assert.That(_enemies.TryGet(id, out EnemyView view), Is.True, "The body was never bound.");

        return view;
    }

    /// <summary>
    /// Builds a one-arena pool with a single hazard at <paramref name="hazardAt"/> and raises it.
    /// </summary>
    /// <remarks>
    /// A synthetic arena rather than <c>Arena_Pillars.prefab</c>, which is the opposite of
    /// <c>ArenaPoolTests</c>' choice and for a compatible reason: those rows are about Unity's own
    /// NavMesh behaviour, which a fixture arena would satisfy vacuously, while these are about a
    /// distance between two places this fixture needs to choose. The shipped prefab's own hazard is
    /// <see cref="Arena_HazardIsOnOneArenaOnly"/>'s subject.
    /// </remarks>
    private ArenaView RaiseTestArena(UnityEngine.Vector3 hazardAt)
    {
        GameObject prefab = Keep(new GameObject("TestArena"));

        prefab.SetActive(false);

        ArenaView view = prefab.AddComponent<ArenaView>();

        var brazier = new GameObject("Brazier");

        brazier.transform.SetParent(prefab.transform, false);
        brazier.transform.localPosition = hazardAt;

        var so = new SerializedObject(view);

        so.FindProperty("_id").stringValue = TestArena.Value;

        SerializedProperty hazards = so.FindProperty("_hazards");

        hazards.arraySize = 1;
        hazards.GetArrayElementAtIndex(0).objectReferenceValue = brazier.transform;

        so.ApplyModifiedPropertiesWithoutUndo();

        PlayerView player = Keep(new GameObject("Player")).AddComponent<PlayerView>();

        _arenas = new ArenaPool(
            _container,
            new[] { view },
            Keep(new GameObject("Arenas")).transform,
            _hub,
            player);

        _hub.Publish(new StageArrived(1, TestArena));

        Assert.That(_arenas.Active, Is.Not.Null, "The test arena was never raised.");
        Assert.That(_arenas.Active.HazardCount, Is.EqualTo(1));

        return _arenas.Active;
    }

    /// <summary>
    /// A ground decal or shell carrying <typeparamref name="T"/>, with its renderer field filled the
    /// way the shipped prefab's is.
    /// </summary>
    /// <remarks>
    /// Dressed through reflection, which is <c>ReticleViewTests</c>' idiom for a serialized field a
    /// fixture has to fill: without it the colour and alpha writes are skipped rather than exercised,
    /// and every row about what is on screen would pass against a body that draws nothing.
    /// </remarks>
    private T Decal<T>(string name, string rendererField)
        where T : Component
    {
        GameObject root = Keep(new GameObject(name));

        T view = root.AddComponent<T>();

        typeof(T)
            .GetField(rendererField, Private)
            .SetValue(view, root.AddComponent<MeshRenderer>());

        return view;
    }

    /// <summary>Asserts that the shipped prefab at <paramref name="path"/> carries a dressed view.</summary>
    private static void AssertDressed<T>(string path, Func<T, bool> isDrawable)
        where T : Component
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        Assert.That(asset, Is.Not.Null, $"{path} is not in the project.");

        var view = asset.GetComponent<T>();

        Assert.That(
            view,
            Is.Not.Null,
            $"{path} carries no {typeof(T).Name}. A MonoBehaviour in a file-scoped namespace "
                + "deserialises as null off every asset that references it, silently (Traps §5).");

        Assert.That(
            isDrawable(view),
            Is.True,
            $"{path}'s renderer field is empty. Every one of them in the run would be timed, sized "
                + "and returned correctly and none of them would ever be visible.");
    }

    /// <summary>Remembers an object so the teardown destroys it.</summary>
    private GameObject Keep(GameObject go)
    {
        _spawned.Add(go);

        return go;
    }
}
