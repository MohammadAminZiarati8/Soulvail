using System;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>Executes a core break decision on one authored pillar.</summary>
    public sealed class ShardbackPillar : MonoBehaviour
    {
        [SerializeField, Range(0, 2)] private int _slot;
        private (Collider Component, bool Enabled)[] _colliders;
        private (Renderer Component, bool Enabled)[] _renderers;

        public int Slot => _slot;
        public int BossId { get; private set; }
        public Guid Lifetime { get; private set; }
        public bool IsBroken { get; private set; }

        internal void Capture(Transform coverRoot, int coverLayer)
        {
            if (GetComponent<ShardbackPillar>() != this ||
                (transform != coverRoot && transform.parent.GetComponentInParent<ShardbackPillar>(true) != null))
            {
                throw new InvalidOperationException("Shardback pillar markers must have separate hierarchies.");
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            _colliders = new (Collider, bool)[colliders.Length];
            bool hasBlocker = false;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                _colliders[i] = (collider, collider.enabled);
                if (collider.enabled && !collider.isTrigger && collider.gameObject.layer == coverLayer)
                {
                    Transform child = collider.transform;
                    while (child != coverRoot && child.gameObject.activeSelf)
                    {
                        child = child.parent;
                    }

                    hasBlocker |= child == coverRoot;
                }
            }

            if (!hasBlocker)
            {
                throw new InvalidOperationException($"Shardback pillar slot {_slot} needs an enabled Cover blocker.");
            }

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            _renderers = new (Renderer, bool)[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                _renderers[i] = (renderers[i], renderers[i].enabled);
            }
        }

        public void Bind(int bossId, Guid lifetime)
        {
            if (bossId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bossId));
            }
            if (lifetime == Guid.Empty)
            {
                throw new ArgumentException("A lifetime is required.", nameof(lifetime));
            }
            if (_slot < 0 || _slot > 2)
            {
                throw new InvalidOperationException("Author a pillar slot from 0 to 2.");
            }
            BossId = bossId;
            Lifetime = lifetime;
        }

        internal void Unbind()
        {
            BossId = 0;
            Lifetime = Guid.Empty;
        }

        public void Break() => SetBroken(true);

        public void Restore() => SetBroken(false);

        private void SetBroken(bool broken)
        {
            foreach (var (component, enabled) in _colliders)
            {
                if (component != null)
                {
                    component.enabled = !broken && enabled;
                }
            }

            foreach (var (component, enabled) in _renderers)
            {
                if (component != null)
                {
                    component.enabled = !broken && enabled;
                }
            }

            IsBroken = broken;
        }
    }
}
