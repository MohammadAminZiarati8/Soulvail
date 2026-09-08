using Soulvail.Game.Presentation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

// Block namespace, deliberately — see the note in BootScope.cs. Unity 6.3's script importer
// cannot find the type in a file-scoped namespace, and the Menu scene's reference to this
// component would silently deserialise as null (M0-11).
namespace Soulvail.Game.Composition
{
    /// <summary>
    /// The scope the menu lives in. A child of <see cref="BootScope"/>, created with the Menu scene
    /// and disposed when it unloads. See AR §7.
    /// </summary>
    /// <remarks>
    /// <para>
    /// No parent is assigned in the scene, for the same reason <c>RunScope</c> assigns none: a
    /// <see cref="LifetimeScope"/> without one asks <c>VContainerSettings</c> for the root, so this
    /// resolves against <see cref="BootScope"/> whether the Menu was reached from Boot or opened
    /// and played directly.
    /// </para>
    /// <para>
    /// There is no <c>MenuInstaller</c> beside it. The split that <c>RunInstaller</c> earns — a
    /// static half an EditMode test can build a container from — buys nothing for a scope whose one
    /// registration <em>is</em> a scene object; a headless test of this class would be a test of
    /// <c>RegisterComponent</c>. The Menu's behaviour is proven by playing it (M0-17's PlayMode
    /// tests) instead.
    /// </para>
    /// </remarks>
    public sealed class MenuScope : LifetimeScope
    {
        [SerializeField] private MenuPresenter _menu;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_menu == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuScope)} has no {nameof(MenuPresenter)} assigned. Drag the Canvas " +
                    "in this scene onto its Menu field — without it nothing injects the presenter " +
                    "and the Descend button does nothing.");
            }

            // The scene owns this object's lifetime, and VContainer does not dispose what it did
            // not construct, so registering the live component is exactly right: the container
            // injects it and destroys nothing.
            builder.RegisterComponent(_menu);
        }
    }
}
