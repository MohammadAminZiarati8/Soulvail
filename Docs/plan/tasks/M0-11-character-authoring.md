# M0-11 — `CharacterDefinition` SO, `Oathbound.asset`, conversion + validation tests

**Size:** S · **Depends on:** M0-08 · **Branch:** `m0-11-character-authoring`
**Design refs:** AR §10.1, ADR-0006; numbers from CC §2.5, §7

## Goal

The Oathbound exists as data a designer can tune in the Inspector, and converts into the immutable `CharacterSpec` core consumes — with tests that lock the shipped numbers.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Game/Authoring/CharacterDefinition.cs` | Game | ScriptableObject + `ToSpec()` |
| `Data/Characters/Oathbound.asset` | — | The first class, CC §7 numbers |
| `Tests/Game/Authoring/CharacterDefinitionTests.cs` | Tests.Game | Conversion, shipped values, project-wide validation |

## Public API

```csharp
namespace Soulvail.Game.Authoring;

[CreateAssetMenu(menuName = "Soulvail/Content/Character", fileName = "Character")]
public sealed class CharacterDefinition : ScriptableObject
{
    [SerializeField] private string _id = "character.new";
    [SerializeField] private string _nameKey = "character.new.name";
    [SerializeField, Min(1f)]     private float _maxHp = 100f;
    [SerializeField, Min(0.01f)]  private float _speed = 6f;
    [SerializeField, Min(0.001f)] private float _accelTime = 0.06f;
    [SerializeField, Min(0.001f)] private float _decelTime = 0.08f;
    [SerializeField, Min(1f)]     private float _turnSpeedDeg = 720f;

    public string Id => _id;
    public CharacterSpec ToSpec();          // ArgumentException naming the asset if any field is invalid
}
```

`Oathbound.asset`: `_id = character.oathbound`, `_nameKey = character.oathbound.name`, `_maxHp = 140`, `_speed = 5.4`, `_accelTime = 0.06`, `_decelTime = 0.08`, `_turnSpeedDeg = 720`.

## Behaviour

1. `ToSpec()` builds `new CharacterSpec(new ContentId(_id), new LocKey(_nameKey), _maxHp, new MovementSpec(...))`. Any validation failure from those constructors is rethrown as `ArgumentException` whose message includes `name` (the asset name) so the Console points at the asset.
2. `ToSpec()` returns a new instance every call; the SO holds no runtime state.
3. `OnValidate` logs a warning (with the asset as context) when `_id` fails `ContentId.IsValid` — designers see it immediately in the Inspector.
4. Fields are `[SerializeField] private`; nothing is public except `Id` and `ToSpec()`.

## Tests

| Test | Given / When / Then |
|---|---|
| `Oathbound_LoadsFromAssetPath` | `AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/_Project/Data/Characters/Oathbound.asset")` / — / not null |
| `Oathbound_ToSpec_MatchesCoreCombatNumbers` | loaded asset / ToSpec / id `character.oathbound`, nameKey `character.oathbound.name`, MaxHp 140, Speed 5.4, AccelTime 0.06, DecelTime 0.08, TurnSpeedDeg 720 |
| `ToSpec_ReturnsNewInstanceEachCall` | asset / ToSpec twice / different references, equal values |
| `ToSpec_InvalidId_ThrowsNamingAsset` | `CreateInstance`, set `_id = "Bad Id"` via `SerializedObject` / ToSpec / `ArgumentException` containing the asset's `name` |
| `ToSpec_InvalidHp_ThrowsNamingAsset` | `_maxHp = 0` via `SerializedObject` / ToSpec / throws, message contains name |
| `AllCharacterDefinitions_HaveValidUniqueIds` | `AssetDatabase.FindAssets("t:CharacterDefinition")` / ToSpec each / no throws; ids distinct |

Instances created with `CreateInstance` are destroyed in teardown.

## Manual verification (Editor)

1. Select `Oathbound.asset` — Inspector shows the seven fields with the CC §7 values.
2. Temporarily set `_id` to `Bad Id` — a warning appears in the Console pointing at the asset. Revert.

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- Weapon, movement skill, passive, tree references on the definition — added by the M1/M3 tasks that need them.
- A custom Inspector.
- Registering definitions with the container — M0-12.

## As built

_Filled at merge._
