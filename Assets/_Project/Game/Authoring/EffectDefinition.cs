using Soulvail.Core.Effects;
using UnityEngine;

// Block namespace, deliberately — see the note in CharacterDefinition.cs. Unity 6.3's script
// importer parses a file to find the type it declares and does not understand `namespace X;`,
// so a ScriptableObject declared that way is never linked to a MonoScript: an asset referencing
// it would serialise as `m_Script: {fileID: 0}` and load as null, with nothing reported (M0-11,
// Traps §5). This one is abstract and has no assets of its own, but every subclass inherits the
// same importer, and a base declared file-scoped would break all of them at once.
namespace Soulvail.Game.Authoring
{
    /// <summary>
    /// One effect primitive's Inspector half: a <see cref="ScriptableObject"/> per primitive, which
    /// a node references as an asset. Converted at boot into the immutable <see cref="IEffect"/>
    /// core consumes. See AR §13 and ADR-0009.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Polymorphism by asset type, and the two alternatives were rejected for named reasons.</b>
    /// A <c>[SerializeReference]</c> list of <see cref="IEffect"/>s has no built-in type picker in
    /// Unity 6's Inspector, so authoring one needs a custom drawer — tooling M3-02b did not exist to
    /// grow (M3-00a's finding). A flat union struct with a kind enum makes <c>ToEffect</c> a
    /// <c>switch (kind)</c>, which is ADR-0009's ban arriving on the authoring side of the seam
    /// rather than the run side. A subclass per primitive has neither problem: the asset reads in
    /// the Inspector as what it is, is reusable across nodes — <em>+15 % fire rate</em> is one asset
    /// three nodes may point at — and a new primitive is a new subclass with no edit to any existing
    /// file, which is the same promise <c>EffectRegistry</c> makes one layer down.
    /// </para>
    /// <para>
    /// <b>Abstract, with no <c>[CreateAssetMenu]</c>.</b> There is no such thing as a generic
    /// effect asset; every menu entry belongs to a concrete primitive, and each arrives with its
    /// primitive rather than ahead of it (M3-05's rule).
    /// </para>
    /// <para>
    /// <see cref="ToEffect"/> is the only way out, the bargain every definition type in this folder
    /// makes: the fields are <c>[SerializeField] private</c>, the core constructor is the one
    /// account of what a legal effect is, and the failure is rewrapped with the asset's name so a
    /// designer reading the Console knows which file to open (M0-11).
    /// </para>
    /// </remarks>
    public abstract class EffectDefinition : ScriptableObject
    {
        /// <summary>
        /// Builds the immutable effect core consumes. A fresh instance every call — a definition
        /// holds no runtime state and hands out nothing it keeps a reference to (ADR-0006).
        /// </summary>
        /// <exception cref="System.ArgumentException">
        /// Any authored field is invalid. Always this exact type on the way out, never one of its
        /// subclasses, and the message leads with the asset's name.
        /// </exception>
        public abstract IEffect ToEffect();
    }
}
