using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The cyan disc under a planted character (CC §4.3), and the switch that turns it off (RS-06c).
/// Off, a full Focus ramp draws nothing; on, the disc grows with the ramp as it always did.
/// </summary>
/// <remarks>
/// <para>
/// The body is a scene object carrying the field initialisers, so the switch starts on, as it does in
/// any scene that never mentions it; the rows that want it off clear it. <c>Awake</c> never runs in
/// EditMode (Traps §5), and it is what builds the disc and hides it, so the fixture calls it.
/// </para>
/// <para>
/// The disc's mesh is destroyed in the teardown by hand, for <c>PlayerViewTests</c>' reason: Unity
/// sends no <c>OnDestroy</c> to a component it never sent an <c>Awake</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class FocusGlowViewTests
{
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameObject _body;
    private GameObject _disc;
    private FocusGlowView _view;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateBody()
    {
        _body = new GameObject("Player");
        _view = _body.AddComponent<FocusGlowView>();

        _disc = new GameObject("FocusGlow");
        _disc.transform.SetParent(_body.transform, false);
        _disc.AddComponent<MeshRenderer>();

        MeshFilter filter = _disc.AddComponent<MeshFilter>();

        using (var serialized = new SerializedObject(_view))
        {
            serialized.FindProperty("_glow").objectReferenceValue = filter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        _hub = new DomainEventHub();
        _view.Construct(_hub);
    }

    [TearDown]
    public void DestroyBody()
    {
        _hub?.Dispose();
        _hub = null;

        if (_disc != null)
        {
            Mesh mesh = _disc.GetComponent<MeshFilter>().sharedMesh;

            if (mesh != null)
            {
                Object.DestroyImmediate(mesh);
            }
        }

        if (_body != null)
        {
            Object.DestroyImmediate(_body);
        }
    }

    [Test]
    public void Glow_SwitchedOffNeverShows()
    {
        Show(false);
        Awake();

        _hub.Publish(new FocusRampChanged(0.5f));
        _hub.Publish(new FocusRampChanged(1f));

        // Rule 1: a full ramp, and no disc.
        Assert.That(_disc.activeSelf, Is.False, "The disc showed with the switch off.");
    }

    [Test]
    public void Glow_SwitchedOnGrowsWithTheRamp()
    {
        Awake();

        Assert.That(_disc.activeSelf, Is.False, "Sanity: hidden until Focus starts.");

        _hub.Publish(new FocusRampChanged(1f));

        // Rule 2: exactly as before RS-06c — shown, at 1 m plus 0.5 m at full Focus.
        Assert.That(_disc.activeSelf, Is.True);
        Assert.That(_disc.transform.localScale.x, Is.EqualTo(1.5f).Within(1e-5f));

        _hub.Publish(new FocusRampChanged(0f));

        Assert.That(_disc.activeSelf, Is.False, "Gone again when Focus drops.");
    }

    [Test]
    public void Player_ShipsWithTheDiscOffAndStillDressed()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PlayerPrefabPath}.");

        using var serialized = new SerializedObject(prefab.GetComponentInChildren<FocusGlowView>(true));

        // Rule 3: off in the build the owner plays, and one tick from coming back.
        Assert.That(serialized.FindProperty("_show").boolValue, Is.False, "Player.prefab shows the Focus disc.");
        Assert.That(serialized.FindProperty("_glow").objectReferenceValue, Is.Not.Null,
            "The disc was unhooked rather than switched off; ticking Show would bring nothing back.");
    }

    private void Show(bool show)
    {
        using var serialized = new SerializedObject(_view);

        serialized.FindProperty("_show").boolValue = show;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private void Awake() => typeof(FocusGlowView).GetMethod("Awake", Private).Invoke(_view, null);
}
