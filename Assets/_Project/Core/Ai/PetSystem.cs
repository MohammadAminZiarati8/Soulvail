using System;
using System.Numerics;
using Soulvail.Core.Combat;
using Soulvail.Core.Content;
using Soulvail.Core.Effects;
using Soulvail.Core.Events;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;

namespace Soulvail.Core.Ai;

public sealed class PetSystem
{
    public const int Capacity = 4;
    public const int BoarSlot = 3;
    public const int MaxHunters = 2;
    public const float HuntMemory = 2f;
    public const float RetargetInterval = 0.5f;
    public const float AtHeelRadius = 2f;
    public const float RoarRadius = 3f;
    public const float RoarSlowSeconds = 2f;

    private const float SwapImmunityTrail = 0.2f;
    private const float SwapRescueHeadStart = 0.5f;

    private readonly PetSpec _spec;
    private readonly PetSpit _spit;
    private readonly PetAgent[] _live = new PetAgent[Capacity];
    private int _liveCount;
    private bool _boarActive;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly long[] _biteCounts = new long[Capacity];
    private readonly (float Bonus, float Until)[] _frenzyBuffs = new (float, float)[Capacity];
    private readonly PetAgent[] _pets = new PetAgent[Capacity];
    private readonly int[,] _hunters = new int[Capacity, MaxHunters];
    private readonly PackFormation _formation = new();
    private EnemyRegistry _enemies;
    private float _now;
    private float _leapUntil = float.NegativeInfinity;
    private Vector3 _swapLanding;
    private Vector3 _playerPosition;
    private Vector3 _pounceLanding;
    private Pounce _pounce;
    private int _pounceTargetId;
    private float _swapImmuneUntil = float.NegativeInfinity;
    private Vector3 _spawnFacing;
    private bool _spawnAnnounced;
    private int _count;
    private int _nextId = 1;
    private int _lastThrowTargetId;

    public PetSystem(PetSpec spec, IDomainEvents events, IIntentSink intents)
        : this(spec, null, events, intents)
    {
    }

    public PetSystem(PetSpec hound, PetSpec boar, IDomainEvents events, IIntentSink intents)
    {
        _spec = hound;
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        _spit = new PetSpit(events);
        if (hound is not null)
        {
            for (int index = 0; index < BoarSlot; index++)
            {
                _pets[index] = new PetAgent(hound, index == 0 ? PetKind.Hound : PetKind.PackHound);
            }

            PetAgent lead = _pets[0];
            lead.Stats.PackHounds.Changed += OnPackHoundsChanged;
            lead.Stats.PackShare.Changed += OnPackShareChanged;
            lead.MaxHp.Changed += OnPackShareChanged;
            lead.ContactDamage.Changed += OnPackShareChanged;
            if (boar is not null)
            {
                _pets[BoarSlot] = new PetAgent(boar, PetKind.Boar);
                lead.Stats.BoarCount.Changed += OnBoarCountChanged;
            }
        }
    }

    public ReadOnlySpan<PetAgent> Pets => _live.AsSpan(0, _liveCount);
    public PetSpit Spit => _spit;
    public IStatBlock LeadStats => _count == 0 ? null : _pets[0].Stats;
    public Vector2 FormationFacing => _formation.Facing;

    public void SpawnBeside(Vector3 player, float now) => SpawnBeside(player, Vector3.UnitZ, now);

    internal void SpawnBeside(Vector3 player, Vector3 facing, float now)
    {
        PlaceBeside(player, facing, now);
        AnnounceSpawn();
    }

    // The opening pet must exist for restored stats before any run events are published.
    internal void PlaceBeside(Vector3 player, Vector3 facing, float now)
    {
        if (_spec is null)
        {
            return;
        }

        Clear();
        _playerPosition = player;
        _spawnFacing = facing;
        _formation.Reset(new Vector2(facing.X, facing.Z));
        _now = now;
        PlacePet(0, player - facing * _spec.HeelDistance);
    }

    internal void AnnounceSpawn()
    {
        if (_count == 0 || _spawnAnnounced)
        {
            return;
        }

        AnnouncePet(_pets[0]);
        _spawnAnnounced = true;
        MatchPackSize();
        MatchBoar();
    }

    private void PlacePet(int index, Vector3 position)
    {
        PetAgent pet = _pets[index];
        pet.Id = _nextId++;
        pet.Position = position;
        pet.State = PetState.Heel;
        pet.QuarryId = 0;
        pet.RecoveringAtHeel = false;
        pet.RescueProgress = 0f;
        pet.NextBiteAt = _now;
        pet.NextRetargetAt = _now;
        _biteCounts[index] = 0;
        _frenzyBuffs[index] = default;
        if (index == BoarSlot)
        {
            _boarActive = true;
        }
        else
        {
            _count++;
        }

        RefreshLive();
    }

    private bool IsActive(int index) => index < _count || (index == BoarSlot && _boarActive);

    // Hounds stay contiguous from slot 0 and the boar keeps slot 3, so Pets is a compact copy made
    // only when an animal joins or leaves, never on a tick.
    private void RefreshLive()
    {
        _liveCount = 0;
        for (int index = 0; index < Capacity; index++)
        {
            if (IsActive(index))
            {
                _live[_liveCount++] = _pets[index];
            }
        }
    }

    private void AnnouncePet(PetAgent pet)
    {
        pet.Health.Reset();
        _events.Publish(new PetSpawned(pet.Id, pet.Position, _playerPosition, pet.Health.Fraction, pet.IsLead,
            pet.Kind));
    }

    private void OnBoarCountChanged(Stat _) => MatchBoar();

    // BT-07a rule 1: BoarCount 1 or more joins one boar at slot 3; 0 sends it away.
    private void MatchBoar()
    {
        PetAgent boar = _pets[BoarSlot];
        if (!_spawnAnnounced || boar is null)
        {
            return;
        }

        bool wanted = _pets[0].Stats.BoarCount.Value >= 1f;
        if (wanted && !_boarActive)
        {
            UpdateBoarScale(boar);
            Vector2 slot = _formation.WorldOffset(BoarSlot);
            PlacePet(BoarSlot, _playerPosition + new Vector3(slot.X, 0f, slot.Y));
            AnnouncePet(boar);
        }
        else if (!wanted && _boarActive)
        {
            ReleaseHunters(BoarSlot);
            _boarActive = false;
            RefreshLive();
            _events.Publish(new PetDespawned(boar.Id));
        }
    }

    // BT-07a rule 1: the boar grows by the lead's own ratio, so level growth and Thick Hide reach it.
    private void UpdateBoarScale(PetAgent boar)
    {
        PetAgent lead = _pets[0];
        float previousMax = boar.MaxHp.Value;
        boar.MaxHp.Base = boar.Spec.MaxHp * (lead.MaxHp.Value / _spec.MaxHp);
        boar.ContactDamage.Base = boar.Spec.BiteDamage * (lead.ContactDamage.Value / _spec.BiteDamage);
        float gain = boar.MaxHp.Value - previousMax;
        if (_boarActive && boar.State != PetState.Fallen && gain > 0f)
        {
            boar.Health.Heal(gain);
        }
    }

    private void OnPackHoundsChanged(Stat _) => MatchPackSize();

    private void MatchPackSize()
    {
        if (!_spawnAnnounced)
        {
            return;
        }

        int count = 1 + (int)Math.Clamp(_pets[0].Stats.PackHounds.Value, 0f, BoarSlot - 1);
        while (_count > count)
        {
            int index = _count - 1;
            ReleaseHunters(index);
            _count--;
            RefreshLive();
            _events.Publish(new PetDespawned(_pets[index].Id));
        }

        Vector3 right = new(_spawnFacing.Z, 0f, -_spawnFacing.X);
        while (_count < count)
        {
            int index = _count;
            PetAgent pet = _pets[index];
            UpdateShare(pet);
            Vector3 side = index == 1 ? right : -right;
            PlacePet(index, _playerPosition + (side - _spawnFacing) * (_spec.HeelDistance * 0.70710677f));
            AnnouncePet(pet);
        }
    }

    private void OnPackShareChanged(Stat _)
    {
        for (int index = 1; index < _count; index++)
        {
            UpdateShare(_pets[index]);
        }

        if (_pets[BoarSlot] is PetAgent boar)
        {
            UpdateBoarScale(boar);
        }
    }

    private void UpdateShare(PetAgent pet)
    {
        PetAgent lead = _pets[0];
        float share = lead.Stats.PackShare.Value;
        float previousMax = pet.MaxHp.Value;
        pet.MaxHp.Base = share * lead.MaxHp.Value;
        pet.ContactDamage.Base = share * lead.ContactDamage.Value;
        float gain = pet.MaxHp.Value - previousMax;
        if (pet.State != PetState.Fallen && gain > 0f)
        {
            pet.Health.Heal(gain);
        }
    }

    public void Ingest(WorldSnapshot snapshot)
    {
        _playerPosition = snapshot.PlayerPosition;
        foreach (PetAgent pet in Pets)
        {
            for (int i = 0; i < snapshot.PetCount; i++)
            {
                ref EnemySense sense = ref snapshot.Pets[i];
                if (sense.Id == pet.Id && pet.State != PetState.Fallen)
                {
                    pet.Position = sense.Position;
                    break;
                }
            }
        }
    }

    public void Tick(float dt, float now, Vector3 player, EnemySystem enemies, PlayerCombat combat,
        Vector3 playerFacing = default, Func<Vector3, Vector3, Vector3?> traceCover = null)
    {
        _playerPosition = player;
        _now = now;
        _formation.Tick(dt, new Vector2(playerFacing.X, playerFacing.Z));
        // BT-04 rule 5: spacing reads where everyone stood at the start of the tick.
        Span<Vector2> standing = stackalloc Vector2[Capacity];
        Span<int> standingSlot = stackalloc int[Capacity];
        int standingCount = GatherStanding(now, standing, standingSlot);
        _enemies = enemies?.Registry;
        int throwTargetId = combat?.LastThrowTargetId ?? 0;
        // BT-03 rule 3: a new throw at a marked enemy pulls every hunter onto it this tick.
        bool switchToThrow = throwTargetId != _lastThrowTargetId && IsMarkedAlive(throwTargetId);
        _lastThrowTargetId = throwTargetId;
        int ready = 0;
        for (int index = 0; index < Capacity; index++)
        {
            if (!IsActive(index))
            {
                continue;
            }

            PetAgent pet = _pets[index];
            float playerDistanceSquared = Offset(pet.Position, player).LengthSquared();
            if (pet.IsLead && now >= _leapUntil && _leapUntil != float.NegativeInfinity)
            {
                _leapUntil = float.NegativeInfinity;
                if (_pounce is not null)
                {
                    LandPounce(pet, now, enemies, combat);
                    continue;
                }

                if (pet.State != PetState.Fallen)
                {
                    Roar(pet, enemies);
                }
            }

            if (pet.State == PetState.Fallen)
            {
                if (dt > 0f && playerDistanceSquared <= pet.Spec.RescueRadius * pet.Spec.RescueRadius)
                {
                    float seconds = _pets[0].RescueSeconds.Value;
                    AdvanceRescue(pet, seconds > 0f ? dt / seconds : 1f);
                }

                continue;
            }

            if (pet.IsLead && now < _leapUntil)
            {
                continue;
            }

            if (!pet.RecoveringAtHeel && pet.Health.Fraction < pet.HeelAt.Value)
            {
                pet.RecoveringAtHeel = true;
                pet.State = PetState.Heel;
                SetQuarry(pet, 0);
                _events.Publish(new PetHeeled(pet.Id));
            }

            EnemyAgent quarry = pet.RecoveringAtHeel ? null
                : ChooseQuarry(pet, now, player, enemies, throwTargetId, switchToThrow);
            if (pet.State == PetState.Heel && dt > 0f && playerDistanceSquared <= AtHeelRadius * AtHeelRadius)
            {
                pet.Health.Heal(pet.HeelRegen.Value * pet.MaxHp.Value * dt);
            }

            if (pet.RecoveringAtHeel && pet.Health.Fraction >= pet.ReturnAt.Value)
            {
                pet.RecoveringAtHeel = false;
                quarry = ChooseQuarry(pet, now, player, enemies, throwTargetId, false);
                _events.Publish(new PetReturned(pet.Id));
            }
            Vector2 velocity2;
            Vector2 facing;
            bool canStrike;
            if (quarry is null)
            {
                velocity2 = HeelVelocity(pet, index, dt, player, playerDistanceSquared, out facing);
                canStrike = false;
            }
            else if (pet.Spec.IsRanged)
            {
                velocity2 = RangedVelocity(pet, index, quarry, dt, now, traceCover, out facing, out canStrike);
            }
            else
            {
                Vector2 offset = Offset(pet.Position, quarry.Position);
                float distance = offset.Length();
                facing = distance > 0.0001f ? offset / distance : Vector2.Zero;
                float speed = MathF.Max(0f, pet.MoveSpeed.Value) * FrenzyMultiplier(index, now);
                canStrike = distance <= pet.Spec.Reach;
                if (canStrike)
                {
                    speed = 0f;
                }
                else if (dt > 0f)
                {
                    speed = MathF.Min(speed, distance / dt);
                }

                velocity2 = facing * speed;
            }

            int standingIndex = standingSlot.Slice(0, standingCount).IndexOf(index);
            if (standingIndex >= 0)
            {
                velocity2 += PackFormation.Separation(standing.Slice(0, standingCount), standingIndex);
            }

            var velocity = new Vector3(velocity2.X, 0f, velocity2.Y);
            _intents.PetMove(new EnemyMoveIntent(pet.Id, velocity, facing));

            if (quarry is not null && canStrike)
            {
                ready |= 1 << index;
            }
        }

        // Globs already in the air move before this tick's are loosed.
        if (enemies is not null)
        {
            _spit.Tick(dt, now, enemies, combat, traceCover);
        }

        // Resolve every pet's quarry before biting so both can qualify on the first tick.
        for (int index = 0; index < Capacity; index++)
        {
            if (!IsActive(index))
            {
                continue;
            }

            PetAgent pet = _pets[index];
            float bite = pet.ContactDamage.Value * FrenzyMultiplier(index, now);
            if ((ready & (1 << index)) == 0 || !(bite > 0f) || !BiteReady(pet, index, now)
                || !_enemies.TryGet(pet.QuarryId, out EnemyAgent quarry) || !quarry.IsAlive)
            {
                continue;
            }

            if (pet.Spec.IsRanged)
            {
                // BT-07a rule 3: a glob carries the boar's bite times SpitDamage, and SpitSlow.
                PetStats lead = _pets[0].Stats;
                float damage = bite * lead.SpitDamage.Value;
                if (_spit.TryLaunch(pet.Id, pet.Position, quarry.Id, damage, lead.SpitSlow.Value, pet.Spec.SpitSpeed))
                {
                    pet.NextBiteAt = now + pet.Spec.SpitInterval;
                }

                continue;
            }

            pet.NextBiteAt = now + _spec.BiteInterval;
            quarry.Blackboard.LastBitBy = pet.Id;
            quarry.Blackboard.LastBitAt = now;
            enemies.ApplyDamageFrom(quarry.Id, bite, now, combat, pet.Position);

            long biteCount = ++_biteCounts[index];
            _events.Publish(new PetBit(pet.Id, quarry.Id, bite));

            int stunEvery = (int)Math.Clamp(pet.Stats.BiteStunEvery.Value, 0f, 10f);
            float stunSeconds = pet.Stats.BiteStunSeconds.Value;
            if (pet.IsLead && stunEvery > 0 && biteCount % stunEvery == 0 && stunSeconds > 0f && enemies.IsLight(quarry.Id))
            {
                enemies.Stun(quarry.Id, stunSeconds);
            }
        }
    }

    // BT-07a rule 2: close until within HoldDistance with a clear line, stand, and back off inside
    // BackOffDistance. It may spit whenever the line is clear and the quarry is within SpitRange.
    private Vector2 RangedVelocity(PetAgent pet, int index, EnemyAgent quarry, float dt, float now,
        Func<Vector3, Vector3, Vector3?> traceCover, out Vector2 facing, out bool canSpit)
    {
        PetSpec spec = pet.Spec;
        Vector2 offset = Offset(pet.Position, quarry.Position);
        float distance = offset.Length();
        facing = distance > 0.0001f ? offset / distance : Vector2.Zero;
        bool clear = traceCover is null || !traceCover(pet.Position, quarry.Position).HasValue;
        canSpit = clear && distance <= spec.SpitRange;
        float speed = MathF.Max(0f, pet.MoveSpeed.Value) * FrenzyMultiplier(index, now);
        float gap;
        Vector2 direction;
        if (!clear || distance > spec.HoldDistance)
        {
            gap = clear ? distance - spec.HoldDistance : distance;
            direction = facing;
        }
        else if (distance < spec.BackOffDistance)
        {
            gap = spec.BackOffDistance - distance;
            direction = distance > 0.0001f ? -facing : -_formation.Facing;
        }
        else
        {
            return Vector2.Zero;
        }

        if (dt > 0f)
        {
            speed = MathF.Min(speed, gap / dt);
        }

        return direction * speed;
    }

    // BT-04 rules 1, 3, 4, 6: walk the lagged slot, ease in, and stand still in the dead zone — but
    // only inside the heel-regen ring, so a recovering animal never parks where it cannot heal.
    private Vector2 HeelVelocity(PetAgent pet, int index, float dt, Vector3 player, float playerDistanceSquared,
        out Vector2 facing)
    {
        Vector2 slot = new Vector2(player.X, player.Z) + _formation.WorldOffset(index);
        Vector2 toSlot = slot - new Vector2(pet.Position.X, pet.Position.Z);
        float distance = toSlot.Length();
        bool atHeel = playerDistanceSquared <= AtHeelRadius * AtHeelRadius;
        if ((distance <= PackFormation.DeadZone && atHeel) || !(distance > 0.0001f))
        {
            facing = _formation.Facing;
            return Vector2.Zero;
        }

        facing = toSlot / distance;
        float speed = PackFormation.ArrivalSpeed(distance, MathF.Max(0f, pet.MoveSpeed.Value) * FrenzyMultiplier(index, _now));
        if (dt > 0f)
        {
            speed = MathF.Min(speed, distance / dt);
        }

        return facing * speed;
    }

    private int GatherStanding(float now, Span<Vector2> positions, Span<int> slots)
    {
        int count = 0;
        for (int index = 0; index < Capacity; index++)
        {
            if (!IsActive(index))
            {
                continue;
            }

            PetAgent pet = _pets[index];
            bool leaping = pet.IsLead && _leapUntil != float.NegativeInfinity;
            if (pet.State == PetState.Fallen || leaping)
            {
                continue;
            }

            positions[count] = new Vector2(pet.Position.X, pet.Position.Z);
            slots[count] = index;
            count++;
        }

        return count;
    }

    private bool BiteReady(PetAgent pet, int index, float now)
    {
        (float bonus, float until) = _frenzyBuffs[index];
        float interval = pet.Spec.IsRanged ? pet.Spec.SpitInterval : pet.Spec.BiteInterval;
        // Keep the base deadline so a buff can change a pending bite only while it is active.
        float reduction = now < until ? interval - interval / (1f + bonus) : 0f;
        return now >= pet.NextBiteAt - reduction;
    }

    internal bool HasStandingPet()
    {
        foreach (PetAgent pet in Pets)
        {
            if (pet.State != PetState.Fallen)
            {
                return true;
            }
        }

        return false;
    }

    internal float LowestStandingHpFraction()
    {
        float lowest = 1f;
        foreach (PetAgent pet in Pets)
        {
            if (pet.State != PetState.Fallen)
            {
                lowest = MathF.Min(lowest, pet.Health.Fraction);
            }
        }

        return lowest;
    }

    public int Howl(float healFraction, float now)
    {
        int standing = 0;
        for (int index = 0; index < Capacity; index++)
        {
            PetAgent pet = _pets[index];
            if (!IsActive(index) || pet.State == PetState.Fallen)
            {
                continue;
            }

            standing++;
            if (healFraction > 0f)
            {
                pet.Health.Heal(healFraction * pet.MaxHp.Value);
            }
        }

        if (standing > 0)
        {
            _events.Publish(new PackHowled(_pets[0].Position));
        }

        return standing;
    }

    /// <summary>BT-06a rule 1: every standing animal bites, runs and hits <paramref name="bonus"/> harder.</summary>
    public int Frenzy(float bonus, float seconds, float now)
    {
        if (!(bonus > 0f) || !(seconds > 0f) || float.IsInfinity(bonus) || float.IsInfinity(seconds))
        {
            return 0;
        }

        int standing = 0;
        for (int index = 0; index < Capacity; index++)
        {
            if (!IsActive(index) || _pets[index].State == PetState.Fallen)
            {
                continue;
            }

            standing++;
            (float held, float until) = _frenzyBuffs[index];
            float strongest = now < until ? MathF.Max(held, bonus) : bonus;
            _frenzyBuffs[index] = (strongest, now + seconds);
        }

        if (standing > 0)
        {
            _events.Publish(new PetsFrenzied(seconds, bonus));
        }

        return standing;
    }

    public bool IsFrenzied(int petId, float now)
    {
        int index = IndexOf(petId);
        return index >= 0 && now < _frenzyBuffs[index].Until;
    }

    private float FrenzyMultiplier(int index, float now)
    {
        if (index < 0)
        {
            return 1f;
        }

        (float bonus, float until) = _frenzyBuffs[index];
        return now < until ? 1f + bonus : 1f;
    }

    public void Clear()
    {
        _spawnAnnounced = false;
        for (int index = 0; index < Capacity; index++)
        {
            if (IsActive(index))
            {
                ReleaseHunters(index);
            }
        }

        _count = 0;
        _boarActive = false;
        _liveCount = 0;
        _spit.Clear();
        _leapUntil = float.NegativeInfinity;
        _swapImmuneUntil = float.NegativeInfinity;
        _pounce = null;
        _enemies = null;
    }

    public DamageResult ApplyDamage(int petId, float amount, float now, int enemyId)
    {
        int index = IndexOf(petId);
        if (index < 0 || _pets[index].State == PetState.Fallen)
        {
            return DamageResult.None;
        }

        PetAgent pet = _pets[index];
        pet.Health.SetExternalInvulnerable(pet.IsLead && now < _swapImmuneUntil);
        float damageTaken = Math.Clamp(_pets[0].Stats.PetDamageTaken.Value, 0.1f, 1f);
        DamageResult result = pet.Health.ApplyDamage(amount * damageTaken, now);
        if (result.Killed)
        {
            pet.State = PetState.Fallen;
            _frenzyBuffs[index] = default;
            pet.QuarryId = 0;
        }

        if (result.Applied > 0f)
        {
            _events.Publish(new PetHurt(petId, result.Applied, enemyId));
        }

        if (result.Killed)
        {
            ReleaseHunters(index);
            _events.Publish(new PetFell(petId, pet.Position));
        }

        return result;
    }

    public bool SwapTo(Vector3 playerFrom, float now, float seconds)
    {
        if (_count == 0)
        {
            return false;
        }

        PetAgent pet = _pets[0];
        if (pet.State == PetState.Fallen)
        {
            AddRescueHeadStart(pet.Id, SwapRescueHeadStart);
            return true;
        }

        _now = now;
        _leapUntil = now + seconds;
        _swapLanding = playerFrom;
        _pounce = null;
        _swapImmuneUntil = _leapUntil + SwapImmunityTrail;
        _intents.PetLeap(new PetLeapIntent(pet.Id, playerFrom, seconds));
        DropHunters(pet.Id);
        _events.Publish(new Swapped(pet.Id, playerFrom, pet.Position, seconds));
        return true;
    }

    internal bool CanPounce(Pounce pounce, EnemySystem enemies) =>
        LeadCanPounce() && FindPounceTarget(pounce.MinReach, pounce.MaxReach, enemies.Registry, out _) is not null;

    internal int CountMarkedInPounceReach(EnemySystem enemies)
    {
        FindPounceTarget(Pounce.DefaultMinReach, Pounce.DefaultMaxReach, enemies.Registry, out int count);
        return count;
    }

    public bool TryPounce(in Pounce pounce, float now, EnemySystem enemies, PlayerCombat combat)
    {
        if (!LeadCanPounce())
        {
            return false;
        }

        EnemyAgent target = FindPounceTarget(pounce.MinReach, pounce.MaxReach, enemies.Registry, out _);
        if (target is null)
        {
            return false;
        }

        PetAgent pet = _pets[0];
        _enemies = enemies.Registry;
        _now = now;
        _leapUntil = now + pounce.LeapSeconds;
        _pounce = pounce;
        _pounceTargetId = target.Id;
        _pounceLanding = target.Position;
        _intents.PetLeap(new PetLeapIntent(pet.Id, _pounceLanding, pounce.LeapSeconds));
        DropHunters(pet.Id);
        _events.Publish(new PetPounced(pet.Id, target.Id, pet.Position, _pounceLanding));
        return true;
    }

    private bool LeadCanPounce() => _count != 0 && _leapUntil == float.NegativeInfinity
        && _pets[0].State != PetState.Fallen && !_pets[0].RecoveringAtHeel;

    private EnemyAgent FindPounceTarget(float minReach, float maxReach, EnemyRegistry enemies, out int count)
    {
        count = 0;
        if (_count == 0)
        {
            return null;
        }

        Vector3 from = _pets[0].Position;
        float minSquared = minReach * minReach;
        float maxSquared = maxReach * maxReach;
        EnemyAgent nearest = null;
        float best = maxSquared;
        foreach (EnemyAgent enemy in enemies.Alive)
        {
            if (!enemy.IsAlive || !enemy.CrowdControl.IsMarked)
            {
                continue;
            }

            float distance = Offset(from, enemy.Position).LengthSquared();
            if (!(distance >= minSquared && distance <= maxSquared))
            {
                continue;
            }

            count++;
            if (nearest is null || distance < best || (distance == best && enemy.Id < nearest.Id))
            {
                nearest = enemy;
                best = distance;
            }
        }

        return nearest;
    }

    private void LandPounce(PetAgent pet, float now, EnemySystem enemies, PlayerCombat combat)
    {
        Pounce pounce = _pounce;
        int targetId = _pounceTargetId;
        _pounce = null;
        if (pet.State == PetState.Fallen)
        {
            return;
        }

        pet.Position = _pounceLanding;
        if (enemies.Registry.TryGet(targetId, out EnemyAgent target) && target.IsAlive)
        {
            float bite = pet.ContactDamage.Value * FrenzyMultiplier(IndexOf(pet.Id), now);
            if (bite > 0f)
            {
                bite *= pounce.BiteMultiplier;
                pet.NextBiteAt = now + _spec.BiteInterval;
                target.Blackboard.LastBitBy = pet.Id;
                target.Blackboard.LastBitAt = now;
                enemies.ApplyDamageFrom(targetId, bite, now, combat, pet.Position);
                _events.Publish(new PetBit(pet.Id, targetId, bite));
            }

            if (enemies.IsLight(targetId))
            {
                enemies.Stun(targetId, pounce.KnockdownSeconds);
            }

            if (target.IsAlive)
            {
                pet.State = PetState.Hunt;
                pet.NextRetargetAt = now + RetargetInterval;
                SetQuarry(pet, targetId);
            }
        }

        _events.Publish(new PetLanded(pet.Id, targetId));
    }

    private void Roar(PetAgent pet, EnemySystem enemies)
    {
        float slow = pet.Stats.SwapRoarSlow.Value;
        if (!(slow > 0f))
        {
            return;
        }

        slow = MathF.Min(slow, CrowdControl.MaxSlow);
        foreach (EnemyAgent enemy in enemies.Registry.Alive)
        {
            if (enemy.IsAlive && Offset(_swapLanding, enemy.Position).LengthSquared() <= RoarRadius * RoarRadius)
            {
                enemies.Slow(enemy.Id, slow, RoarSlowSeconds);
            }
        }

        _events.Publish(new PetRoared(pet.Id, _swapLanding, RoarRadius));
    }

    public void AddRescueHeadStart(int petId, float fraction)
    {
        int index = IndexOf(petId);
        if (index >= 0 && _pets[index].State == PetState.Fallen && fraction > 0f)
        {
            AdvanceRescue(_pets[index], fraction);
        }
    }

    private void AdvanceRescue(PetAgent pet, float fraction)
    {
        float progress = MathF.Min(1f, pet.RescueProgress + fraction);
        if (progress == pet.RescueProgress)
        {
            return;
        }

        pet.RescueProgress = progress;
        _events.Publish(new RescueProgressed(pet.Id, progress));
        if (progress == 1f)
        {
            pet.Health.Restore(pet.Spec.RiseFraction * pet.MaxHp.Value, 0f);
            pet.State = PetState.Heel;
            pet.RecoveringAtHeel = true;
            pet.RescueProgress = 0f;
            _events.Publish(new PetRose(pet.Id));
        }
    }

    public bool BitWithin(int petId, int enemyId, float now, float seconds)
    {
        return IndexOf(petId) >= 0 && _enemies is not null
            && _enemies.TryGet(enemyId, out EnemyAgent enemy)
            && WasBitten(enemy.Blackboard, petId, now, seconds);
    }

    public int HuntersOf(int petId)
    {
        int index = IndexOf(petId);
        int count = 0;
        if (index >= 0)
        {
            for (int slot = 0; slot < MaxHunters; slot++)
            {
                if (_hunters[index, slot] != 0)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public void DropHunters(int petId)
    {
        if (_count != 0 && _pets[0].Id == petId)
        {
            ReleaseHunters(0);
        }
    }

    private void ReleaseHunters(int index)
    {
        int petId = _pets[index].Id;
        Span<int> dropped = stackalloc int[MaxHunters];
        for (int slot = 0; slot < MaxHunters; slot++)
        {
            int enemyId = _hunters[index, slot];
            dropped[slot] = enemyId;
            _hunters[index, slot] = 0;
            if (enemyId != 0 && _enemies is not null && _enemies.TryGet(enemyId, out EnemyAgent enemy))
            {
                enemy.Blackboard.PetHuntSuppressedUntil = _now + HuntMemory;
            }
        }

        foreach (int enemyId in dropped)
        {
            if (enemyId != 0)
            {
                _events.Publish(new PetHunted(petId, enemyId, false));
            }
        }
    }

    internal void ReviewHunters(EnemyRegistry enemies, Vector3 player, float now)
    {
        _enemies = enemies;
        _now = now;
        for (int index = 0; index < Capacity; index++)
        {
            if (!IsActive(index))
            {
                continue;
            }

            for (int slot = 0; slot < MaxHunters; slot++)
            {
                int enemyId = _hunters[index, slot];
                if (enemyId != 0 && (!enemies.TryGet(enemyId, out EnemyAgent enemy)
                    || !CanHunt(enemy, _pets[index], player)))
                {
                    ReleaseHunter(index, slot);
                }
            }
        }
    }

    internal PetAgent HuntQuarry(EnemyAgent enemy, Vector3 player)
    {
        for (int index = 0; index < Capacity; index++)
        {
            PetAgent pet = _pets[index];
            if (!IsActive(index) || !CanHunt(enemy, pet, player))
            {
                continue;
            }

            for (int slot = 0; slot < MaxHunters; slot++)
            {
                if (_hunters[index, slot] == enemy.Id)
                {
                    return pet;
                }
            }

            for (int slot = 0; slot < MaxHunters; slot++)
            {
                if (_hunters[index, slot] == 0)
                {
                    _hunters[index, slot] = enemy.Id;
                    _events.Publish(new PetHunted(pet.Id, enemy.Id, true));
                    return pet;
                }
            }
        }

        return null;
    }

    private bool CanHunt(EnemyAgent enemy, PetAgent pet, Vector3 player)
    {
        EnemyBlackboard board = enemy.Blackboard;
        return enemy.IsAlive
            && enemy.Behaviour is not BossBehaviour
            && enemy.Spec.Behaviour is EnemyBehaviourKind.Chaser or EnemyBehaviourKind.Lunger
            && pet.State != PetState.Fallen
            && (!pet.IsLead || _pounce is null)
            && _now >= board.PetHuntSuppressedUntil
            && WasBitten(board, pet.Id, _now, HuntMemory)
            && Offset(enemy.Position, pet.Position).LengthSquared()
                < Offset(enemy.Position, player).LengthSquared();
    }

    private static bool WasBitten(EnemyBlackboard board, int petId, float now, float seconds)
    {
        float age = now - board.LastBitAt;
        return board.LastBitBy == petId && age >= 0f && age < seconds;
    }

    private void ReleaseHunter(int index, int slot)
    {
        int enemyId = _hunters[index, slot];
        _hunters[index, slot] = 0;
        _events.Publish(new PetHunted(_pets[index].Id, enemyId, false));
    }

    private int IndexOf(int petId)
    {
        for (int index = 0; index < Capacity; index++)
        {
            if (IsActive(index) && _pets[index].Id == petId)
            {
                return index;
            }
        }

        return -1;
    }

    private EnemyAgent ChooseQuarry(PetAgent pet, float now, Vector3 player, EnemySystem enemies,
        int throwTargetId, bool switchToThrow)
    {
        EnemyAgent quarry = null;
        if (switchToThrow)
        {
            enemies.Registry.TryGet(throwTargetId, out quarry);
            pet.NextRetargetAt = now + RetargetInterval;
        }
        else if (pet.QuarryId == 0 || now >= pet.NextRetargetAt)
        {
            quarry = FindQuarry(pet, player, throwTargetId, enemies.Registry);
            pet.NextRetargetAt = now + RetargetInterval;
        }
        else if (enemies.Registry.TryGet(pet.QuarryId, out EnemyAgent current) && current.IsAlive)
        {
            quarry = current;
        }

        int id = quarry?.Id ?? 0;
        pet.State = id == 0 ? PetState.Heel : PetState.Hunt;
        SetQuarry(pet, id);
        return quarry;
    }

    private bool IsMarkedAlive(int enemyId) => enemyId > 0 && _enemies is not null
        && _enemies.TryGet(enemyId, out EnemyAgent enemy) && enemy.IsAlive && enemy.CrowdControl.IsMarked;

    private void SetQuarry(PetAgent pet, int id)
    {
        if (pet.QuarryId != id)
        {
            pet.QuarryId = id;
            _events.Publish(new PetRetargeted(pet.Id, id));
        }
    }

    // BT-03 rule 2: the last throw's target while marked, then the marked enemy nearest the
    // animal, then the enemy nearest the player — anywhere in the arena.
    private static EnemyAgent FindQuarry(PetAgent pet, Vector3 player, int throwTargetId, EnemyRegistry enemies)
    {
        if (enemies.TryGet(throwTargetId, out EnemyAgent thrown) && thrown.IsAlive && thrown.CrowdControl.IsMarked)
        {
            return thrown;
        }

        EnemyAgent nearest = null;
        EnemyAgent marked = null;
        float nearestDistance = float.PositiveInfinity;
        float markedDistance = float.PositiveInfinity;
        foreach (EnemyAgent enemy in enemies.Alive)
        {
            if (!enemy.IsAlive)
            {
                continue;
            }

            float playerDistance = Offset(player, enemy.Position).LengthSquared();
            if (playerDistance < nearestDistance)
            {
                nearest = enemy;
                nearestDistance = playerDistance;
            }

            if (enemy.CrowdControl.IsMarked)
            {
                float petDistance = Offset(pet.Position, enemy.Position).LengthSquared();
                if (petDistance < markedDistance)
                {
                    marked = enemy;
                    markedDistance = petDistance;
                }
            }
        }

        return marked ?? nearest;
    }

    private static Vector2 Offset(Vector3 from, Vector3 to) => new Vector2(to.X - from.X, to.Z - from.Z);
}
