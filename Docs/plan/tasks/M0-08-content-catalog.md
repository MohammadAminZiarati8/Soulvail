# M0-08 — `ContentId`, `LocKey`, `CharacterSpec`, `ContentCatalog`

**Size:** M · **Depends on:** M0-07 · **Branch:** `m0-08-content-catalog`
**Design refs:** AR §10.1, §11.3, §11.5; ADR-0006, ADR-0010, ADR-0012

## Goal

Core has stable identities for content and text, an immutable class spec, and a catalog to look them up — so nothing in core ever references an asset or a raw string.

## Files

| Path (under `Assets/_Project/`) | Assembly | Purpose |
|---|---|---|
| `Core/Content/ContentId.cs` | Core | Validated, stable content identity |
| `Core/Content/LocKey.cs` | Core | Localisation key |
| `Core/Content/CharacterSpec.cs` | Core | Immutable class definition |
| `Core/Content/ContentCatalog.cs` | Core | Lookup by id, duplicates rejected |
| `Tests/Core/Content/ContentTests.cs` | Tests.Core | All four types (one module, one test file) |

## Public API

```csharp
namespace Soulvail.Core.Content;

public readonly struct ContentId : IEquatable<ContentId>
{
    public string Value { get; }
    public ContentId(string value);                        // ArgumentException if invalid
    public static bool TryParse(string value, out ContentId id);
    public static bool IsValid(string value);              // ^[a-z0-9]+(\.[a-z0-9_-]+)+$
    // Equals, GetHashCode, ToString (returns Value), ==, !=
}

public readonly struct LocKey : IEquatable<LocKey>
{
    public string Key { get; }
    public LocKey(string key);                             // ArgumentException if null/empty/whitespace-containing
    // Equals, GetHashCode, ToString, ==, !=
}

public sealed class CharacterSpec
{
    public ContentId    Id       { get; }
    public LocKey       NameKey  { get; }
    public float        MaxHp    { get; }
    public MovementSpec Movement { get; }
    public CharacterSpec(ContentId id, LocKey nameKey, float maxHp, MovementSpec movement);   // maxHp > 0, movement non-null
}

public sealed class ContentCatalog
{
    public ContentCatalog(IReadOnlyList<CharacterSpec> characters);   // ArgumentException on duplicate id, listing it
    public IReadOnlyList<CharacterSpec> Characters { get; }
    public CharacterSpec Character(ContentId id);                     // KeyNotFoundException naming the id
    public bool TryGetCharacter(ContentId id, out CharacterSpec spec);
}
```

## Behaviour

1. A valid `ContentId` is lowercase, dot-separated, at least two segments: `character.oathbound`, `skill.oathbound.consecrate`. Segments after the first may contain `_` and `-`. Anything else — uppercase, spaces, leading/trailing dot, single segment, empty — is rejected.
2. `ContentId` equality is ordinal on `Value`. Default (`default(ContentId)`) has `Value == null` and is not equal to any valid id; using it in the catalog throws `KeyNotFoundException` like any unknown id.
3. `LocKey` accepts any non-empty string without whitespace. It is *not* validated against a table — that's the localiser's job (M6).
4. `CharacterSpec` is immutable and validates `maxHp > 0` and `movement != null`.
5. The catalog copies the input list; later mutation of the caller's list has no effect.
6. Duplicate ids in the constructor throw with the duplicate id in the message.
7. Lookups are O(1) (dictionary keyed by `ContentId`).

## Tests

| Test | Given / When / Then |
|---|---|
| `ContentId_ValidForms_Accepted` | `"a.b"`, `"character.oathbound"`, `"skill.x.y_z-1"` / new / no throw, `Value` preserved |
| `ContentId_InvalidForms_Rejected` | `""`, `"single"`, `"Has.Upper"`, `"a b.c"`, `".a.b"`, `"a.b."`, `"a..b"`, null / new / `ArgumentException` |
| `ContentId_TryParse_MatchesIsValid` | same inputs / TryParse / true iff valid |
| `ContentId_Equality_IsOrdinal` | `"a.b"` twice / == / true; `"a.b"` vs `"a.c"` / false; hash codes equal for equal ids |
| `ContentId_Default_NotEqualToAny` | default vs `"a.b"` / == / false |
| `LocKey_Valid_Accepted` | `"character.oathbound.name"` / new / ok |
| `LocKey_Invalid_Rejected` | `""`, `"has space"`, null / new / `ArgumentException` |
| `CharacterSpec_InvalidHp_Throws` | maxHp 0 / new / `ArgumentOutOfRangeException` |
| `CharacterSpec_NullMovement_Throws` | movement null / new / `ArgumentNullException` |
| `Catalog_LooksUpById` | one spec / Character(id) / same instance |
| `Catalog_UnknownId_Throws_NamingId` | empty catalog / Character("x.y") / `KeyNotFoundException`, message contains `x.y` |
| `Catalog_TryGet_FalseForUnknown` | — / TryGetCharacter / false, out null |
| `Catalog_DuplicateId_Throws_NamingId` | two specs same id / new / `ArgumentException` containing the id |
| `Catalog_CopiesInput` | list / construct, then list.Clear() / `Characters.Count` unchanged |

## Acceptance

- [ ] All tests green
- [ ] Zero errors, zero new analyzer warnings
- [ ] `PROGRESS.md` entry appended; Current State updated; ROADMAP box ticked

## Out of scope

- `TagSet` (ADR-0010) — first needed by M1-06 enemies.
- Enemy, skill, mode specs — their milestones.
- Building the catalog from ScriptableObjects — M0-11.

## As built

_Filled at merge._
