using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Vector2 = System.Numerics.Vector2;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// The swing wedge on the player's body (RS-06a): the Censer's 8 m arc, flashed for a tenth of a
/// second when a cone swing starts, and for no other kind of swing. Until RS-06a it flashed on every
/// <see cref="PlayerAttacked"/>, so the Ranger drew a sword's arc with every arrow.
/// </summary>
/// <remarks>
/// <para>
/// The body is a scene object rather than <c>Player.prefab</c>, for <c>ReticleViewTests</c>' reason:
/// the rows are about behaviour, and the prefab's own dressing is the last row's business.
/// <c>Awake</c> never runs in EditMode (Traps §5), and it is what builds the wedge and hides it, so
/// the fixture calls it.
/// </para>
/// <para>
/// The mesh <c>Awake</c> builds is destroyed in the teardown by hand: <c>OnDestroy</c> is a message,
/// and Unity sends none to a component whose <c>Awake</c> it never sent.
/// </para>
/// </remarks>
[TestFixture]
public sealed class PlayerViewTests
{
    private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Player/Player.prefab";

    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>A swing along world +X.</summary>
    private static Vector2 East => new Vector2(1f, 0f);

    private GameObject _body;
    private GameObject _cone;
    private PlayerView _view;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateBody()
    {
        // RequireComponent adds the CharacterController.
        _body = new GameObject("Player");
        _view = _body.AddComponent<PlayerView>();

        _cone = new GameObject("SwingCone");
        _cone.transform.SetParent(_body.transform, false);
        _cone.AddComponent<MeshRenderer>();

        MeshFilter filter = _cone.AddComponent<MeshFilter>();

        using (var serialized = new SerializedObject(_view))
        {
            serialized.FindProperty("_swingCone").objectReferenceValue = filter;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        _hub = new DomainEventHub();
        _view.Construct(_hub);

        typeof(PlayerView).GetMethod("Awake", Private).Invoke(_view, null);
    }

    [TearDown]
    public void DestroyBody()
    {
        _hub?.Dispose();
        _hub = null;

        if (_cone != null)
        {
            Mesh wedge = _cone.GetComponent<MeshFilter>().sharedMesh;

            if (wedge != null)
            {
                Object.DestroyImmediate(wedge);
            }
        }

        if (_body != null)
        {
            Object.DestroyImmediate(_body);
        }
    }

    [Test]
    public void Swing_AConeSwingFlashesTheWedge()
    {
        Assert.That(_cone.activeSelf, Is.False, "Sanity: the wedge is hidden until the first swing.");

        _hub.Publish(new PlayerAttacked(East, WeaponKind.Cone));

        // Rule 2: the Censer's tell, exactly as before.
        Assert.That(_cone.activeSelf, Is.True);
        Assert.That(_cone.transform.forward.x, Is.EqualTo(1f).Within(1e-4f), "Along the swing's own facing.");
    }

    [Test]
    public void Swing_AShotDrawsNoWedge()
    {
        _hub.Publish(new PlayerAttacked(East, WeaponKind.Projectile));

        // Rule 2: a bow's swing sweeps no wedge, so none is drawn, and the hidden one is not turned
        // either — a shot tells this body nothing.
        Assert.That(_cone.activeSelf, Is.False, "An arrow's swing drew the Censer's wedge.");
        Assert.That(_cone.transform.rotation, Is.EqualTo(Quaternion.identity));

        // The control: the same body still draws a cone's swing, so the refusal above is about the
        // kind and not a wedge that could never be drawn.
        _hub.Publish(new PlayerAttacked(East, WeaponKind.Cone));

        Assert.That(_cone.activeSelf, Is.True);
    }

    [Test]
    public void Player_TheOathboundsWedgeIsUnchanged()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

        Assert.That(prefab, Is.Not.Null, $"No prefab at {PlayerPrefabPath}.");

        using var serialized = new SerializedObject(prefab.GetComponent<PlayerView>());

        var cone = serialized.FindProperty("_swingCone").objectReferenceValue as MeshFilter;

        // Rule 3: every class wears this body, so the Oathbound's wedge is still dressed on it, with
        // the numbers and the material it shipped with.
        Assert.That(cone, Is.Not.Null, "Player.prefab no longer dresses a swing cone.");
        Assert.That(cone.name, Is.EqualTo("SwingCone"));
        Assert.That(cone.transform.parent, Is.EqualTo(prefab.transform));
        Assert.That(cone.GetComponent<MeshRenderer>().sharedMaterial.name, Is.EqualTo("M_Reticle"));

        Assert.That(serialized.FindProperty("_swingSeconds").floatValue, Is.EqualTo(0.1f).Within(1e-6f));
        Assert.That(serialized.FindProperty("_swingRange").floatValue, Is.EqualTo(8f).Within(1e-6f));
        Assert.That(serialized.FindProperty("_swingAngleDeg").floatValue, Is.EqualTo(60f).Within(1e-6f));
    }
}
