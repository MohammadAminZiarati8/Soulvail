using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Num2 = System.Numerics.Vector2;
using Num3 = System.Numerics.Vector3;

namespace Soulvail.Tests.Game.Views;

public sealed class RescueRingViewTests
{
    private const string Prefabs = "Assets/_Project/Prefabs/";
    private const int HoundId = 1;
    private readonly PetSpec _spec = new(new ContentId("pet.hound"), 50f, 4f, 8f, 1f, 1f, 8f, 10f, 1.5f);
    private DomainEventHub _hub;
    private IObjectResolver _resolver;
    private GameObject _root;
    private PetViews _pets;
    private RescueRingView _ring;
    private LineRenderer _track;
    private LineRenderer _arc;
    private Animator _hound;
    private Image _bar;
    private float _health;

    [SetUp]
    public void SetUp()
    {
        _hub = new DomainEventHub();
        _resolver = new ContainerBuilder().Build();
        _root = new GameObject("Rescue test views");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Pets/Hound.prefab");
        _pets = new PetViews(_resolver, prefab.GetComponent<PetView>(), _root.transform, _hub,
            healthFraction: id => id == HoundId ? _health : null);
        _health = 1f;
        _hub.Publish(new PetSpawned(HoundId, Num3.Zero));
        _hound = _root.GetComponentInChildren<Animator>();
        _hound.Rebind();
        _hound.Update(0f);
        _bar = _root.GetComponentInChildren<PetHealthBar>().transform.Find("Pet Health/Fill").GetComponent<Image>();

        var ringPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Vfx/RescueRing.prefab");
        _ring = Object.Instantiate(ringPrefab, _root.transform).GetComponent<RescueRingView>();
        _ring.Construct(_hub, _spec.RescueRadius);
        _ring.gameObject.SetActive(true);
        _track = _ring.transform.Find("Track").GetComponent<LineRenderer>();
        _arc = _ring.transform.Find("Fill").GetComponent<LineRenderer>();
    }

    [TearDown]
    public void TearDown()
    {
        _pets?.Dispose();
        Object.DestroyImmediate(_root);
        _resolver?.Dispose();
        _hub?.Dispose();
    }

    [Test]
    public void Hurt_RestartsHitAndDropsCyanBar()
    {
        _health = 0.65f;
        _hub.Publish(new PetHurt(HoundId, 17.5f, 9));
        _hound.Update(0f);
        _hound.Update(0.15f);
        Assert.That(StateOf(_hound).shortNameHash, Is.EqualTo(Animator.StringToHash("Hit")));
        Assert.That(StateOf(_hound).normalizedTime, Is.GreaterThan(0f));
        Assert.That(_bar.rectTransform.anchorMax.x, Is.EqualTo(0.65f));
        Assert.That(_bar.color, Is.EqualTo(Palette.Player));

        _health = 0.55f;
        _hub.Publish(new PetHurt(HoundId, 5f, 9));
        _hound.Update(0f);
        Assert.That(StateOf(_hound).normalizedTime, Is.EqualTo(0f).Within(0.001f));
        Assert.That(_bar.rectTransform.anchorMax.x, Is.EqualTo(0.55f));
    }

    [Test]
    public void Heeled_RunsWithPulsingCyanBar()
    {
        _health = 0.3f;
        _hub.Publish(new PetHurt(HoundId, 35f, 9));
        _hub.Publish(new PetHeeled(HoundId));
        _pets.ApplyMoves(new[] { new EnemyMoveIntent(HoundId, Num3.UnitX, Num2.UnitX) }, 0.1f);
        Assert.That(_hound.GetFloat("Speed"), Is.EqualTo(4f));
        Assert.That(_root.GetComponentInChildren<PetView>().transform.position.x, Is.EqualTo(0.1f));
        Color dim = _bar.color;
        _root.GetComponentInChildren<PetHealthBar>().UpdatePulse(1f / 3f);
        Assert.That(_bar.color, Is.EqualTo(Palette.Player));
        Assert.That(dim, Is.EqualTo(new Color(Palette.Player.r, Palette.Player.g, Palette.Player.b, 0.5f)));

        _hub.Publish(new PetReturned(HoundId));
        _pets.ApplyMoves(new[] { new EnemyMoveIntent(HoundId, Num3.UnitX, Num2.UnitX) }, 0.1f);
        Assert.That(_hound.GetFloat("Speed"), Is.EqualTo(1f));
        Assert.That(_bar.color, Is.EqualTo(Palette.Player));
    }

    [Test]
    public void Fell_ShowsEmptyRing_HidesBar()
    {
        Assert.That(_track.enabled, Is.False);
        _hub.Publish(new PetFell(HoundId, new Num3(2f, 0f, 4f)));
        _hound.Update(0.01f);
        _hound.Update(3f);

        Assert.That(StateOf(_hound).shortNameHash, Is.EqualTo(Animator.StringToHash("Fall")));
        Assert.That(_bar.isActiveAndEnabled, Is.False);
        Assert.That(_ring.transform.position, Is.EqualTo(new Vector3(2f, 0f, 4f)));
        Assert.That(_track.enabled, Is.True);
        Assert.That(_arc.enabled, Is.False);
        Assert.That(_arc.positionCount, Is.Zero);
        Assert.That(_arc.startColor, Is.EqualTo(Palette.Player));
        Assert.That(_track.GetPosition(0).magnitude, Is.EqualTo(_spec.RescueRadius));
        Assert.That(_track.sharedMaterial, Is.Not.Null);
    }

    [Test]
    public void Progressed_FillsArc_KeptWhenAway()
    {
        _hub.Publish(new PetFell(HoundId, Num3.Zero));
        _hub.Publish(new RescueProgressed(HoundId, 0.25f));
        Vector3 quarter = _arc.GetPosition(_arc.positionCount - 1);
        Assert.That(_arc.enabled, Is.True);
        Assert.That(quarter.x, Is.EqualTo(_spec.RescueRadius).Within(0.001f));
        Assert.That(quarter.y, Is.EqualTo(0f).Within(0.001f));

        _ring.Step(1f);
        _hub.Publish(new RescueProgressed(HoundId + 1, 1f));
        Assert.That(_arc.GetPosition(_arc.positionCount - 1), Is.EqualTo(quarter));

        _hub.Publish(new RescueProgressed(HoundId, 0.5f));
        Assert.That(_arc.GetPosition(_arc.positionCount - 1).y, Is.EqualTo(-_spec.RescueRadius).Within(0.001f));
    }

    [Test]
    public void Rose_FadesRing_ShowsBar()
    {
        _hub.Publish(new PetFell(HoundId, Num3.Zero));
        _hub.Publish(new RescueProgressed(HoundId, 1f));
        _health = _spec.RiseFraction;
        _hub.Publish(new PetRose(HoundId));
        _hound.Update(0.01f);

        Assert.That(StateOf(_hound).shortNameHash, Is.EqualTo(Animator.StringToHash("GetUp")));
        Assert.That(_bar.isActiveAndEnabled, Is.True);
        Assert.That(_bar.rectTransform.anchorMax.x, Is.EqualTo(_spec.RiseFraction));
        _ring.Step(0.15f);
        Assert.That(_track.enabled, Is.True);
        Assert.That(_arc.startColor.a, Is.EqualTo(0.5f).Within(1f / 255f));
        _ring.Step(0.15f);
        Assert.That(_track.enabled, Is.False);
        Assert.That(_arc.enabled, Is.False);
    }

    [Test]
    public void Progressed_KneelsWhileStill_MovingOnlyCancelsKneel()
    {
        var player = new GameObject("Rescuing player", typeof(CharacterController), typeof(PlayerView));
        player.transform.SetParent(_root.transform);
        InvokeLifecycle(player.GetComponent<PlayerView>(), "Awake");
        var body = Object.Instantiate(
            AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Characters/Beastmaster.prefab"), player.transform);
        var view = body.GetComponentInChildren<BeastmasterAnimatorView>();
        var animator = body.GetComponentInChildren<Animator>();
        InvokeLifecycle(view, "Awake");
        view.Construct(_hub);
        animator.Rebind();
        animator.Update(0f);

        _hub.Publish(new PetFell(HoundId, Num3.Zero));
        _hub.Publish(new RescueProgressed(HoundId, 0.25f));
        UpdatePlayerAnimator(view, animator);
        Assert.That(StateOf(animator).shortNameHash, Is.EqualTo(Animator.StringToHash("Interact")));

        player.GetComponent<PlayerView>().Apply(new PlayerMoveIntent(Num3.UnitX, Num3.UnitX), 0.01f);
        _hub.Publish(new RescueProgressed(HoundId, 0.5f));
        UpdatePlayerAnimator(view, animator);
        Assert.That(StateOf(animator).shortNameHash, Is.EqualTo(Animator.StringToHash("Run")));
        Assert.That(_arc.enabled, Is.True);
        Assert.That(_arc.GetPosition(_arc.positionCount - 1).y, Is.EqualTo(-_spec.RescueRadius).Within(0.001f));

        player.GetComponent<PlayerView>().Apply(default, 0.01f);
        UpdatePlayerAnimator(view, animator);
        _hub.Publish(new RescueProgressed(HoundId, 0.75f));
        UpdatePlayerAnimator(view, animator);
        Assert.That(StateOf(animator).shortNameHash, Is.EqualTo(Animator.StringToHash("Interact")));

        _hub.Publish(new RescueProgressed(HoundId, 1f));
        _hub.Publish(new PetRose(HoundId));
        UpdatePlayerAnimator(view, animator);
        Assert.That(StateOf(animator).shortNameHash, Is.EqualTo(Animator.StringToHash("Idle")));
    }

    [Test]
    public void StageChange_ClearsRing()
    {
        _hub.Publish(new PetFell(HoundId, Num3.Zero));
        _hub.Publish(new RescueProgressed(HoundId, 0.5f));
        _hub.Publish(new StageTransitionStarted(1, 0.3f));
        Assert.That(_track.enabled, Is.False);
        Assert.That(_arc.enabled, Is.False);

        _hub.Publish(new RescueProgressed(HoundId, 1f));
        Assert.That(_arc.enabled, Is.False);

        _hub.Publish(new PetFell(HoundId, Num3.Zero));
        _hub.Publish(new StageArrived(2, default));
        Assert.That(_track.enabled, Is.False);
    }

    private static void InvokeLifecycle<T>(T view, string method) where T : MonoBehaviour =>
        typeof(T).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);

    private static AnimatorStateInfo StateOf(Animator animator) => animator.GetCurrentAnimatorStateInfo(0);

    private static void UpdatePlayerAnimator(BeastmasterAnimatorView view, Animator animator)
    {
        InvokeLifecycle(view, "Update");
        InvokeLifecycle(view, "LateUpdate");
        animator.Update(0.01f);
        animator.Update(0.15f);
        animator.Update(0.01f);
    }
}
