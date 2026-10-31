using System;
using Soulvail.Core.Combat;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Game.Adapters;
using Soulvail.Game.Presentation;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>
    /// Pooled Hunter's Snare props, driven by core's trap events. Cosmetic only: core decides
    /// arming, springing and expiry; this view times its own looks on core's clock.
    /// </summary>
    public sealed class TrapViews : MonoBehaviour, IDisposable
    {
        public const float ArmSeconds = 0.3f;
        public const float SnapSeconds = 0.15f;
        public const float FadeSeconds = 0.3f;
        public const float LoopRadius = 0.35f;
        private const float DropSeconds = 0.12f;
        private const float DropHeight = 0.25f;
        private const float PulseSeconds = 1.6f;
        private const float SinkDepth = 0.2f;
        private const float ClosedRadius = 0.12f;
        private const float SnapLift = 0.25f;
        private const int LoopPoints = 16;

        [SerializeField] private Transform _snare;
        [SerializeField] private ParticleSystem _dust;

        private readonly Color _rope = new Color(0.86f, 0.93f, 0.95f, 1f);

        private Slot[] _slots;
        private EnemyViews _enemies;
        private IRunSession _session;
        private float _clock;
        private IDisposable _placed, _sprung, _expired, _boundary, _arrived, _ended;

        public int PoolSize => _slots?.Length ?? 0;
        public int ActiveCount { get; private set; }

        public void Construct(DomainEventHub hub, IRunSession session, EnemyViews enemies)
        {
            if (hub is null)
            {
                throw new ArgumentNullException(nameof(hub));
            }
            WarmUp();
            _session = session;
            _enemies = enemies;
            _clock = session?.State?.Time ?? 0f;
            _placed = hub.Subscribe<TrapPlaced>(e => Place(e.Id, e.At.ToUnity()));
            _sprung = hub.Subscribe<TrapSprung>(OnSprung);
            _expired = hub.Subscribe<TrapExpired>(e => Expire(e.Id));
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
            // A third snare fades its oldest while the new one drops, so twice core's capacity.
            _slots = new Slot[TrapSystem.Capacity * 2];
            var parked = new GameObject("Snares (pooled)");
            parked.SetActive(false);
            parked.transform.SetParent(transform, false);
            for (int i = 0; i < _slots.Length; i++)
            {
                Transform root = Instantiate(_snare, parked.transform, false);
                _slots[i] = new Slot
                {
                    Root = root,
                    Rope = root.Find("Rope Loop").GetComponent<LineRenderer>(),
                    Ring = root.Find("Cyan Ring").GetComponent<LineRenderer>(),
                };
                _slots[i].Rope.positionCount = LoopPoints;
                _slots[i].Ring.positionCount = LoopPoints;
                root.gameObject.SetActive(false);
            }
            _snare.gameObject.SetActive(false);
            parked.SetActive(true);
        }

        public Transform Place(int id, Vector3 at)
        {
            WarmUp();
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots[i];
                if (slot.Live)
                {
                    continue;
                }
                slot.Live = true;
                slot.Id = id;
                slot.At = at;
                slot.Age = 0f;
                slot.Snap = 0f;
                slot.Fade = 0f;
                slot.Target = null;
                slot.SnapTo = at;
                slot.Root.gameObject.SetActive(true);
                ActiveCount++;
                Draw(ref slot);
                if (_dust != null)
                {
                    _dust.transform.position = at + Vector3.up * 0.05f;
                    _dust.Emit(8);
                }
                return slot.Root;
            }
            return null;
        }

        public void Spring(int id, Transform target)
        {
            int index = IndexOf(id);
            if (index < 0)
            {
                return;
            }
            ref Slot slot = ref _slots[index];
            slot.Snap = float.Epsilon;
            slot.Fade = 0f;
            slot.Target = target;
            slot.SnapTo = target != null ? target.position : slot.At;
            Draw(ref slot);
        }

        public void Expire(int id)
        {
            int index = IndexOf(id);
            if (index < 0 || _slots[index].Snap > 0f)
            {
                return;
            }
            _slots[index].Fade = float.Epsilon;
        }

        public void Step(float dt)
        {
            if (_slots == null)
            {
                return;
            }
            float step = dt > 0f && float.IsFinite(dt) ? dt : 0f;
            if (_dust != null && step > 0f)
            {
                _dust.Simulate(step, true, false, false);
            }
            for (int i = 0; i < _slots.Length; i++)
            {
                ref Slot slot = ref _slots[i];
                if (!slot.Live)
                {
                    continue;
                }
                slot.Age += step;
                if (slot.Snap > 0f)
                {
                    slot.Snap += step;
                    if (slot.Snap >= SnapSeconds)
                    {
                        // MK-06b's root coils carry the rest.
                        Release(ref slot);
                        continue;
                    }
                }
                else if (slot.Fade > 0f)
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

        public bool IsArmed(int id)
        {
            int index = IndexOf(id);
            return index >= 0 && _slots[index].Ring.enabled;
        }

        public float RingAlpha(int id)
        {
            int index = IndexOf(id);
            return index >= 0 && _slots[index].Ring.enabled ? _slots[index].Ring.startColor.a : 0f;
        }

        public float RopeRadius(int id)
        {
            int index = IndexOf(id);
            return index >= 0 ? _slots[index].Radius : 0f;
        }

        public float Opacity(int id)
        {
            int index = IndexOf(id);
            return index >= 0 ? _slots[index].Rope.startColor.a : 0f;
        }

        public Transform Find(int id)
        {
            int index = IndexOf(id);
            return index >= 0 ? _slots[index].Root : null;
        }

        private void Draw(ref Slot slot)
        {
            float drop = Mathf.Clamp01(slot.Age / DropSeconds);
            float fade = slot.Fade > 0f ? Mathf.Clamp01(slot.Fade / FadeSeconds) : 0f;
            float snap = slot.Snap > 0f ? Mathf.Clamp01(slot.Snap / SnapSeconds) : 0f;
            slot.Root.position = slot.At + Vector3.up * (DropHeight * (1f - drop) * (1f - drop) - SinkDepth * fade);

            // The loop jerks up and cinches toward the victim's feet; the stakes stay put.
            Vector3 feet = slot.Target != null ? slot.Target.position : slot.SnapTo;
            Vector3 centre = Vector3.Lerp(Vector3.zero, feet - slot.At, snap);
            centre.y = SnapLift * Mathf.Sin(snap * Mathf.PI * 0.5f) + 0.03f;
            slot.Radius = Mathf.Lerp(LoopRadius, ClosedRadius, snap);
            for (int j = 0; j < LoopPoints; j++)
            {
                float angle = j * Mathf.PI * 2f / LoopPoints;
                var spoke = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                slot.Rope.SetPosition(j, centre + spoke * slot.Radius);
                slot.Ring.SetPosition(j, new Vector3(0f, 0.02f, 0f) + spoke * (LoopRadius + 0.06f));
            }
            Color rope = _rope;
            rope.a = 1f - fade;
            slot.Rope.startColor = slot.Rope.endColor = rope;

            bool armed = slot.Age >= ArmSeconds && slot.Snap <= 0f;
            slot.Ring.enabled = armed;
            if (armed)
            {
                float wave = 0.5f + 0.5f * Mathf.Sin((slot.Age - ArmSeconds) * Mathf.PI * 2f / PulseSeconds);
                Color cyan = Palette.Player;
                cyan.a = (0.18f + 0.17f * wave) * (1f - fade);
                slot.Ring.startColor = slot.Ring.endColor = cyan;
            }
        }

        private void OnSprung(TrapSprung sprung)
        {
            Transform target = null;
            if (_enemies != null && _enemies.TryGet(sprung.EnemyId, out EnemyView enemy))
            {
                target = enemy.transform;
            }
            Spring(sprung.Id, target);
        }

        private int IndexOf(int id)
        {
            if (_slots == null)
            {
                return -1;
            }
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Live && _slots[i].Id == id)
                {
                    return i;
                }
            }
            return -1;
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
            if (_dust != null)
            {
                _dust.Clear(true);
            }
        }

        private void Release(ref Slot slot)
        {
            if (!slot.Live)
            {
                return;
            }
            slot.Live = false;
            slot.Target = null;
            slot.Root.gameObject.SetActive(false);
            ActiveCount--;
        }

        private void LateUpdate()
        {
            // Core's clock, so pauses and clamped frames agree with when traps arm and expire.
            if (_session?.State == null)
            {
                return;
            }
            float now = _session.State.Time;
            Step(now - _clock);
            _clock = now;
        }

        public void Dispose()
        {
            _placed?.Dispose();
            _sprung?.Dispose();
            _expired?.Dispose();
            _boundary?.Dispose();
            _arrived?.Dispose();
            _ended?.Dispose();
            _placed = _sprung = _expired = _boundary = _arrived = _ended = null;
            Clear();
        }

        private void OnDestroy() => Dispose();

        private struct Slot
        {
            public Transform Root, Target;
            public LineRenderer Rope, Ring;
            public Vector3 At, SnapTo;
            public int Id;
            public float Age, Snap, Fade, Radius;
            public bool Live;
        }
    }
}
