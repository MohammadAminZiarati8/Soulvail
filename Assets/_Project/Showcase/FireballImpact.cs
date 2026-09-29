using UnityEngine;

namespace Soulvail.Showcase
{
    /// <summary>
    /// Drives the three layers of the fireball's impact and then deletes itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The layers exist for different reasons. The <em>flash</em> is the pop — it lives about a
    /// tenth of a second and is mostly gone before anyone parses it. The <em>ring</em> is the one
    /// that reads from a top-down camera, where a spherical burst is a dot and a ring on the ground
    /// plane is unmistakably "it landed here". The <em>shards</em> are mesh particles rather than
    /// billboards, which is what keeps the effect in the same art language as the characters.
    /// </para>
    /// <para>
    /// Alpha rides on a <see cref="MaterialPropertyBlock"/>, so two impacts overlapping never fight
    /// over one shared material. That costs SRP batching for these renderers, which is the right
    /// trade for a handful of quads that live half a second.
    /// </para>
    /// </remarks>
    public class FireballImpact : MonoBehaviour
    {
        private static readonly int AlphaProperty = Shader.PropertyToID("_Alpha");

        [SerializeField] private Transform _flash;
        [SerializeField] private Renderer _flashRenderer;
        [SerializeField] private Transform _ring;
        [SerializeField] private Renderer _ringRenderer;
        [SerializeField] private Light _light;

        [Header("Timing")]
        [SerializeField] private float _flashDuration = 0.1f;
        [SerializeField] private float _ringDuration = 0.28f;
        [SerializeField] private float _lightDuration = 0.15f;
        [SerializeField] private float _lifetime = 0.7f;

        [Header("Shape")]
        [SerializeField] private float _flashStartScale = 0.3f;
        [SerializeField] private float _flashEndScale = 1.6f;
        [SerializeField] private float _ringStartScale = 0.2f;
        [SerializeField] private float _ringEndScale = 2.4f;
        [SerializeField] private float _lightIntensity = 8f;

        private MaterialPropertyBlock _block;
        private float _age;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            _age += Time.deltaTime;

            DriveFlash();
            DriveRing();
            DriveLight();

            if (_age >= _lifetime)
            {
                Destroy(gameObject);
            }
        }

        private void DriveFlash()
        {
            if (_flash == null)
            {
                return;
            }

            float t = Mathf.Clamp01(_age / _flashDuration);
            _flash.localScale = Vector3.one * Mathf.Lerp(_flashStartScale, _flashEndScale, t);
            SetAlpha(_flashRenderer, 1f - t);

            if (t >= 1f && _flash.gameObject.activeSelf)
            {
                _flash.gameObject.SetActive(false);
            }
        }

        private void DriveRing()
        {
            if (_ring == null)
            {
                return;
            }

            float t = Mathf.Clamp01(_age / _ringDuration);

            // Ease out: a shockwave is fastest at the moment it is born.
            float eased = 1f - ((1f - t) * (1f - t));
            _ring.localScale = Vector3.one * Mathf.Lerp(_ringStartScale, _ringEndScale, eased);
            SetAlpha(_ringRenderer, 1f - t);

            if (t >= 1f && _ring.gameObject.activeSelf)
            {
                _ring.gameObject.SetActive(false);
            }
        }

        private void DriveLight()
        {
            if (_light == null)
            {
                return;
            }

            float t = Mathf.Clamp01(_age / _lightDuration);
            _light.intensity = Mathf.Lerp(_lightIntensity, 0f, t);

            if (t >= 1f && _light.enabled)
            {
                _light.enabled = false;
            }
        }

        private void SetAlpha(Renderer target, float alpha)
        {
            if (target == null)
            {
                return;
            }

            target.GetPropertyBlock(_block);
            _block.SetFloat(AlphaProperty, alpha);
            target.SetPropertyBlock(_block);
        }
    }
}
