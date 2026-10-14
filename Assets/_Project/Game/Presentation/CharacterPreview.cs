using System;
using Soulvail.Game.Authoring;
using UnityEngine;
using Object = UnityEngine.Object;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer cannot
// find the type in a file-scoped namespace, and the stage prefab's reference to this component
// would silently deserialise as null with nothing reported anywhere (M0-11, Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// A class's real 3D body, lit and idling on a pedestal, rendered to a texture a
    /// <c>RawImage</c> can show (UI-05b).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A dumb view: it reads a <see cref="CharacterLook"/> and holds no game state.</b> The page
    /// (UI-05c) owns the pointer and calls <see cref="Rotate"/>; this owns the stage, the camera and
    /// the texture.
    /// </para>
    /// <para>
    /// <b>The stage sits at y = −500 and the camera sees the default layer</b>, so nothing else in
    /// the Menu scene is in frame and no layer or <c>TagManager</c> edit is needed. The camera is
    /// disabled while no body shows, so a hidden stage costs nothing.
    /// </para>
    /// <para>
    /// <b>The body's own <see cref="MonoBehaviour"/>s are switched off.</b> The body is the prefab
    /// the run clones, and the Ranger's carries <c>RangerAnimatorView</c>, which throws in
    /// <c>Start</c> when nothing above it is a <c>PlayerView</c> and nothing injected it. Disabled
    /// right after instantiation, they never reach <c>Start</c> or <c>Update</c>; the
    /// <see cref="Animator"/> is not a behaviour in that sense and idles on its own.
    /// </para>
    /// </remarks>
    public sealed class CharacterPreview : MonoBehaviour
    {
        /// <summary>The longest side the texture is allowed, in pixels.</summary>
        public const int MaxSide = 1024;

        [SerializeField] private Camera _camera;

        [SerializeField] private Transform _anchor;

        // Only for the context menu: a body to Show from the Inspector without a page to call it.
        [SerializeField] private GameObject _debugBody;

        private float _yaw;

        /// <summary>The texture the camera renders into, or null until <see cref="Resize"/>.</summary>
        public RenderTexture Texture { get; private set; }

        /// <summary>Whether a body is standing on the pedestal.</summary>
        public bool IsShowing => Body != null;

        /// <summary>The live body instance, or null.</summary>
        public GameObject Body { get; private set; }

        /// <summary>
        /// Makes the texture <paramref name="width"/> × <paramref name="height"/> pixels, scaled down
        /// to keep the longer side at or under <see cref="MaxSide"/>. The same size twice makes
        /// nothing; a new one frees the old texture first.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">A side is not positive.</exception>
        public void Resize(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "A texture needs at least one pixel.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "A texture needs at least one pixel.");
            }

            int longSide = Mathf.Max(width, height);

            if (longSide > MaxSide)
            {
                float scale = (float)MaxSide / longSide;
                width = Mathf.Max(1, Mathf.RoundToInt(width * scale));
                height = Mathf.Max(1, Mathf.RoundToInt(height * scale));
            }

            if (Texture != null && Texture.width == width && Texture.height == height)
            {
                return;
            }

            FreeTexture();

            Texture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "CharacterPreview",
                antiAliasing = 2,
            };

            if (_camera != null)
            {
                _camera.targetTexture = Texture;
            }
        }

        /// <summary>
        /// Stands <paramref name="look"/>'s body on the anchor, replacing any body already there. A
        /// look with no body (the run's default, which the preview has none of) hides the stage.
        /// </summary>
        public void Show(CharacterLook look)
        {
            DestroyBody();

            if (look.Body == null)
            {
                SetCamera(false);
                return;
            }

            _yaw = 0f;
            Body = Instantiate(look.Body, _anchor);
            Body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);

            foreach (MonoBehaviour behaviour in Body.GetComponentsInChildren<MonoBehaviour>(true))
            {
                behaviour.enabled = false;
            }

            foreach (Animator animator in Body.GetComponentsInChildren<Animator>(true))
            {
                animator.updateMode = AnimatorUpdateMode.UnscaledTime;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            SetCamera(true);
        }

        /// <summary>Destroys the body and stops the camera.</summary>
        public void Hide()
        {
            DestroyBody();
            SetCamera(false);
        }

        /// <summary>
        /// Turns the body about the pedestal's axis, not the camera, so the lights stay put and the
        /// model catches them. The yaw wraps to 0–360. Nothing happens with no body.
        /// </summary>
        public void Rotate(float degrees)
        {
            if (Body == null)
            {
                return;
            }

            _yaw = Mathf.Repeat(_yaw + degrees, 360f);
            Body.transform.localRotation = Quaternion.Euler(0f, _yaw, 0f);
        }

        [ContextMenu("Show debug body")]
        private void ShowDebugBody()
        {
            Show(new CharacterLook(_debugBody));
        }

        private void OnDestroy()
        {
            FreeTexture();
        }

        private void SetCamera(bool on)
        {
            if (_camera != null)
            {
                _camera.enabled = on;
            }
        }

        private void DestroyBody()
        {
            if (Body == null)
            {
                Body = null;
                return;
            }

            // Destroy is deferred to the end of the frame and does nothing in EditMode; the edit-time
            // path is DestroyImmediate. Detached first so the anchor never holds two bodies at once.
            Body.transform.SetParent(null, false);

            if (Application.isPlaying)
            {
                Object.Destroy(Body);
            }
            else
            {
                Object.DestroyImmediate(Body);
            }

            Body = null;
        }

        private void FreeTexture()
        {
            if (Texture == null)
            {
                return;
            }

            if (_camera != null && _camera.targetTexture == Texture)
            {
                _camera.targetTexture = null;
            }

            Texture.Release();
            Object.DestroyImmediate(Texture);
            Texture = null;
        }
    }
}
