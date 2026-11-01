using System;
using Soulvail.Core.Ai;
using Soulvail.Core.Events;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>A fixed pool of simple chest-height chains, driven by core's stun events.</summary>
    public sealed class BindingChainView : MonoBehaviour, IDisposable
    {
        private const float TautenSeconds = 0.15f;
        private const float FadeSeconds = 0.12f;
        [SerializeField] private LineRenderer _line;
        [SerializeField] private Transform _startClasp;
        [SerializeField] private Transform _endClasp;
        private Slot[] _slots;
        private EnemyViews _enemies;
        private IDisposable _bound, _status, _died, _despawned, _boundary, _arrived, _ended;

        public int PoolSize => _slots?.Length ?? 0;
        public int ActiveCount { get; private set; }

        public void Construct(DomainEventHub hub, EnemyViews enemies)
        {
            WarmUp();
            _enemies = enemies;
            _bound = hub.Subscribe<BindingShotBound>(OnBound);
            _status = hub.Subscribe<EnemyStatusChanged>(OnStatus);
            _died = hub.Subscribe<EnemyDied>(e => Remove(e.Id));
            _despawned = hub.Subscribe<EnemyDespawned>(e => Remove(e.Id));
            _boundary = hub.Subscribe<StageTransitionStarted>(_ => Clear());
            _arrived = hub.Subscribe<StageArrived>(_ => Clear());
            _ended = hub.Subscribe<RunEnded>(_ => Clear());
        }

        public void WarmUp()
        {
            if (_slots != null)
            {
                return;
            }
            _slots = new Slot[Composition.BootInstaller.ProjectileCapacity];
            var parked = new GameObject("Chains (pooled)");
            parked.SetActive(false);
            parked.transform.SetParent(transform, false);
            for (int i = 0; i < _slots.Length; i++)
            {
                LineRenderer line = Instantiate(_line, parked.transform, false);
                Transform first = Instantiate(_startClasp, line.transform, false);
                Transform last = Instantiate(_endClasp, line.transform, false);
                _slots[i] = new Slot { Line = line, First = first, Last = last };
                line.gameObject.SetActive(false);
            }
            _line.gameObject.SetActive(false);
            _startClasp.gameObject.SetActive(false);
            _endClasp.gameObject.SetActive(false);
            parked.SetActive(true);
        }

        public LineRenderer Show(in BindingShotBound bound, Transform target, Transform anchor)
        {
            WarmUp();
            if (target == null || (bound.Anchor == BindAnchor.Enemy && anchor == null))
            {
                return null;
            }
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots[i];
                if (slot.Live)
                {
                    continue;
                }
                slot.Live = true;
                slot.TargetId = bound.TargetId;
                slot.AnchorId = bound.AnchorEnemyId;
                slot.Kind = bound.Anchor;
                slot.Target = target;
                slot.Anchor = anchor;
                slot.Point = bound.AnchorPoint.ToUnity();
                slot.Age = 0f;
                slot.Fade = 0f;
                slot.Line.loop = bound.Anchor == BindAnchor.None;
                slot.Line.positionCount = bound.Anchor == BindAnchor.None ? 12 : 3;
                slot.First.gameObject.SetActive(bound.Anchor != BindAnchor.None);
                slot.Last.gameObject.SetActive(bound.Anchor != BindAnchor.None);
                slot.Line.gameObject.SetActive(true);
                ActiveCount++;
                Draw(ref slot);
                return slot.Line;
            }
            return null;
        }

        public void Step(float dt)
        {
            if (_slots == null)
            {
                return;
            }
            float step = dt > 0f && float.IsFinite(dt) ? dt : 0f;
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots[i];
                if (!slot.Live)
                {
                    continue;
                }
                if (slot.Target == null || (slot.Kind == BindAnchor.Enemy && slot.Anchor == null))
                {
                    Release(ref slot);
                    continue;
                }
                slot.Age += step;
                if (slot.Fade > 0f)
                {
                    slot.Fade += step;
                    if (slot.Fade >= FadeSeconds)
                    {
                        Release(ref slot);
                        continue;
                    }
                }
                Draw(ref slot);
            }
        }

        private void Draw(ref Slot slot)
        {
            Vector3 from = slot.Target.position + Vector3.up * LineOfSightSense.EyeHeight;
            float taut = Mathf.Clamp01(slot.Age / TautenSeconds);
            if (slot.Kind == BindAnchor.None)
            {
                float radius = Mathf.Lerp(0.65f, 0.35f, taut);
                for (int j = 0; j < 12; j++)
                {
                    float angle = j * Mathf.PI * 2f / 12f;
                    slot.Line.SetPosition(j, from + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                }
            }
            else
            {
                Vector3 to = slot.Kind == BindAnchor.Enemy
                    ? slot.Anchor.position + Vector3.up * LineOfSightSense.EyeHeight : slot.Point;
                slot.Line.SetPosition(0, from);
                slot.Line.SetPosition(1, (from + to) * 0.5f + Vector3.down * (0.45f * (1f - taut)));
                slot.Line.SetPosition(2, to);
                slot.First.position = from;
                slot.Last.position = to;
            }
            Color cyan = Palette.Player;
            cyan.a = slot.Fade > 0f ? 1f - slot.Fade / FadeSeconds : 1f;
            slot.Line.startColor = cyan;
            slot.Line.endColor = cyan;
            slot.Line.widthMultiplier = 0.065f * cyan.a;
            // The clasps shrink with the fade without allocating per-renderer materials.
            slot.First.localScale = slot.Last.localScale = Vector3.one * (0.13f * cyan.a);
        }

        private void OnBound(BindingShotBound bound)
        {
            if (_enemies == null || !_enemies.TryGet(bound.TargetId, out EnemyView target))
            {
                return;
            }
            Transform anchor = null;
            if (bound.Anchor == BindAnchor.Enemy && _enemies.TryGet(bound.AnchorEnemyId, out EnemyView other))
            {
                anchor = other.transform;
            }
            Show(bound, target.transform, anchor);
        }

        private void OnStatus(EnemyStatusChanged status)
        {
            if (status.Status.Value != EnemyStatuses.StunnedKey || status.Seconds > 0f)
            {
                return;
            }
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots[i];
                if (slot.Live && (slot.TargetId == status.EnemyId || slot.AnchorId == status.EnemyId))
                {
                    slot.Fade = float.Epsilon;
                }
            }
        }

        private void Remove(int id)
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots[i];
                if (slot.Live && (slot.TargetId == id || slot.AnchorId == id))
                {
                    Release(ref slot);
                }
            }
        }

        public void Clear()
        {
            if (_slots == null)
            {
                return;
            }
            for (int i = 0; i < _slots.Length; i++)
            {
                Release(ref _slots[i]);
            }
        }

        private void Release(ref Slot slot)
        {
            if (!slot.Live)
            {
                return;
            }
            slot.Live = false;
            slot.Target = slot.Anchor = null;
            slot.Line.gameObject.SetActive(false);
            ActiveCount--;
        }

        public void Dispose()
        {
            _bound?.Dispose();
            _status?.Dispose();
            _died?.Dispose();
            _despawned?.Dispose();
            _boundary?.Dispose();
            _arrived?.Dispose();
            _ended?.Dispose();
            Clear();
        }

        private void OnDestroy() => Dispose();

        private struct Slot
        {
            public LineRenderer Line;
            public Transform First, Last, Target, Anchor;
            public Vector3 Point;
            public int TargetId, AnchorId;
            public BindAnchor Kind;
            public float Age, Fade;
            public bool Live;
        }
    }
}
