using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
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
/// <b>The commands phase was the one step not observed here, and since M3-10a it is observed.</b>
/// Its two original members reach core only when the Input System reports a press, and this assembly
/// does not reference the Input System — adding it would put un-isolated device state into the
/// assembly that also holds the boot smoke tests. <c>SkillSlotInput</c> needs none: a slot button
/// writes an <c>int</c> into it and <c>CommandPhase</c> polls that, so
/// <see cref="Frame_SlotPollSitsBesideTapToFocus"/> pins the adjacency the other two still inherit
/// by sitting in the same three lines.
/// </para>
/// </remarks>
public sealed class FrameOrderTests
{
    private static readonly ContentId HuskId = new ContentId("enemy.husk");
    private static readonly ContentId WightId = new ContentId("minion.wight");

    /// <summary>The one body in the arena. Core's id, and the only one these rows use.</summary>
    private const int EnemyId = 1;

    /// <summary>
    /// The one Wight, when a row raises one — <b>deliberately the same number as
    /// <see cref="EnemyId"/></b>.
    /// </summary>
    /// <remarks>
    /// M5-05a rule 7 is that the two id spaces are different and both count from 1, so an intent
    /// routed through the wrong census finds a stranger rather than nothing. A fixture that gave
    /// the Wight id 2 would prove only that a lookup missed; sharing the number is what makes
    /// <see cref="Ticker_AMinionMoveWalksAMinion"/> able to fail.
    /// </remarks>
    private const int MinionId = 1;

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

    /// <summary>
    /// How fast core walks the Wight, in m/s. Ten times <see cref="WalkSpeed"/>, and the reason is
    /// <see cref="Ticker_MinionsMoveBeforeTheFlush"/>'s: that row asks physics a question about a
    /// 0.1 m sphere, so one frame's displacement has to clear the body's own radius by a margin
    /// that survives whatever frame rate the Editor happens to be running at.
    /// </summary>
    private const float MinionWalkSpeed = 600f;

    /// <summary>
    /// How far from the body the fact-time probe looks, in metres. Small on purpose: a body that
    /// had <em>not</em> moved this frame must be outside it.
    /// </summary>
    private const float ProbeRadius = 0.1f;

    /// <summary>
    /// Where the Wight stands, relative to <see cref="Origin"/>. Twelve metres down +Z, so it is
    /// nowhere near the enemy's wedge, the enemy's walk, or the enemy's body.
    /// </summary>
    private static readonly Vector3 MinionOffset = new Vector3(0f, 0f, 12f);

    /// <summary>
    /// What <see cref="DiagnoseTheEmptyReport"/> says when the re-issued query <em>found</em> the
    /// body. [Ledger row 4]
    /// </summary>
    private const string LateSync =
        "The cone found nothing where the body had just walked to — and the same query, re-issued "
            + "in the same frame, found it. The body was in the right place and the physics scene "
            + "did not know yet, so the transform sync at the seam was late.";

    /// <summary>
    /// What <see cref="DiagnoseTheEmptyReport"/> says when the re-issued query found nothing
    /// either. [Ledger row 4]
    /// </summary>
    private const string WrongWedge =
        "The cone found nothing where the body had just walked to — and the same query, re-issued "
            + "in the same frame, found nothing either. The physics scene is settled and the body "
            + "is not in it, so the wedge was in the wrong place.";

    private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

    /// <summary>
    /// Colliders the fact-time probe may see. One array for the fixture's life, so the probe does
    /// not allocate inside the frame it is measuring.
    /// </summary>
    private readonly Collider[] _probeHits = new Collider[8];

    private IObjectResolver _container;
    private DomainEventHub _hub;
    private RecordingCore _core;
    private IntentBuffer _intents;
    private WorldSnapshot _snapshot;
    private EnemyViews _enemyViews;
    private MinionViews _minionViews;
    private ProjectileViews _projectileViews;
    private TelegraphRings _telegraphRings;
    private ZoneViews _zoneViews;
    private BossViews _bossViews;
    private DecoyViews _decoyViews;
    private InputAdapter _input;
    private RunTicker _ticker;
    private EnemyView _body;

    /// <summary>
    /// The frame's one-press buffer for CC §6.2's slot buttons. Real rather than a fake: it is half
    /// of what <see cref="Frame_SlotPollSitsBesideTapToFocus"/> is about, and the other half is where
    /// the ticker polls it (M3-10a rule 3).
    /// </summary>
    private SkillSlotInput _skillSlots;

    /// <summary>
    /// The gate the level-up phase raises. Real rather than a fake: it is the object under test in
    /// the four <c>Frame_*</c> rows, and it writes two engine globals that <c>TearDown</c> has to
    /// put back.
    /// </summary>
    private RunPause _pause;

    /// <summary>
    /// The swing's sweep, and <b>a field rather than a local as of M5-05a</b> — [ledger row 4].
    /// The ticker has always taken one; keeping the reference is what lets
    /// <see cref="DiagnoseTheEmptyReport"/> re-issue the very query that came back empty, in the
    /// same frame, against the same physics scene.
    /// </summary>
    private ConeOverlapQuery _cone;

    /// <summary>Where the re-issued query's answer goes. Sized like the ticker's own buffer.</summary>
    private int[] _diagnosisIds;

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

        // One body, because one Wight is what the two minion rows raise. Empty until a row raises
        // one, which is the state every other row in this fixture runs in.
        _minionViews = new MinionViews(_container, MinionTemplate(), null, _hub, prewarm: 1);

        _projectileViews = new ProjectileViews(_container, ProjectileTemplate(), null, _hub);

        // Nothing in this fixture publishes a telegraph or a blast, so the census stays empty and
        // steps nothing. It is here because the ticker takes one, which is M2-12b rule 4's other
        // half: being on that constructor is what guarantees the rings are listening before a run
        // can announce a wave.
        _telegraphRings = new TelegraphRings(_container, RingTemplate(), null, _hub, prewarm: 0);

        // Empty for the rings' reason and on the rings' terms: nothing here casts a skill, so no
        // zone is ever spawned and the census steps nothing. It is here because the ticker takes
        // one (M3-11c rule 3).
        _zoneViews = new ZoneViews(_container, ZoneTemplate(), null, _hub, prewarm: 0);

        // Empty on the zones' terms and with a null arena pool besides: nothing here reaches a
        // boss, so no ring, crack or shell is ever rented. It is here because the ticker takes one
        // (M4-03 rule 5).
        _bossViews = new BossViews(
            _container,
            Template<ShockwaveView>("ShockwaveTemplate"),
            Template<FissureView>("FissureTemplate"),
            Template<BossBeatView>("BeatTemplate"),
            null,
            _hub,
            _enemyViews,
            null,
            shockwavePrewarm: 0,
            fissurePrewarm: 0,
            beatPrewarm: 0);

        // Empty on the zones' terms and for a sharper version of their reason: nothing here drops a
        // decoy, and this census has no Step for the order below to place even if one did (M5-05b
        // rule 2). It is here because the ticker takes one — the subscription guarantee, and
        // nothing else.
        _decoyViews = new DecoyViews(_container, Template<DecoyView>("DecoyTemplate"), null, _hub, prewarm: 0);

        _input = new InputAdapter();

        // Never enabled, which is what makes the command phase a no-op: both properties the ticker
        // reads answer false while the adapter is disabled, so Poll returns before it touches the
        // camera. See the class remarks.
        var cameraObject = new GameObject("Camera");
        Track(cameraObject);

        _cone = new ConeOverlapQuery(
            ConeOverlapQuery.DefaultCapacity,
            1 << enemyLayer,
            _enemyViews);

        _diagnosisIds = new int[_cone.Capacity];

        charge.Construct(_enemyViews, 1 << enemyLayer);

        _core = new RecordingCore(_intents);

        _pause = new RunPause();

        _skillSlots = new SkillSlotInput(_core);

        _ticker = new RunTicker(
            _core,
            _core,
            _core,
            _pause,
            new PendingRun(),
            new ContentCatalog(Array.Empty<CharacterSpec>(), Array.Empty<EnemySpec>()),
            new SeededRandom(7),
            _snapshot,
            new SnapshotBuilder(player, _input, _enemyViews, _minionViews, null, null, null),
            _intents,
            player,
            charge,
            _enemyViews,
            _minionViews,
            _projectileViews,
            _telegraphRings,
            _zoneViews,
            _bossViews,
            _decoyViews,

            // Nothing here takes a snapshot, so this writes nothing — it is on the constructor for
            // the reason the rings above are (M2-14a rule 8): being on that constructor is what
            // guarantees the writer is subscribed before a run can announce its opening snapshot.
            new SaveWriter(new InertSaveStore(), _hub),

            // And M4-05b's writer, on the constructor for the same reason: a Scoped registration
            // nobody resolves is never constructed, so the parameter is what makes the object exist.
            new ShardWriter(new ProfileStore(new InertSaveStore()), _hub),
            _input,
            SpawnPlan.Empty,
            new TapToFocusAdapter(_input, _core, cameraObject.AddComponent<Camera>()),
            _skillSlots,
            _cone);

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
        // First, and unconditionally: it writes Time.timeScale and Application.targetFrameRate, so a
        // row that ended paused would otherwise leave the Editor — and every later fixture in the
        // run — on a frozen clock.
        _pause?.Dispose();
        _pause = null;

        _enemyViews?.Dispose();
        _enemyViews = null;

        _minionViews?.Dispose();
        _minionViews = null;

        _projectileViews?.Dispose();
        _projectileViews = null;

        _telegraphRings?.Dispose();
        _telegraphRings = null;

        _zoneViews?.Dispose();
        _zoneViews = null;

        _bossViews?.Dispose();
        _bossViews = null;

        _decoyViews?.Dispose();
        _decoyViews = null;

        _input?.Dispose();
        _input = null;

        _hub?.Dispose();
        _hub = null;

        _container?.Dispose();
        _container = null;

        _ticker = null;
        _core = null;
        _body = null;
        _skillSlots = null;
        _cone = null;
        _diagnosisIds = null;

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
        //
        //    **This is the row that fails about one run in ten** (PROGRESS → Known issues, ledger
        //    row 4), and since M5-05a it says which of two things happened rather than restating
        //    what was expected. Thirteen tasks of tallies diagnosed nothing; this converts every
        //    future failure into one of two named answers. **It is not a fix** — nothing in
        //    RunTicker changed and no re-run was added — so a milestone in which this stays green
        //    is luck rather than evidence.
        if (_core.ConeReport.Count == 0)
        {
            Assert.Fail(DiagnoseTheEmptyReport());
        }

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

    // ---- The minion step (M5-05a rules 6, 7) ----------------------------------------------------

    /// <summary>
    /// A minion move walks a minion and an enemy move walks an enemy, on the same frame, with the
    /// same id — rule 7 against real components rather than argued.
    /// </summary>
    /// <remarks>
    /// <b>The two bodies carry the same number on purpose.</b> <c>MinionSystem</c> and
    /// <c>EnemyRegistry</c> both hand out ids from 1, so a minion intent resolved through
    /// <c>EnemyViews.TryGet</c> does not miss — it finds a Husk and walks it. That is the failure
    /// this row exists for, and it is invisible to any fixture whose two bodies have different
    /// ids.
    /// </remarks>
    [UnityTest]
    public IEnumerator Ticker_AMinionMoveWalksAMinion()
    {
        MinionView wight = RaiseTheWight();

        Vector3 enemyBefore = _body.Position;
        Vector3 wightBefore = wight.Position;

        _core.Walk = new Vector3(WalkSpeed, 0f, 0f);
        _core.MinionWalk = new Vector3(0f, 0f, WalkSpeed);

        yield return Frame();

        Assert.That(
            _body.Position.x - enemyBefore.x,
            Is.GreaterThan(0.1f),
            "The enemy did not take its own intent.");

        Assert.That(
            Mathf.Abs(_body.Position.z - enemyBefore.z),
            Is.LessThan(0.01f),
            "The enemy moved along +Z, which is the *minion's* intent — the two lists have been "
                + "crossed, and every Wight in the game is steering a Husk.");

        Assert.That(
            wight.Position.z - wightBefore.z,
            Is.GreaterThan(0.1f),
            "The Wight never moved, so ApplyMinionMoves either did not run or resolved the id "
                + "through the wrong census.");

        Assert.That(
            Mathf.Abs(wight.Position.x - wightBefore.x),
            Is.LessThan(0.01f),
            "The Wight moved along +X, which is the *enemy's* intent.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// The Wight is walked above <c>Physics.SyncTransforms()</c>, so a query asked during the fact
    /// phase finds its body where this frame put it — rule 6.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The probe runs from inside <c>ReportConeHits</c>, which is below the flush, and looks for
    /// the body's <em>trigger capsule</em> specifically. That is the collider the flush governs:
    /// <c>CharacterController.Move</c> moves the controller's own shape as it sweeps, so a probe
    /// that accepted any collider on the object would pass whatever the ordering was.
    /// </para>
    /// <para>
    /// At a very short frame the pre- and post-move positions overlap inside
    /// <see cref="ProbeRadius"/> and the row degrades to "the sweep found the body", which is
    /// weaker but never wrong — hence <see cref="MinionWalkSpeed"/>, which is ten times the
    /// enemy's so that one frame is metres rather than centimetres.
    /// </para>
    /// </remarks>
    [UnityTest]
    public IEnumerator Ticker_MinionsMoveBeforeTheFlush()
    {
        MinionView wight = RaiseTheWight();

        Vector3 before = wight.Position;

        _core.EmitCone = true;
        _core.MinionWalk = new Vector3(0f, 0f, MinionWalkSpeed);

        var sweptAtFactTime = false;
        var reachedFactTime = false;
        Vector3 whereAtFactTime = Vector3.zero;

        _core.OnFactTime = () =>
        {
            reachedFactTime = true;
            whereAtFactTime = wight.Position;
            sweptAtFactTime = TheSweepFinds(wight, whereAtFactTime);
        };

        yield return Frame();

        Assert.That(reachedFactTime, Is.True, "Sanity: the frame never reached its fact phase.");

        Assert.That(
            whereAtFactTime.z - before.z,
            Is.GreaterThan(0.1f),
            "Sanity: the Wight had not been walked at all by the time the fact was answered, so "
                + "this row is measuring nothing.");

        Assert.That(
            sweptAtFactTime,
            Is.True,
            "A sweep taken below the flush did not find the Wight where the frame had just put "
                + "it, so the minion step is running after Physics.SyncTransforms() — the body "
                + "would be the one exception on the day something starts sweeping for one.");

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// The control for [ledger row 4]'s instrument: a wedge deliberately placed where no body is,
    /// and a diagnosis that says so by name.
    /// </summary>
    /// <remarks>
    /// <b>Without it the instrument is untested in one direction.</b>
    /// <see cref="Ticker_RunsTheStepsInOrder"/> only reaches
    /// <see cref="DiagnoseTheEmptyReport"/> on the one run in ten that fails, so nothing would
    /// ever exercise the re-issued query on a build where the row happened to pass — and an
    /// instrument nobody has watched read both ways is a message rather than a measurement.
    /// </remarks>
    [UnityTest]
    public IEnumerator Ticker_AnEmptyConeReportIsDiagnosed()
    {
        _core.Walk = new Vector3(WalkSpeed, 0f, 0f);
        _core.EmitCone = true;

        // Forty metres off the body, on the axis it is walking down. Nothing is there, this frame
        // or any other.
        _core.ConeOffset = new CoreVector3(40f, 0f, 0f);

        yield return Frame();

        Assert.That(
            _core.ConeReport,
            Is.Empty,
            "The fixture's premise: a wedge 40 m from the only body in the arena found something.");

        // StartWith rather than EqualTo: the diagnosis carries the measurement after the verdict,
        // and the verdict is what this row is about.
        Assert.That(DiagnoseTheEmptyReport(), Does.StartWith(WrongWedge));

        LogAssert.NoUnexpectedReceived();
    }

    // ---- Fixture ------------------------------------------------------------------------------

    /// <summary>One real Unity frame, with the ticker run in it exactly once.</summary>
    /// <remarks>
    /// The wait comes first so that the <c>Time.deltaTime</c> the ticker reads belongs to a frame
    /// this coroutine did not spend setting itself up.
    /// </remarks>
    // ---- The level-up phase and the gate (M3-08a rules 4, 10, 11, 14) ----------------------------

    /// <summary>
    /// The phase runs above the commands, and a held pause returns before either the commands or
    /// the tick.
    /// </summary>
    [UnityTest]
    public IEnumerator Frame_LevelUpPhaseRunsAboveCommands()
    {
        // Core says a pick is owed, and opening it puts an offer on the table — which is what the
        // real flow does, and what the gate reads.
        _core.IsLevelUpPending = true;
        _core.OnOpenLevelUp = () =>
        {
            _core.IsLevelUpPending = false;
            _core.HasOffer = true;
        };

        yield return Frame();

        // The phase ran, and it is the *only* thing that reached core this frame: no tick, and no
        // command. A return placed below CommandPhase would have let a tap focus an enemy or spend
        // the Charge on the frame the screen opened (rule 14).
        Assert.That(_core.Touched, Is.EqualTo(new[] { "level-up:open" }));

        Assert.That(_pause.IsPaused, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.LevelUp));

        // And the gate really is gating: the engine clock is stopped too, so a Husk cannot finish a
        // wind-up animation the frozen simulation will never honour (rule 13).
        Assert.That(Time.timeScale, Is.EqualTo(0f));

        // Handed back inside the row rather than left to TearDown: this fixture shares a PlayMode
        // run with rows that take real physics sweeps, and every frame spent at timeScale 0 is a
        // frame of that run behaving unlike the others.
        _core.HasOffer = false;

        yield return Frame();

        Assert.That(_pause.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// The tick that earns the level finishes: its facts are still reported, and the pause takes
    /// effect on the frame after.
    /// </summary>
    [UnityTest]
    public IEnumerator Frame_TheLevellingTickCompletes()
    {
        _core.Walk = new Vector3(WalkSpeed, 0f, 0f);
        _core.EmitCone = true;

        // Nothing is owed at the top of this frame, so it is an ordinary one — and core earns the
        // level part way through it, from inside its own Tick, exactly as a stage's last kill does.
        yield return Frame();

        _core.OnOpenLevelUp = () => { _core.IsLevelUpPending = false; _core.HasOffer = true; };
        _core.LevelUpDuringTick = true;

        yield return Frame();

        // **The levelling frame was not cut short**: it ticked, and it went on to *report its
        // facts*, which is the last thing in the frame and sits below `session.Tick` — so everything
        // between the two happened as well.
        //
        // It asserts that the fact phase was **reached**, not what the sweep found. The two are
        // different claims and only one of them is this row's: `Ticker_RunsTheStepsInOrder` owns
        // "the cone found the body", and that assertion is the project's one known intermittent
        // failure (PROGRESS → Known issues). Hanging a second row on it measured at 14 % flaky over
        // 50 isolated runs and took the fixture from 10 % to 30 % — a row that would have failed
        // one morning in seven for a reason that has nothing to do with level-ups.
        Assert.That(_core.Touched, Does.Contain("tick"));
        Assert.That(_core.Touched, Does.Contain("facts"), "the levelling frame stopped before its fact phase.");

        Assert.That(
            IndexIn(_core.Touched, "facts"),
            Is.GreaterThan(IndexIn(_core.Touched, "tick")),
            "the facts have to follow the tick that earned the level, not precede it.");

        Assert.That(_pause.IsPaused, Is.False, "the pause must not take effect inside the tick that earned it.");

        int touchedBefore = _core.Touched.Count;

        // The frame *after* is where the flag is read and the gate goes up.
        yield return Frame();

        Assert.That(_pause.IsPaused, Is.True);
        Assert.That(
            _core.Touched.Count - touchedBefore,
            Is.EqualTo(1),
            "the gated frame reached core once, to open the level-up, and not to tick it.");

        _core.HasOffer = false;

        yield return Frame();

        LogAssert.NoUnexpectedReceived();
    }

    // ---- CH §5.4's moment and the second pause reason (M5-07a-ii rule 5) -------------------------

    /// <summary>
    /// The half-tree moment holds the pause under its own reason, and a level-up owed on the same
    /// frame does not throw.
    /// </summary>
    /// <remarks>
    /// <b>The premise is not hypothetical.</b> A sixth node taken between ticks crosses CH §5.4's
    /// threshold, and a kill on the same tick can bank a pick — so the two arrive together, and
    /// <c>RunPause.Pause</c> throws for a second holder. A gate that discovered that from inside the
    /// frame loop would turn a screen collision into a dead run.
    /// </remarks>
    [UnityTest]
    public IEnumerator Run_TheSplashPauseIsItsOwnReason()
    {
        _core.IsSplashPending = true;
        _core.OnOpenSplash = () =>
        {
            _core.IsSplashPending = false;
            _core.IsSplashOpen = true;
        };

        // Owed on the same frame, and deliberately left owed: the phase must not reach for it while
        // the splash is holding the run.
        _core.IsLevelUpPending = true;

        yield return Frame();

        Assert.That(_pause.IsPaused, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Splash));
        Assert.That(Time.timeScale, Is.EqualTo(0f));

        // Answered: the screen closes and the pause comes back on the next frame, under the reason
        // that took it.
        _core.IsSplashOpen = false;

        yield return Frame();

        Assert.That(_pause.IsPaused, Is.False, "the splash never gave the pause back.");
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>
    /// Both owed on one frame: the splash opens and the level-up does not.
    /// </summary>
    /// <remarks>
    /// The splash is the rarer and more consequential of the two, and a player who took a node and
    /// then chose a discipline has seen them in the order they happened. The other direction is
    /// core's: <c>RunState.IsSplashPending</c> is false while an offer is on the table, so a
    /// level-up already mid-episode finishes first (<c>SplashFlowTests.Run_BothCanBeOwedOnOneFrame</c>).
    /// </remarks>
    [UnityTest]
    public IEnumerator Run_TheSplashIsReadBeforeTheLevelUp()
    {
        _core.IsSplashPending = true;
        _core.IsLevelUpPending = true;

        _core.OnOpenSplash = () =>
        {
            _core.IsSplashPending = false;
            _core.IsSplashOpen = true;
        };

        _core.OnOpenLevelUp = () => { _core.IsLevelUpPending = false; _core.HasOffer = true; };

        yield return Frame();

        // The only thing that reached core this frame was the splash: no level-up was opened, so no
        // offer was drawn and the Offers stream is where it was.
        Assert.That(_core.Touched, Is.EqualTo(new[] { "splash:open" }));
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.Splash));

        // And the level-up is still owed — it was deferred rather than lost, which is the half a
        // "the splash won" assertion alone would not say.
        Assert.That(_core.IsLevelUpPending, Is.True);

        // The branch is chosen, and the frame after is the level-up's: the pause changes hands in
        // one frame, which is what release-before-acquire buys.
        _core.IsSplashOpen = false;

        yield return Frame();

        Assert.That(_core.Touched, Does.Contain("level-up:open"));
        Assert.That(_pause.IsPaused, Is.True);
        Assert.That(_pause.Holder, Is.EqualTo(PauseReason.LevelUp));

        _core.HasOffer = false;

        yield return Frame();

        Assert.That(_pause.IsPaused, Is.False);
        Assert.That(Time.timeScale, Is.EqualTo(1f));

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>A paused run is <em>idled</em>, not clocked at zero.</summary>
    [UnityTest]
    public IEnumerator Frame_PausedFrameDoesNotTickCore()
    {
        _core.HasOffer = true;

        yield return Frame();

        Assert.That(_pause.IsPaused, Is.True, "the fixture's premise.");

        int touchedBefore = _core.Touched.Count;

        for (int i = 0; i < 60; i++)
        {
            yield return Frame();
        }

        // Zero ticks and zero facts across a whole second. A Dt = 0 tick would have walked the
        // entire pipeline sixty times — targeting re-resolving, the director asked, and a Weapon
        // whose next swing was already due firing once — and would have spent sixty snapshot builds
        // on the one screen GD §11.4 wants cheap.
        Assert.That(_core.Touched.Count, Is.EqualTo(touchedBefore), "core was reached on a gated frame.");

        // Handed back inside the row rather than left to TearDown: this fixture shares a PlayMode
        // run with rows that take real physics sweeps, and every frame spent at timeScale 0 is a
        // frame of that run behaving unlike the others. TearDown still restores unconditionally.
        _core.HasOffer = false;

        yield return Frame();

        Assert.That(_pause.IsPaused, Is.False);

        LogAssert.NoUnexpectedReceived();
    }

    /// <summary>A gated frame costs no simulated seconds, which is ledger row 8's whole point.</summary>
    [UnityTest]
    public IEnumerator Frame_PausedTicksCostNoSimulatedTime()
    {
        // One ordinary frame first, so the row is measuring a clock that was demonstrably moving.
        yield return Frame();

        float before = _core.SimulatedTime;

        Assert.That(before, Is.GreaterThan(0f), "the fixture never ticked, so it proves nothing.");

        _core.HasOffer = true;

        for (int i = 0; i < 60; i++)
        {
            yield return Frame();
        }

        // **RunState.Time sums each tick's Dt, and a gated frame contributes none.** So the level-up
        // screen costs zero *simulated* seconds while a stopwatch keeps running — GD §7.3's 40–75 s
        // band measured from RunState.Time is play time, and from this task on the two numbers are
        // no longer the same. M3-15 has to say which it is quoting (ledger row 8).
        Assert.That(_core.SimulatedTime, Is.EqualTo(before), "a paused frame advanced the simulated clock.");

        // Handed back inside the row, for the reason above.
        _core.HasOffer = false;

        // **Two frames, and the first of them is a finding rather than padding.** `Resume` restores
        // `Time.timeScale` at the top of `RunTicker.Tick`, but `Time.deltaTime` for *that* frame was
        // already scaled to zero before the frame began — so the frame that lifts the gate ticks core
        // with `Dt` 0 and the simulated clock does not move until the one after. Harmless at one
        // frame, and worth knowing before anything is built on "the clock restarts the instant the
        // screen closes"; the alternative is unscaled time, which is a decision about the whole
        // game's timing model rather than this gate's.
        yield return Frame();

        Assert.That(_pause.IsPaused, Is.False);
        Assert.That(_core.SimulatedTime, Is.EqualTo(before), "the resuming frame itself still carries Dt 0.");

        yield return Frame();

        Assert.That(_core.SimulatedTime, Is.GreaterThan(before), "and the clock moves again on the next one.");

        LogAssert.NoUnexpectedReceived();
    }

    // ---- The command phase's second poller (M3-10a rule 3) ---------------------------------------

    /// <summary>
    /// A slot press becomes a command inside <c>CommandPhase</c>, above the snapshot build — the
    /// same place <c>TapToFocusAdapter</c>'s tap becomes one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This is the row that retires the fixture's standing exemption.</b> The class remarks above
    /// say the commands phase is "the one step not observed here", because it reached core only when
    /// the Input System reported a press and this assembly does not reference the Input System.
    /// <see cref="SkillSlotInput"/> needs no Input System at all — a button writes an
    /// <see cref="int"/> into it — so the phase is finally observable, and AR §18.1's first row is
    /// asserted end to end.
    /// </para>
    /// <para>
    /// <b>It asserts that the snapshot had not been built when the command landed, and nothing
    /// about physics.</b> M3-08a's lesson: the row that took this fixture's flake rate from 10 % to
    /// 30 % did it by hanging a second assertion on the intermittent cone sweep. The player's
    /// position is written into the snapshot by <c>SnapshotBuilder.Build</c> and by nothing else,
    /// and this fixture's body never moves — core decides a zero velocity — so it is a deterministic
    /// read rather than a question for the physics scene.
    /// </para>
    /// </remarks>
    [UnityTest]
    public IEnumerator Frame_SlotPollSitsBesideTapToFocus()
    {
        CoreVector3 snapshotAtCommandTime = default;
        bool sawCommand = false;

        _core.OnCastSkill = () =>
        {
            sawCommand = true;
            snapshotAtCommandTime = _snapshot.PlayerPosition;
        };

        // A thumb, whenever in the frame uGUI happened to report it. Nothing has polled it yet.
        _skillSlots.Press(0);

        Assert.That(_core.Touched, Is.Empty, "The press reached core without a frame asking for it.");

        yield return Frame();

        Assert.That(sawCommand, Is.True, "The slot poll never ran, so a tap on S1 casts nothing.");

        // 1. It ran *above the snapshot build*, which is the claim: the player stands a kilometre
        //    from the origin and the snapshot still said zero when the command arrived, so nothing
        //    had written this frame's senses into it yet.
        Assert.That(
            snapshotAtCommandTime.X,
            Is.EqualTo(0f).Within(1e-3f),
            "The command landed after the snapshot was built, so a cast would be acted on against "
                + "senses taken before it — which is the adjacency CommandPhase exists to fix.");

        // 2. And the build really did happen on this frame, so the assertion above is about
        //    ordering rather than about a frame in which nothing was sensed at all.
        Assert.That(
            _snapshot.PlayerPosition.X,
            Is.EqualTo(Origin.x).Within(1e-2f),
            "Sanity: the frame built its snapshot.");

        // 3. Beside the focus tap and above the tick, in the phase AR §18.1 names.
        Assert.That(
            IndexIn(_core.Touched, "command:cast-slot"),
            Is.EqualTo(0),
            "Something reached core before the command phase did.");

        Assert.That(
            IndexIn(_core.Touched, "command:cast-slot"),
            Is.LessThan(IndexIn(_core.Touched, "tick")),
            "The command phase ran below the tick it is meant to precede.");

        // 4. One press, one command — the buffer holds an int and clears on Poll (rule 4). A second
        //    frame with nothing pressed sends nothing.
        yield return Frame();

        Assert.That(
            CountIn(_core.Touched, "command:cast-slot"),
            Is.EqualTo(1),
            "The press outlived the frame it was made in.");

        LogAssert.NoUnexpectedReceived();
    }

    private IEnumerator Frame()
    {
        yield return null;

        _ticker.Tick();
    }

    /// <summary>
    /// Stands one Wight up, announced the way core announces it, and hands back its body.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Through the hub rather than by reaching into the census, for the reason the opening
    /// <c>EnemySpawned</c> is published rather than bound by hand: what these rows are about is the
    /// wiring a run actually has.
    /// </para>
    /// <para>
    /// <b>The flush at the end is not tidiness, and what it stands in for is a finding.</b>
    /// <c>Bind</c> teleports the transform and <c>Physics.autoSyncTransforms</c> is 0, so the
    /// <see cref="CharacterController"/> PhysX holds is still parked wherever the pool left the
    /// body — and the first <c>Move</c> can resolve from <em>there</em>, snapping the body back to
    /// the pool's spot. Without this line the row failed roughly one run in three with the Wight a
    /// kilometre from where it was raised, which is a spawn artefact rather than the flush
    /// ordering this row is about. <c>EnemyView</c> has had the identical shape since M1-19 and
    /// <c>RunTicker</c> applies the first move <em>above</em> its own flush, so the same window is
    /// open in the game; it is out of this task's Files table and is reported rather than fixed
    /// here.
    /// </para>
    /// </remarks>
    private MinionView RaiseTheWight()
    {
        Vector3 where = Origin + MinionOffset;

        _hub.Publish(new MinionSpawned(
            MinionId,
            WightId,
            new CoreVector3(where.x, where.y, where.z),
            lifespan: 20f));

        Assert.That(_minionViews.TryGet(MinionId, out MinionView view), Is.True, "The Wight was never bound.");

        Physics.SyncTransforms();

        return view;
    }

    /// <summary>
    /// [Ledger row 4] Re-issues the cone that came back empty, in the same frame, and names which
    /// of two things happened.
    /// </summary>
    /// <remarks>
    /// <b>The whole of the instrument, and deliberately not a fix.</b> M4-07's experiment retired
    /// the last standing hypothesis about <c>Ticker_RunsTheStepsInOrder</c>'s intermittent failure
    /// and produced no new one, so what this row owed was a way to tell the two remaining
    /// explanations apart rather than a fourteenth tally. The query is the same object, the intent
    /// is the one <c>RecordingCore</c> actually wrote, and the physics scene is whatever the frame
    /// left behind — so a second answer that differs from the first can only be the sync, and one
    /// that agrees can only be the geometry.
    /// </remarks>
    private string DiagnoseTheEmptyReport()
    {
        if (!_core.WroteCone)
        {
            return "The cone report was empty because core never wrote a cone this frame, which is "
                + "a different failure: the tick did not reach the line that emits one.";
        }

        int count = _cone.Query(_core.LastCone, _diagnosisIds);

        if (count > 0)
        {
            return LateSync;
        }

        // The wedge and the body, measured against each other. "Wrong place" is two words and a
        // shrug without them: what a reader needs is *how* wrong, and in which direction — a body
        // outside the range is a different fault from one a millimetre behind the apex, and only
        // one of those is about the game.
        Vector3 apex = _core.LastCone.Origin.ToUnity();
        Vector2 facing = _core.LastCone.FacingXZ.ToUnity();
        Vector3 body = _body.Position;

        float dx = body.x - apex.x;
        float dz = body.z - apex.z;

        float along = (dx * facing.x) + (dz * facing.y);
        float across = (dx * -facing.y) + (dz * facing.x);
        float distance = Mathf.Sqrt((dx * dx) + (dz * dz));

        return WrongWedge
            + $" The body stood {distance:F4} m from the apex — {along:F4} m along the facing and "
            + $"{across:F4} m across it — against a range of {_core.LastCone.Range:F2} m and a "
            + $"{_core.LastCone.AngleDeg:F0}° wedge. A negative figure along the facing is a body "
            + "*behind* the apex, which no wedge of any width contains.";
    }

    /// <summary>
    /// Does the physics scene hold <paramref name="view"/>'s trigger capsule at
    /// <paramref name="point"/> right now?
    /// </summary>
    /// <remarks>
    /// The capsule by reference rather than "any collider on that object", which is the whole
    /// point: a <see cref="CharacterController"/> moves its own shape as it sweeps, so it is the
    /// one collider on the body that the flush does not govern.
    /// </remarks>
    private bool TheSweepFinds(MinionView view, Vector3 point)
    {
        int found = Physics.OverlapSphereNonAlloc(
            point,
            ProbeRadius,
            _probeHits,
            ~0,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < found; i++)
        {
            if (ReferenceEquals(_probeHits[i], view.Body))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many times <paramref name="step"/> appears in what the frames reached.</summary>
    private static int CountIn(IReadOnlyList<string> touched, string step)
    {
        int count = 0;

        for (int i = 0; i < touched.Count; i++)
        {
            if (touched[i] == step)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Where <paramref name="step"/> first appears in what the frame reached, or −1.</summary>
    private static int IndexIn(IReadOnlyList<string> touched, string step)
    {
        for (int i = 0; i < touched.Count; i++)
        {
            if (touched[i] == step)
            {
                return i;
            }
        }

        return -1;
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

    /// <summary>
    /// The smallest body <see cref="MinionView"/> will accept, with its trigger capsule switched
    /// on as <c>Wight.prefab</c> authors it.
    /// </summary>
    /// <remarks>
    /// On the Default layer and not the Enemy one, exactly as the shipped prefab is: no mask in the
    /// game selects it, so the swing's sphere never even returns it — and rule 1's other guard,
    /// that a Wight is absent from <c>EnemyViews</c>' collider index, holds whatever layer anybody
    /// later puts it on. Inactive first, for <see cref="EnemyTemplate"/>'s reason.
    /// </remarks>
    private MinionView MinionTemplate()
    {
        var root = new GameObject("MinionTemplate");

        root.SetActive(false);

        Track(root);

        MinionView view = root.AddComponent<MinionView>();

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

    private ZoneView ZoneTemplate()
    {
        var root = new GameObject("ZoneTemplate");

        root.SetActive(false);

        Track(root);

        return root.AddComponent<ZoneView>();
    }

    /// <summary>
    /// An inactive body carrying <typeparamref name="T"/>, for a pool that is never rented from.
    /// </summary>
    /// <remarks>
    /// The three templates above are each a method because each dresses something; these three
    /// dress nothing, so one generic is the whole of what M4-03's censuses need from this fixture.
    /// </remarks>
    private T Template<T>(string name)
        where T : Component
    {
        var root = new GameObject(name);

        root.SetActive(false);

        Track(root);

        return root.AddComponent<T>();
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
    /// <summary>
    /// An <see cref="ISaveStore"/> that accepts everything and keeps nothing.
    /// </summary>
    /// <remarks>
    /// Nested rather than borrowed from <c>Soulvail.Tests.Core</c>'s fakes shelf, which this
    /// assembly deliberately does not reference: <c>Soulvail.Tests.PlayMode</c> sees core and the
    /// game and nothing else, and opening it to another test assembly to save eight lines would be
    /// the wrong trade. Nothing in this fixture publishes a snapshot, so every method here is a
    /// stand-in for a dependency rather than a behaviour under test.
    /// </remarks>
    private sealed class InertSaveStore : ISaveStore
    {
        public Task<PlayerProfile?> LoadProfile() => Task.FromResult<PlayerProfile?>(null);

        public Task SaveProfile(PlayerProfile profile) => Task.CompletedTask;

        public Task<RunSnapshot?> LoadRun() => Task.FromResult<RunSnapshot?>(null);

        public Task SaveRun(RunSnapshot run) => Task.CompletedTask;

        public Task ClearRun() => Task.CompletedTask;
    }

    private sealed class RecordingCore : IRunSession, IPlayerCommands, IProgressionCommands
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

        /// <summary>
        /// Simulated seconds, summed from the ticks this fake was actually given — the stand-in for
        /// <c>RunState.Time</c>, which this assembly cannot build one of.
        /// </summary>
        public float SimulatedTime { get; private set; }

        /// <summary>
        /// When set, this tick earns the level: the flag goes up from <em>inside</em> <c>Tick</c>,
        /// which is where a stage's last kill raises it. Set from the row, cleared as it fires.
        /// </summary>
        public bool LevelUpDuringTick { get; set; }

        /// <summary>The velocity core decides for the body. Set by the row.</summary>
        public Vector3 Walk { get; set; }

        /// <summary>
        /// The velocity core decides for the Wight. Written every tick through
        /// <c>IIntentSink.MinionMove</c> whether or not a row set it, which is
        /// <c>MinionSystem.Walk</c>'s own rule — one intent per standing Wight per tick, zero
        /// velocity included, because a tick that emitted nothing would leave the body applying
        /// whatever it last read.
        /// </summary>
        public Vector3 MinionWalk { get; set; }

        public bool EmitCone { get; set; }

        /// <summary>
        /// How far the wedge is displaced from where the body will be. Zero for every row but
        /// <see cref="Ticker_AnEmptyConeReportIsDiagnosed"/>, which is the control that proves the
        /// instrument reads both ways [ledger row 4].
        /// </summary>
        public CoreVector3 ConeOffset { get; set; }

        /// <summary>
        /// The last wedge this fake wrote, kept so the fixture can re-issue it against the same
        /// physics scene in the same frame [ledger row 4]. One field, and the whole cost of the
        /// instrument on this side.
        /// </summary>
        public ConeHitIntent LastCone { get; private set; }

        /// <summary>Whether a cone was written at all, so an empty report can rule that out first.</summary>
        public bool WroteCone { get; private set; }

        /// <summary>
        /// Run from inside <see cref="ReportConeHits"/>, which is the one moment a row can ask
        /// physics a question at the same point in the frame core is being answered.
        /// <see cref="OnCastSkill"/>'s shape, at the other end of the tick.
        /// </summary>
        public Action OnFactTime { get; set; }

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

            // The level is earned here, mid-tick, exactly where core publishes LeveledUp — and
            // nothing may cut this tick short for it. The frame goes on to apply its intents, move
            // its bodies and answer its cone; the flag is not read until the top of the next one.
            if (LevelUpDuringTick)
            {
                LevelUpDuringTick = false;
                IsLevelUpPending = true;
            }

            // Read before anything is written, which is the only place it can be read: the flag is
            // about the frame's *previous* occupant of this buffer.
            IntentFlagAtTick = _intents.HasPlayerMove;

            Dt = snapshot.Dt;

            // What RunState.Time is: the sum of each tick's Dt, and nothing else (AR §18.2). This
            // fixture cannot build a RunState — the constructor is internal with no
            // InternalsVisibleTo — so the sum is kept here instead, which is the same number arrived
            // at the same way.
            SimulatedTime += snapshot.Dt;

            SnapshotEnemyPosition = Find(snapshot, EnemyId);

            _sink.PlayerMove(new PlayerMoveIntent(CoreVector3.Zero, new CoreVector3(0f, 0f, 1f)));

            _sink.EnemyMove(new EnemyMoveIntent(
                EnemyId,
                new CoreVector3(Walk.x, Walk.y, Walk.z),
                new CoreVector2(1f, 0f)));

            // Through the minion door and never the enemy one (M5-05a rule 7). Unconditional, like
            // the walk above: one intent per tick is MinionSystem.Walk's rule, and in every row but
            // the two that raise one it reaches a census holding no bodies and does nothing.
            _sink.MinionMove(new EnemyMoveIntent(
                MinionId,
                new CoreVector3(MinionWalk.x, MinionWalk.y, MinionWalk.z),
                new CoreVector2(0f, 1f)));

            if (!EmitCone)
            {
                return;
            }

            // Placed where this frame's own move puts the body, with a wedge tight enough that the
            // position the snapshot reported is well outside it. Core knows both halves — it chose
            // the velocity and it was handed the step — so the expectation needs no arithmetic from
            // the test.
            var expected = new CoreVector3(
                SnapshotEnemyPosition.X + (Walk.x * Dt) + ConeOffset.X,
                SnapshotEnemyPosition.Y + ConeOffset.Y,
                SnapshotEnemyPosition.Z + (Walk.z * Dt) + ConeOffset.Z);

            var cone = new ConeHitIntent(
                _touched.Count,
                expected,
                new CoreVector2(1f, 0f),
                range: 0.5f,
                angleDeg: 60f);

            // Kept before it is sent, so the fixture is holding exactly the wedge that was asked
            // about rather than one it reconstructed [ledger row 4].
            LastCone = cone;
            WroteCone = true;

            _sink.ConeHit(cone);
        }

        public void ReportConeHits(ReadOnlySpan<int> enemyIds)
        {
            _touched.Add("facts");

            LiveEnemyPositionAtFactTime = LiveEnemyPosition();

            OnFactTime?.Invoke();

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

        /// <summary>
        /// What <see cref="CastSkill"/> does when the frame calls it — how
        /// <see cref="Frame_SlotPollSitsBesideTapToFocus"/> looks at the snapshot from inside the
        /// command phase. <see cref="OnOpenLevelUp"/>'s shape.
        /// </summary>
        public Action OnCastSkill { get; set; }

        // M3-07a grew IPlayerCommands. Recorded like the three above rather than left empty, so
        // this fake keeps saying what it was asked for — and since M3-10a one row does send it,
        // through SkillSlotInput and RunTicker's command phase.
        public void CastSkill(int slot)
        {
            _touched.Add("command:cast-slot");

            OnCastSkill?.Invoke();
        }

        public void SetAutoCast(ContentId skillId, bool auto) => _touched.Add("command:auto-cast");

        // M3-08a made this fake an IProgressionCommands too, which is what lets the four Frame_*
        // rows drive the level-up phase at all: RunState's constructor is internal with no
        // InternalsVisibleTo (AR §18.2), so this fixture cannot build one and cannot answer
        // State.IsLevelUpPending. The port is the route, and the two reads are settable so a row can
        // say "a pick is owed" without a tree, a catalog or a run.
        public bool IsLevelUpPending { get; set; }

        public bool HasOffer { get; set; }

        /// <summary>
        /// What <see cref="OpenLevelUp"/> does when the frame calls it. A row that wants the offer
        /// to appear sets <see cref="HasOffer"/> from here, the way core would.
        /// </summary>
        public Action OnOpenLevelUp { get; set; }

        public void OpenLevelUp()
        {
            _touched.Add("level-up:open");

            OnOpenLevelUp?.Invoke();
        }

        public void ChooseOffer(int index) => _touched.Add("level-up:choose");

        // M5-07a-ii grew the port again, with CH §5.4's moment: a second pair of reads and a second
        // pair of commands, settable for the reason the level-up's pair is. The two Frame_Splash*
        // rows drive them, and every other row in this file leaves both false, which is what keeps
        // the phase's new branch inert everywhere it is not the subject.
        public bool IsSplashPending { get; set; }

        public bool IsSplashOpen { get; set; }

        /// <summary>
        /// What <see cref="OpenSplash"/> does when the frame calls it. <see cref="OnOpenLevelUp"/>'s
        /// shape: a row that wants the screen to appear sets <see cref="IsSplashOpen"/> from here,
        /// the way core would.
        /// </summary>
        public Action OnOpenSplash { get; set; }

        public void OpenSplash()
        {
            _touched.Add("splash:open");

            OnOpenSplash?.Invoke();
        }

        public void ChooseSplash(ContentId characterId, int branch) =>
            _touched.Add("splash:choose");

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
