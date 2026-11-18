using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Ai;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
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
public sealed class DuneclawViewTests
{
    private const string Project = "Assets/_Project/";
    private GameObject _parent;
    private DomainEventHub _events;

    [SetUp]
    public void SetUp()
    {
        _parent = new GameObject("Duneclaw test pool");
        _parent.SetActive(false);
        _events = new DomainEventHub();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_parent);
        _events.Dispose();
    }

    [TestCase(DuneclawMove.Sweep)]
    [TestCase(DuneclawMove.Slam)]
    public void Arm_MatchingBody_ShowsExactSectorOrCircleAndPlaysDistinctCue(DuneclawMove move)
    {
        DuneclawView owner = Body(17);
        DuneclawView other = Body(18);
        owner.transform.localScale = Vector3.one * 1.7f;
        _parent.SetActive(true);
        _events.Publish(Arm(17, move));
        Assert.That(owner.IsShowing, Is.True);
        Assert.That(other.IsShowing, Is.False);
        Assert.That(Field<AudioSource>(other, "_audio").clip, Is.Null);
        Assert.That(owner.State, Is.EqualTo(move == DuneclawMove.Sweep ? "SweepWindup" : "SlamWindup"));
        Assert.That(Field<Renderer>(owner, "_claw").enabled, Is.EqualTo(move == DuneclawMove.Sweep));
        Assert.That(Field<Renderer>(owner, "_stinger").enabled, Is.EqualTo(move == DuneclawMove.Slam));
        var audio = Field<AudioSource>(owner, "_audio");
        Assert.That(audio.clip, Is.EqualTo(AssetDatabase.LoadAssetAtPath<AudioClip>(Project +
            "Audio/Enemies/" + (move == DuneclawMove.Sweep ? "DuneclawSweep.wav" : "DuneclawSlam.wav"))));
        Assert.That(audio.playOnAwake, Is.False);
        Assert.That(audio.spatialBlend, Is.EqualTo(1f));
        Assert.That(Field<AudioClip>(owner, "_sweepCue"), Is.Not.EqualTo(Field<AudioClip>(owner, "_slamCue")));

        MeshFilter tell = Field<MeshFilter>(owner, "_tell");
        Mesh mesh = tell.sharedMesh;
        Vector3 centre = move == DuneclawMove.Sweep ? new Vector3(2f, 0.08f, 3f) : new Vector3(6f, 0.08f, 3f);
        Assert.That(Vector3.Distance(tell.transform.position, centre), Is.LessThan(1e-5f));
        Vector3[] vertices = mesh.vertices;
        Assert.That(vertices[0], Is.EqualTo(Vector3.zero));
        foreach (Vector3 vertex in vertices.Skip(1))
        {
            Assert.That(vertex.magnitude, Is.EqualTo(4.5f).Within(1e-5f));
            Assert.That(Vector3.Distance(tell.transform.TransformPoint(vertex), centre), Is.EqualTo(4.5f).Within(1e-5f));
        }
        if (move == DuneclawMove.Sweep)
        {
            Assert.That(Vector3.Angle(vertices[1], Vector3.right), Is.EqualTo(65f).Within(1e-4f));
            Assert.That(Vector3.Angle(vertices[^1], Vector3.right), Is.EqualTo(65f).Within(1e-4f));
            Assert.That(Vector3.Distance(vertices[33].normalized, Vector3.right), Is.LessThan(1e-5f));
        }
        else
        {
            Assert.That(Vector3.Distance(vertices[1], vertices[^1]), Is.LessThan(1e-5f));
            Assert.That(vertices.Min(v => v.x), Is.EqualTo(-4.5f).Within(1e-5f));
            Assert.That(vertices.Max(v => v.x), Is.EqualTo(4.5f).Within(1e-5f));
        }
        owner.Step(0.4f);
        Assert.That(mesh.colors[0].a, Is.EqualTo(0.6f).Within(1e-5f));
        Assert.That(tell.sharedMesh, Is.SameAs(mesh));
        owner.Step(0.4f);
        Assert.That(owner.IsShowing, Is.False);
    }

    [TestCase(DuneclawMove.Sweep)]
    [TestCase(DuneclawMove.Slam)]
    public void CastAndRecovery_TurnSmoothly_WithoutMovingLockedTell(DuneclawMove move)
    {
        DuneclawView view = Body(17);
        _parent.SetActive(true);
        Transform model = Field<Transform>(view, "_model");
        _events.Publish(Arm(17, move));
        Assert.That(Vector3.Angle(model.forward, Vector3.forward), Is.LessThan(0.01f));
        view.Step(0.05f);
        Assert.That(Vector3.Angle(model.forward, Vector3.forward), Is.EqualTo(18f).Within(0.01f));
        view.Step(0.2f);
        Assert.That(Vector3.Angle(model.forward, Vector3.right), Is.LessThan(0.01f));

        Vector3 centre = Field<MeshFilter>(view, "_tell").transform.position;
        view.GetComponent<EnemyView>().Apply(new EnemyMoveIntent(17, NVector3.Zero, -NVector2.UnitY), 0.01f);
        view.Step(0.05f);
        Assert.That(Vector3.Angle(model.forward, Vector3.right), Is.LessThan(0.01f));
        Assert.That(Field<MeshFilter>(view, "_tell").transform.position, Is.EqualTo(centre));
        _events.Publish(new DuneclawImpact(17, move, new NVector3(6, 0, 3), 0.8f));
        view.Step(0.8f);
        Assert.That(Vector3.Angle(model.forward, Vector3.right), Is.LessThan(0.01f), "Recovery release preserves the visible heading.");
        view.Step(0.05f);
        Assert.That(Vector3.Angle(model.forward, Vector3.right), Is.EqualTo(18f).Within(0.01f));
        view.Step(0.2f);
        Assert.That(Vector3.Angle(model.forward, Vector3.back), Is.LessThan(0.01f));

        _events.Publish(Arm(17, move));
        Assert.That(Vector3.Angle(model.forward, Vector3.back), Is.LessThan(0.01f), "The next cast must also ease into its heading.");
        view.Step(0.05f);
        Assert.That(Vector3.Angle(model.forward, Vector3.back), Is.EqualTo(18f).Within(0.01f));
    }

    [Test]
    public void ImpactAndPhase_ClearTell_AndPinUsesRecovery()
    {
        DuneclawView view = Body(17);
        _parent.SetActive(true);
        _events.Publish(Arm(17, DuneclawMove.Sweep));
        _events.Publish(new DuneclawImpact(18, DuneclawMove.Sweep, NVector3.Zero, 0.35f));
        Assert.That(view.IsShowing, Is.True);
        _events.Publish(new DuneclawImpact(17, DuneclawMove.Sweep, NVector3.Zero, 0.35f));
        AssertCleared(view);
        Assert.That(view.State, Is.EqualTo("Sweep"));
        _events.Publish(Arm(17, DuneclawMove.Slam));
        Assert.That(view.State, Is.EqualTo("SlamWindup"));
        Assert.That(Field<Renderer>(view, "_claw").enabled, Is.False);
        var target = new NVector3(8, 0, 3);
        _events.Publish(new DuneclawImpact(17, DuneclawMove.Slam, target, 2f));
        AssertCleared(view);
        Assert.That(view.State, Is.EqualTo("Slam"));
        view.Step(0.31f);
        Assert.That(view.State, Is.EqualTo("Pinned"));
        Assert.That(view.RecoveryRemaining, Is.EqualTo(1.69f).Within(1e-5f));
        var animator = Field<Animator>(view, "_animator");
        animator.Update(0f);
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Pinned"), Is.True);
        Transform stinger = view.GetComponentsInChildren<Transform>().Single(t => t.name == "Stinger");
        Vector3 localTip = Field<Transform>(view, "_model").InverseTransformPoint(stinger.position);
        Assert.That(localTip.z, Is.GreaterThan(0.8f));
        Assert.That(Vector3.Dot(Field<Transform>(view, "_model").forward, Vector3.right), Is.GreaterThan(0.999f));
        _events.Publish(new EnemyDamaged(17, 1f, 0.8f, false));
        Assert.That(view.State, Is.EqualTo("Pinned"));
        view.Step(1.68f);
        Assert.That(view.State, Is.EqualTo("Pinned"));
        view.Step(0.02f);
        Assert.That(view.State, Is.EqualTo("Idle"));
        _events.Publish(Arm(17, DuneclawMove.Sweep));
        _events.Publish(new BossPhaseChanged(18, 1, 3));
        Assert.That(view.IsShowing, Is.True);
        _events.Publish(new BossPhaseChanged(17, 1, 3));
        AssertCleared(view);
        Assert.That(view.State, Is.EqualTo("Phase"));
        view.Step(1.51f);
        Assert.That(view.State, Is.EqualTo("Idle"));
        _events.Publish(new EnemyDamaged(17, 1f, 0.8f, false));
        Assert.That(view.State, Is.EqualTo("Hit"));
        view.Step(0.41f);
        view.GetComponent<EnemyView>().Apply(new EnemyMoveIntent(17, new NVector3(2, 0, 0), NVector2.UnitX), 0.1f);
        view.Step(0f);
        Assert.That(view.State, Is.EqualTo("Skitter"));
        _events.Publish(new EnemyDied(17, new ContentId("boss.duneclaw"), NVector3.Zero));
        Assert.That(view.State, Is.EqualTo("Death"));
        _events.Publish(Arm(17, DuneclawMove.Slam));
        view.Step(5f);
        AssertCleared(view);
        Assert.That(view.State, Is.EqualTo("Death"));
    }

    [Test]
    public void DespawnAndReuse_ClearsPresentation()
    {
        var builder = new ContainerBuilder();
        builder.RegisterInstance(_events);
        var random = new FixedRandom(7);
        var recorder = new RunRecorder(random, new FixedClock(System.DateTimeOffset.UnixEpoch), _events);
        builder.RegisterInstance<IRunSession>(new RunSession(new ContentCatalog(
                System.Array.Empty<CharacterSpec>(), System.Array.Empty<EnemySpec>(), System.Array.Empty<ModeSpec>()),
            random, _events, new RecordingIntents(), recorder, 8, 8, 8));
        using IObjectResolver resolver = builder.Build();
        using var pool = new ViewPool<EnemyView>(resolver,
            AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/Duneclaw.prefab").GetComponent<EnemyView>(),
            _parent.transform, 1);
        EnemyView body = pool.Get();
        body.Bind(17, Vector3.zero);
        _parent.SetActive(true);
        var view = body.GetComponent<DuneclawView>();
        _events.Publish(Arm(17, DuneclawMove.Slam));
        Assert.That(view.IsShowing, Is.True);
        Mesh mesh = Field<MeshFilter>(view, "_tell").sharedMesh;
        pool.Release(body);
        // EditMode does not dispatch ordinary OnDisable; exercise the rental's lifecycle message.
        typeof(DuneclawView).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        AssertCleared(view);
        Assert.That(Field<AudioSource>(view, "_audio").clip, Is.Null);
        Assert.That(Field<AudioSource>(view, "_audio").isPlaying, Is.False);
        Assert.That(view.State, Is.EqualTo("Idle"));
        Assert.That(view.RecoveryRemaining, Is.Zero);
        Assert.That(Field<Animator>(view, "_animator").speed, Is.EqualTo(1f));
        Assert.That(Field<Transform>(view, "_model").localRotation, Is.EqualTo(Quaternion.identity));
        EnemyView reused = pool.Get();
        Assert.That(reused, Is.SameAs(body));
        reused.Bind(23, Vector3.zero);
        _events.Publish(Arm(17, DuneclawMove.Sweep));
        AssertCleared(view);
        _events.Publish(Arm(23, DuneclawMove.Sweep));
        Assert.That(view.IsShowing, Is.True);
        Assert.That(Field<MeshFilter>(view, "_tell").sharedMesh, Is.SameAs(mesh));
        view.OnDespawn();
        view.OnSpawn();
        AssertCleared(view);
    }

    [Test]
    public void SlabView_StationaryAndNonblocking_ShatterAndDespawnCleanUp()
    {
        DuneclawView view = Body(17);
        DuneclawView other = Body(18);
        _parent.SetActive(true);
        var serialized = new SerializedObject(view);
        var slabs = new Transform[2];
        var tiles = new GameObject[2];
        var dust = new ParticleSystem[2];
        for (int i = 0; i < 2; i++)
        {
            slabs[i] = (Transform)serialized.FindProperty("_slabs").GetArrayElementAtIndex(i).objectReferenceValue;
            tiles[i] = (GameObject)serialized.FindProperty("_slabTiles").GetArrayElementAtIndex(i).objectReferenceValue;
            dust[i] = (ParticleSystem)serialized.FindProperty("_slabDust").GetArrayElementAtIndex(i).objectReferenceValue;
            Assert.That(slabs[i].gameObject.activeSelf, Is.False);
            Assert.That(slabs[i].GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(slabs[i].GetComponentsInChildren<Unity.AI.Navigation.NavMeshModifier>(true), Is.Empty);
            Assert.That(slabs[i].GetComponentsInChildren<Unity.AI.Navigation.NavMeshModifierVolume>(true), Is.Empty);
            Assert.That(dust[i].main.playOnAwake, Is.False);
            _events.Publish(new DuneclawSlabPlaced(17, i, new NVector3(i * 7 - 3.5f, 0, 2)));
            Assert.That(slabs[i].gameObject.activeSelf, Is.True);
            Assert.That(tiles[i].activeSelf, Is.True);
            Assert.That(slabs[i].parent, Is.EqualTo(_parent.transform));
        }
        Assert.That(other.transform.Find("Slab0").gameObject.activeSelf, Is.False);
        view.transform.SetPositionAndRotation(new Vector3(10, 0, 8), Quaternion.Euler(0, 90, 0));
        view.transform.localScale = Vector3.one * 1.7f;
        view.Step(0f);
        Assert.That(slabs[0].position, Is.EqualTo(new Vector3(-3.5f, 0, 2)));
        Assert.That(slabs[0].lossyScale, Is.EqualTo(Vector3.one));
        var stone = slabs[0].Find("Intact/Sandstone").GetComponent<Renderer>();
        Assert.That(stone.bounds.size.x, Is.EqualTo(1.6f).Within(1e-4f));
        _events.Publish(new BossPhaseChanged(17, 2, 3));
        Assert.That(tiles[0].activeSelf, Is.True);
        _events.Publish(new DuneclawSlabShattered(18, 0));
        Assert.That(tiles[0].activeSelf, Is.True);
        _events.Publish(new DuneclawSlabShattered(17, 0));
        Assert.That(tiles[0].activeSelf, Is.False);
        Assert.That(dust[0].isPlaying, Is.True);
        Assert.That(tiles[1].activeSelf, Is.True);
        dust[0].Simulate(0.1f, true, false);
        Assert.That(dust[0].particleCount, Is.GreaterThan(0));
        view.OnDespawn();
        for (int i = 0; i < 2; i++)
        {
            Assert.That(slabs[i].gameObject.activeSelf, Is.False);
            Assert.That(slabs[i].parent, Is.EqualTo(view.transform));
            Assert.That(dust[i].particleCount, Is.Zero);
            Assert.That(dust[i].isPlaying, Is.False);
        }
        view.OnSpawn();
        _events.Publish(new DuneclawSlabPlaced(17, 0, NVector3.Zero));
        Assert.That(tiles[0].activeSelf, Is.True);
        _events.Publish(new DuneclawSlabShattered(17, -1));
        _events.Publish(new DuneclawSlabPlaced(17, 2, NVector3.Zero));
        typeof(DuneclawView).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
        Assert.That(slabs[0].gameObject.activeSelf, Is.False);
        _events.Publish(new DuneclawSlabPlaced(17, 0, NVector3.Zero));
        Assert.That(slabs[0].gameObject.activeSelf, Is.False, "Disposal unsubscribes slab facts.");
    }
    private DuneclawView Body(int id)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Project + "Prefabs/Enemies/Duneclaw.prefab"), _parent.transform);
        root.GetComponent<EnemyView>().Bind(id, new Vector3(2, 0, 3));
        var view = root.GetComponent<DuneclawView>();
        view.Construct(_events);
        Assert.That(root.GetComponentInChildren<EnemyAnimatorView>(), Is.Null, "Only one driver may own these clips.");
        var controller = (AnimatorController)Field<Animator>(view, "_animator").runtimeAnimatorController;
        Assert.That(controller.layers[0].stateMachine.states.Select(s => s.state.name), Is.EquivalentTo(new[]
        {
            "Idle", "Skitter", "SweepWindup", "Sweep", "SlamWindup", "Slam", "Pinned", "Phase", "Hit", "Death",
        }));
        Assert.That(Field<Animator>(view, "_animator").applyRootMotion, Is.False);
        return view;
    }

    private static DuneclawArmed Arm(int id, DuneclawMove move) => new DuneclawArmed(id, move,
        new NVector3(2, 0, 3), NVector2.UnitX, new NVector3(6, 0, 3), 4.5f, 130f, 0.8f);

    private static T Field<T>(DuneclawView view, string name) where T : Object =>
        (T)new SerializedObject(view).FindProperty(name).objectReferenceValue;

    private static void AssertCleared(DuneclawView view)
    {
        Assert.That(view.IsShowing, Is.False);
        Assert.That(Field<Renderer>(view, "_claw").enabled, Is.False);
        Assert.That(Field<Renderer>(view, "_stinger").enabled, Is.False);
    }
}
