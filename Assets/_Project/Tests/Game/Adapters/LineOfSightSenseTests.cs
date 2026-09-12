using System;
using NUnit.Framework;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Tests.Core.Support;
using UnityEngine;

namespace Soulvail.Tests.Game.Adapters;

/// <summary>
/// The raycast that makes a pillar something to hide behind, measured. Every row here is a fact
/// core cannot check for itself: if the mask is wrong, the eye height is wrong, or the budget
/// throttles the whole population to nothing, the game is exactly the one that shipped before this
/// class existed and nothing anywhere reports it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It builds real colliders, because the half of this class that can be wrong is the half
/// physics owns.</b> The cadence and the budget are arithmetic and could be asserted without a
/// scene; the mask, the trigger interaction and the height the ray runs at cannot, and those three
/// are the ones that fail silently. <c>ConeOverlapQueryTests</c> makes the same trade for the same
/// reason, and an EditMode physics query answers against live scene colliders exactly as it does in
/// play mode.
/// </para>
/// <para>
/// <b>Everything is built a kilometre from the origin.</b> These rows run against whatever scene
/// the Editor happens to have open, which for this project is <c>Run.unity</c> — so geometry at the
/// origin would share space with whatever that scene is dressed with, and a row would pass or fail
/// on the strength of a collider somebody else put there.
/// </para>
/// <para>
/// <b>Every row that measures twice uses two different times</b>, and that is load-bearing rather
/// than tidy: the sense reads a change of <c>time</c> as a change of frame, so two calls sharing one
/// value share one frame's budget — and the second of them is silently answered from the cache.
/// </para>
/// </remarks>
[TestFixture]
public sealed class LineOfSightSenseTests
{
    /// <summary>Where every row's geometry is built. See the class remarks.</summary>
    private static readonly Vector3 Base = new Vector3(1000f, 0f, 1000f);

    /// <summary>The enemy. Every row measures from here.</summary>
    private static readonly Vector3 Eye = Base;

    /// <summary>The player, ten metres away on Z. Far enough for a pillar to stand in between.</summary>
    private static readonly Vector3 Target = Base + new Vector3(0f, 0f, 10f);

    /// <summary>The cap M2-04 chose, and the population every budget row is written against.</summary>
    private const int Population = 28;

    /// <summary>One 60 fps frame — the step the budget rows are priced at.</summary>
    private const float Step = 1f / 60f;

    private LayerMask _cover;
    private LineOfSightSense _sense;

    /// <summary>The row's geometry, under one root so teardown destroys the lot.</summary>
    private GameObject _geometry;

    [SetUp]
    public void CreateSense()
    {
        Assert.That(
            CoverLayer,
            Is.GreaterThanOrEqualTo(0),
            $"The project has no '{ArenaView.CoverLayerName}' layer, so nothing can be cover "
                + "(TagManager.asset, M2-11a rule 9).");

        _cover = 1 << CoverLayer;
        _sense = new LineOfSightSense(
            Population,
            _cover,
            NewBudget(),
            LineOfSightSense.DefaultRefreshHz);
    }

    [TearDown]
    public void DestroyGeometry()
    {
        DropGeometry();

        _sense = null;
    }

    /// <summary>The <c>Cover</c> layer's index, or −1 when the project has none.</summary>
    private static int CoverLayer => LayerMask.NameToLayer(ArenaView.CoverLayerName);

    // ---- Guards -------------------------------------------------------------------------------

    [Test]
    public void Constructor_RefusesANonPositiveCapacity([Values(0, -1)] int capacity)
    {
        Assert.Catch<ArgumentOutOfRangeException>(
            () => new LineOfSightSense(capacity, _cover, NewBudget()));
    }

    [Test]
    public void Constructor_RefusesAnEmptyMask()
    {
        // Not a degraded run: an empty mask is a game in which GD §7.2's cover blocks nothing,
        // which is precisely the state this class exists to leave.
        Assert.Catch<ArgumentException>(() => new LineOfSightSense(8, default, NewBudget()));
    }

    [Test]
    public void Constructor_RefusesANullBudget()
    {
        Assert.Catch<ArgumentNullException>(() => new LineOfSightSense(8, _cover, null));
    }

    [Test]
    public void Constructor_RefusesANonFiniteOrNonPositiveRate(
        [Values(0f, -1f, float.NaN, float.PositiveInfinity)] float refreshHz)
    {
        // NaN is in the list on purpose: `<= 0f` admits it, and a NaN interval is one no elapsed
        // time is ever greater than — so nothing would ever be measured and every answer would be
        // the first one, for ever (AR §18.3).
        Assert.Catch<ArgumentOutOfRangeException>(
            () => new LineOfSightSense(8, _cover, NewBudget(), refreshHz));
    }

    // ---- What the ray sees --------------------------------------------------------------------

    [Test]
    public void Unknown_CanSee()
    {
        // Rule 3, and the only place it is observable: the first call for an enemy measures, so the
        // permissive default shows up exactly where the budget has run out. Cover is in the way for
        // all twenty-eight, five of them are measured, and the rest can see — because a sense that
        // answered `false` when it had not looked would switch the whole archetype off in silence.
        Pillar();

        for (int id = 1; id <= 5; id++)
        {
            Assert.That(
                AskInFrame(id, Step, Population, Step),
                Is.False,
                $"Sanity: enemy {id} is inside the frame's budget and has cover in the way.");
        }

        Assert.That(
            AskInFrame(Population, Step, Population, Step),
            Is.True,
            "An enemy the budget never reached has never been measured, and an unmeasured enemy "
                + "can see (rule 3).");
    }

    [Test]
    public void Blocked_WhenCoverIsBetween()
    {
        Pillar();

        Assert.That(Ask(1, Step), Is.False);
    }

    [Test]
    public void Clear_WhenNothingIsBetween()
    {
        Assert.That(Ask(1, Step), Is.True, "Ten metres of open ground blocks nothing.");
    }

    [Test]
    public void IgnoresNonCoverColliders()
    {
        // Rule 5. A body between a Spitter and the player is not cover: GD §7.2 makes cover a
        // property of the arena rather than of the crowd, and a Spitter that could not fire because
        // a Husk was standing in front of it would read as broken three systems from its cause.
        // Both boxes below stand squarely in the ray's path and neither is on the mask.
        NewGeometry();

        int enemyLayer = LayerMask.NameToLayer("Enemy");

        Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0), "The project has no Enemy layer.");

        Box("Ground", new Vector3(0f, 1f, 4f), new Vector3(8f, 3f, 0.5f), 0);
        Box("EnemyBody", new Vector3(0f, 1f, 6f), new Vector3(1f, 2f, 1f), enemyLayer);

        Assert.That(
            Ask(1, Step),
            Is.True,
            "Only the Cover layer blocks a shot. A wall on Default and a body on Enemy are not "
                + "cover (rule 5).");
    }

    [Test]
    public void RayRunsAtEyeHeight()
    {
        // A 0.4 m kerb is something to trip over, not something to hide behind, and the ray has to
        // pass over it — otherwise every tier edge and every doorstep in an arena silently stops a
        // Spitter from firing (rule 4).
        NewGeometry();
        Box("Kerb", new Vector3(0f, 0.2f, 5f), new Vector3(8f, 0.4f, 0.5f), CoverLayer);

        Assert.That(
            Ask(1, Step),
            Is.True,
            $"A 0.4 m kerb stands below {LineOfSightSense.EyeHeight} m and does not block a shot.");

        DropGeometry();
        Pillar();

        // A second frame, not a second call in the first: the budget for the frame above is spent,
        // and a query sharing its time would be answered from a cache the kerb filled.
        Assert.That(
            Ask(2, 2f * Step),
            Is.False,
            "GD §7.2's 1.5 m pillar stands taller than the ray and blocks it.");
    }

    [Test]
    public void DistanceIsNotXzClamped()
    {
        // Rule 4's other half. Every *distance* in this project is XZ (AR §18.4) and occlusion is
        // not a distance: the ray runs from one chest to the other in three dimensions, so a target
        // four metres up is four metres up. The box below spans y 2.1–4.1, which a ray flattened
        // onto the ground plane would pass underneath while answering "can see".
        NewGeometry();
        Box("HighCover", new Vector3(0f, 3.1f, 5f), new Vector3(8f, 2f, 0.5f), CoverLayer);

        bool sight = _sense.HasLineOfSight(
            1,
            Eye,
            Base + new Vector3(0f, 4f, 10f),
            Step,
            1,
            Step);

        Assert.That(sight, Is.False, "Occlusion is measured in 3D even though separation is not.");
    }

    // ---- The cadence and the budget -----------------------------------------------------------

    [Test]
    public void Cadence_HoldsTheAnswerForATenth()
    {
        Pillar();

        // The clock starts at zero here rather than at one frame in, because every time below is
        // measured from *this* measurement: the refresh falls due a tenth of a second after the
        // answer was taken, not a tenth of a second after the run began.
        Assert.That(Ask(1, 0f), Is.False, "Sanity: the pillar is in the way.");

        // The pillar leaves, and nothing tells the sense. That is the point: the answer is held for
        // a tenth of a second, because a pillar cannot move and a 100 ms stale answer about one is
        // imperceptible (rule 2).
        _geometry.transform.position += new Vector3(0f, 0f, 500f);

        // Explicit rather than trusting `Physics.autoSyncTransforms`, which is a project setting a
        // test has no business depending on: a row that silently measured the pillar's old place
        // would assert the cache is working while proving the opposite.
        Physics.SyncTransforms();

        Assert.That(
            Ask(1, 0.05f),
            Is.False,
            "Half a refresh period later, the cached answer still stands.");

        Assert.That(
            Ask(1, 0.11f),
            Is.True,
            "Past the refresh period it is measured again, and now nothing is in the way.");
    }

    [Test]
    public void Budget_LimitsRaycastsPerFrame()
    {
        Pillar();

        AskEveryone(Step, Step);

        Assert.That(
            _sense.RaycastsLastFrame,
            Is.EqualTo(5),
            $"{Population} enemies at {LineOfSightSense.DefaultRefreshHz} Hz and 60 fps is "
                + "ceil(28 · 10 / 60) = 5 raycasts a frame. Twenty-eight would mean the budget is "
                + "not being applied at all.");
    }

    [Test]
    public void Budget_AtThirtyFps()
    {
        Pillar();

        AskEveryone(1f / 30f, 1f / 30f);

        Assert.That(
            _sense.RaycastsLastFrame,
            Is.EqualTo(10),
            "A longer frame owes more of the second's refreshes, so the cadence stays the same "
                + "tenth of a second at 30 fps as at 60.");
    }

    [Test]
    public void Budget_ClampAtLeastOne()
    {
        Pillar();

        // ceil(1 · 10 / 240) rounds up to 1 by itself; the floor is what covers a product of
        // exactly zero, which is an empty arena or a run's very first frame.
        Ask(1, 1f / 240f);

        Assert.That(_sense.RaycastsLastFrame, Is.EqualTo(1));
    }

    [Test]
    public void Budget_RoundRobinCoversEveryone()
    {
        Pillar();

        // Five a frame over six frames is thirty allowances for twenty-eight enemies, which is the
        // tenth of a second the cadence asked for. Nothing schedules whose turn it is: a measured
        // enemy stops being stale, so the next frame's budget falls to the ones behind it and the
        // population rotates through on its own — NavPathSense's emergent round-robin.
        for (int frame = 1; frame <= 5; frame++)
        {
            AskEveryone(frame * Step, Step);
        }

        // Asserted *during* the sixth frame rather than after it. A seventh frame would carry a
        // budget of its own, so an enemy this one never reached could be measured there and the row
        // would pass on the strength of the very thing it is checking for.
        float time = 6f * Step;

        for (int id = 1; id <= Population; id++)
        {
            Assert.That(
                _sense.HasLineOfSight(id, Eye, Target, time, Population, Step),
                Is.False,
                $"Enemy {id} has still never been measured after six frames, so the round-robin is "
                    + "not reaching the whole population — and a sight line that ages says nothing "
                    + "as it does so.");
        }
    }

    [Test]
    public void Clear_ForgetsEveryAnswer()
    {
        Pillar();

        AskEveryone(Step, Step);

        Assert.That(
            _sense.HasLineOfSight(1, Eye, Target, Step, Population, Step),
            Is.False,
            "Sanity: enemy 1 was measured this frame and the pillar is in the way.");

        _sense.Clear();

        // A cleared sense opens a new frame on its next call, and that frame has an allowance. It
        // is spent on an enemy no assertion below cares about, so the five that follow can only
        // read `true` by having genuinely forgotten (rule 8).
        _sense.HasLineOfSight(99, Eye, Target, 2f * Step, 0, 0f);

        for (int id = 1; id <= 5; id++)
        {
            Assert.That(
                _sense.HasLineOfSight(id, Eye, Target, 2f * Step, 0, 0f),
                Is.True,
                $"Enemy {id} still carries an answer about the pillars of a room that has been "
                    + "torn down.");
        }
    }

    [Test]
    public void Query_AllocatesNothing()
    {
        Pillar();

        float time = 0f;

        AllocationAssert.None(() =>
        {
            time += Step;

            for (int id = 1; id <= Population; id++)
            {
                _sense.HasLineOfSight(id, Eye, Target, time, Population, Step);
            }
        });
    }

    // ---- Fixture ------------------------------------------------------------------------------

    private static PathRefreshBudget NewBudget() =>
        new PathRefreshBudget(
            LineOfSightSense.DefaultRefreshHz,
            PathRefreshBudget.DefaultMaxPerFrame);

    /// <summary>
    /// One query in a frame of its own, from an arena holding one enemy: the time is the frame's,
    /// so each fresh value opens a new frame and a new budget.
    /// </summary>
    private bool Ask(int enemyId, float time) => AskInFrame(enemyId, time, 1, time);

    private bool AskInFrame(int enemyId, float time, int activeCount, float dt) =>
        _sense.HasLineOfSight(enemyId, Eye, Target, time, activeCount, dt);

    /// <summary>One frame in which every enemy of a full arena asks, in id order.</summary>
    private void AskEveryone(float time, float dt)
    {
        for (int id = 1; id <= Population; id++)
        {
            _sense.HasLineOfSight(id, Eye, Target, time, Population, dt);
        }
    }

    /// <summary>
    /// GD §7.2's cover: a 1.5 m pillar standing on the floor, halfway between the two points and on
    /// the <c>Cover</c> layer.
    /// </summary>
    private void Pillar()
    {
        NewGeometry();

        Box("Pillar", new Vector3(0f, 0.75f, 5f), new Vector3(8f, 1.5f, 0.5f), CoverLayer);
    }

    private void NewGeometry()
    {
        DropGeometry();

        _geometry = new GameObject("Geometry");
        _geometry.transform.position = Base;
    }

    private void DropGeometry()
    {
        if (_geometry != null)
        {
            // DestroyImmediate, never Destroy: in edit mode Destroy destroys nothing and logs an
            // error, which both leaks the object and reddens the test (Traps §7).
            UnityEngine.Object.DestroyImmediate(_geometry);
        }

        _geometry = null;
    }

    /// <summary>
    /// A solid box, offset from <see cref="Base"/> and parented to the row's one tracked root.
    /// </summary>
    /// <remarks>
    /// A <c>BoxCollider</c> on a bare <c>GameObject</c> rather than <c>CreatePrimitive</c>: nothing
    /// here is rendered, and a scaled primitive brings a collider whose shape is not the one it
    /// looks like (Traps §5).
    /// </remarks>
    private void Box(string name, Vector3 offset, Vector3 size, int layer)
    {
        var box = new GameObject(name) { layer = layer };

        box.transform.SetParent(_geometry.transform, false);
        box.transform.localPosition = offset;

        BoxCollider collider = box.AddComponent<BoxCollider>();
        collider.size = size;
    }
}
