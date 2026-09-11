using System;
using System.Collections.Generic;
using Soulvail.Core.Content;
using UnityEngine;

// File-scoped, unlike EnemyDefinition beside it: neither type here derives from
// UnityEngine.Object, so Unity's script importer never has to find them and the block-namespace
// rule of Traps §5 does not apply. The same split EnemyViews and EnemyView make one folder over.
namespace Soulvail.Game.Authoring;

/// <summary>
/// How one archetype is told apart on screen: a tint and a scale on the one shared body. Game side
/// only — core has no opinion about colour, and an <c>EnemySpec</c> carries neither of these.
/// </summary>
/// <remarks>
/// <para>
/// <b>A tint and a scale rather than a prefab per archetype</b>, which is what GD §11.3 already
/// asks for: <em>"one shared material and texture atlas across all enemy variants, with
/// colour/variation driven by per-instance properties."</em> It costs no draw call, no second pool
/// and no new art, and it is what makes a fat rust-coloured Bloater legible as <em>do not melee
/// this</em> next to a grey Husk. Ruled by the owner at M2-00c against importing the KayKit
/// skeletons now: the pack has no Bloater, and three bodies would mean three prefabs, three
/// animator controllers and a pool each.
/// </para>
/// <para>
/// A <c>readonly struct</c> because it is two numbers with no identity, looked up once per spawn —
/// the shape <c>EnemySense</c> and <c>Modifier</c> have, and for the same reason: a class would be
/// one allocation per archetype at boot and a dereference per rental, both for nothing.
/// </para>
/// </remarks>
public readonly struct EnemyLook
{
    /// <summary>
    /// <c>M_BoneGrey</c>'s own base colour, so an archetype that authors no tint is drawn exactly
    /// as the untinted prefab was before M2-06.
    /// </summary>
    /// <remarks>
    /// The one place this project writes a material's number down twice, and it is deliberate: the
    /// alternative is reading the shared material at boot, which would make the default depend on a
    /// <c>Renderer</c> reference the look book has no business holding. If <c>M_BoneGrey</c> is ever
    /// recoloured, <c>EnemyLookTests</c>' Husk row is what says so — it asserts the shipped asset's
    /// tint, and the Husk authors this value.
    /// </remarks>
    private static readonly Color BoneGrey = new Color(0.43137255f, 0.41568628f, 0.3882353f, 1f);

    /// <param name="tint">
    /// What the body's <c>_BaseColor</c> is driven to. Written through a
    /// <c>MaterialPropertyBlock</c>, never by assigning a material, so every archetype still shares
    /// one asset and one draw call.
    /// </param>
    /// <param name="bodyScale">
    /// A multiple of the prefab's own scale: 1 is the Husk, 1.35 is a Bloater half again as wide.
    /// Not validated here — see the remarks.
    /// </param>
    /// <remarks>
    /// <b>Nothing is refused, and that is the one place this type departs from every spec in
    /// core.</b> A nonsense tint is a visibly wrong colour and a nonsense scale is a visibly wrong
    /// size, both on the frame they arrive; neither can make a rule answer wrongly, because no rule
    /// reads them. The cost of a throw here is a run that ends on a cosmetic mistake, which rule 9
    /// already decided against for the missing-look case.
    /// </remarks>
    public EnemyLook(Color tint, float bodyScale)
    {
        Tint = tint;
        BodyScale = bodyScale;
    }

    /// <summary>Grey, scale 1 — what an id nobody authored a look for gets.</summary>
    /// <remarks>
    /// <c>default(EnemyLook)</c> is <em>not</em> this: it is transparent black at scale zero, an
    /// invisible enemy. That is the struct-with-an-invariant trap AR §18.3 names, and the reason
    /// <see cref="EnemyLookBook.For"/> returns this explicitly rather than letting a failed
    /// dictionary lookup produce the default.
    /// </remarks>
    public static EnemyLook Default => new EnemyLook(BoneGrey, 1f);

    /// <summary>What the body's base colour is driven to.</summary>
    public Color Tint { get; }

    /// <summary>A multiple of the prefab's own scale; 1 leaves the body as authored.</summary>
    public float BodyScale { get; }
}

/// <summary>
/// Archetype id → its look, built once at boot from the same definitions the <c>ContentCatalog</c>
/// is built from. The colour half of an <c>EnemyDefinition</c>, kept out of core.
/// </summary>
/// <remarks>
/// <para>
/// A separate object rather than a field on the catalog, because the catalog lives in core and a
/// <see cref="Color"/> cannot: <c>Soulvail.Core</c> is <c>noEngineReferences</c> (AR §2). The split
/// is the hexagon doing its job — what an archetype <em>is</em> goes to core, what it <em>looks
/// like</em> stays in Unity.
/// </para>
/// <para>
/// Registered as a root singleton by <c>BootInstaller</c> and resolved by <c>EnemyViews</c>, which
/// is the only reader: a look is applied at the moment a body is rented, and nothing else in the
/// game asks what colour an archetype is.
/// </para>
/// </remarks>
public sealed class EnemyLookBook
{
    private readonly Dictionary<ContentId, EnemyLook> _looks;

    /// <param name="looks">
    /// Every authored look, keyed by archetype id. Copied; the caller's dictionary is not retained,
    /// so a boot list mutated afterwards cannot change what the arena draws.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="looks"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Two entries share an id, or an entry is keyed by <c>default(ContentId)</c>. The catalog's own
    /// rule, repeated here because this is a second index over the same assets and a duplicate would
    /// otherwise keep whichever one the source enumerated last, in silence.
    /// </exception>
    public EnemyLookBook(IReadOnlyDictionary<ContentId, EnemyLook> looks)
    {
        if (looks is null)
        {
            throw new ArgumentNullException(nameof(looks));
        }

        _looks = new Dictionary<ContentId, EnemyLook>(looks.Count);

        foreach (KeyValuePair<ContentId, EnemyLook> entry in looks)
        {
            if (entry.Key.Value is null)
            {
                throw new ArgumentException(
                    "A look is keyed by default(ContentId), which names no archetype. Every entry " +
                    "must carry the id of the EnemyDefinition it came from.",
                    nameof(looks));
            }

            if (_looks.ContainsKey(entry.Key))
            {
                throw new ArgumentException(
                    $"Two looks are keyed '{entry.Key.Value}'. One archetype has one look; a " +
                    "duplicate means two definitions share an id, which ContentCatalog refuses " +
                    "outright.",
                    nameof(looks));
            }

            _looks.Add(entry.Key, entry.Value);
        }
    }

    /// <summary>
    /// The look authored for <paramref name="specId"/>, or <see cref="EnemyLook.Default"/> for an id
    /// nobody authored one for.
    /// </summary>
    /// <remarks>
    /// Never throws, including on <c>default(ContentId)</c>. A missing colour is not worth ending a
    /// run over (M2-06 rule 9) — the archetype is drawn as an ordinary grey body, which is legible
    /// as <em>some enemy</em> even when it is the wrong one, where an exception on the spawn path
    /// would take the arena down over a cosmetic gap.
    /// </remarks>
    public EnemyLook For(ContentId specId)
        => _looks.TryGetValue(specId, out EnemyLook look) ? look : EnemyLook.Default;
}
