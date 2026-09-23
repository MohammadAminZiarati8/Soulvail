using System;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately, against the project's file-scoped convention: Unity 6.3's
// script importer parses a file to find the type it declares, and its parser does not
// understand `namespace X;`. A ScriptableObject declared that way compiles, but Unity never
// links a MonoScript to it — assets referencing it serialise as `m_Script: {fileID: 0}` and
// load as null. Verified A/B in one compile cycle (M0-11). Every UnityEngine.Object-derived
// type in this project uses a block namespace for that reason; pure C# stays file-scoped.
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// A playable class as a designer tunes it: the Inspector half of
    /// <see cref="CharacterSpec"/>. Converted once at boot into the immutable spec core
    /// consumes and registered in the <c>ContentCatalog</c>. See AR §10.1 and ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two shapes — this and <see cref="CharacterSpec"/> — are the price ADR-0006 accepts
    /// for a pure core: a <see cref="ScriptableObject"/> is a <see cref="UnityEngine.Object"/>,
    /// which core may never see. The conversion is the seam that lets the *source* of balance
    /// data become server JSON later without core noticing.
    /// </para>
    /// <para>
    /// The <c>[Min]</c> attributes are Inspector affordances, not guarantees. They clamp what a
    /// designer can drag or type; they do not touch a value set through
    /// <c>SerializedProperty</c>, arriving from a merge, or written by hand into the YAML. The
    /// real validation is in the core constructors <see cref="ToSpec"/> calls, which is why
    /// <see cref="ToSpec"/> is the only way out of this type.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Character", fileName = "Character")]
    public sealed class CharacterDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "character.new";
        [SerializeField] private string _nameKey = "character.new.name";

        [Tooltip("The one line the class-select screen's card draws under the name (M5-07). " +
                 "Required — there is no such thing as a class with nothing to say about it, and " +
                 "a card with a blank half is what the spec's constructor refuses.")]
        [SerializeField] private string _descriptionKey = "character.new.description";

        [SerializeField, Min(1f)] private float _maxHp = 100f;
        [SerializeField, Min(0.01f)] private float _speed = 6f;
        [SerializeField, Min(0.001f)] private float _accelTime = 0.06f;
        [SerializeField, Min(0.001f)] private float _decelTime = 0.08f;
        [SerializeField, Min(1f)] private float _turnSpeedDeg = 720f;

        [Tooltip("Points of shield. 0 means this class has no shield at all — only the " +
                 "Oathbound's Aegis has one in V1. The two fields below are ignored at 0.")]
        [SerializeField, Min(0f)] private float _shieldMax;
        [SerializeField, Min(0.01f)] private float _shieldRechargeDelay = 4f;
        [SerializeField, Min(0.01f)] private float _shieldRefillPerSecond = 15f;
        [SerializeField, Min(0f)] private float _hitIFrames = 0.5f;

        [Tooltip("Auto-aim (CC §3.2). Every class has these — unlike the shield above, there " +
                 "is no such thing as a class that does not aim, so none of them may be 0 " +
                 "except a weight a designer means to switch off.")]
        [SerializeField, Min(0.01f)] private float _acquireRange = 12f;
        [SerializeField, Min(0f)] private float _distanceWeight = 3f;
        [SerializeField, Min(0f)] private float _eliteBonus = 2f;
        [SerializeField, Min(0f)] private float _finisherBonus = 1f;

        [Tooltip("The current target's bonus — a challenger must beat it by this to steal " +
                 "focus. Zero makes the character twitch between similar targets (CC §3.3).")]
        [SerializeField, Min(0f)] private float _targetHysteresis = 1.5f;

        [Tooltip("Seconds between targeting decisions. 0.1 is CC §3.1's 10 Hz loop.")]
        [SerializeField, Min(0.01f)] private float _targetCadence = 0.1f;

        [Tooltip("The basic attack (CC §4.1). Every class has one — like targeting and unlike " +
                 "the shield above, there is no 'off' here. The Censer's numbers are the " +
                 "defaults: 13 damage, 3 swings a second, an 8 m 60° arc.")]
        [SerializeField] private WeaponKind _weaponKind = WeaponKind.Cone;
        [SerializeField, Min(0.01f)] private float _weaponDamage = 13f;
        [SerializeField, Min(0.01f)] private float _weaponSwingsPerSecond = 3f;
        [SerializeField, Min(0.01f)] private float _weaponRange = 8f;

        [Tooltip("The full opening angle of the arc, not the half-angle: 60 means 30° either side.")]
        [SerializeField, Range(1f, 360f)] private float _weaponConeAngleDeg = 60f;

        [Tooltip("How far through the swing the damage lands, as a fraction of the interval. " +
                 "0.4 is CC §4.2's readable-but-not-a-commitment windup.")]
        [SerializeField, Range(0f, 0.99f)] private float _weaponDamageFrame = 0.4f;

        [Tooltip("Shot numbers — Projectile weapons only. Leave all three at 0 for a Cone; a " +
                 "cone carrying a shot speed is refused rather than ignored. A Projectile needs " +
                 "a speed and a radius above 0 and the cone angle set to exactly 360, since an " +
                 "arc does not gate a shot. Spread must stay 0: it is reserved.")]
        [SerializeField, Min(0f)] private float _weaponShotSpeed;
        [SerializeField, Min(0f)] private float _weaponShotRadius;
        [SerializeField, Min(0f)] private float _weaponShotSpread;

        [Tooltip("The Focus ramp (CC §4.3): standing still speeds the swing up. Nothing to do " +
                 "with tap-to-focus, which is the targeting block above. Every class has one — " +
                 "set the multiplier to 1 for a class that should not ramp at all.")]
        [SerializeField, Min(0f)] private float _focusDelay = 0.4f;
        [SerializeField, Min(0.01f)] private float _focusRampTime = 1f;

        [Tooltip("Fire rate at full Focus as a multiple of the resting rate. 1.3 is CC §4.3's " +
                 "130%: the Censer's 3 swings a second becoming 3.9.")]
        [SerializeField, Min(1f)] private float _focusMaxMultiplier = 1.3f;

        [Tooltip("The movement skill (CC §5). Every class has exactly one, on a permanent " +
                 "button, and it never auto-casts. The Charge's numbers are the defaults: 10 m " +
                 "in 0.22 s, 20 damage and 5 m of knockback to everything passed through.")]
        [SerializeField] private MovementSkillKind _movementSkillKind = MovementSkillKind.Charge;
        [SerializeField, Min(0.01f)] private float _movementSkillDistance = 10f;
        [SerializeField, Min(0.01f)] private float _movementSkillDuration = 0.22f;
        [SerializeField, Min(0.01f)] private float _movementSkillCooldown = 2.5f;

        [Tooltip("Seconds a press stays live while the dash is unavailable — 0.15, so a tap " +
                 "just before the cooldown ends still fires. This and the i-frame trail below " +
                 "absorb touch latency; CC §5 says the dodge feels unreliable without them.")]
        [SerializeField, Min(0f)] private float _movementSkillInputBuffer = 0.15f;

        [Tooltip("Damage and knockback dealt to everything the dash passes through. Both may be " +
                 "0 for a movement skill that only repositions.")]
        [SerializeField, Min(0f)] private float _movementSkillDamage = 20f;
        [SerializeField, Min(0f)] private float _movementSkillKnockback = 5f;

        [Tooltip("Extra seconds of invulnerability after the dash ends — 0.05.")]
        [SerializeField, Min(0f)] private float _movementSkillIFrameTrail = 0.05f;

        [Tooltip("Seconds a Shroudstep's corpse decoy stands and taunts — 3 (CH §3.2). It is " +
                 "validated against the kind above: a Shroudstep must be above 0, and every " +
                 "other kind must be exactly 0. A Charge with a duration here is a forgotten " +
                 "field, and nothing would ever read it.")]
        [SerializeField, Min(0f)] private float _movementSkillDecoyDuration;

        [Tooltip("A Blink's fire pool (CH §3.3): how far it reaches, how long it burns, and what " +
                 "one pulse takes off — 3 m, 3 s and 4 for the Emberwright. It pulses every " +
                 "0.5 s, which is not authored. Validated against the kind above: a Blink must " +
                 "have all three above 0, and every other kind must have all three at exactly 0.")]
        [SerializeField, Min(0f)] private float _movementSkillPoolRadius;
        [SerializeField, Min(0f)] private float _movementSkillPoolDuration;
        [SerializeField, Min(0f)] private float _movementSkillPoolDamagePerPulse;

        [Tooltip("The class's minions (CH §3.2's Rise). Cap 0 means this class has none — only " +
                 "the Gravecaller's Wights do in V1, and every field below is ignored at 0. The " +
                 "cap is the switch for the reason the shield max above is: a zeroed block would " +
                 "have to be read against the class id to be understood.")]
        [SerializeField, Min(0)] private int _minionCap;
        [SerializeField] private string _minionSpecId = "minion.new";
        [SerializeField] private string _minionNameKey = "minion.new.name";
        [SerializeField, Min(0.01f)] private float _minionLifespan = 20f;

        [Tooltip("The fraction of kills that raise one — 0.25 is CH §3.2's 25 %. A fraction, " +
                 "never a percentage: 25 here would be a probability that is not one.")]
        [SerializeField, Range(0.01f, 1f)] private float _minionRiseChance = 0.25f;

        [SerializeField, Min(0.01f)] private float _minionMaxHp = 20f;
        [SerializeField, Min(0.01f)] private float _minionMoveSpeed = 3f;
        [SerializeField, Min(0.01f)] private float _minionDamage = 8f;
        [SerializeField, Min(0.01f)] private float _minionAttackInterval = 1f;
        [SerializeField, Min(0.01f)] private float _minionReach = 1.5f;

        [Tooltip("The class's Kindling (CH §3.3): consecutive weapon hits without taking damage " +
                 "stack weapon damage. Max stacks 0 means this class has none — only the " +
                 "Emberwright does in V1, and the per-stack below is ignored at 0. The count is " +
                 "the switch for the minion cap's reason.")]
        [SerializeField, Min(0)] private int _kindlingMaxStacks;

        [Tooltip("Weapon damage one stack is worth, as a fraction — 0.02 is CH §3.3's +2 %. " +
                 "Never a percentage: 2 here would be +200 % a hit.")]
        [SerializeField, Min(0f)] private float _kindlingPerStack = 0.02f;

        [Header("Veilrot (CH §3) — all five neutral means the Veil treats this class ordinarily")]
        [Tooltip("Where a fresh run opens on the meter, below 100. The Gravecaller's 15.")]
        [SerializeField, Min(0f)] private float _veilrotStart;

        [Tooltip("Every Veilrot gain is multiplied by this. 1 is neutral; the Oathbound's 0.6, " +
                 "the Gravecaller's 1.5. Above 0.")]
        [SerializeField, Min(0.01f)] private float _veilrotGainMultiplier = 1f;

        [Tooltip("The Sanctum's Cleanse price is multiplied by this, rounded, and never below 1. " +
                 "1 is neutral; the Oathbound's 0.5.")]
        [SerializeField, Min(0.01f)] private float _veilrotCleansePriceMultiplier = 1f;

        [Tooltip("Weapon damage per point on the meter, as a fraction — 0.01 is CH §3.2's +1 %. " +
                 "0 is neutral.")]
        [SerializeField, Min(0f)] private float _veilrotDamagePerPoint;

        [Tooltip("Veilrot one cast through a cooldown costs, once per cooldown. 0 means this " +
                 "class cannot buy one; the Emberwright's 5.")]
        [SerializeField, Min(0f)] private float _veilrotInstantCastCost;

        [Header("Unlock (GD §14.2) — a price of 0 means free, which is the starter")]
        [Tooltip("Soul Shards this class costs. 0 means it is always playable and every field " +
                 "below is ignored — the Oathbound's. The Gravecaller's 2000, the Emberwright's 3500.")]
        [SerializeField, Min(0)] private int _unlockShardPrice;

        [Tooltip("The depth whose reaching unlocks it for free, or 0 for none. The Emberwright's " +
                 "20. At most one deed: set this or the boss below, never both.")]
        [SerializeField, Min(0)] private int _unlockDeedStage;

        [Tooltip("The boss whose death unlocks it for free, or empty for none. The Gravecaller's " +
                 "'boss.choirmother' — which no mode authors until M7-03, so the deed cannot be " +
                 "done yet and the id is deliberately not validated against the catalog.")]
        [SerializeField] private string _unlockDeedBossId = "";

        /// <summary>
        /// The authored id text, exactly as it sits in the asset — for grouping and diagnostics
        /// before conversion. It is <em>not</em> known to be well-formed: only a
        /// <see cref="ToSpec"/> that returned tells you that.
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Builds the immutable spec core consumes. A fresh instance every call — this asset
        /// holds no runtime state and hands out nothing it keeps a reference to.
        /// </summary>
        /// <remarks>
        /// A <see cref="_shieldMax"/> of zero means the class has no shield, and produces a
        /// <see langword="null"/> <see cref="CharacterSpec.Shield"/> rather than a
        /// <see cref="ShieldSpec"/> full of zeroes. Zero is the switch instead of a separate
        /// "has shield" toggle because the toggle would allow a state the spec cannot
        /// represent — shield on, max zero — and a designer would have to keep the two
        /// agreeing by hand. The delay and refill fields simply go unread at zero.
        /// <para>
        /// The <see cref="TargetingSpec"/> has no such switch and is built unconditionally:
        /// every class aims (CC §3), so there is no "no targeting" state to express, and the
        /// spec's own constructor is what refuses a range or cadence of zero. The
        /// <see cref="WeaponSpec"/> is built the same way and for the same reason — CC §4 gives
        /// every class a basic attack — and so is the <see cref="FocusSpec"/>, one step further
        /// out: every character can stand still, so the ramp's off switch is a multiplier of 1
        /// rather than the absence of a spec. The <see cref="MovementSkillSpec"/> joins them:
        /// CC §5 opens with "every class has exactly one, on a permanent button", so there is no
        /// "no dash" to author either.
        /// </para>
        /// <para>
        /// A <see cref="_minionCap"/> of zero is the shield's switch a second time, and it is the
        /// same argument rather than a copy of it: CH §3.2's Rise is one class's signature, so a
        /// class with no minions produces a <see langword="null"/>
        /// <see cref="CharacterSpec.Minions"/> rather than a block of zeroes that would have to be
        /// read against the id to be understood (M5-02 rule 6). The cap is the switch rather than
        /// the hit points because it is the one field a <see cref="MinionSpec"/> cannot represent
        /// at zero — a cap of nothing is a class whose minions can never be alive.
        /// </para>
        /// <para>
        /// A <see cref="_kindlingMaxStacks"/> of zero is the same switch a third time (M6-07a rule 1):
        /// CH §3.3's Kindling is one class's signature, so every other class produces a
        /// <see langword="null"/> <see cref="CharacterSpec.Kindling"/>. The count rather than the
        /// per-stack, for the minion cap's reason — it is the field a <see cref="KindlingSpec"/>
        /// cannot represent at zero.
        /// </para>
        /// <para>
        /// The Veilrot block has no single switch, because none of its five dials is the one a
        /// <see cref="VeilrotSpec"/> cannot represent: <b>all five neutral is the switch</b>
        /// (M6-07c rule 1), and it produces a <see langword="null"/>
        /// <see cref="CharacterSpec.Veilrot"/> — which is also what the spec's own constructor
        /// refuses to be built as.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid. Always this exact type, never one of its subclasses:
        /// the caller cannot act on <em>which</em> field failed, only on <em>which asset</em>
        /// failed, and that is what the message leads with. The original is kept as the inner
        /// exception, so the field and its value survive into the log.
        /// </exception>
        public CharacterSpec ToSpec()
        {
            try
            {
                return new CharacterSpec(
                    new ContentId(_id),
                    new LocKey(_nameKey),
                    new LocKey(_descriptionKey),
                    _maxHp,
                    new MovementSpec(_speed, _accelTime, _decelTime, _turnSpeedDeg),
                    new TargetingSpec(
                        _acquireRange,
                        _distanceWeight,
                        _eliteBonus,
                        _finisherBonus,
                        _targetHysteresis,
                        _targetCadence),
                    new WeaponSpec(
                        _weaponKind,
                        _weaponDamage,
                        _weaponSwingsPerSecond,
                        _weaponRange,
                        _weaponConeAngleDeg,
                        _weaponDamageFrame,
                        _weaponShotSpeed,
                        _weaponShotRadius,
                        _weaponShotSpread),
                    new FocusSpec(_focusDelay, _focusRampTime, _focusMaxMultiplier),
                    new MovementSkillSpec(
                        _movementSkillKind,
                        _movementSkillDistance,
                        _movementSkillDuration,
                        _movementSkillCooldown,
                        _movementSkillInputBuffer,
                        _movementSkillDamage,
                        _movementSkillKnockback,
                        _movementSkillIFrameTrail,
                        _movementSkillDecoyDuration,
                        _movementSkillPoolRadius,
                        _movementSkillPoolDuration,
                        _movementSkillPoolDamagePerPulse),
                    _shieldMax > 0f
                        ? new ShieldSpec(_shieldMax, _shieldRechargeDelay, _shieldRefillPerSecond)
                        : null,
                    _hitIFrames,
                    _minionCap > 0
                        ? new MinionSpec(
                            new ContentId(_minionSpecId),
                            new LocKey(_minionNameKey),
                            _minionCap,
                            _minionLifespan,
                            _minionRiseChance,
                            _minionMaxHp,
                            _minionMoveSpeed,
                            _minionDamage,
                            _minionAttackInterval,
                            _minionReach)
                        : null,
                    _kindlingMaxStacks > 0
                        ? new KindlingSpec(_kindlingPerStack, _kindlingMaxStacks)
                        : null,
                    VeilrotIsNeutral()
                        ? null
                        : new VeilrotSpec(
                            _veilrotStart,
                            _veilrotGainMultiplier,
                            _veilrotCleansePriceMultiplier,
                            _veilrotDamagePerPoint,
                            _veilrotInstantCastCost),
                    _unlockShardPrice > 0
                        ? new UnlockSpec(
                            _unlockShardPrice,
                            _unlockDeedStage,
                            string.IsNullOrEmpty(_unlockDeedBossId)
                                ? default
                                : new ContentId(_unlockDeedBossId))
                        : null);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching
                // here. Uncaught, a designer reading the Console sees "maxHp must be greater
                // than zero" with a stack trace through the boot installer and no way to tell
                // which of the catalog's assets to open.
                throw new ArgumentException(
                    $"CharacterDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <summary>Whether all five Veilrot dials sit at the value that changes nothing.</summary>
        /// <remarks>
        /// The Unlock block beside it has a single switch, the price, for the minion cap's reason: it
        /// is the one field an <see cref="UnlockSpec"/> cannot represent at zero, and a free class is
        /// one that authors none (M6-09a rule 3).
        /// </remarks>
        private bool VeilrotIsNeutral() =>
            _veilrotStart == 0f
            && _veilrotGainMultiplier == 1f
            && _veilrotCleansePriceMultiplier == 1f
            && _veilrotDamagePerPoint == 0f
            && _veilrotInstantCastCost == 0f;

        /// <remarks>
        /// Only the ids, and only their shape. A malformed id is the one authoring mistake that
        /// cannot be caught any earlier — a bad number is visibly a bad number in the
        /// Inspector, while <c>Character.Oathbound</c> looks perfectly reasonable and fails at
        /// boot. The asset is passed as the log context so clicking the warning selects it.
        /// <para>
        /// The minion's id is asked about only when <see cref="_minionCap"/> says there are
        /// minions: the field keeps its authoring placeholder on every class without them, and a
        /// warning about an id nothing converts would be noise on two shipped assets out of three.
        /// </para>
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"CharacterDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'character.oathbound'.",
                    this);
            }

            if (_minionCap > 0 && !ContentId.IsValid(_minionSpecId))
            {
                Debug.LogWarning(
                    $"CharacterDefinition '{name}': '{_minionSpecId}' is not a valid content id " +
                    "for its minion. Expected lowercase dot-separated segments, at least two, " +
                    "e.g. 'minion.wight'.",
                    this);
            }

            // Asked only when the count says the class has Kindling, for the minion id's reason: the
            // per-stack keeps its authoring default on every class without one. `!(x > 0f)` so NaN
            // is caught with zero, which [Min(0f)] lets through.
            if (_kindlingMaxStacks > 0 && !(_kindlingPerStack > 0f))
            {
                Debug.LogWarning(
                    $"CharacterDefinition '{name}': Kindling has {_kindlingMaxStacks} stack(s) worth " +
                    $"{_kindlingPerStack} each, so the ramp would do nothing. Set a per-stack above " +
                    "0, or set max stacks to 0 for a class without Kindling.",
                    this);
            }
        }
    }
}
