using System;
using Soulvail.Game.Pooling;
using Soulvail.Game.Presentation;
using UnityEngine;

// Block namespace, deliberately — see the note in BootScope.cs. FX_ThornWall.prefab's reference to
// this component would otherwise deserialise as null with nothing reported (Traps §5).
namespace Soulvail.Game.Views
{
    /// <summary>
    /// One thorn wall: the warning strip, the bramble rising out of the floor, the collider that
    /// stops the player, and the wither. <c>ThornSystem</c> decides every moment; this draws them
    /// and owns the one thing core cannot — the collider (JG-20e, AR §18.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The collider is layer <c>Cover</c>, excluding <c>Enemy</c>, both authored on the
    /// prefab</b> (rule 4): the player's <c>CharacterController</c> — and so the roll and
    /// <c>ChargeMotion</c> — stops at it, enemies walk through, and <c>LineOfSightSense</c> rays
    /// see it as cover. Nothing touches the NavMesh.
    /// </para>
    /// <para>
    /// <b>Its clocks are cosmetic</b> and run on <see cref="Step"/>, which <c>Update</c> drives
    /// with <c>Time.deltaTime</c> (AR §18.2's exception). The one gameplay question answered here —
    /// when the collider turns on — waits for the player rather than for a clock (rule 3).
    /// </para>
    /// </remarks>
    public sealed class ThornWallView : MonoBehaviour, IPoolable
    {
        /// <summary>The id of a view standing in for nothing. <c>ThornSystem</c> issues ids from 1.</summary>
        public const int Unbound = 0;

        /// <summary>How long the bramble takes to rise (rule 3).</summary>
        public const float RiseSeconds = 0.15f;

        /// <summary>How long the bramble takes to sink once withered (rule 5).</summary>
        public const float WitherSeconds = 0.3f;

        /// <summary>The wall's height, in metres — above the Ranger's head, so it reads as a wall.</summary>
        public const float Height = 1.6f;

        /// <summary>How opaque the warning strip is drawn at full fill.</summary>
        public const float StripAlpha = 0.55f;

        [Tooltip("The flat warning quad, lying on the floor. Scaled along its length as the arm runs.")]
        [SerializeField] private Transform _strip;

        [Tooltip("The strip's renderer; tinted Palette.Danger through a property block.")]
        [SerializeField] private Renderer _stripRenderer;

        [Tooltip("The bramble body. A unit mesh scaled to the wall and slid up out of the floor.")]
        [SerializeField] private Transform _bramble;

        [Tooltip("The bramble's renderer, hidden while it is below the floor.")]
        [SerializeField] private Renderer _brambleRenderer;

        [Tooltip("The blocking box: layer Cover, Exclude Layers = Enemy (rule 4). Off until risen.")]
        [SerializeField] private BoxCollider _collider;

        private readonly int _baseColorId = Shader.PropertyToID("_BaseColor");

        private MaterialPropertyBlock _block;
        private Action<ThornWallView> _finished;
        private CharacterController _player;

        private Phase _phase;
        private int _id = Unbound;
        private float _length;
        private float _halfWidth;
        private float _armSeconds;
        private float _armElapsed;
        private float _rise;
        private bool _solidPending;

        private enum Phase
        {
            Idle,
            Arming,
            Rising,
            Withering,
        }

        /// <summary>The core-side id this wall stands for, or <see cref="Unbound"/>.</summary>
        public int Id => _id;

        /// <summary>Whether the collider is on and the player is stopped by it.</summary>
        public bool IsSolid => _collider != null && _collider.enabled;

        /// <summary>How far the warning strip has filled, 0 to 1.</summary>
        public float StripFill => _armSeconds > 0f ? Mathf.Clamp01(_armElapsed / _armSeconds) : 1f;

        /// <summary>How far the bramble stands out of the floor, 0 to 1.</summary>
        public float RiseFraction => _rise;

        /// <summary>Whether the warning strip is showing.</summary>
        public bool IsStripShown => _strip != null && _strip.gameObject.activeSelf;

        /// <summary>The strip's tint. <see cref="Palette.Danger"/> — a wall is a threat.</summary>
        public Color StripColour => new Color(Palette.Danger.r, Palette.Danger.g, Palette.Danger.b, StripAlpha);

        /// <summary>The blocking box. Exposed for the fixture.</summary>
        public BoxCollider Collider => _collider;

        /// <summary>
        /// Called once by <see cref="ThornWallViews"/> when the wall has sunk and wants returning.
        /// </summary>
        public void SetFinishedHandler(Action<ThornWallView> finished) => _finished = finished;

        /// <summary>
        /// Puts the wall into service as <paramref name="id"/>: placed on the event's rectangle,
        /// the strip starting to fill, no collider (rule 2).
        /// </summary>
        /// <param name="centre">The rectangle's centre on the floor.</param>
        /// <param name="rotation">Facing down the wall's length.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not positive.</exception>
        public void Arm(int id, Vector3 centre, Quaternion rotation, float length, float halfWidth, float armSeconds)
        {
            if (id <= Unbound)
            {
                throw new ArgumentOutOfRangeException(nameof(id), id, "ThornSystem issues ids from 1.");
            }

            _id = id;
            _length = length > 0f ? length : 0f;
            _halfWidth = halfWidth > 0f ? halfWidth : 0f;
            _armSeconds = armSeconds > 0f ? armSeconds : 0f;
            _armElapsed = 0f;
            _rise = 0f;
            _solidPending = false;
            _player = null;
            _phase = Phase.Arming;

            transform.SetPositionAndRotation(centre, rotation);

            if (_collider != null)
            {
                _collider.enabled = false;
                _collider.size = new Vector3(_halfWidth * 2f, Height, _length);
                _collider.center = new Vector3(0f, Height * 0.5f, 0f);
            }

            if (_strip != null)
            {
                _strip.gameObject.SetActive(true);
            }

            TintStrip();
            Draw();
        }

        /// <summary>
        /// The arm is over: the bramble rises, and the collider turns on as soon as
        /// <paramref name="player"/> is clear of the box — at once if it already is (rule 3).
        /// </summary>
        /// <param name="player">The player's controller, or null when there is none to wait for.</param>
        public void Rise(CharacterController player)
        {
            if (_phase != Phase.Arming)
            {
                return;
            }

            _player = player;
            _armElapsed = _armSeconds;
            _phase = Phase.Rising;
            _solidPending = true;

            if (_strip != null)
            {
                _strip.gameObject.SetActive(false);
            }

            TrySolidify();
            Draw();
        }

        /// <summary>The wall has withered: collider off at once, the bramble sinks (rule 5).</summary>
        public void Wither()
        {
            if (_phase == Phase.Idle || _phase == Phase.Withering)
            {
                return;
            }

            _phase = Phase.Withering;
            _solidPending = false;

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            if (_strip != null)
            {
                _strip.gameObject.SetActive(false);
            }

            if (_rise <= 0f)
            {
                Finish();
            }
        }

        /// <summary>Advances the cosmetic clocks and waits for the player to clear. Allocates nothing.</summary>
        public void Step(float dt)
        {
            if (!(dt > 0f))
            {
                dt = 0f;
            }

            switch (_phase)
            {
                case Phase.Arming:
                    _armElapsed = Mathf.Min(_armElapsed + dt, _armSeconds);
                    break;
                case Phase.Rising:
                    _rise = Mathf.Min(_rise + (dt / RiseSeconds), 1f);
                    TrySolidify();
                    break;
                case Phase.Withering:
                    _rise = Mathf.Max(_rise - (dt / WitherSeconds), 0f);
                    if (_rise <= 0f)
                    {
                        Draw();
                        Finish();
                        return;
                    }

                    break;
                default:
                    return;
            }

            Draw();
        }

        /// <inheritdoc />
        public void OnSpawn()
        {
        }

        /// <inheritdoc />
        public void OnDespawn()
        {
            _id = Unbound;
            _phase = Phase.Idle;
            _player = null;
            _solidPending = false;
            _armElapsed = 0f;
            _rise = 0f;

            if (_collider != null)
            {
                _collider.enabled = false;
            }

            if (_strip != null)
            {
                _strip.gameObject.SetActive(false);
            }

            Draw();
        }

        /// <summary>
        /// Whether a capsule at <paramref name="centre"/> with <paramref name="radius"/> overlaps
        /// this wall's footprint. Geometric rather than a physics query, so it needs no sync and
        /// answers the same in EditMode.
        /// </summary>
        public bool Overlaps(Vector3 centre, float radius)
        {
            Vector3 local = transform.InverseTransformPoint(centre);

            return Mathf.Abs(local.x) < _halfWidth + radius && Mathf.Abs(local.z) < (_length * 0.5f) + radius;
        }

        private void Update()
        {
            Step(Time.deltaTime);
        }

        private void TrySolidify()
        {
            if (!_solidPending || PlayerInside())
            {
                return;
            }

            _solidPending = false;

            if (_collider != null)
            {
                _collider.enabled = true;
            }
        }

        private bool PlayerInside()
        {
            // Unity's ==: a destroyed player is no one to wait for.
            if (_player == null)
            {
                return false;
            }

            Transform body = _player.transform;
            Vector3 scale = body.lossyScale;
            float radius = (_player.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))) + _player.skinWidth;

            return Overlaps(body.TransformPoint(_player.center), radius);
        }

        private void Finish()
        {
            _phase = Phase.Idle;
            _finished?.Invoke(this);
        }

        private void Draw()
        {
            float width = _halfWidth * 2f;

            if (_strip != null)
            {
                float fill = StripFill * _length;
                _strip.localPosition = new Vector3(0f, 0.02f, (-_length * 0.5f) + (fill * 0.5f));
                _strip.localScale = new Vector3(width, fill, 1f);
            }

            if (_bramble != null)
            {
                _bramble.localPosition = new Vector3(0f, (Height * 0.5f) - (Height * (1f - _rise)), 0f);
                _bramble.localScale = new Vector3(width, Height, _length);
            }

            if (_brambleRenderer != null)
            {
                _brambleRenderer.enabled = _rise > 0f;
            }
        }

        private void TintStrip()
        {
            if (_stripRenderer == null)
            {
                return;
            }

            _block ??= new MaterialPropertyBlock();

            _stripRenderer.GetPropertyBlock(_block);
            _block.SetColor(_baseColorId, StripColour);
            _stripRenderer.SetPropertyBlock(_block);
        }
    }
}
