using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Pooling;
using Soulvail.Game.Views;
using Soulvail.Tests.Core.Fakes;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VContainer;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

[TestFixture]
public sealed class ColossusViewTests
{
    private const string Project = "Assets/_Project/";
    private const string ArmPath = "Rig/SweepPivot/Rig_Medium/root/hips/spine/chest/upperarm.r/lowerarm.r";
    private GameObject _root;
    private DomainEventHub _events;

    [SetUp]
    public void SetUp()
    {
        _root = new GameObject("Colossus test");
        _events = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_root);
        _events.Dispose();
    }

    [Test]
    public void Prefab_BindsGroundedStoneRigAndCues()
    {
        GameObject prefab = Prefab();
        Assert.That(prefab.GetComponent<EnemyView>(), Is.Not.Null);
        Assert.That(prefab.GetComponentsInChildren<Collider>(), Has.Length.EqualTo(2));
        Assert.That(prefab.GetComponent<CapsuleCollider>().isTrigger, Is.True);
        Assert.That(prefab.GetComponent<CharacterController>().height, Is.InRange(3f, 4f));
        Transform rig = prefab.transform.Find("Rig");
        Assert.That(rig.GetComponentInChildren<SkinnedMeshRenderer>(), Is.Not.Null);
        Assert.That(prefab.transform.Find(ArmPath + "/HandTip"), Is.Not.Null);
        var animator = rig.GetComponent<Animator>();
        Assert.That(animator.applyRootMotion, Is.False);
        var controller = (AnimatorController)animator.runtimeAnimatorController;
        string[] states = { "Idle", "Walk", "SweepTell", "SweepStrike", "ReturnTell", "ReturnStrike",
            "StompTell", "StompStrike", "Recover" };
        Assert.That(controller.layers[0].stateMachine.states.Select(entry => entry.state.name),
            Is.EquivalentTo(states));
        Assert.That(controller.layers[0].stateMachine.defaultState.name, Is.EqualTo("Idle"));
        foreach (AnimationClip clip in controller.animationClips)
        {
            Assert.That(clip.events, Is.Empty, clip.name);
            Assert.That(AssetDatabase.GetAssetPath(clip), Is.EqualTo(Project + "Art/Enemies/SundialColossus.fbx"));
        }
        ColossusView view = Spawn();
        view.Step(0f);
        AudioSource audio = view.GetComponent<AudioSource>();
        Assert.That(audio.playOnAwake, Is.False);
        Assert.That(audio.spatialBlend, Is.EqualTo(1f));
        _events.Publish(Arm(17, ColossusMove.Sweep, Vector3.zero));
        Assert.That(audio.clip, Is.SameAs(Cue("DuneclawSweep")));
        _events.Publish(Arm(17, ColossusMove.Stomp, Vector3.zero));
        Assert.That(audio.clip, Is.SameAs(Cue("DuneclawSlam")));
    }

    [TestCase(ColossusMove.Sweep, "SweepTell", "SweepStrike", 1.8f, false)]
    [TestCase(ColossusMove.ReverseSweep, "ReturnTell", "ReturnStrike", 2.4f, false)]
    [TestCase(ColossusMove.Stomp, "StompTell", "StompStrike", 1.25f, true)]
    [TestCase(ColossusMove.Stomp, "StompTell", "StompStrike", 1.25f, false)]
    public void Warnings_UseLockedFootprintAndOrder(ColossusMove move, string tell, string strike, float recovery,
        bool stompHits)
    {
        var dunes = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Project +
            "Prefabs/Arenas/Sunscar/Arena_Dunes.prefab"), _root.transform);
        BoxCollider floor = dunes.GetComponentsInChildren<BoxCollider>().Single(collider => collider.name == "Floor");
        Vector3 center = new(1f, floor.bounds.max.y, -2f);
        ColossusView view = Spawn(center, id: 1);
        var animator = view.GetComponentInChildren<Animator>();
        Transform foot = view.transform.Find("Rig/SweepPivot/Rig_Medium/root/hips/upperleg.r/lowerleg.r/foot.r/FootContact");
        float plantedY = foot.position.y;
        ColossusArmed armed = default;
        using var armSubscription = _events.Subscribe<ColossusArmed>(evt => armed = evt);
        System.Action<float> tick = StartCoreAttack(move, center, out EnemyBlackboard board, out PlayerCombat player);
        Assert.That(armed.Move, Is.EqualTo(move));
        MeshRenderer warning = Field<MeshRenderer>(view, "_warning");
        var mesh = warning.GetComponent<MeshFilter>().sharedMesh;
        Vector3[] vertices = mesh.vertices;
        bool stomp = move == ColossusMove.Stomp;
        Assert.That(warning.enabled, Is.True);
        Assert.That(warning.GetComponentsInChildren<Collider>(), Is.Empty);
        Assert.That(warning.transform.position, Is.EqualTo(center + Vector3.up * 0.08f));
        AssertState(animator, tell);
        Assert.That(mesh.colors[0].a, Is.EqualTo(0.25f).Within(0.001f));
        foreach (Vector3 vertex in vertices)
        {
            Vector3 world = warning.transform.TransformPoint(vertex);
            Assert.That(world.y - floor.bounds.max.y, Is.InRange(0.079f, 0.087f));
            Assert.That(world.x, Is.InRange(floor.bounds.min.x, floor.bounds.max.x));
            Assert.That(world.z, Is.InRange(floor.bounds.min.z, floor.bounds.max.z));
        }
        Vector3[] rim = vertices.Where(vertex => vertex.y == 0f
            && Mathf.Abs(vertex.magnitude - armed.Radius) < 0.001f).ToArray();
        if (stomp)
        {
            Assert.That(rim.Min(vertex => vertex.x), Is.EqualTo(-armed.Radius).Within(0.001f));
            Assert.That(rim.Max(vertex => vertex.z), Is.EqualTo(armed.Radius).Within(0.001f));
            Assert.That(mesh.colors.Max(color => color.a), Is.GreaterThan(mesh.colors[0].a + 0.2f));
        }
        else
        {
            Assert.That(rim.Min(Bearing), Is.EqualTo(-30f).Within(0.001f));
            Assert.That(rim.Max(Bearing), Is.EqualTo(30f).Within(0.001f));
        }

        tick(armed.Seconds * 0.8f);
        view.Step(armed.Seconds * 0.8f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(0.8f).Within(0.002f));
        Assert.That(mesh.colors[0].a, Is.EqualTo(0.49f).Within(0.001f));
        int[] slots = move switch
        {
            ColossusMove.Sweep => new[] { 0, 1, 2 },
            ColossusMove.ReverseSweep => new[] { 2, 1, 0 },
            _ => new[] { -1 }
        };
        if (stomp)
        {
            Assert.That(foot.position.y, Is.GreaterThan(plantedY + 0.3f));
        }
        else
        {
            Color[] tellColors = mesh.colors;
            int mark = System.Array.FindIndex(tellColors, color => color.a > 0.86f);
            Assert.That(mark, Is.GreaterThanOrEqualTo(0));
            Assert.That(Bearing(vertices[mark + 1]), Is.EqualTo(slots[0] == 0 ? -30f : 30f).Within(0.01f));
            AssertChevronDirection(mesh, move == ColossusMove.Sweep ? 1 : -1);
        }

        Matrix4x4 locked = warning.localToWorldMatrix;
        view.transform.SetPositionAndRotation(center + new Vector3(2f, 0f, 3f), Quaternion.Euler(0f, 175f, 0f));
        view.transform.localScale = Vector3.one * 1.2f;
        view.Step(0.2f);
        Assert.That(warning.localToWorldMatrix, Is.EqualTo(locked));
        Assert.That(mesh.vertices, Is.EqualTo(vertices));
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.EqualTo(0.8f).Within(0.002f));
        for (int i = 0; i < slots.Length; i++)
        {
            int sector = slots[i];
            float before = player.Health.Current;
            bool hit = stomp ? stompHits : sector == 1;
            NVector3 forward = new(armed.Facing.X, 0f, armed.Facing.Y);
            board.PlayerPosition = armed.Origin + (hit ? (stomp ? 2f : 6f) : 12f) * forward;
            tick(i == 0 ? armed.Seconds * 0.21f : 0.36f);
            Assert.That(player.Health.Current, Is.EqualTo(before - (hit ? (stomp ? 16f : 20f) : 0f)));
            AssertState(animator, strike);
            Assert.That(warning.localToWorldMatrix, Is.EqualTo(locked));
            Assert.That(mesh.colors[FillIndex(vertices, stomp ? 0f : (sector - 1) * 20f)].a,
                Is.GreaterThan(0.95f));
            Assert.That(view.GetComponent<AudioSource>().clip, Is.Null);
            if (stomp)
            {
                Assert.That(foot.position.y, Is.EqualTo(plantedY).Within(0.01f));
            }
            else
            {
                Vector3 tip = view.transform.Find(ArmPath + "/HandTip").position - view.transform.position;
                Assert.That(tip.y, Is.InRange(-0.04f, 0.38f));
                Assert.That(Bearing(warning.transform.InverseTransformDirection(tip)),
                    Is.EqualTo((sector - 1) * 20f).Within(0.05f));
            }
            view.Step(0.15f);
            Assert.That(mesh.colors[FillIndex(vertices, stomp ? 0f : (sector - 1) * 20f)].a,
                Is.LessThan(0.1f));
            if (i < slots.Length - 1)
            {
                Assert.That(warning.enabled, Is.True);
                Assert.That(mesh.colors[FillIndex(vertices, (slots[i + 1] - 1) * 20f)].a, Is.GreaterThan(0.4f));
            }
        }
        Assert.That(warning.enabled, Is.False);
        Quaternion pinned = view.transform.Find(ArmPath).rotation;
        view.Step(recovery - 0.16f);
        AssertState(animator, strike);
        Assert.That(view.transform.Find(ArmPath).rotation, Is.EqualTo(pinned));
        view.Step(0.02f);
        view.Step(0.25f);
        AssertState(animator, "Idle");
    }

    private System.Action<float> StartCoreAttack(ColossusMove move, Vector3 center, out EnemyBlackboard board,
        out PlayerCombat player)
    {
        var character = new CharacterSpec(new ContentId("character.test"), new LocKey("character.test.name"),
            new LocKey("character.test.description"), 100f,
            new MovementSpec(3f, 0.06f, 0.08f, 720f), new TargetingSpec(12f, 3f, 2f, 1f, 1.5f, 0.1f),
            new WeaponSpec(WeaponKind.Cone, 13f, 3f, 8f, 60f, 0.4f), new FocusSpec(0.4f, 1f, 1f),
            new MovementSkillSpec(MovementSkillKind.Charge, 10f, 0.22f, 2.5f, 0.15f, 20f, 5f, 0.05f));
        var spec = new EnemySpec(new ContentId("enemy.colossus"), new LocKey("enemy.colossus.name"),
            500f, 2.2f, 8, 40, 300f, false, 20f, 9.5f, 1.35f, 1.8f, 40f, EnemyBehaviourKind.Static);
        var catalog = new ContentCatalog(new[] { character }, new[] { spec });
        var intents = new RecordingIntents();
        var combat = new PlayerCombat(character, _events, intents, 8);
        var sunscar = AssetDatabase.LoadAssetAtPath<ModeDefinition>(Project + "Data/Modes/Sunscar.asset").ToSpec();
        var enemies = new EnemySystem(catalog, _events, new FixedRandom(0.5f), new DepthScaling(sunscar.Scaling), 8);
        var projectiles = new ProjectileSystem(_events, 8);
        NVector3 origin = new(center.x, center.y, center.z);
        EnemyAgent agent = enemies.Registry.Spawn(spec, origin);
        var boss = new ColossusBehaviour(agent, new ColossusStats());
        board = agent.Blackboard;
        player = combat;
        float now = 0f;
        NVector3 forward = NVector3.Normalize(new NVector3(1f, 0f, 2f));
        board.SelfPosition = origin;
        board.PlayerPosition = origin + forward * 8f;
        if (move == ColossusMove.ReverseSweep)
        {
            boss.OnPhaseEntered(2);
        }
        Tick(0f);
        if (move != ColossusMove.Sweep)
        {
            board.PlayerPosition = origin - forward * 12f;
            Tick(1.4f);
            Tick(0.36f);
            Tick(0.36f);
            board.PlayerPosition = origin + forward * 2f;
            Tick(2f);
            Assert.That(combat.Health.Current, Is.EqualTo(100f));
        }
        return Tick;

        void Tick(float dt)
        {
            now += dt;
            boss.Tick(new EnemyTickContext(dt, now, combat, intents, _events, projectiles, enemies));
        }
    }

    [Test]
    public void Ray_ReplacesTheLockedLaneWithABeamAtImpact()
    {
        ColossusView view = Spawn();
        _events.Publish(new ColossusArmed(17, ColossusMove.SundialRay, NVector3.Zero, NVector2.UnitX,
            14f, 0f, 1.2f, 0.4f, 0.65f));
        MeshRenderer warning = Field<MeshRenderer>(view, "_warning");
        MeshRenderer effect = Field<MeshRenderer>(view, "_effect");
        Mesh dialOrBeam = effect.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(warning.enabled && effect.enabled, Is.True);
        Assert.That(dialOrBeam.bounds.size.z, Is.Zero);
        Matrix4x4 locked = warning.localToWorldMatrix;
        Assert.That(Vector3.Angle(effect.transform.forward, warning.transform.forward), Is.EqualTo(90f).Within(0.01f));
        view.Step(0.1f);
        Assert.That(Vector3.Angle(effect.transform.forward, warning.transform.forward), Is.EqualTo(54f).Within(0.01f));
        Assert.That(warning.localToWorldMatrix, Is.EqualTo(locked));
        float initialDialWidth = dialOrBeam.bounds.size.x;
        _events.Publish(new ColossusTellProgress(17, 0.9f));
        Assert.That(dialOrBeam.bounds.size.x, Is.GreaterThan(initialDialWidth));
        Assert.That(dialOrBeam.bounds.size.z, Is.Zero);
        view.Step(0.2f);
        view.transform.SetPositionAndRotation(Vector3.right, Quaternion.Euler(0f, 90f, 0f));
        _events.Publish(new ColossusImpact(17, ColossusMove.SundialRay, -1, 0.8f));
        Assert.That(warning.enabled, Is.False);
        Assert.That(effect.enabled, Is.True);
        Assert.That(effect.localToWorldMatrix, Is.EqualTo(locked));
        Bounds lane = warning.GetComponent<MeshFilter>().sharedMesh.bounds;
        Assert.That(dialOrBeam.bounds.min.x, Is.EqualTo(lane.min.x).Within(0.001f));
        Assert.That(dialOrBeam.bounds.max.x, Is.EqualTo(lane.max.x).Within(0.001f));
        Assert.That(dialOrBeam.bounds.min.z, Is.EqualTo(lane.min.z).Within(0.001f));
        Assert.That(dialOrBeam.bounds.max.z, Is.EqualTo(lane.max.z).Within(0.001f));
        view.Step(0.25f);
        Assert.That(effect.enabled, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Recovery_TurnsAtABoundedRateEvenWhenTheNextTellStarts(bool nextTell)
    {
        ColossusView view = Spawn();
        var model = view.GetComponentInChildren<Animator>().transform;
        var ray = new ColossusArmed(17, ColossusMove.SundialRay, NVector3.Zero, NVector2.UnitY,
            14f, 0f, 1.2f, 0.4f, 0.65f);
        _events.Publish(ray);
        _events.Publish(new ColossusImpact(17, ray.Move, -1, 0.8f));

        Quaternion locked = model.rotation;
        Quaternion target = Quaternion.Euler(0f, 170f, 0f);
        view.Step(0.79f);
        Assert.That(Quaternion.Angle(model.rotation, locked), Is.LessThan(0.01f));
        view.transform.rotation = target;
        if (nextTell)
        {
            Vector3 forward = target * Vector3.forward;
            _events.Publish(new ColossusArmed(17, ColossusMove.Sweep, NVector3.Zero,
                new NVector2(forward.x, forward.z), 9.5f, 20f, 1.35f));
            Assert.That(Quaternion.Angle(model.rotation, locked), Is.LessThan(0.01f));
        }
        else
        {
            view.Step(0.02f);
            Assert.That(Quaternion.Angle(model.rotation, locked), Is.LessThan(0.01f));
        }

        view.Step(0.1f);
        Assert.That(Quaternion.Angle(model.rotation, locked), Is.EqualTo(36f).Within(0.01f));
        view.Step(0.2f);
        Assert.That(Quaternion.Angle(model.rotation, locked), Is.EqualTo(108f).Within(0.01f));
        view.Step(0.2f);
        Assert.That(Quaternion.Angle(model.rotation, target), Is.LessThan(0.01f));
    }

    [Test]
    public void Stomp_ImpactSendsAnOutwardWaveWithinItsFloorMark()
    {
        ColossusView view = Spawn();
        _events.Publish(Arm(17, ColossusMove.Stomp, Vector3.zero));
        Assert.That(Field<MeshRenderer>(view, "_effect"), Is.Null);
        _events.Publish(new ColossusImpact(17, ColossusMove.Stomp, -1, 1.25f));
        MeshRenderer effect = Field<MeshRenderer>(view, "_effect");
        Mesh mesh = effect.GetComponent<MeshFilter>().sharedMesh;
        Assert.That(effect.enabled, Is.True);
        float firstRadius = mesh.bounds.extents.x;
        view.Step(0.15f);
        Assert.That(mesh.bounds.extents.x, Is.InRange(firstRadius + 0.5f, 3.3f));
        _events.Publish(new BossPhaseChanged(17, 1, 3));
        Assert.That(effect.enabled, Is.False);
    }

    [Test]
    public void Repair_GathersSandstoneAndRestoresBothPlatesWhenTheBeatEnds()
    {
        ColossusView view = Spawn();
        Transform chest = view.transform.Find("Rig/SweepPivot/Rig_Medium/root/hips/spine/chest");
        Transform left = chest.Find("upperarm.l/Left shoulder plate");
        Transform right = chest.Find("upperarm.r/Right shoulder plate");
        Transform repair = chest.Find("upperarm.l/Shoulder repair");
        Transform rightRepair = chest.Find("upperarm.r/Shoulder repair");
        _events.Publish(new ColossusPlateHit(17, ColossusShoulder.Left, 0f));
        _events.Publish(new ColossusPlateHit(17, ColossusShoulder.Right, 0.25f));
        _events.Publish(new BossPhaseChanged(17, 1, 3));
        _events.Publish(new BossBeatStarted(17, 3f));
        float spread = repair.GetChild(0).localPosition.magnitude;
        Assert.That(repair.gameObject.activeSelf && rightRepair.gameObject.activeSelf, Is.True);
        view.Step(2.8f);
        Assert.That(repair.GetChild(0).localPosition.magnitude, Is.LessThan(spread));
        Assert.That(left.gameObject.activeSelf, Is.False);
        Assert.That(right.Find("Shoulder split").gameObject.activeSelf, Is.True);
        _events.Publish(new BossBeatEnded(17));
        Assert.That(left.gameObject.activeSelf && right.gameObject.activeSelf, Is.True);
        Assert.That(right.Find("Shoulder crack").gameObject.activeSelf, Is.False);
        Assert.That(repair.gameObject.activeSelf || rightRepair.gameObject.activeSelf, Is.False);
    }

    [Test]
    public void Lifecycle_ClearsOnlyForBoundBoss()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_events);
        using IObjectResolver resolver = builder.Build();
        using var pool = new ViewPool<EnemyView>(resolver, Prefab().GetComponent<EnemyView>(), _root.transform, 1);
        EnemyView body = pool.Get();
        body.Bind(17, Vector3.zero);
        var view = body.GetComponent<ColossusView>();
        var audio = view.GetComponent<AudioSource>();
        var animator = view.GetComponentInChildren<Animator>();
        var foreignDeath = new EnemyDied(18, new ContentId("enemy.colossus"), NVector3.Zero);
        _events.Publish(Arm(18, ColossusMove.Stomp, Vector3.zero));
        Assert.That(Field<MeshRenderer>(view, "_warning"), Is.Null);
        _events.Publish(Arm(17, ColossusMove.ReverseSweep, Vector3.zero));
        MeshRenderer warning = Field<MeshRenderer>(view, "_warning");
        _events.Publish(new ColossusTellProgress(18, 1f));
        _events.Publish(new ColossusImpact(18, ColossusMove.ReverseSweep, 2, 0f));
        _events.Publish(new BossPhaseChanged(18, 1, 3));
        _events.Publish(foreignDeath);
        Assert.That(warning.enabled, Is.True);
        Assert.That(audio.clip, Is.SameAs(Cue("DuneclawSweep")));
        AssertState(animator, "ReturnTell");
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.Zero.Within(0.001f));

        _events.Publish(new BossPhaseChanged(17, 1, 3));
        AssertNeutral(view, warning);
        _events.Publish(new ColossusImpact(17, ColossusMove.ReverseSweep, 2, 0f));
        AssertNeutral(view, warning);
        _events.Publish(Arm(17, ColossusMove.Stomp, Vector3.zero));
        _events.Publish(new EnemyDied(17, foreignDeath.SpecId, NVector3.Zero));
        _events.Publish(Arm(17, ColossusMove.Sweep, Vector3.zero));
        AssertNeutral(view, warning);

        Release(pool, body, view);
        EnemyView reused = pool.Get();
        Assert.That(reused, Is.SameAs(body));
        reused.Bind(23, Vector3.right);
        view.Step(0f);
        AssertNeutral(view, warning);
        _events.Publish(Arm(17, ColossusMove.Stomp, Vector3.zero));
        AssertNeutral(view, warning);
        _events.Publish(Arm(23, ColossusMove.Stomp, reused.Position));
        _events.Publish(new ColossusTellProgress(23, 0.9f));
        Assert.That(warning.enabled, Is.True);
        Assert.That(audio.clip, Is.SameAs(Cue("DuneclawSlam")));
        AssertState(animator, "StompTell");
        Release(pool, reused, view);
        Assert.That(warning.enabled, Is.False);
        Assert.That(audio.clip, Is.Null);
        Assert.That(animator.transform.localRotation, Is.EqualTo(Quaternion.identity));
        reused = pool.Get();
        reused.Bind(24, Vector3.zero);
        view.Step(0f);
        AssertNeutral(view, warning);
    }

    private static void Release(ViewPool<EnemyView> pool, EnemyView body, ColossusView view)
    {
        pool.Release(body);
        // Ordinary MonoBehaviour messages do not run in EditMode.
        typeof(ColossusView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
    }

    private static void AssertNeutral(ColossusView view, MeshRenderer warning)
    {
        Assert.That(warning.enabled, Is.False);
        Assert.That(view.GetComponent<AudioSource>().clip, Is.Null);
        Animator animator = view.GetComponentInChildren<Animator>();
        Assert.That(animator.transform.localRotation, Is.EqualTo(Quaternion.identity));
        Assert.That(animator.transform.Find("SweepPivot").localRotation, Is.EqualTo(Quaternion.identity));
        AssertState(animator, "Idle");
    }

    private static void AssertState(Animator animator, string state) =>
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, state);

    private static void AssertChevronDirection(Mesh mesh, int direction)
    {
        Vector3[] vertices = mesh.vertices;
        int tips = 0;
        for (int i = 0; i < vertices.Length; i += 3)
        {
            if (vertices[i].y < 0.005f)
            {
                continue;
            }
            Assert.That(direction * Mathf.DeltaAngle(Bearing(vertices[i]), Bearing(vertices[i + 1])),
                Is.GreaterThan(0f));
            tips++;
        }
        Assert.That(tips, Is.EqualTo(6));
    }

    private static int FillIndex(Vector3[] vertices, float bearing)
    {
        for (int i = 0; i < vertices.Length; i += 3)
        {
            if (vertices[i] == Vector3.zero && Mathf.Abs(Mathf.DeltaAngle(bearing,
                Bearing(vertices[i + 1] + vertices[i + 2]))) < 3f)
            {
                return i;
            }
        }
        throw new AssertionException("Missing footprint fill at bearing " + bearing);
    }

    private static float Bearing(Vector3 point) => Mathf.Atan2(point.x, point.z) * Mathf.Rad2Deg;

    private static ColossusArmed Arm(int id, ColossusMove move, Vector3 origin)
    {
        bool stomp = move == ColossusMove.Stomp;
        return new ColossusArmed(id, move, new NVector3(origin.x, origin.y, origin.z),
            NVector2.Normalize(new NVector2(1f, 2f)), stomp ? 3.3f : 9.5f, stomp ? 0f : 20f, stomp ? 1f : 1.35f);
    }

    private ColossusView Spawn(Vector3 at = default, int id = 17)
    {
        GameObject instance = Object.Instantiate(Prefab(), _root.transform);
        instance.GetComponent<EnemyView>().Bind(id, at);
        var view = instance.GetComponent<ColossusView>();
        view.Construct(_events);
        return view;
    }

    private static T Field<T>(ColossusView view, string name) where T : class =>
        (T)typeof(ColossusView).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(view);

    private static GameObject Prefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/SundialColossus.prefab");

    private static AudioClip Cue(string name) =>
        AssetDatabase.LoadAssetAtPath<AudioClip>(Project + "Audio/Enemies/" + name + ".wav");
}
