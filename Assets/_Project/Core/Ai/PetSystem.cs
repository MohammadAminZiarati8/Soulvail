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
    public const float RetargetInterval = 0.5f;

    private readonly PetSpec _spec;
    private readonly IDomainEvents _events;
    private readonly IIntentSink _intents;
    private readonly PetAgent[] _pets = new PetAgent[Capacity];
    private int _count;
    private int _nextId = 1;

    public PetSystem(PetSpec spec, IDomainEvents events, IIntentSink intents)
    {
        _spec = spec;
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _intents = intents ?? throw new ArgumentNullException(nameof(intents));
        if (spec is not null)
        {
            _pets[0] = new PetAgent(spec);
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

        PetAgent pet = _pets[0];
        pet.Id = _nextId++;
        pet.Position = player - facing * _spec.HeelDistance;
        pet.State = PetState.Heel;
        pet.QuarryId = 0;
        pet.Returning = false;
        pet.NextBiteAt = now;
        pet.NextRetargetAt = now;
        _count = 1;
    }

    internal void AnnounceSpawn()
    {
        if (_count != 0)
        {
            PetAgent pet = _pets[0];
            pet.Health.Reset();
            _events.Publish(new PetSpawned(pet.Id, pet.Position));
        }
    }

    public void Ingest(WorldSnapshot snapshot)
    {
        foreach (PetAgent pet in Pets)
        {
            for (int i = 0; i < snapshot.PetCount; i++)
            {
                ref EnemySense sense = ref snapshot.Pets[i];
                if (sense.Id == pet.Id)
                {
                    pet.Position = sense.Position;
                    break;
                }
            }
        }
    }

    public void Tick(float dt, float now, Vector3 player, EnemySystem enemies, PlayerCombat combat)
    {
        foreach (PetAgent pet in Pets)
        {
            EnemyAgent quarry = ChooseQuarry(pet, now, player, enemies);
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
                enemies.ApplyDamage(quarry.Id, bite, now, combat);
                _events.Publish(new PetBit(pet.Id, quarry.Id, bite));
            }
        }
    }

    public void Clear() => _count = 0;

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
        else if (now >= pet.NextRetargetAt)
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
        if (pet.QuarryId != id)
        {
            pet.QuarryId = id;
            _events.Publish(new PetRetargeted(pet.Id, id));
        }

        return quarry;
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
