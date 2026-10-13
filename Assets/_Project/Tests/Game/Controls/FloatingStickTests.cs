using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Game.Controls;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Controls;

/// <summary>
/// UI-02b: FloatingStick dressed with Stick_Base and Stick_Knob on Hud.prefab, preserving the
/// 120 dp base and 52 dp knob dimensions. UI-02d: the stick is fixed at a dp offset from the safe
/// area's bottom-left, always visible, read from that centre; the floating behaviour of CC §2.3
/// survives behind <c>_floating</c>. CC §2.1–2.3; GD §16.4.
/// </summary>
[TestFixture]
public sealed class FloatingStickTests
{
    private const string HudPath = "Assets/_Project/Prefabs/UI/Hud.prefab";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private readonly List<GameObject> _spawned = new List<GameObject>();

    [TearDown]
    public void DestroySpawned()
    {
        foreach (GameObject go in _spawned)
        {
            if (go != null)
            {
                Object.DestroyImmediate(go);
            }
        }

        _spawned.Clear();
    }

    // ---- Dressing tests (Rules 1, 2) -------------------------------------------------------------

    [Test]
    public void FloatingStick_BaseWearsStickBase()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        var stick = prefab.GetComponentInChildren<FloatingStick>(includeInactive: true);
        Assert.That(stick, Is.Not.Null, "FloatingStick component not found on Hud.prefab.");

        var baseRect = Field<RectTransform>(stick, "_base");
        Assert.That(baseRect, Is.Not.Null, "_base RectTransform is null on FloatingStick.");

        var image = baseRect.GetComponent<Image>();
        Assert.That(image, Is.Not.Null, "Base GameObject has no Image component.");
        Assert.That(image.sprite, Is.Not.Null, "Base Image has no sprite assigned.");
        Assert.That(image.sprite.name, Is.EqualTo("Stick_Base"), "Base does not wear Stick_Base.");
        Assert.That(image.preserveAspect, Is.True, "Base Image does not preserve native aspect.");
        Assert.That(image.color, Is.EqualTo(Color.white), "Base Image is tinted away from Color.white.");
    }

    [Test]
    public void FloatingStick_KnobWearsStickKnob()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        var stick = prefab.GetComponentInChildren<FloatingStick>(includeInactive: true);
        Assert.That(stick, Is.Not.Null, "FloatingStick component not found on Hud.prefab.");

        var knobRect = Field<RectTransform>(stick, "_knob");
        Assert.That(knobRect, Is.Not.Null, "_knob RectTransform is null on FloatingStick.");

        var image = knobRect.GetComponent<Image>();
        Assert.That(image, Is.Not.Null, "Knob GameObject has no Image component.");
        Assert.That(image.sprite, Is.Not.Null, "Knob Image has no sprite assigned.");
        Assert.That(image.sprite.name, Is.EqualTo("Stick_Knob"), "Knob does not wear Stick_Knob.");
        Assert.That(image.preserveAspect, Is.True, "Knob Image does not preserve native aspect.");
        Assert.That(image.color, Is.EqualTo(Color.white), "Knob Image is tinted away from Color.white.");
    }

    // ---- Sizing and layout tests (Rules 1, 2) -----------------------------------------------------

    [Test]
    public void FloatingStick_SizingMatchesDpSpecification()
    {
        FloatingStick stick = SpawnStick();
        var baseRect = Field<RectTransform>(stick, "_base");
        var knobRect = Field<RectTransform>(stick, "_knob");

        var canvas = stick.GetComponentInParent<Canvas>();
        float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);

        float expectedBase = 120f * pxPerDp / scale;
        float expectedKnob = 52f * pxPerDp / scale;

        Assert.That(baseRect.sizeDelta.x, Is.EqualTo(expectedBase).Within(1e-2f), "Base width does not match 120 dp.");
        Assert.That(baseRect.sizeDelta.y, Is.EqualTo(expectedBase).Within(1e-2f), "Base height does not match 120 dp.");
        Assert.That(knobRect.sizeDelta.x, Is.EqualTo(expectedKnob).Within(1e-2f), "Knob width does not match 52 dp.");
        Assert.That(knobRect.sizeDelta.y, Is.EqualTo(expectedKnob).Within(1e-2f), "Knob height does not match 52 dp.");

        // Sizing logic verified across test DPIs (160, 320, 400).
        MethodInfo resizeMethod = typeof(FloatingStick).GetMethod("Resize", Private);
        Assert.That(resizeMethod, Is.Not.Null, "Resize method not found on FloatingStick.");

        float[] testDpis = { 160f, 320f, 400f };
        foreach (float dpi in testDpis)
        {
            float dpiPxPerDp = StickShaper.PixelsPerDp(dpi);
            resizeMethod.Invoke(stick, new object[] { baseRect, 120f, dpiPxPerDp });
            resizeMethod.Invoke(stick, new object[] { knobRect, 52f, dpiPxPerDp });

            Assert.That(baseRect.sizeDelta.x, Is.EqualTo(120f * dpiPxPerDp / scale).Within(1e-2f));
            Assert.That(baseRect.sizeDelta.y, Is.EqualTo(120f * dpiPxPerDp / scale).Within(1e-2f));
            Assert.That(knobRect.sizeDelta.x, Is.EqualTo(52f * dpiPxPerDp / scale).Within(1e-2f));
            Assert.That(knobRect.sizeDelta.y, Is.EqualTo(52f * dpiPxPerDp / scale).Within(1e-2f));
        }
    }

    // ---- Visibility and capture tests (Rule 3) ---------------------------------------------------

    [Test]
    public void Floating_HiddenUntilTouched()
    {
        FloatingStick stick = SpawnStick(floating: true);
        var baseRect = Field<RectTransform>(stick, "_base");
        var knobRect = Field<RectTransform>(stick, "_knob");

        Assert.That(baseRect.gameObject.activeSelf, Is.False, "Base is visible before touch.");
        Assert.That(knobRect.gameObject.activeSelf, Is.False, "Knob is visible before touch.");

        var downEvent = new PointerEventData(EventSystem.current)
        {
            pointerId = 0,
            position = new Vector2(100f, 100f),
        };
        stick.OnPointerDown(downEvent);

        Assert.That(baseRect.gameObject.activeSelf, Is.True, "Base did not show on touch down.");
        Assert.That(knobRect.gameObject.activeSelf, Is.True, "Knob did not show on touch down.");

        var upEvent = new PointerEventData(EventSystem.current)
        {
            pointerId = 0,
            position = new Vector2(100f, 100f),
        };
        stick.OnPointerUp(upEvent);

        Assert.That(baseRect.gameObject.activeSelf, Is.False, "Base remained visible after touch up.");
        Assert.That(knobRect.gameObject.activeSelf, Is.False, "Knob remained visible after touch up.");
    }

    [Test]
    public void FloatingStick_SecondPointer_IsIgnored()
    {
        FloatingStick stick = SpawnStick();

        var firstDown = new PointerEventData(EventSystem.current)
        {
            pointerId = 0,
            position = new Vector2(100f, 100f),
        };
        stick.OnPointerDown(firstDown);

        int capturedPointer = Field<int>(stick, "_pointerId");
        Vector2 capturedOrigin = Field<Vector2>(stick, "_originPx");
        Assert.That(capturedPointer, Is.EqualTo(0));

        var secondDown = new PointerEventData(EventSystem.current)
        {
            pointerId = 1,
            position = new Vector2(300f, 300f),
        };
        stick.OnPointerDown(secondDown);

        Assert.That(Field<int>(stick, "_pointerId"), Is.EqualTo(0), "Second touch captured the stick.");
        Assert.That(Field<Vector2>(stick, "_originPx"), Is.EqualTo(capturedOrigin), "Second touch moved the origin.");

        // An up event from the second pointer does not release the stick.
        var secondUp = new PointerEventData(EventSystem.current)
        {
            pointerId = 1,
            position = new Vector2(300f, 300f),
        };
        stick.OnPointerUp(secondUp);

        Assert.That(Field<int>(stick, "_pointerId"), Is.EqualTo(0), "Second touch up released the first pointer.");
    }

    // ---- Fixed-stick tests (UI-02d rules 1 to 4) -------------------------------------------------

    [Test]
    public void Fixed_CentreIsRegionBottomLeftPlusOffsetInDp()
    {
        FloatingStick stick = SpawnStick();
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);

        Assert.That(Field<bool>(stick, "_floating"), Is.False, "Hud.prefab's stick is floating.");
        Assert.That(Field<Vector2>(stick, "_fixedOffsetDp"), Is.EqualTo(new Vector2(110f, 110f)));
        Assert.That(Field<Vector2>(stick, "_originPx"), Is.EqualTo(ExpectedCentre(stick)).Using(new Vector2Comparer(1e-2f)));
        Assert.That(ExpectedCentre(stick) - RegionCornerPx(stick), Is.EqualTo(new Vector2(110f, 110f) * pxPerDp).Using(new Vector2Comparer(1e-2f)));
    }

    [Test]
    public void Fixed_IsVisibleAtRest_AndAfterRelease()
    {
        FloatingStick stick = SpawnStick();
        var baseRect = Field<RectTransform>(stick, "_base");
        var knobRect = Field<RectTransform>(stick, "_knob");
        Assert.That(baseRect.gameObject.activeSelf && knobRect.gameObject.activeSelf, Is.True, "Hidden before touch.");

        stick.OnPointerDown(Pointer(0, ExpectedCentre(stick) + new Vector2(50f, 0f)));
        stick.OnPointerUp(Pointer(0, ExpectedCentre(stick)));

        Assert.That(baseRect.gameObject.activeSelf && knobRect.gameObject.activeSelf, Is.True, "Hidden after release.");
        Assert.That(Field<int>(stick, "_pointerId"), Is.EqualTo(int.MinValue), "Release did not free the stick.");
        Assert.That(knobRect.anchoredPosition, Is.EqualTo(baseRect.anchoredPosition).Using(new Vector2Comparer(1e-2f)), "Knob did not return to centre.");
    }

    [Test]
    public void Fixed_TouchAwayFromCentre_ReadsFromCentre()
    {
        FloatingStick stick = SpawnStick();
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);
        Vector2 centre = ExpectedCentre(stick);

        stick.OnPointerDown(Pointer(0, centre + new Vector2(30f * pxPerDp, 0f)));

        Assert.That(Field<Vector2>(stick, "_originPx"), Is.EqualTo(centre).Using(new Vector2Comparer(1e-2f)), "Touch moved the origin.");
        Assert.That(LastValue(stick), Is.EqualTo(StickShaper.Shape(new Vector2(30f, 0f))).Using(new Vector2Comparer(1e-3f)));
    }

    [Test]
    public void Fixed_DragPastRim_DoesNotRecentre_AndKnobClamps()
    {
        FloatingStick stick = SpawnStick();
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);
        Vector2 centre = ExpectedCentre(stick);

        stick.OnPointerDown(Pointer(0, centre));
        stick.OnDrag(Pointer(0, centre + new Vector2(100f * pxPerDp, 0f)));

        Assert.That(Field<Vector2>(stick, "_originPx"), Is.EqualTo(centre).Using(new Vector2Comparer(1e-2f)), "Origin recentred.");
        Assert.That(LastValue(stick), Is.EqualTo(new Vector2(1f, 0f)).Using(new Vector2Comparer(1e-3f)), "Not full deflection.");

        var baseRect = Field<RectTransform>(stick, "_base");
        var knobRect = Field<RectTransform>(stick, "_knob");
        float scale = stick.GetComponentInParent<Canvas>() is { scaleFactor: > 0f } c ? c.scaleFactor : 1f;
        float knobDistance = (knobRect.anchoredPosition - baseRect.anchoredPosition).magnitude * scale;
        Assert.That(knobDistance, Is.EqualTo(StickShaper.MaxRadiusDp * pxPerDp).Within(0.5f), "Knob did not clamp at the 60 dp rim.");
    }

    // ---- Region and routing tests (Rules 3, 4) ---------------------------------------------------

    [Test]
    public void FloatingStick_Region_CoversLeftFortyFivePercent()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var stick = prefab.GetComponentInChildren<FloatingStick>(includeInactive: true);
        var region = Field<RectTransform>(stick, "_region");

        Assert.That(region, Is.Not.Null, "_region is not wired on FloatingStick.");
        Assert.That(region.anchorMin, Is.EqualTo(Vector2.zero), "Region anchorMin does not start at bottom-left.");
        Assert.That(region.anchorMax.x, Is.EqualTo(0.45f).Within(1e-4f), "Region does not cover left 45 %.");
        Assert.That(region.anchorMax.y, Is.EqualTo(1f).Within(1e-4f), "Region does not span full vertical height.");
    }

    [Test]
    public void FloatingStick_ControlPath_IsVirtualGamepadLeftStick()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        var stick = prefab.GetComponentInChildren<FloatingStick>(includeInactive: true);
        string controlPath = Field<string>(stick, "_controlPath");

        Assert.That(controlPath, Is.EqualTo("<Gamepad>/leftStick"), "Control path is not <Gamepad>/leftStick.");
    }

    [Test]
    public void Floating_DragRecentres_WhenExceedingMaxRadius()
    {
        FloatingStick stick = SpawnStick(floating: true);
        float pxPerDp = StickShaper.PixelsPerDp(Screen.dpi);

        var downEvent = new PointerEventData(EventSystem.current)
        {
            pointerId = 0,
            position = new Vector2(200f, 200f),
        };
        stick.OnPointerDown(downEvent);

        // Drag 100 dp right (exceeds 60 dp MaxRadius).
        Vector2 dragPos = new Vector2(200f + (100f * pxPerDp), 200f);
        var dragEvent = new PointerEventData(EventSystem.current)
        {
            pointerId = 0,
            position = dragPos,
        };
        stick.OnDrag(dragEvent);

        Vector2 origin = Field<Vector2>(stick, "_originPx");
        float expectedOriginX = dragPos.x - (StickShaper.MaxRadiusDp * pxPerDp);
        Assert.That(origin.x, Is.EqualTo(expectedOriginX).Within(1e-2f), "Origin did not recentre behind touch.");
        Assert.That(origin.y, Is.EqualTo(200f).Within(1e-2f), "Origin drifted in Y.");
    }

    // ---- Fixtures --------------------------------------------------------------------------------

    private FloatingStick SpawnStick(bool floating = false)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath);
        Assert.That(prefab, Is.Not.Null, $"No prefab at {HudPath}.");

        GameObject instance = Object.Instantiate(prefab);
        _spawned.Add(instance);

        var stick = instance.GetComponentInChildren<FloatingStick>(includeInactive: true);
        Assert.That(stick, Is.Not.Null, "FloatingStick component not found on instantiated Hud.");

        typeof(FloatingStick).GetField("_floating", Private)!.SetValue(stick, floating);
        typeof(FloatingStick)
            .GetMethod("Awake", Private)!
            .Invoke(stick, null);

        return stick;
    }

    private static PointerEventData Pointer(int id, Vector2 position) =>
        new PointerEventData(EventSystem.current) { pointerId = id, position = position };

    private static Vector2 RegionCornerPx(FloatingStick stick)
    {
        var corners = new Vector3[4];
        Field<RectTransform>(stick, "_region").GetWorldCorners(corners);
        return RectTransformUtility.WorldToScreenPoint(null, corners[0]);
    }

    private static Vector2 ExpectedCentre(FloatingStick stick) =>
        RegionCornerPx(stick) + Field<Vector2>(stick, "_fixedOffsetDp") * StickShaper.PixelsPerDp(Screen.dpi);

    /// <summary>The last value the stick wrote to its control; EditMode has no virtual gamepad to read it from.</summary>
    private static Vector2 LastValue(FloatingStick stick) => Field<Vector2>(stick, "_reported");

    private sealed class Vector2Comparer : IEqualityComparer<Vector2>
    {
        private readonly float _tolerance;

        public Vector2Comparer(float tolerance) => _tolerance = tolerance;

        public bool Equals(Vector2 a, Vector2 b) => (a - b).magnitude <= _tolerance;

        public int GetHashCode(Vector2 v) => v.GetHashCode();
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType()
            .GetField(name, Private)!
            .GetValue(target);
}
