using System;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`,
// so a ScriptableObject declared that way is never linked to a MonoScript: Husk.asset would
// serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One enemy archetype as a designer tunes it: the Inspector half of <see cref="EnemySpec"/>.
    /// Converted once at boot into the immutable spec core consumes and registered in the
    /// <c>ContentCatalog</c>. See AR §10.1 and ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second definition type in the project, and deliberately the same shape as the first:
    /// <c>[SerializeField] private</c> fields, one <see cref="ToSpec"/> that is the only way out,
    /// an <see cref="OnValidate"/> that checks the id, and every failure rewrapped as a plain
    /// <see cref="ArgumentException"/> naming the asset. A designer who has learned to read one
    /// Console message has learned to read all of them.
    /// </para>
    /// <para>
    /// The <c>[Min]</c> and <c>[Range]</c> attributes are Inspector affordances, not guarantees.
    /// They clamp what can be dragged or typed; they do nothing to a value written through
    /// <c>SerializedProperty</c>, arriving from a merge, or hand-edited into the YAML. The real
    /// validation is in <see cref="EnemySpec"/>'s constructor, which is why <see cref="ToSpec"/>
    /// is the only way to read this asset (M0-11).
    /// </para>
    /// <para>
    /// The defaults below are GD §8.1's Husk rather than neutral zeroes, because the fields
    /// <see cref="EnemySpec"/> refuses at zero — <c>maxHp</c> and <c>reach</c> — would otherwise
    /// make every freshly created asset invalid content until all of them were filled in. A new
    /// archetype starts from a Husk and is edited into whatever it is.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Enemy", fileName = "Enemy")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "enemy.new";
        [SerializeField] private string _nameKey = "enemy.new.name";

        [Tooltip("Starting maximum health — GD §8.1's Base HP column. Seeds the agent's Stat, " +
                 "so depth scaling and Elite affixes modify that rather than this.")]
        [SerializeField, Min(1f)] private float _maxHp = 36f;

        [Tooltip("Top speed in m/s. 0 is legal and means something that never moves, which is " +
                 "what the Static behaviour below already says.")]
        [SerializeField, Min(0f)] private float _moveSpeed = 3.5f;

        [Tooltip("GD §8.1's target priority, 1–8. The knob that makes auto-aim feel " +
                 "intelligent: it dominates the scoring formula, so a Choir at 8 outranks a " +
                 "Husk at 1 from much further away.")]
        [SerializeField, Range(1, 8)] private int _targetPriority = 1;

        [Tooltip("GD §8.1's Threat Cost column — what one of these costs a stage's threat " +
                 "budget. Husk 4, Spitter 7, Bloater 8, Revenant 18. It prices this archetype " +
                 "against the others rather than saying how dangerous one is on its own.")]
        [SerializeField, Min(1)] private int _threatCost = 4;

        [SerializeField] private bool _isElite;

        [Tooltip("Damage one strike deals. 0 is legal and means an enemy that never hurts the " +
                 "player directly — GD §8.1's Choir.")]
        [SerializeField, Min(0f)] private float _contactDamage = 8f;

        [SerializeField, Min(0.01f)] private float _reach = 1.2f;

        [Tooltip("Seconds of telegraph before the damage frame. 0 is an untelegraphed hit — " +
                 "never right for a melee enemy, but a contact explosion has no windup.")]
        [SerializeField, Min(0f)] private float _windupTime = 0.4f;

        [SerializeField, Min(0f)] private float _recoverTime = 0.6f;

        [Tooltip("Which code moves it. Static stands where it spawned; Chaser beelines at the " +
                 "player and is implemented in M1-18.")]
        [SerializeField] private EnemyBehaviourKind _behaviour = EnemyBehaviourKind.Static;

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
        /// Every field is passed straight through with no switch and no default: unlike
        /// <c>CharacterDefinition</c>'s shield, an enemy has no optional block, so there is no
        /// authored value here that means "this part does not apply".
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid. Always this exact type, never one of its subclasses:
        /// the caller cannot act on <em>which</em> field failed, only on <em>which asset</em>
        /// failed, and that is what the message leads with. The original is kept as the inner
        /// exception, so the field and its value survive into the log.
        /// </exception>
        public EnemySpec ToSpec()
        {
            try
            {
                return new EnemySpec(
                    new ContentId(_id),
                    new LocKey(_nameKey),
                    _maxHp,
                    _moveSpeed,
                    _targetPriority,
                    _threatCost,
                    _isElite,
                    _contactDamage,
                    _reach,
                    _windupTime,
                    _recoverTime,
                    _behaviour);
            }
            catch (ArgumentException inner)
            {
                // The asset name, first thing in the message, is the whole point of catching
                // here. Uncaught, a designer reading the Console sees "targetPriority must be
                // between 1 and 8" with a stack trace through the boot installer and no way to
                // tell which of the catalog's assets to open.
                throw new ArgumentException(
                    $"EnemyDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <remarks>
        /// Only the id, and only its shape — the same bargain <c>CharacterDefinition</c> makes. A
        /// bad number is visibly a bad number in the Inspector, while <c>Enemy.Husk</c> looks
        /// perfectly reasonable and fails at boot. The asset is passed as the log context so
        /// clicking the warning selects it.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"EnemyDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'enemy.husk'.",
                    this);
            }
        }
    }
}
