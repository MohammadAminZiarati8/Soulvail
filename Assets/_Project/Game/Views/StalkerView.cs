using System;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Views
{
    /// <summary>
    /// Draws core's hidden Chameleon stalker (JG-22c): blends the body to the cloak, flashes it on a
    /// strike's warning, and hides its bar while it hides. Pool deactivation clears every transient.
    /// </summary>
    /// <remarks>
    /// The cloak is a <c>sharedMaterial</c> swap, <c>EnemyHitFeedback</c>'s dissolve way: an opaque
    /// material cannot fade (Traps §9). The live material is back on whenever the cloak is at 0 and no
    /// flash is running, so a seen stalker draws exactly as before.
    /// </remarks>
    public sealed class StalkerView : MonoBehaviour
    {
        [SerializeField] private Renderer _body;
        [SerializeField] private Material _cloak;
        [Min(0.01f)]
        [SerializeField] private float _hideSeconds = 0.3f;
        [Min(0.01f)]
        [SerializeField] private float _showSeconds = 0.15f;
        [Min(0f)]
        [SerializeField] private float _flashSeconds = 0.1f;
        private readonly int _cloakId = Shader.PropertyToID("_Cloak");
        private readonly int _flashId = Shader.PropertyToID("_Flash");
        private MaterialPropertyBlock _block;
        private DomainEventHub _hub;
        private EnemyView _enemy;
        private EnemyHealthBar _bar;
        private Material _live;
        private IDisposable _hiddenSub;
        private IDisposable _telegraphSub;
        private IDisposable _damagedSub;
        private IDisposable _diedSub;
        private int _boundId = EnemyView.Unbound;
        private bool _hidden;
        private bool _dead;

        /// <summary>0 shown … 1 hidden.</summary>
        public float CloakAmount { get; private set; }

        /// <summary>Seconds left of the reveal's white flash.</summary>
        public float FlashRemaining { get; private set; }

        private int Id => _boundId != EnemyView.Unbound ? _boundId : _enemy != null ? _enemy.Id : EnemyView.Unbound;

        [Inject]
        public void Construct(DomainEventHub hub)
        {
            _block ??= new MaterialPropertyBlock();
            _enemy = GetComponent<EnemyView>();
            if (_bar == null)
            { _bar = GetComponent<EnemyHealthBar>(); }
            if (_live == null && _body != null && _body.sharedMaterial != _cloak)
            { _live = _body.sharedMaterial; }
            Unsubscribe();
            _hub = hub ?? throw new ArgumentNullException(nameof(hub));
            Subscribe();
        }

        public void Bind(int enemyId, DomainEventHub hub, EnemyHealthBar bar)
        {
            _bar = bar;
            Construct(hub);
            _boundId = enemyId;
        }

        public void Step(float dt)
        {
            if (_dead || !(dt > 0f) || !float.IsFinite(dt))
            { return; }
            if (FlashRemaining > 0f)
            {
                FlashRemaining = Mathf.Max(0f, FlashRemaining - dt);
                Apply();
                return;
            }
            float target = _hidden ? 1f : 0f;
            if (Mathf.Approximately(CloakAmount, target))
            { return; }
            float rate = 1f / (_hidden ? _hideSeconds : _showSeconds);
            CloakAmount = Mathf.MoveTowards(CloakAmount, target, rate * dt);
            Apply();
        }

        private void Update() => Step(Time.deltaTime);
        public void OnSpawn() => Subscribe();
        public void OnDespawn() { ResetVisuals(); Unsubscribe(); _boundId = EnemyView.Unbound; }
        private void OnEnable() => Subscribe();
        private void OnDisable() { ResetVisuals(); Unsubscribe(); _boundId = EnemyView.Unbound; }
        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_hub == null || _hiddenSub != null)
            { return; }
            _hiddenSub = _hub.Subscribe<EnemyHiddenChanged>(OnHiddenChanged);
            _telegraphSub = _hub.Subscribe<EnemyTelegraph>(OnTelegraph);
            _damagedSub = _hub.Subscribe<EnemyDamaged>(OnDamaged);
            _diedSub = _hub.Subscribe<EnemyDied>(OnDied);
        }

        private void Unsubscribe()
        {
            _hiddenSub?.Dispose();
            _telegraphSub?.Dispose();
            _damagedSub?.Dispose();
            _diedSub?.Dispose();
            _hiddenSub = _telegraphSub = _damagedSub = _diedSub = null;
        }

        private bool IsMine(int id) => !_dead && Id != EnemyView.Unbound && id == Id;

        private void OnHiddenChanged(EnemyHiddenChanged evt)
        {
            if (!IsMine(evt.Id))
            { return; }
            _hidden = evt.Hidden;
            if (_bar != null)
            { _bar.SetHidden(evt.Hidden); }
        }

        // Rule 4: the warning is the one moment it must be unmissable, so it snaps rather than blends.
        private void OnTelegraph(EnemyTelegraph evt)
        {
            if (!IsMine(evt.Id))
            { return; }
            Reveal();
            FlashRemaining = _flashSeconds;
            Apply();
        }

        // Rule 5: core reveals a hit stalker, but the view does not wait for that event's order.
        private void OnDamaged(EnemyDamaged evt)
        {
            if (!IsMine(evt.Id))
            { return; }
            Reveal();
            FlashRemaining = 0f;
            Apply();
        }

        // The dissolve owns the material from here; nothing below may write it again this life.
        private void OnDied(EnemyDied evt)
        {
            if (IsMine(evt.Id))
            { _dead = true; }
        }

        private void Reveal()
        {
            _hidden = false;
            CloakAmount = 0f;
            if (_bar != null)
            { _bar.SetHidden(false); }
        }

        private void Apply()
        {
            if (_body == null || _cloak == null || _live == null)
            { return; }
            bool cloaked = CloakAmount > 0f || FlashRemaining > 0f;
            Material wanted = cloaked ? _cloak : _live;
            if (_body.sharedMaterial != wanted)
            { _body.sharedMaterial = wanted; }
            if (!cloaked)
            { return; }
            _block ??= new MaterialPropertyBlock();
            _body.GetPropertyBlock(_block);
            _block.SetFloat(_cloakId, CloakAmount);
            _block.SetFloat(_flashId, FlashRemaining > 0f ? 1f : 0f);
            _body.SetPropertyBlock(_block);
        }

        private void ResetVisuals()
        {
            _dead = false;
            Reveal();
            FlashRemaining = 0f;
            if (_body != null && _live != null && _body.sharedMaterial != _live)
            { _body.sharedMaterial = _live; }
        }
    }
}
