using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and Hud.prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11).
namespace Soulvail.Game.Controls
{
    /// <summary>
    /// Pins its own <see cref="RectTransform"/> to <see cref="Screen.safeArea"/>, so nothing
    /// beneath it can land under a notch, a punch-hole or a gesture bar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity ships no such component, and the HUD needs one before it can be trusted on a modern
    /// phone: in landscape the cutout eats into the *left* edge on one rotation, which is exactly
    /// where the virtual joystick region lives.
    /// </para>
    /// <para>
    /// It polls rather than subscribing because there is no safe-area-changed event. The check is
    /// three struct comparisons per frame against cached values and it does nothing at all until
    /// one moves, which is cheaper than the alternative of re-applying blindly and dirtying the
    /// layout every frame.
    /// </para>
    /// <para>
    /// Sizing is done with anchors, not offsets, so it survives a canvas whose scale factor
    /// changes underneath it — the safe area is expressed as a fraction of the screen and the
    /// canvas resolves that to whatever units it is currently working in.
    /// </para>
    /// </remarks>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rect;
        private Rect _appliedArea;
        private int _appliedWidth;
        private int _appliedHeight;

        private void Awake()
        {
            _rect = (RectTransform)transform;
        }

        private void OnEnable()
        {
            // Forces the next Apply to do work even if the screen has not moved since the last
            // time this component was enabled — the RectTransform may have been reset meanwhile.
            _appliedWidth = 0;
            _appliedHeight = 0;
            Apply();
        }

        private void Update()
        {
            Apply();
        }

        private void Apply()
        {
            Rect area = Screen.safeArea;
            int width = Screen.width;
            int height = Screen.height;

            if (area == _appliedArea && width == _appliedWidth && height == _appliedHeight)
            {
                return;
            }

            // A zero-sized screen is reported on the odd frame during a resolution change and
            // while the app is backgrounded. Dividing by it would write NaN anchors, and a NaN
            // RectTransform never recovers on its own.
            if (width <= 0 || height <= 0)
            {
                return;
            }

            _appliedArea = area;
            _appliedWidth = width;
            _appliedHeight = height;

            Vector2 min = new Vector2(area.xMin / width, area.yMin / height);
            Vector2 max = new Vector2(area.xMax / width, area.yMax / height);

            _rect.anchorMin = min;
            _rect.anchorMax = max;
            _rect.offsetMin = Vector2.zero;
            _rect.offsetMax = Vector2.zero;
        }
    }
}
