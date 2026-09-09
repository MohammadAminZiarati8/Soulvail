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
        /// every class a basic attack.
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
                        _weaponDamageFrame),
                    _shieldMax > 0f
                        ? new ShieldSpec(_shieldMax, _shieldRechargeDelay, _shieldRefillPerSecond)
                        : null,
                    _hitIFrames);
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

        /// <remarks>
        /// Only the id, and only its shape. A malformed id is the one authoring mistake that
        /// cannot be caught any earlier — a bad number is visibly a bad number in the
        /// Inspector, while <c>Character.Oathbound</c> looks perfectly reasonable and fails at
        /// boot. The asset is passed as the log context so clicking the warning selects it.
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
        }
    }
}
