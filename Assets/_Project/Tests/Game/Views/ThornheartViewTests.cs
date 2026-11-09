using System.Linq;
using NUnit.Framework;
using Soulvail.Core.Ai;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Arena;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using VContainer;
using Object = UnityEngine.Object;
using Num = System.Numerics;

namespace Soulvail.Tests.Game.Views;

/// <summary>JG-20g rules 1–5: <see cref="ThornheartView"/> on the real <c>Thornheart.prefab</c>.</summary>
[TestFixture]
public sealed class ThornheartViewTests
{
    private const string PrefabPath = "Assets/_Project/Prefabs/Enemies/Thornheart.prefab";
    private const int Id = 20;
    private const int PodId = 40;
    private const string ClearingPath = "Assets/_Project/Prefabs/Arenas/Jungle/Arena_Clearing.prefab";

    // The band's spoke pointing −x (spoke i sits at angle 2πi/64, x = sin).
    private const int West = 48;

    private static readonly ContentId Pod = new("enemy.thornpod");

    private GameObject _body;
    private DomainEventHub _hub;
    private ThornheartView _view;
    private EnemyAnimatorView _animatorView;
    private Animator _animator;

    [SetUp]
    public void Rent()
    {
        _body = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        _animator = _body.GetComponentInChildren<Animator>();
        _animator.Rebind();
        _animator.Update(0f);
        _hub = new DomainEventHub();
        _view = _body.GetComponent<ThornheartView>();
        _view.Construct(_hub);
        _animatorView = _body.GetComponentInChildren<EnemyAnimatorView>();
        _animatorView.Construct(_hub);
        _body.GetComponent<EnemyView>().Bind(Id, Vector3.zero);
    }

    [TearDown]
    public void Return()
    {
        _hub.Dispose();
        Object.DestroyImmediate(_body);
    }

    [Test]
    public void View_TelegraphKinds_PlayClips()
    {
        Assert.That(_animator.applyRootMotion, Is.False);
        Assert.That(_animator.runtimeAnimatorController.name, Is.EqualTo("AC_Thornheart"));
        Assert.That(_animator.runtimeAnimatorController.animationClips.All(c => c.events.Length == 0), Is.True,
            "No clip carries an Animation Event (AR §18.2).");

        (ThornheartMove Move, string State, float Windup)[] moves =
        {
            (ThornheartMove.Roots, "Roots", ThornheartBehaviour.RootTelegraphSeconds),
            (ThornheartMove.Slam, "Slam", ThornheartBehaviour.SlamTelegraphSeconds),
            (ThornheartMove.Pods, "Plant", ThornheartBehaviour.PodTelegraphSeconds),
        };

        foreach ((ThornheartMove move, string state, float windup) in moves)
        {
            _hub.Publish(new EnemyTelegraph(Id, windup, (int)move));
            Settle();

            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName(state), Is.True, $"Kind {(int)move} plays {state}.");
            Assert.That(_animator.GetCurrentAnimatorClipInfo(0)[0].clip.name, Is.EqualTo("A_Thornheart_" + state));
        }

        _hub.Publish(new EnemyTelegraph(Id + 1, 1f, (int)ThornheartMove.Slam));
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Plant"), Is.True, "Another body's wind-up is not this one's.");
    }

    [Test]
    public void View_HeartExposed_LightsForSeconds_NotCyan()
    {
        Assert.That(_view.IsHeartLit, Is.False);
        Assert.That(Emission(), Is.EqualTo(Color.black));

        _hub.Publish(new HeartExposed(Id + 1, true, 2f));
        Assert.That(_view.IsHeartLit, Is.False, "Another boss's heart.");

        _hub.Publish(new HeartExposed(Id, true, 2f));
        Assert.That(_view.IsHeartLit, Is.True);
        Assert.That(_animator.GetBool("Exposed"), Is.True);
        Color lit = Emission();
        Assert.That(lit.maxColorComponent, Is.GreaterThan(1f), "Glows.");
        Color.RGBToHSV(lit, out float hue, out float saturation, out _);
        Assert.That(hue * 360f, Is.LessThan(70f), $"{lit} is warm.");
        Assert.That(hue * 360f >= 175f && hue * 360f <= 200f && saturation > 0.35f, Is.False, "Never cyan.");

        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Exposed"), Is.True);

        _view.Step(1.9f);
        Assert.That(_view.IsHeartLit, Is.True);
        _view.Step(0.2f);
        Assert.That(_view.IsHeartLit, Is.False, "Out after its seconds.");
        Assert.That(Emission(), Is.EqualTo(Color.black));
        Assert.That(_animator.GetBool("Exposed"), Is.False);

        _hub.Publish(new HeartExposed(Id, true, 2f));
        _hub.Publish(new HeartExposed(Id, false, 0f));
        Assert.That(_view.IsHeartLit, Is.False, "Closing puts it out.");
        Assert.That(Emission(), Is.EqualTo(Color.black));
    }

    [Test]
    public void View_PodPlanted_RingFills_RemovedOnDespawn()
    {
        _hub.Publish(new ThornPodPlanted(Id, PodId, new Num.Vector3(3f, 0f, 4f), 6f));
        _hub.Publish(new ThornPodPlanted(Id, PodId + 1, new Num.Vector3(-3f, 0f, 4f), 6f));
        _hub.Publish(new ThornPodPlanted(Id, PodId + 2, new Num.Vector3(0f, 0f, -5f), 6f));
        Assert.That(_view.RipeningCount, Is.EqualTo(3));

        GameObject ring = _view.RipenRing(PodId);
        Assert.That(ring.activeInHierarchy, Is.True);
        Assert.That(_view.RipenFill(PodId), Is.Zero);

        _body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        _view.Step(3f);
        Assert.That(Vector2.Distance(new Vector2(ring.transform.position.x, ring.transform.position.z), new Vector2(3f, 4f)), Is.LessThan(1e-4f),
            "At the pod, however the boss turns.");
        Assert.That(Quaternion.Angle(ring.transform.rotation, Quaternion.identity), Is.LessThan(0.01f), "Flat.");
        Assert.That(_view.RipenFill(PodId), Is.EqualTo(0.5f).Within(1e-4f));
        Transform fill = ring.transform.Find("Fill");
        Assert.That(fill.localScale.x, Is.EqualTo(ThornheartView.RipenRadius).Within(1e-4f), "Half-way: half the diameter.");
        Assert.That(ring.transform.Find("Rim").localScale.x, Is.EqualTo(ThornheartView.RipenRadius * 2f).Within(1e-4f));

        var block = new MaterialPropertyBlock();
        fill.GetComponent<Renderer>().GetPropertyBlock(block);
        Color colour = block.GetColor("_BaseColor");
        Assert.That(Vector3.Distance(new Vector3(colour.r, colour.g, colour.b), new Vector3(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b)), Is.LessThan(1e-3f), "Danger.");

        _view.Step(4f);
        Assert.That(_view.RipenFill(PodId), Is.EqualTo(1f), "Full when it ripens.");

        _hub.Publish(new EnemyDespawned(PodId));
        Assert.That(_view.RipenRing(PodId), Is.Null, "Ripened or despawned: gone.");
        Assert.That(ring.activeInHierarchy, Is.False);

        _hub.Publish(new EnemyDied(PodId + 1, Pod, Num.Vector3.Zero));
        Assert.That(_view.RipenRing(PodId + 1), Is.Null, "Shot: gone.");
        Assert.That(_view.RipeningCount, Is.EqualTo(1));

        _hub.Publish(new ThornPodPlanted(Id, PodId + 3, new Num.Vector3(1f, 0f, 1f), 6f));
        Assert.That(_view.RipenRing(PodId + 3), Is.Not.Null);
        Assert.That(_view.RipenFill(PodId + 3), Is.Zero, "A reused ring starts empty.");
    }

    [Test]
    public void View_StrangleRing_ShrinksToRadius()
    {
        Assert.That(_view.RingRadiusShown, Is.Zero);

        _hub.Publish(new StrangleRing(Id, 17f, 11f, 6f));
        Assert.That(_view.RingRadiusShown, Is.EqualTo(17f));
        Assert.That(InnerEdge(), Is.EqualTo(17f).Within(1e-3f));

        _view.Step(3f);
        Assert.That(_view.RingRadiusShown, Is.EqualTo(14f).Within(1e-4f));
        Assert.That(InnerEdge(), Is.EqualTo(14f).Within(1e-3f));

        _view.Step(4f);
        Assert.That(_view.RingRadiusShown, Is.EqualTo(11f), "Holds at the close.");
        Assert.That(InnerEdge(), Is.EqualTo(11f).Within(1e-3f), "The inner edge is the core radius exactly.");
        Assert.That(OuterEdge(), Is.EqualTo(11f + ThornheartView.BandWidth).Within(1e-3f), "The bramble lies outside, where it bites.");
        Assert.That(Radii().Max(), Is.EqualTo(ThornheartView.ZoneReach).Within(1e-3f), "The tint covers everything past it.");
        Assert.That(_view.Ring.activeInHierarchy, Is.True);
        Assert.That(new Vector2(_view.Ring.transform.position.x, _view.Ring.transform.position.z), Is.EqualTo(Vector2.zero));
    }

    // JG-20j: the area that bites is tinted Danger, and a bite flashes it.
    [Test]
    public void View_StrangleBite_TintsOutsideAndFlashes()
    {
        Assert.That(_view.ZoneAlphaShown, Is.Zero, "No ring, no tint.");
        _hub.Publish(new StrangleBite(Id, 2f));
        Assert.That(_view.ZoneAlphaShown, Is.Zero, "A bite before any ring draws nothing.");

        _hub.Publish(new StrangleRing(Id, 17f, 11f, 6f));
        Assert.That(_view.ZoneAlphaShown, Is.EqualTo(ThornheartView.ZoneAlpha));

        Color[] colours = _view.Ring.GetComponent<MeshFilter>().sharedMesh.colors;
        Vector3[] vertices = _view.Ring.GetComponent<MeshFilter>().sharedMesh.vertices;
        int zone = 0;

        for (int i = 0; i < vertices.Length; i++)
        {
            if (new Vector2(vertices[i].x, vertices[i].z).magnitude > 17f + ThornheartView.BandWidth + 1f)
            {
                zone++;
                Assert.That(colours[i].a, Is.EqualTo(ThornheartView.ZoneAlpha).Within(1e-3f));
                Assert.That(new Vector3(colours[i].r, colours[i].g, colours[i].b), Is.EqualTo(RgbOf(Palette.Vertex(Palette.Danger))), "Danger, nothing else.");
            }
        }

        Assert.That(zone, Is.GreaterThan(0), "The far edge carries the tint.");

        _hub.Publish(new StrangleBite(Id + 1, 2f));
        Assert.That(_view.ZoneAlphaShown, Is.EqualTo(ThornheartView.ZoneAlpha), "Another boss's bite is not ours.");

        _hub.Publish(new StrangleBite(Id, 2f));
        Assert.That(_view.ZoneAlphaShown, Is.EqualTo(ThornheartView.BiteAlpha).Within(1e-4f), "Flashes the instant it bites.");
        Assert.That(MaxZoneAlpha(), Is.EqualTo(ThornheartView.BiteAlpha).Within(1e-3f), "Drawn, not just counted.");

        _view.Step(ThornheartView.BiteFlashSeconds * 0.5f);
        Assert.That(_view.ZoneAlphaShown, Is.InRange(ThornheartView.ZoneAlpha + 1e-3f, ThornheartView.BiteAlpha - 1e-3f), "Fading.");

        _view.Step(ThornheartView.BiteFlashSeconds);
        Assert.That(_view.ZoneAlphaShown, Is.EqualTo(ThornheartView.ZoneAlpha), "Back to rest.");
        Assert.That(MaxZoneAlpha(), Is.EqualTo(ThornheartView.ZoneAlpha).Within(1e-3f));
    }

    [Test]
    public void View_Despawn_DropsRing()
    {
        _hub.Publish(new StrangleRing(Id, 17f, 11f, 6f));
        _view.Step(1f);

        _view.OnDespawn();

        Assert.That(_view.RingRadiusShown, Is.Zero);
        Assert.That(_view.Ring.activeInHierarchy, Is.False);

        _hub.Publish(new StrangleRing(Id, 17f, 11f, 6f));
        Assert.That(_view.RingRadiusShown, Is.EqualTo(17f), "The next life opens it afresh.");
    }

    [Test]
    public void View_Despawn_ResetsAll()
    {
        _hub.Publish(new HeartExposed(Id, true, 4f));
        _hub.Publish(new ThornPodPlanted(Id, PodId, new Num.Vector3(3f, 0f, 4f), 6f));
        _hub.Publish(new StrangleRing(Id, 17f, 11f, 6f));
        _hub.Publish(new EnemyDamaged(Id, 5f, 0.5f, false));
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(_animator.GetLayerIndex("Flinch")).IsName("Hit"), Is.True, "A hit flinches.");
        _hub.Publish(new EnemyDied(Id, Pod, Num.Vector3.Zero));
        Settle();
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Death"), Is.True, "A death falls.");
        Assert.That(_view.IsHeartLit, Is.False, "Death puts the heart out.");

        _view.OnDespawn();
        _animatorView.Forget();
        _animator.Update(0f);

        Assert.That(_view.IsHeartLit, Is.False);
        Assert.That(Emission(), Is.EqualTo(Color.black));
        Assert.That(_view.RipeningCount, Is.Zero);
        Assert.That(_view.RingRadiusShown, Is.Zero);
        Assert.That(_animator.GetBool("Exposed"), Is.False);
        Assert.That(_animator.GetBool("Dead"), Is.False);
        Assert.That(_animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), Is.True);
        Assert.That(_body.transform.Find("Live").childCount, Is.Zero, "Nothing left shown.");
    }

    /// <summary>One update starts a transition; the next carries it past its 0.05–0.1 s blend.</summary>
    private void Settle()
    {
        _animator.Update(0f);
        _animator.Update(0.12f);
    }

    // JG-20j: on a standing arena the band and tint stop at its floor; the open side keeps the core radius.
    [Test]
    public void View_StrangleRing_StopsAtTheArenaWalls()
    {
        var playerObject = new GameObject("Player");
        PlayerView player = playerObject.AddComponent<PlayerView>();
        IObjectResolver container = new ContainerBuilder().Build();
        ArenaView clearing = AssetDatabase.LoadAssetAtPath<GameObject>(ClearingPath).GetComponent<ArenaView>();
        var arenas = new ArenaPool(container, new[] { clearing }, null, _hub, player);

        try
        {
            _view.Construct(_hub, arenas);
            _hub.Publish(new StageArrived(20, clearing.Id));
            Transform surface = arenas.Active.Surface.transform;
            var floor = new Bounds(surface.position + arenas.Active.Surface.navMeshData.sourceBounds.center,
                arenas.Active.Surface.navMeshData.sourceBounds.size);

            Assert.That(ThornheartBehaviour.RingStartRadius + ThornheartView.BandWidth, Is.LessThan(Mathf.Min(floor.extents.x, floor.extents.z) - 1f),
                "Round a centred boss the ring opens on the floor, inside the walls, where a player can see it.");

            // Off-centre towards +x, so the ring overruns the east wall but not the west.
            _body.transform.position = new Vector3(10f, 0f, 0f);
            _hub.Publish(new StrangleRing(Id, ThornheartBehaviour.RingStartRadius, ThornheartBehaviour.RingRadius, 6f));
            _view.Step(7f);

            Vector3 at = _view.Ring.transform.position;
            Vector3[] vertices = _view.Ring.GetComponent<MeshFilter>().sharedMesh.vertices;

            foreach (Vector3 v in vertices)
            {
                Assert.That(at.x + v.x, Is.InRange(floor.min.x - 1e-3f, floor.max.x + 1e-3f), "Nothing past the walls.");
                Assert.That(at.z + v.z, Is.InRange(floor.min.z - 1e-3f, floor.max.z + 1e-3f), "Nothing past the walls.");
            }

            Assert.That(vertices.Max(v => at.x + v.x), Is.EqualTo(floor.max.x).Within(1e-3f), "The tint reaches the east wall.");
            Assert.That(vertices.Min(v => at.x + v.x), Is.EqualTo(floor.min.x).Within(1e-3f), "And the west wall.");
            Assert.That(Radii()[West * 4], Is.EqualTo(ThornheartBehaviour.RingRadius).Within(1e-3f), "The open side keeps the core radius.");

            _body.transform.position = new Vector3(-10f, 0f, 0f);
            _view.Step(0.1f);
            Assert.That(_view.Ring.transform.position.x, Is.EqualTo(-10f), "It follows the boss.");
            Assert.That(_view.Ring.GetComponent<MeshFilter>().sharedMesh.vertices.Min(v => -10f + v.x),
                Is.EqualTo(floor.min.x).Within(1e-3f), "And clips again where the boss now stands.");
        }
        finally
        {
            arenas.Dispose();
            container.Dispose();
            Object.DestroyImmediate(playerObject);
        }
    }

    // JG-20j: the look scales the body 3x; the band and pod rings still draw in world metres.
    [Test]
    public void View_ScaledBody_DrawsRingsInWorldMetres()
    {
        _body.transform.localScale = Vector3.one * 3f;
        _hub.Publish(new StrangleRing(Id, ThornheartBehaviour.RingRadius, ThornheartBehaviour.RingRadius, 0f));
        _hub.Publish(new ThornPodPlanted(Id, PodId, new Num.Vector3(4f, 0f, 0f), 6f));
        _view.Step(0.1f);

        Transform ring = _view.Ring.transform;
        float inner = _view.Ring.GetComponent<MeshFilter>().sharedMesh.vertices
            .Min(v => Vector3.Distance(ring.TransformPoint(v), ring.position));
        Assert.That(ring.lossyScale.x, Is.EqualTo(1f).Within(1e-4f));
        Assert.That(inner, Is.EqualTo(ThornheartBehaviour.RingRadius).Within(1e-3f), "The bite edge on the floor is core's 11 m.");

        Assert.That(_view.RipenRing(PodId).transform.lossyScale.x, Is.EqualTo(1f).Within(1e-4f), "A pod ring keeps its size too.");
        Assert.That(_view.RipenRing(PodId).transform.position.x, Is.EqualTo(4f).Within(1e-4f));
    }

    private Color Emission()
    {
        var block = new MaterialPropertyBlock();
        _body.transform.Find("Model/Heart").GetComponent<Renderer>().GetPropertyBlock(block);
        return block.isEmpty ? Color.black : block.GetColor("_EmissionColor");
    }

    private float InnerEdge() => Radii().Min();

    // The bramble's outer edge: the second vertex of every spoke (the tint starts there too).
    private float OuterEdge() => Radii().Where((_, i) => i % 4 == 1).Max();

    private static Vector3 RgbOf(Color colour) => new Vector3(colour.r, colour.g, colour.b);

    private float MaxZoneAlpha() =>
        _view.Ring.GetComponent<MeshFilter>().sharedMesh.colors.Where((_, i) => i % 4 == 3).Max(c => c.a);

    private float[] Radii() =>
        _view.Ring.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => new Vector2(v.x, v.z).magnitude).ToArray();
}
