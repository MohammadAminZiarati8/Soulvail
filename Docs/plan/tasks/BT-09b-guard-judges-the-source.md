# BT-09b — A frontal guard judges where a pet's hit comes from

**Size:** M · **Depends on:** BT-09a · **Branch:** `dev`
**Design refs:** CH §3.6.1, CH §3.7 (Hawk, Boar)

## Goal

A Mossguard or the Mossguard Warlord, shield raised at the Beastmaster, takes the hound's, boar's and hawk's hits from behind or the side.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Ai/IFrontalGuard.cs` | Core | the arc question a guard answers |
| `Core/Ai/EnemySystem.cs` | Core | `ApplyDamageFrom` |
| `Core/Ai/PetSystem.cs` | Core | bite and pounce pass the pet's position |
| `Core/Ai/PetSpit.cs` | Core | a glob passes its own position |
| `Core/Ai/HawkSystem.cs` | Core | a dive comes from above |
| `Tests/Core/Ai/GuardFromSourceTests.cs` | Tests.Core | rules 1–6 |
| *small edits* | | `BulwarkBehaviour`, `WarlordBehaviour` implement `IFrontalGuard` |

## Public API

```csharp
internal interface IFrontalGuard
{
    /// <summary>True while the guard is raised and the XZ direction (enemy → source) is inside its arc.</summary>
    bool Covers(Vector2 towardSource);
}
// EnemySystem
public DamageResult ApplyDamageFrom(int enemyId, float amount, float now, PlayerCombat player, Vector3 from);
```

## Behaviour

1. `ApplyDamageFrom` finds the guard: the behaviour if it is an `IFrontalGuard`, or a `BossBehaviour` outside its beat whose `Inner` is one. Without one it is `ApplyDamage`.
2. With a guard, a hit from a direction the guard does not `Covers` takes the guard-piercing path. A hit from inside the arc is blocked as today. A phase beat still blocks everything.
3. `from` within `MinDirectionDistance` of the enemy, or non-finite, counts as covered: today's behaviour.
4. `BulwarkBehaviour.Covers` uses its facing and `ShieldHalfArcDegrees`. `WarlordBehaviour.Covers` uses `Facing` and `GuardHalfArcDeg`, and is false while that is 0 (dazed, shattered).
5. A bite and a pounce landing pass the pet's position. A glob passes its position at impact.
6. A hawk dive comes from above and is never covered: it takes `ApplyGuardPiercingDamage`.
7. The guard's own raising rule is unchanged: it still faces the player, and every player hit still reads it.

## Tests

| Test | Given / When / Then |
|---|---|
| `Bite_FromBehindBulwark_Lands` | Mossguard facing the player, hound behind / bite / damage applied |
| `Bite_FromFrontOfBulwark_Blocked` | hound beside the player / bite / blocked |
| `Bite_FromSideOutsideArc_Lands` | hound at 90° past the arc / bite / applied |
| `Bite_AtWarlordBack_Lands` | Warlord guarding the player, hound behind / bite / applied |
| `Bite_AtWarlordInBeat_Blocked` | Warlord in a phase beat, hound behind / bite / blocked |
| `Bite_FromSourceOnTop_TreatedAsCovered` | `from` at the enemy's centre / bite / blocked |
| `Glob_FromBehind_Lands` | boar behind a raised Mossguard / glob hits / applied |
| `HawkDive_AtRaisedGuard_Lands` | raised Mossguard / dive / applied |
| `NoGuard_ApplyDamageFrom_SameAsApplyDamage` | a Frog / both calls / equal results |

## Manual verification (Editor / device)

1. **[Editor]** Stage 17–19: stand in front of a Mossguard while the hound bites its back. Its HP drops.
2. **[Editor]** Stage 15 Warlord: the same from behind. During a phase change nothing lands.

## Out of scope

- Returning axes judged by direction (owner's call, not asked yet).
- Pets in front blocked while the player stands behind.
