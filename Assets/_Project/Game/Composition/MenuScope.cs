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
    /// static half an EditMode test can build a container from — buys nothing for a scope whose
    /// registrations <em>are</em> scene objects; a headless test of this class would be a test of
    /// <c>RegisterComponent</c>. The Menu's behaviour is proven by playing it (M0-17's PlayMode
    /// tests) instead.
    /// </para>
    /// <para>
    /// <b>Two presenters as of M5-07</b>, and the second is required rather than optional — unlike
    /// <c>RunScope</c>'s screens, which may be left undressed so the Run scene can be played
    /// directly. There is no equivalent workflow here: the Menu scene exists to be walked through,
    /// and a class-select screen nothing injects is a <c>Descend</c> that opens a dead panel.
    /// </para>
    /// </remarks>
    public sealed class MenuScope : LifetimeScope
    {
        [SerializeField] private MenuPresenter _menu;

        [SerializeField] private ClassSelectPresenter _classSelect;

        protected override void Configure(IContainerBuilder builder)
        {
            if (_menu == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuScope)} has no {nameof(MenuPresenter)} assigned. Drag the Canvas " +
                    "in this scene onto its Menu field — without it nothing injects the presenter " +
                    "and the Descend button does nothing.");
            }

            if (_classSelect == null)
            {
                throw new MissingReferenceException(
                    $"{nameof(MenuScope)} has no {nameof(ClassSelectPresenter)} assigned. Drag the " +
                    "ClassSelect object in this scene onto its Class Select field — without it " +
                    "nothing injects the screen and Descend opens a panel that cannot start a run.");
            }

            // The scene owns both objects' lifetimes, and VContainer does not dispose what it did
            // not construct, so registering the live components is exactly right: the container
            // injects them and destroys nothing.
            builder.RegisterComponent(_menu);
            builder.RegisterComponent(_classSelect);
        }
    }
}
