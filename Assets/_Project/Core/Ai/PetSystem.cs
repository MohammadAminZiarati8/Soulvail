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
    public const int Capacity = 3;
    public const int MaxHunters = 2;
    public const float HuntMemory = 2f;
    public const float RetargetInterval = 0.5f;
    public const float AtHeelRadius = 2f;
    public const float RoarRadius = 3f;
    public const float RoarSlowSeconds = 2f;

    private const float SwapImmunityTrail = 0.2f;
    private const float SwapRescueHeadStart = 0.5f;

    private readonly PetSpec _spec;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly long[] _biteCounts = new long[Capacity];
    private readonly PetAgent[] _pets = new PetAgent[Capacity];
    private readonly int[,] _hunters = new int[Capacity, MaxHunters];
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

    public PetSystem(PetSpec spec, IDomainEvents events, IIntentSink intents)
    {
        _spec = spec;
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        if (spec is not null)
        {
            for (int index = 0; index < Capacity; index++)
            {
                _pets[index] = new PetAgent(spec, isLead: index == 0);
            }

            PetAgent lead = _pets[0];
            lead.Stats.PackHounds.Changed += OnPackHoundsChanged;
            lead.Stats.PackShare.Changed += OnPackShareChanged;
            lead.MaxHp.Changed += OnPackShareChanged;
            lead.ContactDamage.Changed += OnPackShareChanged;
        }
    }

    public ReadOnlySpan<PetAgent> Pets => _pets.AsSpan(0, _count);
    public IStatBlock LeadStats => _count == 0 ? null : _pets[0].Stats;

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
    }

    private void PlacePet(int index, Vector3 position)
    {
        PetAgent pet = _pets[index];
        pet.Id = _nextId++;
        pet.Position = position;
        pet.State = PetState.Heel;
        pet.QuarryId = 0;
        pet.Returning = false;
        pet.RecoveringAtHeel = false;
        pet.RescueProgress = 0f;
        pet.NextBiteAt = _now;
        pet.NextRetargetAt = _now;
        _biteCounts[index] = 0;
        _count++;
    }

    private void AnnouncePet(PetAgent pet)
    {
        pet.Health.Reset();
        _events.Publish(new PetSpawned(pet.Id, pet.Position, _playerPosition, pet.Health.Fraction, pet.IsLead));
    }

    private void OnPackHoundsChanged(Stat _) => MatchPackSize();

    private void MatchPackSize()
    {
        if (!_spawnAnnounced)
        {
            return;
        }

        int count = 1 + (int)Math.Clamp(_pets[0].Stats.PackHounds.Value, 0f, Capacity - 1);
        while (_count > count)
        {
            int index = _count - 1;
            ReleaseHunters(index);
            _count--;
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

    public void Tick(float dt, float now, Vector3 player, EnemySystem enemies, PlayerCombat combat)
    {
        _playerPosition = player;
        _now = now;
        _enemies = enemies?.Registry;
        for (int index = 0; index < _count; index++)
        {
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
                if (dt > 0f && playerDistanceSquared <= _spec.RescueRadius * _spec.RescueRadius)
                {
                    float seconds = pet.RescueSeconds.Value;
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

            EnemyAgent quarry = pet.RecoveringAtHeel ? null : ChooseQuarry(pet, now, player, enemies);
            if (pet.State == PetState.Heel && dt > 0f && playerDistanceSquared <= AtHeelRadius * AtHeelRadius)
            {
                pet.Health.Heal(pet.HeelRegen.Value * pet.MaxHp.Value * dt);
            }

            if (pet.RecoveringAtHeel && pet.Health.Fraction >= pet.ReturnAt.Value)
            {
                pet.RecoveringAtHeel = false;
                quarry = ChooseQuarry(pet, now, player, enemies);
                _events.Publish(new PetReturned(pet.Id));
            }
            Vector2 offset = Offset(pet.Position, quarry?.Position ?? player);
            float distance = offset.Length();
            float stopAt = quarry is null ? _spec.HeelDistance : _spec.Reach;
            Vector2 facing = distance > 0.0001f ? offset / distance : Vector2.Zero;
            float speed = MathF.Max(0f, pet.MoveSpeed.Value);
            if (distance <= stopAt)
            {
                speed = 0f;
            }
            else if (dt > 0f)
            {
                float travel = quarry is null ? MathF.Max(0f, distance - stopAt) : distance;
                speed = MathF.Min(speed, travel / dt);
            }

            var velocity = new Vector3(facing.X * speed, 0f, facing.Y * speed);
            _intents.PetMove(new EnemyMoveIntent(pet.Id, velocity, facing));

            float bite = pet.ContactDamage.Value;
            if (quarry is not null && distance <= _spec.Reach && now >= pet.NextBiteAt && bite > 0f)
            {
                pet.NextBiteAt = now + _spec.BiteInterval;
                quarry.Blackboard.LastBitBy = pet.Id;
                quarry.Blackboard.LastBitAt = now;
                enemies.ApplyDamage(quarry.Id, bite, now, combat);
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
    }

    public void Clear()
    {
        _spawnAnnounced = false;
        for (int index = 0; index < _count; index++)
        {
            ReleaseHunters(index);
        }

        _count = 0;
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
        DamageResult result = pet.Health.ApplyDamage(amount, now);
        if (result.Killed)
        {
            pet.State = PetState.Fallen;
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
        && _pets[0].State != PetState.Fallen && !_pets[0].RecoveringAtHeel && !_pets[0].Returning;

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
        float leashSquared = _spec.Leash * _spec.Leash;
        EnemyAgent nearest = null;
        float best = maxSquared;
        foreach (EnemyAgent enemy in enemies.Alive)
        {
            if (!enemy.IsAlive || !enemy.CrowdControl.IsMarked
                || !(Offset(_playerPosition, enemy.Position).LengthSquared() <= leashSquared))
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
            float bite = pet.ContactDamage.Value;
            if (bite > 0f)
            {
                bite *= pounce.BiteMultiplier;
                pet.NextBiteAt = now + _spec.BiteInterval;
                target.Blackboard.LastBitBy = pet.Id;
                target.Blackboard.LastBitAt = now;
                enemies.ApplyDamage(targetId, bite, now, combat);
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
            pet.Health.Restore(_spec.RiseFraction * pet.MaxHp.Value, 0f);
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
        for (int index = 0; index < _count; index++)
        {
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
        for (int index = 0; index < _count; index++)
        {
            PetAgent pet = _pets[index];
            if (!CanHunt(enemy, pet, player))
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
        for (int index = 0; index < _count; index++)
        {
            if (_pets[index].Id == petId)
            {
                return index;
            }
        }

        return -1;
    }

    private EnemyAgent ChooseQuarry(PetAgent pet, float now, Vector3 player, EnemySystem enemies)
    {
        float leashSquared = _spec.Leash * _spec.Leash;
        float playerDistance = Offset(pet.Position, player).LengthSquared();
        pet.Returning = pet.Returning
            ? playerDistance > leashSquared
            : playerDistance > _spec.ReturnRange * _spec.ReturnRange;

        EnemyAgent quarry = null;
        if (pet.Returning)
        {
            pet.NextRetargetAt = now;
        }
        else if (pet.QuarryId == 0 || now >= pet.NextRetargetAt)
        {
            quarry = FindQuarry(pet, player, leashSquared, enemies.Registry.Alive);
            pet.NextRetargetAt = now + RetargetInterval;
        }
        else if (enemies.Registry.TryGet(pet.QuarryId, out EnemyAgent current) && current.IsAlive
            && Offset(player, current.Position).LengthSquared() <= leashSquared)
        {
            quarry = current;
        }

        int id = quarry?.Id ?? 0;
        pet.State = id == 0 ? PetState.Heel : PetState.Hunt;
        SetQuarry(pet, id);
        return quarry;
    }

    private void SetQuarry(PetAgent pet, int id)
    {
        if (pet.QuarryId != id)
        {
            pet.QuarryId = id;
            _events.Publish(new PetRetargeted(pet.Id, id));
        }
    }

    private static EnemyAgent FindQuarry(PetAgent pet, Vector3 player, float leashSquared,
        ReadOnlySpan<EnemyAgent> enemies)
    {
        EnemyAgent nearest = null;
        EnemyAgent marked = null;
        float nearestDistance = float.PositiveInfinity;
        float markedDistance = float.PositiveInfinity;
        foreach (EnemyAgent enemy in enemies)
        {
            float playerDistance = Offset(player, enemy.Position).LengthSquared();
            if (!enemy.IsAlive || playerDistance > leashSquared)
            {
                continue;
            }

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
