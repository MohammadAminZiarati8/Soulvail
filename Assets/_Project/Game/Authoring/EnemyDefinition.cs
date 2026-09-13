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

        [Tooltip("Metres within which it notices the player and stops being idle. 30 on all " +
                 "three archetypes — comfortably beyond what the camera shows, so in practice " +
                 "everything in the arena is already coming for you.")]
        [SerializeField, Min(0.01f)] private float _aggroRange = 30f;

        [Tooltip("Which code moves it. Static stands where it spawned; Chaser beelines at the " +
                 "player and is implemented in M1-18. Spitter (M2-07b) and Bloater (M2-08) are " +
                 "declared but not yet runnable — both assets ship Static until their PR.")]
        [SerializeField] private EnemyBehaviourKind _behaviour = EnemyBehaviourKind.Static;

        [Header("Projectile — leave the standoff at 0 on an archetype that throws nothing")]
        [Tooltip("Metres at which it stops approaching and fires. 0 means this archetype has no " +
                 "projectile at all and the three fields below are ignored. 14 for the Spitter.")]
        [SerializeField, Min(0f)] private float _projectileStandoffRange;

        [Tooltip("Metres per second of flight. With the standoff above, this is the dodge window.")]
        [SerializeField, Min(0.01f)] private float _projectileSpeed = 12f;

        [Tooltip("Metres from the impact point that still count as a hit — the forgiveness on a " +
                 "shot, not the size it is drawn at.")]
        [SerializeField, Min(0.01f)] private float _projectileRadius = 1.6f;

        [Header("Explosion — leave the radius at 0 on an archetype that does not explode")]
        [Tooltip("Metres the blast reaches. 0 means this archetype does not explode. 3 for the " +
                 "Bloater (GD §8.1).")]
        [SerializeField, Min(0f)] private float _explosionRadius;

        [Header("Look — one shared body, told apart by colour and size (GD §11.3)")]
        [Tooltip("What this archetype's body is tinted. The Husk authors M_BoneGrey's own " +
                 "colour, so the shared prefab is drawn exactly as it was before the look " +
                 "system existed.")]
        [SerializeField] private Color _tint = new Color(0.43137255f, 0.41568628f, 0.3882353f, 1f);

        [Tooltip("A multiple of the prefab's own scale. 1 is the Husk; a Bloater is fat and a " +
                 "Spitter is slight.")]
        [SerializeField, Min(0.01f)] private float _bodyScale = 1f;

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
        /// <para>
        /// <b>An enemy has two optional blocks as of M2-06</b>, where this note used to say it had
        /// none. A <c>_projectileStandoffRange</c> of zero means the archetype throws nothing and
        /// produces a <see langword="null"/> <see cref="EnemySpec.Projectile"/> rather than a
        /// <see cref="ProjectileSpec"/> full of zeroes; an <c>_explosionRadius</c> of zero says the
        /// same about the blast. Zero is the switch instead of a separate "has projectile" toggle
        /// for the reason <c>CharacterDefinition</c>'s shield gives: a toggle allows a state the
        /// spec cannot represent — <em>on, and zero</em> — and leaves a designer keeping two fields
        /// agreeing with each other by hand.
        /// </para>
        /// <para>
        /// The tint and the scale are <em>not</em> passed: core has no opinion about colour, and
        /// <see cref="ToLook"/> is the door they leave by.
        /// </para>
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
                    _aggroRange,
                    _behaviour,
                    _projectileStandoffRange > 0f
                        ? new ProjectileSpec(
                            _projectileStandoffRange,
                            _projectileSpeed,
                            _projectileRadius)
                        : null,
                    _explosionRadius > 0f
                        ? new ExplosionSpec(_explosionRadius)
                        : null);
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

        /// <summary>
        /// The half of this asset core never sees: what colour and size the body is drawn at.
        /// </summary>
        /// <remarks>
        /// A second conversion beside <see cref="ToSpec"/> rather than two more fields on
        /// <see cref="EnemySpec"/>, because <c>Soulvail.Core</c> has no engine reference and a
        /// <see cref="Color"/> cannot cross into it (AR §2). Unvalidated for the reason
        /// <see cref="EnemyLook"/>'s constructor gives: a wrong colour is wrong on screen, where a
        /// wrong number would be wrong in a rule.
        /// </remarks>
        public EnemyLook ToLook() => new EnemyLook(_tint, _bodyScale);

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
