using System;
using System.Numerics;
using Soulvail.Core.Content;

namespace Soulvail.Core.Combat;

/// <summary>A refreshed speed burst and a fixed window of recently travelled ground.</summary>
public sealed class HitAndRun
{
    public const float BurstSeconds = 1.5f;
    public const float WindowSeconds = 2f;
    public const float MaxMetres = 8f;
    public const float SnapMetres = 3f;
    public const string StatusKey = "status.hit-and-run";

    private const int BucketsPerSecond = 10;
    private const int BucketCount = 20;

    private readonly (long Slice, float LastMove, float Metres)[] _distance = new (long, float, float)[BucketCount];
    private readonly ContentId _statusId = new(StatusKey);
    private readonly Stat _moveSpeed;
    private StatusBoard _status;
    private Vector3 _previousPosition;
    private bool _hasPosition;
    private float _burstUntil = float.NegativeInfinity;
    private float _appliedBonus;

    public HitAndRun(Stat moveSpeed = null)
    {
        _moveSpeed = moveSpeed;
        _moveSpeed?.ReserveModifierSlot(this);
    }

    public Stat HitSpeedBurst { get; } = new(0f);

    public Stat MomentumPerMetre { get; } = new(0f);

    public void AttachStatus(StatusBoard status) => _status = status;

    internal void TrackPosition(Vector3 position, float now)
    {
        float metres = 0f;
        if (_hasPosition)
        {
            Vector3 delta = position - _previousPosition;
            metres = MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        }

        _previousPosition = position;
        _hasPosition = true;
        OnMoved(metres, now);
    }

    public void OnMoved(float metres, float now)
    {
        SetSpeedBonus(SpeedBonus(now));
        if (_burstUntil != float.NegativeInfinity && now >= _burstUntil)
        {
            _burstUntil = float.NegativeInfinity;
            _status?.Clear(_statusId);
        }

        if (!(metres > 0f && metres <= SnapMetres))
        {
            return;
        }

        long slice = (long)Math.Floor((double)now * BucketsPerSecond);
        int index = (int)(slice % BucketCount);
        if (index < 0)
        {
            index += BucketCount;
        }

        ref var bucket = ref _distance[index];
        float previous = bucket.Slice == slice ? bucket.Metres : 0f;
        bucket = (slice, now, MathF.Min(previous + metres, MaxMetres));
    }

    public void OnHit(float now)
    {
        float bonus = HitSpeedBurst.Value;
        if (!(bonus > 0f) || float.IsInfinity(bonus))
        {
            return;
        }

        _burstUntil = now + BurstSeconds;
        SetSpeedBonus(bonus);
        _status?.Post(_statusId, isBuff: true, BurstSeconds, stacks: 1);
    }

    public float SpeedBonus(float now)
    {
        float bonus = HitSpeedBurst.Value;
        return now < _burstUntil && bonus > 0f && !float.IsInfinity(bonus) ? bonus : 0f;
    }

    public float DamageMultiplier(float now)
    {
        float perMetre = MomentumPerMetre.Value;
        if (!(perMetre > 0f) || float.IsInfinity(perMetre))
        {
            return 1f;
        }

        float metres = 0f;
        foreach (var bucket in _distance)
        {
            if (now < bucket.LastMove + WindowSeconds)
            {
                metres += bucket.Metres;
            }
        }

        return 1f + MathF.Min(metres, MaxMetres) * perMetre;
    }

    public void Reset()
    {
        _burstUntil = float.NegativeInfinity;
        _hasPosition = false;
        Array.Clear(_distance, 0, _distance.Length);
        SetSpeedBonus(0f);
        _status?.Clear(_statusId);
    }

    private void SetSpeedBonus(float bonus)
    {
        if (bonus == _appliedBonus)
        {
            return;
        }

        if (_appliedBonus > 0f)
        {
            _moveSpeed?.RemoveAll(this);
        }

        _appliedBonus = bonus;
        if (bonus > 0f)
        {
            _moveSpeed?.Add(new Modifier(ModifierKind.PercentMult, bonus, this));
        }
    }
}
