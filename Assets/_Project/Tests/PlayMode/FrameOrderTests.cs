using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;
using CoreVector2 = System.Numerics.Vector2;
using CoreVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.PlayMode;

/// <summary>
/// <c>RunTicker</c>'s frame order, asserted against a real frame. Ledger row 8's remaining half:
/// the order has been an AR §18.1 invariant since M0-16 and nothing in the project has ever
/// constructed a <c>RunTicker</c>, so the one class whose entire contribution is a sequence has
/// been checked only by playing the game.
/// </summary>
/// <remarks>
/// <para>
/// <b>It asserts the order by observation, never by reading the method.</b> Every row below turns
/// one adjacency in commands → snapshot → clear intents → core tick → bodies → facts → knockbacks
/// into a consequence that a real frame either produces or does not, so a reordering fails them
/// whatever the code looks like. A recording <c>IRunSession</c> stands in for core — it records when
/// it was ticked, what it was told, and what the arena looked like when it was asked for a fact,
/// and it writes intents back exactly where core writes them.
/// </para>
/// <para>
/// <b>The rows that matter are the ones whose failure does not look like its cause.</b> An intent
/// buffer cleared after core wrote to it reads empty every frame and looks like the motor being
/// broken; a fact reported before the bodies moved resolves against last frame's arena and looks
/// like the cone missing; a snapshot built after the tick makes the gun aim at where enemies were
/// and looks like the auto-aim being stupid. M1-16 learned one of these the hard way and it became
/// an invariant with no test under it.
/// </para>
/// <para>
/// <b>It has to be PlayMode.</b> The bodies are moved by <c>CharacterController.Move</c>, the
/// knockback slides in the view's own <c>Update</c>, and the step is <c>Time.deltaTime</c> — none of
/// which exists in an EditMode frame. It also has to build the whole graph by hand: sixteen
/// constructor arguments, six of them scene objects, which is exactly why no task has written this
/// by accident.
/// </para>
/// <para>
/// <b>The commands phase is the one step not observed here, and it is named rather than skipped
/// quietly.</b> <c>CommandPhase</c> reaches core only when the Input System reports a press, and
/// this assembly does not reference the Input System — adding it would put un-isolated device state
/// into the assembly that also holds the boot smoke tests. Nothing in AR §18.1's row for that step
/// is a correctness failure: the consequence of moving it is a tap acted on one frame late.
/// </para>
/// </remarks>
public sealed class FrameOrderTests
{
    private static readonly ContentId HuskId = new ContentId("enemy.husk");

    /// <summary>The one body in the arena. Core's id, and the only one these rows use.</summary>
    private const int EnemyId = 1;

    /// <summary>
    /// Where this fixture's arena is built, a kilometre from the origin.
    /// </summary>
    /// <remarks>
    /// <b>Not tidiness — the cone below is a real physics sweep.</b> <c>BootSmokeTests</c> sorts
    /// before this file and leaves the Run scene loaded with a live run in it, so the origin has
    /// eight dummies standing on the Enemy layer and a sweep taken there would be answered about
    /// somebody else's arena (Traps §7: the PlayMode runner's scene is whatever the test before it
    /// left behind).
    /// </remarks>
    private static readonly Vector3 Origin = new Vector3(1000f, 0f, 1000f);

    /// <summary>
    /// How fast core walks the body. Fast on purpose: the whole point of several rows is that one
    /// frame's displacement is unmistakable, and at a walking pace a frame moves 3 cm.
    /// </summary>
    private const float WalkSpeed = 60f;

    /// <summary>
    /// How far the shove written from inside the cone answer travels. Well clear of the walk, and
    /// on the other axis, so the two cannot be mistaken for each other.
    /// </summary>
    private const float ShoveDistance = 4f;

    /// <summary>
    /// How many frames the shove is given to travel. Generous on purpose: what is being asserted is
    /// that the shove reached the body at all, and pinning it to the view's own slide duration would
    /// be a second test of <c>EnemyView.Knockback</c> wearing this one's name.
    /// </summary>
    private const int ShoveFrames = 20;

    private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private RecordingCore _core;
    private IntentBuffer _intents;
    private WorldSnapshot _snapshot;
    private EnemyViews _enemyViews;
    private ProjectileViews _projectileViews;
    private TelegraphRings _telegraphRings;
    private InputAdapter _input;
    private RunTicker _ticker;
    private EnemyView _body;

    [SetUp]
    public void BuildTheFrame()
    {
        int enemyLayer = LayerMask.NameToLayer("Enemy");

        Assert.That(enemyLayer, Is.GreaterThanOrEqualTo(0), "The project has no Enemy layer.");

        var builder = new ContainerBuilder();

        builder.Register<DomainEventHub>(Lifetime.Scoped).As<IDomainEvents>().AsSelf();

        _container = builder.Build();
        _hub = _container.Resolve<DomainEventHub>();

        _intents = new IntentBuffer();
        _snapshot = new WorldSnapshot(8);

        PlayerView player = PlayerObject(out ChargeMotion charge);

        _enemyViews = new EnemyViews(
            _container,
            EnemyTemplate(enemyLayer),
            null,
            _hub,
            new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
            prewarm: 1);

        _projectileViews = new ProjectileViews(_container, ProjectileTemplate(), null, _hub);

        // Nothing in this fixture publishes a telegraph or a blast, so the census stays empty and
        // steps nothing. It is here because the ticker takes one, which is M2-12b rule 4's other
        // half: being on that constructor is what guarantees the rings are listening before a run
        // can announce a wave.
        _telegraphRings = new TelegraphRings(_container, RingTemplate(), null, _hub, prewarm: 0);

        _input = new InputAdapter();

        // Never enabled, which is what makes the command phase a no-op: both properties the ticker
        // reads answer false while the adapter is disabled, so Poll returns before it touches the
        // camera. See the class remarks.
        var cameraObject = new GameObject("Camera");
        Track(cameraObject);

        var cone = new ConeOverlapQuery(
            ConeOverlapQuery.DefaultCapacity,
            1 << enemyLayer,
            _enemyViews);

        charge.Construct(_enemyViews, 1 << enemyLayer);

        _core = new RecordingCore(_intents);

        _ticker = new RunTicker(
            _core,
            _core,
            new PendingRun(),
            new ContentCatalog(Array.Empty<CharacterSpec>(), Array.Empty<EnemySpec>()),
            new SeededRandom(7),
            _snapshot,
            new SnapshotBuilder(player, _input, _enemyViews, null, null, null),
            _intents,
            player,
            charge,
            _enemyViews,
            _projectileViews,
            _telegraphRings,
            _input,
            SpawnPlan.Empty,
            new TapToFocusAdapter(_input, _core, cameraObject.AddComponent<Camera>()),
            cone);

        // Announced the way core announces it, so the body arrives through the subscription M1-07
        // wired rather than by this fixture reaching into the census.
        _hub.Publish(new EnemySpawned(
            EnemyId,
            HuskId,
            new CoreVector3(Origin.x, Origin.y, Origin.z)));

        Assert.That(_enemyViews.TryGet(EnemyId, out _body), Is.True, "The body was never bound.");

        _core.LiveEnemyPosition = () => _body.Position;
    }

    [TearDown]
    public void DropTheFrame()
    {
        _enemyViews?.Dispose();
        _enemyViews = null;

        _projectileViews?.Dispose();
        _projectileViews = null;

        _telegraphRings?.Dispose();
        _telegraphRings = null;

        _input?.Dispose();
        _input = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        _ticker = null;
        _core = null;
        _body = null;

        for (int i = 0; i < _created.Count; i++)
        {
            // Unity's ==: a pool disposed above has already destroyed some of these, and a
            // destroyed object is a live reference that only compares equal to null through the
            // engine's operator.
            if (_created[i] != null)
            {
                UnityEngine.Object.Destroy(_created[i]);
            }
        }

        _created.Clear();
    }

    /// <summary>
    /// The whole chain in one run of frames: every adjacency of AR §18.1's order, each pinned by a
    /// consequence a reordering removes.
    /// </summary>
    [UnityTest]
    public IEnumerator Ticker_RunsTheStepsInOrder()
    {
        _core.Walk = new Vector3(WalkSpeed, 0f, 0f);
        _core.EmitCone = true;

        // The shove is withheld until the walking half has been asserted: a shoved body stops
        // taking move intents (EnemyView.Apply returns early while it slides), so the two halves of
        // this row cannot be observed on the same frames.
        yield return Frame();

        Vector3 afterFirst = _body.Position;

        yield return Frame();

        // 1. The snapshot precedes the tick: what core was told on the second frame is where the
        //    body stood after the first, not where the first frame's snapshot said it was.
        Assert.That(
            _core.SnapshotEnemyPosition.X,
            Is.EqualTo(afterFirst.x).Within(1e-3f),
            "Core was told a position from before the previous frame's bodies moved.");

        // 2. The intents are cleared before core writes: the flag core raised on the first frame is
        //    down again by the time it is ticked on the second.
        Assert.That(
            _core.IntentFlagAtTick,
            Is.False,
            "The buffer was cleared after core wrote to it, which erases an unread intent (M1-16).");

        // 3. Core is ticked before the bodies move, and 4. the facts are reported after they have:
        //    the position the arena held when the cone was answered is this frame's snapshot
        //    position plus this frame's chosen velocity, which neither exists until the tick nor
        //    lands until the bodies are applied.
        Assert.That(
            _core.LiveEnemyPositionAtFactTime.x,
            Is.EqualTo(_core.SnapshotEnemyPosition.X + (WalkSpeed * _core.Dt)).Within(1e-2f),
            "The sweep was answered against the arena as it stood before the bodies moved.");

        // 5. And the sweep itself agrees, through real physics: the wedge was placed where the body
        //    would be *after* this frame's move, so a body still standing where the snapshot found
        //    it is a metre outside it.
        Assert.That(
            _core.ConeReport,
            Is.EqualTo(new[] { EnemyId }),
            "The cone found nothing where the body had just walked to.");

        // 6. The knockbacks are last of all. The shove is written from inside the cone answer —
        //    later in the frame than every other intent in the buffer — so a reader placed above
        //    that line looks at an empty list on every frame there has ever been.
        float beforeShove = _body.Position.z;

        _core.EmitShove = true;

        for (int i = 0; i < ShoveFrames; i++)
        {
            yield return Frame();
        }

        Assert.That(
            _body.Position.z - beforeShove,
            Is.GreaterThan(1f),
            "The shove core wrote while answering the cone never reached the body, so "
                + "ApplyKnockbacks is running above the facts that produce it.");

        // One tick and one fact per frame, the tick first, on every frame. A fact hoisted above the
        // tick shows up here whatever else the frame does.
        var expected = new List<string>(2 * (ShoveFrames + 2));

        for (int frame = 0; frame < ShoveFrames + 2; frame++)
        {
            expected.Add("tick");
            expected.Add("facts");
        }

        Assert.That(_core.Touched, Is.EqualTo(expected));

        LogAssert.NoUnexpectedReceived();
    }

    [UnityTest]
    public IEnumerator Ticker_SnapshotPrecedesTheTick()
    {
        _core.Walk = new Vector3(WalkSpeed, 0f, 0f);

        yield return Frame();

        Assert.That(
            _core.SnapshotEnemyPosition.X,
            Is.EqualTo(Origin.x).Within(1e-3f),
            "Sanity: the first frame senses the body where it was spawned.");

        Vector3 afterFirst = _body.Position;

        Assert.That(
            afterFirst.x,
            Is.GreaterThan(Origin.x + 0.5f),
            "Sanity: the body walked.");

        yield return Frame();

        // The failure this pins does not look like its cause: a snapshot built after the tick makes
        // the gun aim at where enemies *were*, and reads as the auto-aim being stupid.
        Assert.That(
            _core.SnapshotEnemyPosition.X,
            Is.EqualTo(afterFirst.x).Within(1e-3f),
            "Core is being told a position one frame stale.");
    }

    [UnityTest]
    public IEnumerator Ticker_ClearsIntentsBeforeCoreWrites()
    {
        yield return Frame();

        Assert.That(
            _intents.HasPlayerMove,
            Is.True,
            "Sanity: core wrote a move on the first frame and nothing cleared it afterwards — the "
                + "buffer is still holding this frame's intent when the frame ends, which is what "
                + "every intent reader depends on.");

        yield return Frame();

        // M1-16's invariant, from the other end: a buffer cleared *after* the tick erases the
        // intent nothing has read yet, and the symptom is an intent list that reads empty every
        // single frame — which looks like the motor being broken.
        Assert.That(
            _core.IntentFlagAtTick,
            Is.False,
            "Core saw the previous frame's raised flag, so the clear is on the wrong side of the "
                + "tick.");

        Assert.That(
            _intents.HasPlayerMove,
            Is.True,
            "And this frame's intent is there when the frame ends, never empty.");
    }

    [UnityTest]
    public IEnumerator Ticker_ReportsFactsAfterBodiesMoved()
    {
        _core.Walk = new Vector3(WalkSpeed, 0f, 0f);
        _core.EmitCone = true;

        yield return Frame();

        Assert.That(
            _core.LiveEnemyPositionAtFactTime.x,
            Is.GreaterThan(_core.SnapshotEnemyPosition.X + 0.5f),
            "The arena had not moved when the fact was answered, so the sweep resolved against "
                + "last frame's positions — which reads as a swing that misses an enemy standing "
                + "in it.");

        Assert.That(
            _core.ConeReport,
            Is.EqualTo(new[] { EnemyId }),
            "The wedge was placed where this frame's move puts the body, and the body was not "
                + "there when physics was asked.");
    }

    // ---- Fixture ------------------------------------------------------------------------------

    /// <summary>One real Unity frame, with the ticker run in it exactly once.</summary>
    /// <remarks>
    /// The wait comes first so that the <c>Time.deltaTime</c> the ticker reads belongs to a frame
    /// this coroutine did not spend setting itself up.
    /// </remarks>
    private IEnumerator Frame()
    {
        yield return null;

        _ticker.Tick();
    }

    /// <summary>
    /// The player: a body with a controller, and the dash that shares it. Both are what
    /// <c>RunScope</c> guards for rather than treats as optional.
    /// </summary>
    private PlayerView PlayerObject(out ChargeMotion charge)
    {
        var root = new GameObject("Player");

        root.transform.position = Origin;

        Track(root);

        // PlayerView first: adding it brings the CharacterController it requires, which is what
        // both it and the dash move the capsule with.
        PlayerView view = root.AddComponent<PlayerView>();

        // Injected by hand, because nothing here is built by a container: a view that reaches Start
        // uninjected throws, which is the guard doing its job rather than a fixture problem.
        view.Construct(_hub);

        charge = root.AddComponent<ChargeMotion>();

        return view;
    }

    /// <summary>
    /// The smallest body <see cref="EnemyView"/> will accept, on the layer a sweep looks at.
    /// </summary>
    /// <remarks>
    /// Inactive first, so neither <c>Awake</c> nor <c>Start</c> runs on the template — VContainer
    /// deactivates a prefab before instantiating and injects the copy while it is off, so an
    /// inactive template still produces a correctly injected instance. No
    /// <c>EnemyHitFeedback</c> and no renderer: nothing here is looked at, and an archetype with no
    /// authored look gets <c>EnemyLook.Default</c> rather than being skipped (M2-06).
    /// </remarks>
    private EnemyView EnemyTemplate(int enemyLayer)
    {
        var root = new GameObject("EnemyTemplate") { layer = enemyLayer };

        root.SetActive(false);

        Track(root);

        EnemyView view = root.AddComponent<EnemyView>();

        // A trigger, exactly as Enemy.prefab authors it, and the whole reason the cone query asks
        // for QueryTriggerInteraction.Collide.
        view.Body.isTrigger = true;

        return view;
    }

    private ProjectileView ProjectileTemplate()
    {
        var root = new GameObject("ProjectileTemplate");

        root.SetActive(false);

        Track(root);

        return root.AddComponent<ProjectileView>();
    }

    private TelegraphRingView RingTemplate()
    {
        var root = new GameObject("RingTemplate");

        root.SetActive(false);

        Track(root);

        return root.AddComponent<TelegraphRingView>();
    }

    private T Track<T>(T o)
        where T : UnityEngine.Object
    {
        _created.Add(o);

        return o;
    }

    /// <summary>
    /// Core, as far as the frame is concerned: it records when it was touched and what it could see,
    /// and it writes its intents at exactly the two moments the real session writes them.
    /// </summary>
    /// <remarks>
    /// <b>The shove goes out from inside <see cref="ReportConeHits"/></b>, which is not a detail:
    /// that is where <c>PlayerCombat</c> writes one, later in the frame than every other intent in
    /// the buffer, and it is the entire reason <c>ApplyKnockbacks</c> is the last line of the tick.
    /// </remarks>
    private sealed class RecordingCore : IRunSession, IPlayerCommands
    {
        private readonly IntentBuffer _intents;

        /// <summary>
        /// The same buffer, through the port core actually holds. The write members are explicit
        /// interface implementations — deliberately, so that a *view* holding an
        /// <see cref="IntentBuffer"/> can only read — so core has to be spelled as the thing that
        /// can only write.
        /// </summary>
        private readonly IIntentSink _sink;

        private readonly List<string> _touched = new List<string>();
        private readonly List<int> _coneReport = new List<int>();

        public RecordingCore(IntentBuffer intents)
        {
            _intents = intents;
            _sink = intents;
        }

        /// <summary>Which of this object's members the frame reached, in the order it reached them.</summary>
        public IReadOnlyList<string> Touched => _touched;

        public IReadOnlyList<int> ConeReport => _coneReport;

        public bool IsRunning { get; private set; } = true;

        /// <summary>
        /// Null, deliberately. <c>RunState</c>'s constructor is <c>internal</c> with no
        /// <c>InternalsVisibleTo</c> (AR §18.2), and nothing in the frame reads this — the one thing
        /// that does is <c>DebugOverlay</c>, which this fixture does not build.
        /// </summary>
        public RunState State => null;

        /// <summary>The velocity core decides for the body. Set by the row.</summary>
        public Vector3 Walk { get; set; }

        public bool EmitCone { get; set; }

        public bool EmitShove { get; set; }

        /// <summary>Reads the live body's position. Supplied by the fixture.</summary>
        public Func<Vector3> LiveEnemyPosition { get; set; }

        /// <summary>Where the snapshot said the body was, on the most recent tick.</summary>
        public CoreVector3 SnapshotEnemyPosition { get; private set; }

        /// <summary>The step the most recent tick was handed.</summary>
        public float Dt { get; private set; }

        /// <summary>Whether a player move was still flagged when the most recent tick began.</summary>
        public bool IntentFlagAtTick { get; private set; }

        /// <summary>Where the body actually stood when the most recent fact was answered.</summary>
        public Vector3 LiveEnemyPositionAtFactTime { get; private set; }

        public void Start(RunConfig config) => _touched.Add("start");

        public void Tick(WorldSnapshot snapshot)
        {
            _touched.Add("tick");

            // Read before anything is written, which is the only place it can be read: the flag is
            // about the frame's *previous* occupant of this buffer.
            IntentFlagAtTick = _intents.HasPlayerMove;

            Dt = snapshot.Dt;
            SnapshotEnemyPosition = Find(snapshot, EnemyId);

            _sink.PlayerMove(new PlayerMoveIntent(CoreVector3.Zero, new CoreVector3(0f, 0f, 1f)));

            _sink.EnemyMove(new EnemyMoveIntent(
                EnemyId,
                new CoreVector3(Walk.x, Walk.y, Walk.z),
                new CoreVector2(1f, 0f)));

            if (!EmitCone)
            {
                return;
            }

            // Placed where this frame's own move puts the body, with a wedge tight enough that the
            // position the snapshot reported is well outside it. Core knows both halves — it chose
            // the velocity and it was handed the step — so the expectation needs no arithmetic from
            // the test.
            var expected = new CoreVector3(
                SnapshotEnemyPosition.X + (Walk.x * Dt),
                SnapshotEnemyPosition.Y,
                SnapshotEnemyPosition.Z + (Walk.z * Dt));

            _sink.ConeHit(new ConeHitIntent(
                _touched.Count,
                expected,
                new CoreVector2(1f, 0f),
                range: 0.5f,
                angleDeg: 60f));
        }

        public void ReportConeHits(ReadOnlySpan<int> enemyIds)
        {
            _touched.Add("facts");

            LiveEnemyPositionAtFactTime = LiveEnemyPosition();

            _coneReport.Clear();

            for (int i = 0; i < enemyIds.Length; i++)
            {
                _coneReport.Add(enemyIds[i]);
            }

            if (EmitShove)
            {
                _sink.EnemyKnockback(new EnemyKnockbackIntent(
                    EnemyId,
                    new CoreVector2(0f, 1f),
                    ShoveDistance));
            }
        }

        public void ReportChargeHits(ReadOnlySpan<int> enemyIds) => _touched.Add("charge-facts");

        public void End()
        {
            _touched.Add("end");

            IsRunning = false;
        }

        public void FocusTarget(CoreVector3 worldPoint) => _touched.Add("command:focus");

        public void ClearFocus() => _touched.Add("command:clear-focus");

        public void MovementSkill() => _touched.Add("command:skill");

        private static CoreVector3 Find(WorldSnapshot snapshot, int id)
        {
            for (int i = 0; i < snapshot.EnemyCount; i++)
            {
                if (snapshot.Enemies[i].Id == id)
                {
                    return snapshot.Enemies[i].Position;
                }
            }

            Assert.Fail($"The snapshot carries no enemy {id}.");

            return CoreVector3.Zero;
        }
    }
}
