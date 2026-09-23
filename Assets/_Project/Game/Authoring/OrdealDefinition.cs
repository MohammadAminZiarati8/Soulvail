using System;
using Soulvail.Core.Content;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. A ScriptableObject declared
// with a file-scoped namespace is never linked to its MonoScript, and every asset of it loads as null
// with nothing reported (M0-11, Traps §5).
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One of GD §13.4's Ordeals as a designer tunes it: the Inspector half of
    /// <see cref="OrdealSpec"/>. Listed on a <see cref="ModeDefinition"/>, whose pool it joins, and
    /// converted with it at boot. See AR §10.1 and ADR-0006.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Five dials, every one of them neutral by default, and the spec refuses an Ordeal that
    /// leaves them all there</b> (M6-06a rules 2 and 3). A designer turns one — Famine's income,
    /// Vigil's offer, Swarm's crowd and its Husk, Hunger's Rot — and leaves the rest alone. There is
    /// no kind dropdown, because the thing a kind would be for is a <c>switch</c> nobody may write.
    /// </para>
    /// <para>
    /// <b>It validates nothing <see cref="OrdealSpec"/> already validates</b>, <see cref="ModeDefinition"/>'s
    /// bargain: the spec's constructor is the single account of a legal Ordeal, and every failure is
    /// rewrapped here as a plain <see cref="ArgumentException"/> naming the asset.
    /// </para>
    /// </remarks>
    [CreateAssetMenu(menuName = "Soulvail/Content/Ordeal", fileName = "Ordeal")]
    public sealed class OrdealDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "ordeal.new";
        [SerializeField] private string _nameKey = "ordeal.new.name";
        [SerializeField] private string _descriptionKey = "ordeal.new.description";

        [Header("Dials — turn one, leave the rest neutral (GD §13.4)")]
        [Tooltip("What a stage clear pays, times this. 1 is unchanged; Famine is 0.6.")]
        [SerializeField, Min(0.01f)] private float _essenceMultiplier = 1f;

        [Tooltip("How many nodes a level-up offers. 0 is unchanged; Vigil is 2.")]
        [SerializeField, Min(0)] private int _offerCount;

        [Tooltip("Added to GD §12.2's concurrency cap, device permitting. 0 is unchanged; Swarm is 8.")]
        [SerializeField, Min(0)] private int _concurrencyBonus;

        [Tooltip("What every Veilrot gain is multiplied by. 1 is unchanged; Hunger is 1.5.")]
        [SerializeField, Min(0.01f)] private float _veilrotMultiplier = 1f;

        [Tooltip("The archetype whose threat cost moves, e.g. 'enemy.husk'. Empty for none — and " +
                 "then the multiplier below must stay 1.")]
        [SerializeField] private string _threatCostTarget = string.Empty;

        [Tooltip("What that archetype's threat cost is multiplied by. 1 is unchanged; Swarm is 0.5.")]
        [SerializeField, Min(0.01f)] private float _threatCostMultiplier = 1f;

        /// <summary>
        /// The authored id text, exactly as it sits in the asset — for grouping and diagnostics
        /// before conversion. Only a <see cref="ToSpec"/> that returned says it is well-formed.
        /// </summary>
        public string Id => _id;

        /// <summary>
        /// Builds the immutable spec core consumes. A fresh instance every call.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// Any authored field is invalid. Always this exact type, with the asset's name first and the
        /// original kept as the inner exception — <see cref="ModeDefinition.ToSpec"/>'s shape.
        /// </exception>
        public OrdealSpec ToSpec()
        {
            try
            {
                return new OrdealSpec(
                    new ContentId(_id),
                    new LocKey(_nameKey),
                    new LocKey(_descriptionKey),
                    _essenceMultiplier,
                    _offerCount,
                    _concurrencyBonus,
                    _veilrotMultiplier,
                    string.IsNullOrEmpty(_threatCostTarget) ? default : new ContentId(_threatCostTarget),
                    _threatCostMultiplier);
            }
            catch (ArgumentException inner)
            {
                throw new ArgumentException(
                    $"OrdealDefinition '{name}' is not valid content: {inner.Message}",
                    inner);
            }
        }

        /// <remarks>
        /// Only the id, and only its shape — <see cref="ModeDefinition"/>'s bargain. A neutral
        /// Ordeal is visibly neutral in the Inspector; <c>Ordeal.Famine</c> looks fine and fails at
        /// boot.
        /// </remarks>
        private void OnValidate()
        {
            if (!ContentId.IsValid(_id))
            {
                Debug.LogWarning(
                    $"OrdealDefinition '{name}': '{_id}' is not a valid content id. Expected " +
                    "lowercase dot-separated segments, at least two, e.g. 'ordeal.famine'.",
                    this);
            }
        }
    }
}
