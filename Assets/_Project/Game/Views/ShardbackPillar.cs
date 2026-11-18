using System;
using UnityEngine;

namespace Soulvail.Game.Views
{
    /// <summary>Authored identity only; lifetime and break decisions belong to core.</summary>
    public sealed class ShardbackPillar : MonoBehaviour
    {
        [SerializeField, Range(0, 2)] private int _slot;

        public int Slot => _slot;
        public int BossId { get; private set; }
        public Guid Lifetime { get; private set; }

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
    }
}
