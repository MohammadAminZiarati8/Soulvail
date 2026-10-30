using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Core.Events;
using Soulvail.Tests.Core.Support;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using Soulvail.Game.Presentation;
using Soulvail.Game.Views;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Views;

/// <summary>
/// GD §16.2's other two enemy rows: the bar a basic enemy earns with a hit and loses two seconds
/// later, and the one an Elite never loses.
/// </summary>
/// <remarks>
/// <para>
/// <c>Start</c> and <c>Update</c> do not run in EditMode (Traps §5), so this drives the private
/// <c>Tick(dt)</c> the component carries for that reason. Driving the fade with
/// <c>Time.deltaTime</c> would be a test of the Editor's frame rate.
/// </para>
/// <para>
/// <b>Nothing in the build is an Elite</b> — <c>EnemySpec.IsElite</c> has existed since M1-05 and no
/// authored archetype sets it, because affixes are M7-02 — so the Elite rows here are the whole of
/// that treatment's coverage until then. M3-03 rule 10's null-tree branch makes the same bargain.
/// </para>
/// </remarks>
[TestFixture]
public sealed class EnemyHealthBarTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const BindingFlags Everything =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.DeclaredOnly;

    private const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemies/Enemy.prefab";

    private static readonly ContentId Husk = new ContentId("enemy.husk");

    private const int Id = 7;

    private GameObject _body;
    private EnemyView _view;
    private EnemyHealthBar _bar;
    private CanvasGroup _root;
    private Image _fill;
    private Image _track;
    private Image _ghost;
    private DomainEventHub _hub;

    [SetUp]
    public void CreateBar()
    {
        _hub = new DomainEventHub();

        _body = new GameObject("Enemy");

        _view = _body.AddComponent<EnemyView>();
        _bar = _body.AddComponent<EnemyHealthBar>();

        var barObject = new GameObject("HealthBar", typeof(RectTransform), typeof(CanvasGroup));

        barObject.transform.SetParent(_body.transform, false);

        _root = barObject.GetComponent<CanvasGroup>();

        var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));

        fillObject.transform.SetParent(barObject.transform, false);

        _fill = fillObject.GetComponent<Image>();
        _fill.type = Image.Type.Filled;
        _fill.fillMethod = Image.FillMethod.Horizontal;

        _track = NewImage("Track", barObject.transform);
        _ghost = NewImage("Ghost", barObject.transform);
        Set(_bar, "_track", _track);
        Set(_bar, "_ghost", _ghost);
        Set(_bar, "_root", _root);
        Set(_bar, "_fill", _fill);

        _bar.Construct(_hub);
    }

    [TearDown]
    public void DestroyBar()
    {
        _hub?.Dispose();
        _hub = null;

        if (_body != null)
        {
            Object.DestroyImmediate(_body);
        }

        _body = null;
    }

    // ---- The transient bar (rule 5) --------------------------------------------------------------

    [Test]
    public void Bar_IsHiddenUntilTheFirstHit()
    {
        _bar.Bind(Id, isElite: false);

        // GD §16.2 gives a basic enemy no standing bar: it is shown "only while it's being damaged".
        // A bar on every body in the arena would be twenty-eight readouts nobody asked for.
        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_root.alpha, Is.EqualTo(0f));
    }

    [Test]
    public void Bar_ShowsOnDamage()
    {
        _bar.Bind(Id, isElite: false);

        Damage(0.6f);

        Assert.That(_bar.IsShown, Is.True);
        Assert.That(_root.alpha, Is.EqualTo(1f));
        Assert.That(_fill.fillAmount, Is.EqualTo(0.6f).Within(1e-4f));
    }

    [Test]
    public void Bar_HidesAfterTwoSeconds()
    {
        _bar.Bind(Id, isElite: false);

        Damage(0.6f);

        Tick(Field("_secondsShown") + Field("_fadeSeconds"));

        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_root.alpha, Is.EqualTo(0f));
    }

    [Test]
    public void Bar_EachHitRestartsTheClock()
    {
        _bar.Bind(Id, isElite: false);

        Damage(0.6f);
        Tick(1.5f);

        Damage(0.4f);
        Tick(1.5f);

        // One bar, not a stutter — HpBarView's ghost rule, for its reason. A flurry of three swings a
        // second would otherwise leave the bar blinking in and out while the player is reading it.
        Assert.That(_bar.IsShown, Is.True);
        Assert.That(_root.alpha, Is.EqualTo(1f));
    }

    [Test]
    public void Bar_FreezesUnderAPause()
    {
        _bar.Bind(Id, isElite: false);

        Damage(0.6f);

        // Five seconds of a paused game is five seconds of dt 0, because Time.timeScale is 0 while
        // the game is stopped (M3-08a rule 13) and this bar reads the scaled delta on purpose — a bar
        // draining over a frozen arena would be the only thing moving on the screen. The deliberate
        // opposite of M3-10b's Overflow toast, which counts unscaled seconds to outlive a screen.
        for (int frame = 0; frame < 300; frame++)
        {
            Tick(0f);
        }

        Assert.That(_bar.IsShown, Is.True, "A pause does not spend the bar's two seconds.");

        Tick(Field("_secondsShown") + Field("_fadeSeconds"));

        Assert.That(_bar.IsShown, Is.False, "And it finishes fading once time moves again.");
    }

    [Test]
    public void Bar_FadesRatherThanBlinking()
    {
        _bar.Bind(Id, isElite: false);

        Damage(0.6f);

        Tick(Field("_secondsShown"));

        Assert.That(_root.alpha, Is.EqualTo(1f), "The hold is at full brightness throughout.");

        Tick(Field("_fadeSeconds") * 0.5f);

        float half = _root.alpha;

        Assert.That(half, Is.LessThan(1f).And.GreaterThan(0f), "And then it ramps.");
    }

    // ---- The Elite (rule 7) ----------------------------------------------------------------------

    [Test]
    public void Bar_EliteIsShownFromTheRental()
    {
        _bar.Bind(Id, isElite: true);

        // GD §16.2 states the reason as a decision rather than a style: "they're priority targets —
        // you're making decisions about them, so you need the number." A bar that only appeared after
        // the first hit would hide exactly the body the player is deciding about.
        Assert.That(_bar.IsShown, Is.True);
        Assert.That(_root.alpha, Is.EqualTo(1f));
        Assert.That(_fill.fillAmount, Is.EqualTo(1f).Within(1e-4f));
    }

    [Test]
    public void Bar_EliteNeverFades()
    {
        _bar.Bind(Id, isElite: true);

        Damage(0.5f);

        Tick(10f);

        Assert.That(_bar.IsShown, Is.True);
        Assert.That(_root.alpha, Is.EqualTo(1f));
    }

    [Test]
    public void Bar_EliteStillTracksItsHealth()
    {
        _bar.Bind(Id, isElite: true);

        Damage(0.4f);

        Assert.That(_fill.fillAmount, Is.EqualTo(0.4f).Within(1e-4f));
    }

    // ---- The boss (fix: the HUD's boss bar is its readout) ---------------------------------------

    [Test]
    public void Bar_BossNeverShows()
    {
        _bar.Bind(Id, isElite: false);
        Damage(0.8f);

        // Core announces a boss's phase straight after its spawn; a bar already up goes at once.
        _hub.Publish(new BossPhaseChanged(Id, 0, 3));

        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_root.alpha, Is.EqualTo(0f));

        Damage(0.5f);

        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_root.alpha, Is.EqualTo(0f));
    }

    [Test]
    public void Bar_AnotherBossLeavesItAlone()
    {
        _bar.Bind(Id, isElite: false);

        _hub.Publish(new BossPhaseChanged(Id + 1, 0, 3));
        Damage(0.6f);

        Assert.That(_bar.IsShown, Is.True);
    }

    [Test]
    public void Bar_RebindForgetsTheBoss()
    {
        _bar.Bind(Id, isElite: false);
        _hub.Publish(new BossPhaseChanged(Id, 0, 3));

        _bar.Unbind();
        _bar.Bind(Id, isElite: false);
        Damage(0.6f);

        // AR §18.4: a pooled body rented once by a boss and again by a Frog shows the Frog's bar.
        Assert.That(_bar.IsShown, Is.True);
    }

    // ---- The id filter and the pooled reset (rule 8, AR §18.4) -----------------------------------

    [Test]
    public void Bar_IgnoresOtherEnemies()
    {
        _bar.Bind(Id, isElite: false);

        _hub.Publish(new EnemyDamaged(Id + 1, 5f, 0.2f, killed: false));

        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_root.alpha, Is.EqualTo(0f));
    }

    [Test]
    public void Bar_UnboundMatchesNothing()
    {
        _bar.Bind(Id, isElite: true);

        _bar.Unbind();

        // A body sitting in the pool matches nothing, having been handed the id of nobody —
        // EnemyHitFeedback's rule, and the reason both can hold their subscriptions for the life of
        // the pool rather than of any one enemy.
        Assert.DoesNotThrow(() => Damage(0.5f));
        Assert.DoesNotThrow(() => _hub.Publish(new EnemyDamaged(0, 5f, 0.5f, killed: false)));

        Assert.That(_bar.IsShown, Is.False);
        Assert.That(_root.alpha, Is.EqualTo(0f));
    }

    [Test]
    public void Bar_UnbindForgetsTheEliteFlag()
    {
        _bar.Bind(Id, isElite: true);

        _bar.Unbind();

        _bar.Bind(Id, isElite: false);

        // AR §18.4. A body rented once as a priority target and again as a Husk would otherwise carry
        // a standing bar into a basic enemy, which reads as the wrong enemy being important.
        Assert.That(_bar.IsShown, Is.False);

        Damage(0.6f);

        Assert.That(_bar.IsShown, Is.True, "And it still earns one with the first hit.");
    }

    [Test]
    public void Bar_IsUnboundByTheBodyGoingBackToThePool()
    {
        _bar.Bind(Id, isElite: true);

        _view.Bind(Id, Vector3.zero);

        _view.OnDespawn();

        // EnemyView.OnDespawn's remarks invite "anything else added to the prefab that remembers
        // something" to join its list, and the Elite flag is the first thing to take them up on it.
        // Without this the reset would be split across two files and the one holding the pooled
        // object would be the one that did not have it.
        Assert.That(_bar.IsShown, Is.False);
    }

    // ---- Orientation and size (rule 9) -----------------------------------------------------------

    [Test]
    public void Bar_IsOrientedOnceNotPerFrame()
    {
        _bar.Bind(Id, isElite: true);

        LateUpdate();

        Assert.That(
            Quaternion.Angle(_root.transform.rotation, FacingCamera()),
            Is.LessThan(1e-3f),
            "The first frame points it at the camera.");

        // Deliberately knocked out of true. A per-frame billboard would put it straight back; this
        // one does not, because the body it is standing on has not turned — which is the whole of
        // rule 9's saving, and the only form of "written once" that survives EnemyView.Face.
        var knocked = Quaternion.Euler(11f, 22f, 33f);

        _root.transform.rotation = knocked;

        for (int frame = 0; frame < 60; frame++)
        {
            LateUpdate();
        }

        Assert.That(
            Quaternion.Angle(_root.transform.rotation, knocked),
            Is.LessThan(1e-3f),
            "Sixty frames of a standing body cost zero quaternions.");
    }

    [Test]
    public void Bar_ReorientsWhenTheBodyTurns()
    {
        // Rule 9's argument is about the camera — FollowCamera's rotation "is authored, not derived"
        // — and it is true about the camera and false about this transform: EnemyView.Face writes
        // transform.rotation on the *root* every tick a chaser is walking, and a bar parented to the
        // body inherits that yaw. So the rule's intent is kept rather than its letter: nothing is
        // written on a frame the body did not turn, and this is the other half of that claim.
        _bar.Bind(Id, isElite: true);

        LateUpdate();

        _root.transform.rotation = Quaternion.Euler(11f, 22f, 33f);

        _body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        LateUpdate();

        Assert.That(
            _root.transform.rotation.eulerAngles.y,
            Is.EqualTo(0f).Within(1e-3f),
            "A body that turned puts its bar back on the camera.");
    }

    [Test]
    public void Bar_IsNotOrientedWhileHidden()
    {
        _bar.Bind(Id, isElite: false);

        // The body first, then the bar: setting a parent's rotation moves the child's world rotation
        // with it, so a fixture that wrote them the other way round would be asserting about a value
        // it had already overwritten.
        _body.transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        var knocked = Quaternion.Euler(11f, 22f, 33f);

        _root.transform.rotation = knocked;

        for (int frame = 0; frame < 60; frame++)
        {
            LateUpdate();
        }

        // A basic enemy's bar is down for all but 2.25 s of each of its lives, and rule 9's fear —
        // twenty-eight quaternions a frame for no change — is paid for by exactly nobody while it is.
        Assert.That(Quaternion.Angle(_root.transform.rotation, knocked), Is.LessThan(1e-3f));
    }

    [Test]
    public void Bar_FacesTheAuthoredCameraPitch()
    {
        var cameraObject = new GameObject("Camera");

        try
        {
            var follow = cameraObject.AddComponent<FollowCamera>();

            float pitch = (float)typeof(FollowCamera).GetField("_pitchDeg", Private).GetValue(follow);
            float yaw = (float)typeof(FollowCamera).GetField("_yawDeg", Private).GetValue(follow);

            Quaternion facing = FacingCamera();

            // The angle is written down twice on purpose, the way EnemyLook.BoneGrey and M_BoneGrey
            // are: a pooled prefab cannot hold a reference to a scene camera, and Camera.main is a
            // tagged lookup this project refuses everywhere else. This row is what tells us the day
            // the two stop agreeing — which is the day the fixed whole-arena framing lands.
            Assert.That(
                Quaternion.Angle(facing, Quaternion.Euler(pitch, yaw, 0f)),
                Is.LessThan(1e-3f),
                $"The bar faces {facing.eulerAngles} and the camera is authored at ({pitch}, {yaw}).");
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    [TestCase(false, 36f)]
    [TestCase(true, 48f)]
    public void Bar_IsSizedInDp(bool elite, float widthDp)
    {
        var canvasObject = new GameObject("Canvas", typeof(Canvas));

        canvasObject.transform.SetParent(_root.transform, false);

        _bar.Bind(Id, elite);
        const float heightDp = 6f;

        var canvas = canvasObject.GetComponent<Canvas>();
        float scale = canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi) / scale;

        var rect = (RectTransform)_root.transform;

        // SkillButton's argument: a canvas measures in reference pixels, so a bar authored at 28 of
        // those is a different physical width on every phone.
        Assert.That(rect.sizeDelta.x, Is.EqualTo(widthDp * pxPerDp).Within(1e-2f));
        Assert.That(rect.sizeDelta.y, Is.EqualTo(heightDp * pxPerDp).Within(1e-2f));

        foreach (Image image in new[] { _fill, _ghost })
        {
            Assert.That(image.rectTransform.offsetMin, Is.EqualTo(Vector2.one * pxPerDp));
            Assert.That(image.rectTransform.offsetMax, Is.EqualTo(-Vector2.one * pxPerDp));
        }

        // And it floats above the body rather than standing in it.
        Assert.That(_root.transform.localPosition.y, Is.EqualTo(Field("_heightMetres")).Within(1e-4f));
    }

    // ---- The flag's journey from the spec to the bar (rule 6) ------------------------------------

    [Test]
    public void Spawn_CarriesTheEliteFlag()
    {
        var published = new List<EnemySpawned>();

        using (_hub.Subscribe<EnemySpawned>(evt => published.Add(evt)))
        {
            _hub.Publish(new EnemySpawned(1, Husk, System.Numerics.Vector3.Zero, isElite: true));
            _hub.Publish(new EnemySpawned(2, Husk, System.Numerics.Vector3.Zero));
        }

        // EnemySystem reads Spec.IsElite — a property that has existed since M1-05 and that
        // TargetScorer already scores on — and puts it on the event. The defaulted parameter is what
        // makes the second line here mean "not an Elite" rather than "unset": false *is*
        // not-an-Elite, which is the whole of the ruling that allowed the default.
        Assert.That(published[0].IsElite, Is.True);
        Assert.That(published[1].IsElite, Is.False);
    }

    [Test]
    public void Spawn_ViewsHandTheFlagToTheBar()
    {
        var prefabObject = new GameObject("EnemyPrefab");

        prefabObject.SetActive(false);

        EnemyView prefab = prefabObject.AddComponent<EnemyView>();

        prefabObject.AddComponent<EnemyHealthBar>();

        IObjectResolver container = ContainerWithHub();

        var parent = new GameObject("Pool");

        try
        {
            using var views = new EnemyViews(
                container,
                prefab,
                parent.transform,
                _hub,
                new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
                prewarm: 2);

            _hub.Publish(new EnemySpawned(3, Husk, System.Numerics.Vector3.Zero, isElite: true));

            Assert.That(views.TryGet(3, out EnemyView body), Is.True);

            EnemyHealthBar bar = body.HealthBar;

            Assert.That(bar, Is.Not.Null);

            // Bound on the rental, before the body was put into service — so a bar is never drawn for
            // one frame over the wrong enemy, which is the same ordering SetArchetypeLook needs and
            // for the same reason.
            Assert.That(Bound(bar), Is.EqualTo(3));
            Assert.That(Elite(bar), Is.True);
        }
        finally
        {
            container.Dispose();
            Object.DestroyImmediate(parent);
            Object.DestroyImmediate(prefabObject);
        }
    }

    [Test]
    public void Spawn_NoCatalogIsConsulted()
    {
        FieldInfo[] catalogs = typeof(EnemyViews)
            .GetFields(Everything)
            .Where(f => f.FieldType.Name.Contains("Catalog"))
            .ToArray();

        // The flag rides the event. Handing this class a catalog would answer the question *wrongly*
        // for M7-02, whose Elites are a body upgraded at spawn by spending WavePlan.UnspentThreat
        // (GD §8.3) rather than an archetype — so the census would report an Elite as a Husk for the
        // whole of the milestone that introduces them.
        Assert.That(
            catalogs.Select(f => f.Name),
            Is.Empty,
            "EnemyViews holds a look book and nothing that knows what an archetype is.");
    }

    // ---- Doors (guard rows) ----------------------------------------------------------------------

    [Test]
    public void Bar_InstantiatesNothingDuringARun()
    {
        var prefabObject = new GameObject("EnemyPrefab");

        prefabObject.SetActive(false);

        EnemyView prefab = prefabObject.AddComponent<EnemyView>();

        prefabObject.AddComponent<EnemyHealthBar>();

        IObjectResolver container = ContainerWithHub();

        var parent = new GameObject("Pool");

        try
        {
            using var views = new EnemyViews(
                container,
                prefab,
                parent.transform,
                _hub,
                new EnemyLookBook(new Dictionary<ContentId, EnemyLook>()),
                prewarm: 4);

            int bodies = parent.transform.childCount;

            Assert.That(bodies, Is.EqualTo(4), "Prewarmed before the run, which is the point.");

            for (int i = 1; i <= 50; i++)
            {
                _hub.Publish(new EnemySpawned(i, Husk, System.Numerics.Vector3.Zero));
                _hub.Publish(new EnemyDamaged(i, 5f, 0.5f, killed: false));
                _hub.Publish(new EnemyDespawned(i));

                Assert.That(
                    parent.transform.childCount,
                    Is.EqualTo(bodies),
                    $"The pool grew on enemy {i}. One bar per body, a child of the prefab, and no "
                        + "pool of its own — a total that keeps climbing is a collection during a "
                        + "fight (AR §14).");
            }
        }
        finally
        {
            container.Dispose();
            Object.DestroyImmediate(parent);
            Object.DestroyImmediate(prefabObject);
        }
    }

    [Test]
    public void Bar_CarriesNoSerializedColour()
    {
        FieldInfo[] colours = typeof(EnemyHealthBar)
            .GetFields(Everything)
            .Where(f => f.FieldType == typeof(Color) || f.FieldType == typeof(Color32))
            .Where(f => f.IsPublic || f.GetCustomAttribute<SerializeField>() != null)
            .ToArray();

        // PaletteTests.Views_CarryNoSerializedColour makes this claim about the nine files ledger row
        // 6 collected — a historical set, which a tenth entry would change the meaning of. The claim
        // itself is the one that has to keep holding for every view written after it, so a new view
        // brings its own copy: the fill is Palette.EnemyBar, GD §16.4's "everything else", which is
        // what an enemy is.
        Assert.That(
            colours.Select(f => f.Name),
            Is.Empty,
            "A field defaulted from the palette and then dressed differently on a prefab is the "
                + "placeholder problem with an extra step, and a read-back row would agree with the "
                + "dressed value whatever it was (Traps §7).");
    }

    [Test]
    public void Bar_DrawsThePaletteColours()
    {
        _bar.Bind(Id, isElite: false);

        Damage(0.6f);

        Assert.That(_fill.color, Is.EqualTo(Palette.EnemyBar));
        Assert.That(_ghost.color, Is.EqualTo(Palette.EnemyBarGhost));
        Assert.That(_track.color, Is.EqualTo(Palette.EnemyBarTrack));
    }

    [Test]
    public void Bar_IgnoresANonFiniteFraction()
    {
        _bar.Bind(Id, isElite: false);

        foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Damage(bad);

            // Read as full rather than clamped: Mathf.Clamp01 is two comparisons and every comparison
            // against NaN is false, so a NaN would pass through both bounds and into fillAmount, and
            // uGUI draws a NaN fill as nothing at all rather than reporting it (AR §18.3).
            Assert.That(_fill.fillAmount, Is.EqualTo(1f).Within(1e-4f), $"{bad} reached the fill.");
        }
    }

    [Test]
    public void Bind_RejectsANonPositiveId()
    {
        // A bar bound to Unbound would match every unbound body in the pool at once — the one failure
        // the id filter exists to make impossible.
        Assert.Throws<ArgumentOutOfRangeException>(() => _bar.Bind(0, isElite: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => _bar.Bind(-1, isElite: true));
    }

    [Test]
    public void Construct_RejectsANullHub()
    {
        var bare = new GameObject("Bare");

        try
        {
            bare.AddComponent<EnemyView>();

            var bar = bare.AddComponent<EnemyHealthBar>();

            Assert.Throws<ArgumentNullException>(() => bar.Construct(null));
        }
        finally
        {
            Object.DestroyImmediate(bare);
        }
    }

    [Test]
    public void Prefab_IsDressed()
    {
#if UNITY_EDITOR
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);

        Assert.That(prefab, Is.Not.Null, $"{EnemyPrefabPath} is missing.");

        var bar = prefab.GetComponent<EnemyHealthBar>();

        Assert.That(bar, Is.Not.Null, "Enemy.prefab carries no EnemyHealthBar.");

        // Read back through SerializedObject rather than reflection, because that is what reads what
        // the asset actually stored rather than what a field initialiser would have produced on a
        // fresh instance (Traps §7).
        var serialized = new UnityEditor.SerializedObject(bar);

        Assert.That(
            serialized.FindProperty("_root").objectReferenceValue,
            Is.Not.Null,
            "The bar's root CanvasGroup is not wired.");

        Assert.That(
            serialized.FindProperty("_fill").objectReferenceValue,
            Is.Not.Null,
            "The bar's fill Image is not wired.");
#endif
    }

    [Test]
    public void Ghost_HoldsAfterAHit()
    {
        _bar.Bind(Id, false);
        Damage(0.6f);
        Tick(0.09f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(1f));
        Assert.That(_fill.fillAmount, Is.EqualTo(0.6f));
    }

    [Test]
    public void Ghost_DrainsToTheFill()
    {
        _bar.Bind(Id, false);
        Damage(0.6f);
        Tick(0.1f);
        Tick(0.2f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.7f).Within(1e-4f));
        Tick(1f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.6f));
        Assert.That(_ghost.fillAmount, Is.EqualTo(0.6f));
    }

    [Test]
    public void Ghost_EachHitRestartsTheHold()
    {
        _bar.Bind(Id, false);
        Damage(0.8f);
        Tick(0.08f);
        Damage(0.5f);
        Tick(0.08f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(1f));
    }

    [Test]
    public void Ghost_FreezesUnderAPause()
    {
        _bar.Bind(Id, false);
        Damage(0.6f);
        Tick(0.2f);
        float before = _bar.GhostFraction;
        for (int i = 0; i < 10; i++)
        {
            Tick(0f);
        }
        Assert.That(before, Is.LessThan(1f));
        Assert.That(_bar.GhostFraction, Is.EqualTo(before));
    }

    [Test]
    public void Ghost_DrainsOnAnElite()
    {
        _bar.Bind(Id, true);
        Damage(0.5f);
        Tick(2f);
        Assert.That(_bar.GhostFraction, Is.EqualTo(0.5f));
    }

    [Test]
    public void Ghost_ResetsOnRebind()
    {
        _bar.Bind(Id, false);
        Damage(0.3f);
        Tick(1f);
        _bar.Unbind();
        Assert.That(_bar.GhostFraction, Is.EqualTo(1f));
        _bar.Bind(Id, false);
        Assert.That(_bar.GhostFraction, Is.EqualTo(1f));
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void Ghost_IgnoresANonFiniteFraction(float fraction)
    {
        _bar.Bind(Id, false);
        Damage(0.3f);
        Damage(fraction);
        Tick(1f);
        Assert.That(float.IsFinite(_bar.GhostFraction), Is.True);
        Assert.That(_bar.GhostFraction, Is.EqualTo(_fill.fillAmount));
    }

    [Test]
    public void Tick_AllocatesNothing()
    {
        _bar.Bind(Id, false);
        Damage(0.5f);
        var tick = (Action<float>)Delegate.CreateDelegate(typeof(Action<float>), _bar,
            typeof(EnemyHealthBar).GetMethod("Tick", Private));
        AllocationAssert.None(() => tick(0.016f), iterations: 100);
    }

    [TestCase("Enemy")]
    [TestCase("Croaker")]
    [TestCase("Frog")]
    [TestCase("Panther")]
    [TestCase("Pitcher")]
    [TestCase("Puffball")]
    [TestCase("Rootling")]
    public void Prefab_BarImagesCarrySprites(string name)
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/_Project/Prefabs/Enemies/" + name + ".prefab");
        Assert.That(prefab, Is.Not.Null);
        EnemyHealthBar component = prefab.GetComponent<EnemyHealthBar>();
        // Croaker removes the component and hides these children: it uses the boss HUD instead.
        var bar = component != null ? new UnityEditor.SerializedObject(component) : null;
        Transform root = prefab.transform.Find("HealthBar");
        foreach (string field in new[] { "_track", "_ghost", "_fill" })
        {
            string child = field == "_track" ? "Track" : field == "_ghost" ? "Ghost" : "Fill";
            var image = root.Find(child).GetComponent<Image>();
            if (bar != null)
            {
                Assert.That(bar.FindProperty(field).objectReferenceValue, Is.SameAs(image));
            }
            Assert.That(image, Is.Not.Null, name + field);
            Assert.That(image.sprite, Is.Not.Null, name + field);
            string sprite = field == "_track" ? "Pill_Dark" : "Fill_BarThin";
            Assert.That(UnityEditor.AssetDatabase.GetAssetPath(image.sprite),
                Is.EqualTo("Assets/_Project/Art/UI/Hud/" + sprite + ".png"));
            if (field != "_track")
            {
                Assert.That(image.type, Is.EqualTo(Image.Type.Filled));
                Assert.That(image.fillMethod, Is.EqualTo(Image.FillMethod.Horizontal));
                Assert.That(image.fillOrigin, Is.EqualTo((int)Image.OriginHorizontal.Left));
            }
        }
        if (bar != null)
        {
            Assert.That(bar.FindProperty("_widthDp").floatValue, Is.EqualTo(36f));
            Assert.That(bar.FindProperty("_heightDp").floatValue, Is.EqualTo(6f));
            Assert.That(bar.FindProperty("_eliteWidthDp").floatValue, Is.EqualTo(48f));
        }
        else
        {
            Assert.That(name, Is.EqualTo("Croaker"));
            Assert.That(root.gameObject.activeSelf, Is.False);
        }
    }

    [Test]
    public void Prefab_FillDrawsOnTop()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        Transform root = prefab.transform.Find("HealthBar");
        Assert.That(Enumerable.Range(0, root.childCount).Select(i => root.GetChild(i).name),
            Is.EqualTo(new[] { "Track", "Ghost", "Fill" }));
    }

    private static Image NewImage(string name, Transform parent)
    {
        var child = new GameObject(name, typeof(RectTransform), typeof(Image));
        child.transform.SetParent(parent, false);
        var image = child.GetComponent<Image>();
        image.rectTransform.anchorMin = Vector2.zero;
        image.rectTransform.anchorMax = Vector2.one;
        return image;
    }

    // ---- Fixture helpers ------------------------------------------------------------------------

    /// <summary>
    /// A container that can inject a pooled body. <c>EnemyHealthBar</c> takes the hub through
    /// <c>[Inject]</c>, and <c>IObjectResolver.Instantiate</c> runs that during the instantiate —
    /// so a container without one is a pool that throws on prewarm rather than a pool with no bars.
    /// </summary>
    private IObjectResolver ContainerWithHub()
    {
        var builder = new ContainerBuilder();

        builder.RegisterInstance(_hub);

        return builder.Build();
    }

    private void Damage(float hpFraction) =>
        _hub.Publish(new EnemyDamaged(Id, 5f, hpFraction, killed: false));

    private void Tick(float dt) =>
        typeof(EnemyHealthBar).GetMethod("Tick", Private).Invoke(_bar, new object[] { dt });

    private void LateUpdate() => Invoke(_bar, "LateUpdate");

    private float Field(string field) =>
        (float)typeof(EnemyHealthBar).GetField(field, Private).GetValue(_bar);

    private static int Bound(EnemyHealthBar bar) =>
        (int)typeof(EnemyHealthBar).GetField("_id", Private).GetValue(bar);

    private static bool Elite(EnemyHealthBar bar) =>
        (bool)typeof(EnemyHealthBar).GetField("_isElite", Private).GetValue(bar);

    private static Quaternion FacingCamera() =>
        (Quaternion)typeof(EnemyHealthBar)
            .GetField("FacingCamera", BindingFlags.Static | BindingFlags.NonPublic)
            .GetValue(null);

    private static void Set(EnemyHealthBar bar, string field, Object value) =>
        typeof(EnemyHealthBar).GetField(field, Private).SetValue(bar, value);

    private static void Invoke(EnemyHealthBar bar, string method) =>
        typeof(EnemyHealthBar).GetMethod(method, Private).Invoke(bar, null);
}
