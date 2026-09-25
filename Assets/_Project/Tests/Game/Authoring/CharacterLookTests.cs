using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Soulvail.Core.Content;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Composition;
using Soulvail.Game.Views;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Soulvail.Tests.Game.Authoring;

/// <summary>
/// RS-02b rules 1–3 and 5: the book that says which body a class wears, the definition it is built
/// from, the one choice of class, and the two animator views finding the player above their body.
/// </summary>
/// <remarks>
/// <para>
/// <b>The asset row reads the shipped classes</b>, <see cref="EnemyLookTests"/>' bargain: the three
/// wear the Knight until M7's art rulings give them their own, and a stray drag onto one of them is
/// a red row here rather than a class that plays as a stranger.
/// </para>
/// <para>
/// <b>The view rows are EditMode, on controllers built here</b>, as <c>PlayerAnimatorViewTests</c>
/// builds its own: with a controller assigned an Animator's parameters round-trip outside play
/// mode. <c>Awake</c> never runs here (Traps §5), so each view finds its <see cref="PlayerView"/>
/// through the lazy lookup in <c>Step</c> — the path rule 5 names beside <c>Awake</c>.
/// </para>
/// </remarks>
[TestFixture]
public sealed class CharacterLookTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    private const string KnightPath = "Assets/_Project/Prefabs/Player/Bodies/Knight.prefab";
    private const string PlayerPath = "Assets/_Project/Prefabs/Player/Player.prefab";
    private const string OathboundPath = "Assets/_Project/Data/Characters/Oathbound.asset";
    private const string GravecallerPath = "Assets/_Project/Data/Characters/Gravecaller.asset";
    private const string EmberwrightPath = "Assets/_Project/Data/Characters/Emberwright.asset";

    private static readonly ContentId Descent = new ContentId("mode.descent");
    private static readonly ContentId OathboundId = new ContentId("character.oathbound");
    private static readonly ContentId GravecallerId = new ContentId("character.gravecaller");

    private static readonly int SpeedId = Animator.StringToHash("Speed");
    private static readonly int MoveXId = Animator.StringToHash("MoveX");
    private static readonly int MoveZId = Animator.StringToHash("MoveZ");

    /// <summary>
    /// Everything a row created — bodies, loose definitions and controllers alike. One list and one
    /// teardown, <see cref="EnemyLookTests"/>' reason.
    /// </summary>
    private readonly List<Object> _created = new List<Object>();

    private DomainEventHub _hub;

    [TearDown]
    public void DestroyCreatedObjects()
    {
        _hub?.Dispose();
        _hub = null;

        foreach (Object created in _created)
        {
            if (created != null)
            {
                Object.DestroyImmediate(created);
            }
        }

        _created.Clear();
    }

    // ---- Rule 1: the book ----------------------------------------------------------------------

    [Test]
    public void Book_RefusesADefaultId()
    {
        var looks = new Dictionary<ContentId, CharacterLook> { [default] = new CharacterLook(Knight()) };

        // Refused where the book is built, not where it is read: a look under no id would be asked
        // for by nobody, which is a body silently never worn.
        Assert.Throws<ArgumentException>(() => new CharacterLookBook(looks));
    }

    [Test]
    public void Book_RefusesADuplicateId()
    {
        // A Dictionary cannot hold the duplicate this rule is about, so the source is list-backed —
        // the shape a hand-built boot list would have.
        var duplicate = new ListedLooks(
            new KeyValuePair<ContentId, CharacterLook>(OathboundId, new CharacterLook(Knight())),
            new KeyValuePair<ContentId, CharacterLook>(OathboundId, default));

        var thrown = Assert.Throws<ArgumentException>(() => new CharacterLookBook(duplicate));

        Assert.That(thrown.Message, Does.Contain("character.oathbound"),
            "The message must name the id, or it points at no asset anyone can open.");
    }

    [Test]
    public void Book_AnUnknownIdIsTheDefaultLook()
    {
        GameObject knight = Knight();
        var book = new CharacterLookBook(
            new Dictionary<ContentId, CharacterLook> { [OathboundId] = new CharacterLook(knight) });

        CharacterLook look = default;

        Assert.That(() => look = book.For(new ContentId("character.x")), Throws.Nothing);
        Assert.That(look.Body, Is.Null, "An id nobody authored a look for wears the run's default body.");

        // And the id that was authored still reads its own, so the null above is the miss and not
        // a book that answers null for everything.
        Assert.That(book.For(OathboundId).Body, Is.SameAs(knight));

        Assert.That(() => book.For(default), Throws.Nothing);
    }

    // ---- Rule 2: the definition ----------------------------------------------------------------

    [Test]
    public void Definition_CarriesItsBody()
    {
        CharacterDefinition definition = NewDefinition("Bodied");
        GameObject knight = Knight();

        var serialized = new SerializedObject(definition);
        serialized.FindProperty("_body").objectReferenceValue = knight;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Assert.That(definition.ToLook().Body, Is.SameAs(knight));
    }

    [Test]
    public void Definition_NoBodyIsTheDefault()
    {
        CharacterDefinition definition = NewDefinition("Bodiless");

        CharacterLook look = default;

        // Not an error: an empty body is the run's default one.
        Assert.That(() => look = definition.ToLook(), Throws.Nothing);
        Assert.That(look.Body, Is.Null);
    }

    // ---- Rule 3: one choice of class -----------------------------------------------------------

    [Test]
    public void Choose_ThePendingClassWins()
    {
        ContentCatalog catalog = Catalog(OathboundPath, GravecallerPath);
        var pending = new PendingRun();

        pending.Set(Descent, GravecallerId, seed: 7);

        Assert.That(RunCharacter.Choose(pending, catalog), Is.EqualTo(GravecallerId),
            "The menu chose the Gravecaller; the catalog's first is the Oathbound.");
    }

    [Test]
    public void Choose_NoPendingRunIsTheFirstClass()
    {
        // The Gravecaller first, so the answer is the catalog's first class and not the Oathbound
        // by name.
        ContentCatalog catalog = Catalog(GravecallerPath, OathboundPath);
        var pending = new PendingRun();

        Assert.That(RunCharacter.Choose(pending, catalog), Is.EqualTo(GravecallerId));

        // And a run that was pending and has started — RunTicker.Start clears it — is no run.
        pending.Set(Descent, OathboundId, seed: 7);
        pending.Clear();

        Assert.That(RunCharacter.Choose(pending, catalog), Is.EqualTo(GravecallerId));
    }

    [Test]
    public void Choose_AnEmptyCatalogThrows()
    {
        var catalog = new ContentCatalog(Array.Empty<CharacterSpec>());

        var thrown = Assert.Throws<InvalidOperationException>(
            () => RunCharacter.Choose(new PendingRun(), catalog));

        Assert.That(thrown.Message, Does.Contain("BootScope's character list"),
            "The message names where the missing class is added.");
    }

    // ---- Rule 6: the shipped classes -----------------------------------------------------------

    [Test]
    public void Shipped_EveryClassNamesTheKnightBody()
    {
        GameObject knight = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPath);

        Assert.That(knight, Is.Not.Null, $"No prefab at {KnightPath}.");

        foreach (string path in new[] { OathboundPath, GravecallerPath, EmberwrightPath })
        {
            Assert.That(Definition(path).ToLook().Body, Is.SameAs(knight),
                $"{path} wears the Knight until M7's art rulings give it a body of its own.");
        }

        // The same Knight that Player.prefab carried: AC_Player, driven by PlayerAnimatorView, both
        // now on the body.
        var view = knight.GetComponent<PlayerAnimatorView>();
        Animator animator = knight.GetComponent<Animator>();

        Assert.That(view, Is.Not.Null, "The Knight carries its own animator view.");
        Assert.That(animator.runtimeAnimatorController.name, Is.EqualTo("AC_Player"));
        Assert.That(new SerializedObject(view).FindProperty("_animator").objectReferenceValue, Is.SameAs(animator));

        // And the player carries none, so the one a run raises is the only body under it.
        Assert.That(
            AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath).GetComponentsInChildren<Animator>(true),
            Is.Empty,
            "Player.prefab lost its built-in Knight.");
    }

    // ---- Rule 5: a view on its body ------------------------------------------------------------

    /// <summary>
    /// Both views, each on a body under a moving <see cref="PlayerView"/>: stepped, each reads the
    /// player's velocity.
    /// </summary>
    /// <remarks>
    /// 2 m/s along the player's forward. One second of step takes both views' follow — 24 m/s² —
    /// all the way there, so the Knight's speed reads 2 and the Ranger's forward blend reads 2 m/s
    /// over its run's stride speed. With no player found, both would read standing still.
    /// </remarks>
    [Test]
    public void View_FindsPlayerViewInItsParent()
    {
        PlayerView player = MovingPlayer(new Vector3(0f, 0f, 2f));

        Animator knightAnimator = Body(player, "Knight", "Speed");
        var knight = knightAnimator.gameObject.AddComponent<PlayerAnimatorView>();
        Dress(knight, knightAnimator);

        Animator rangerAnimator = Body(player, "Ranger", "MoveX", "MoveZ", "MoveSpeed");
        var ranger = rangerAnimator.gameObject.AddComponent<RangerAnimatorView>();
        Dress(ranger, rangerAnimator);

        knight.Step(1f);
        ranger.Step(1f);

        Assert.That(knightAnimator.GetFloat(SpeedId), Is.EqualTo(2f).Within(1e-4f),
            "PlayerAnimatorView read the velocity of the PlayerView above its body.");

        Assert.That(rangerAnimator.GetFloat(MoveZId), Is.EqualTo(2f / RangerAnimatorView.ForwardStrideSpeed).Within(1e-4f),
            "RangerAnimatorView read the velocity of the PlayerView above its body.");
        Assert.That(rangerAnimator.GetFloat(MoveXId), Is.Zero.Within(1e-5f));
    }

    /// <summary>
    /// Rule 5's second half, beyond the Tests table: with no <see cref="PlayerView"/> above them,
    /// both views throw in <c>Start</c>, naming their body.
    /// </summary>
    [Test]
    public void View_WithNoPlayerViewAboveThrowsInStart()
    {
        _hub = new DomainEventHub();

        Animator knightAnimator = Body(null, "StrayKnight", "Speed");
        var knight = knightAnimator.gameObject.AddComponent<PlayerAnimatorView>();
        Dress(knight, knightAnimator);
        knight.Construct(_hub);

        Animator rangerAnimator = Body(null, "StrayRanger", "MoveX", "MoveZ", "MoveSpeed");
        var ranger = rangerAnimator.gameObject.AddComponent<RangerAnimatorView>();
        Dress(ranger, rangerAnimator);
        ranger.Construct(_hub);

        AssertStartRefuses(knight, "StrayKnight");
        AssertStartRefuses(ranger, "StrayRanger");
    }

    // ---- Helpers -------------------------------------------------------------------------------

    private static void AssertStartRefuses(MonoBehaviour view, string bodyName)
    {
        MethodInfo start = view.GetType().GetMethod("Start", Private);

        var thrown = Assert.Throws<TargetInvocationException>(() => start.Invoke(view, null));

        Assert.That(thrown.InnerException, Is.InstanceOf<InvalidOperationException>());
        Assert.That(thrown.InnerException.Message, Does.Contain(bodyName).And.Contain(nameof(PlayerView)),
            $"{view.GetType().Name} names the body it is on and what it is missing.");
    }

    private static GameObject Knight()
    {
        var knight = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPath);

        Assert.That(knight, Is.Not.Null, $"No prefab at {KnightPath}.");

        return knight;
    }

    private static CharacterDefinition Definition(string path)
    {
        var definition = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(path);

        Assert.That(definition, Is.Not.Null, $"No CharacterDefinition at {path}.");

        return definition;
    }

    private static ContentCatalog Catalog(params string[] paths)
    {
        var specs = new CharacterSpec[paths.Length];

        for (int i = 0; i < paths.Length; i++)
        {
            specs[i] = Definition(paths[i]).ToSpec();
        }

        return new ContentCatalog(specs);
    }

    private CharacterDefinition NewDefinition(string assetName)
    {
        var definition = ScriptableObject.CreateInstance<CharacterDefinition>();

        definition.name = assetName;
        _created.Add(definition);

        return definition;
    }

    /// <summary>
    /// A player moving at <paramref name="velocity"/>. Written to the view's own field, because
    /// <c>Apply</c> needs the <see cref="CharacterController"/> that <c>Awake</c> caches, and
    /// <c>Awake</c> never runs here.
    /// </summary>
    private PlayerView MovingPlayer(Vector3 velocity)
    {
        var player = new GameObject("Player");

        _created.Add(player);

        var view = player.AddComponent<PlayerView>();

        typeof(PlayerView).GetField("_velocity", Private).SetValue(view, velocity);

        return view;
    }

    /// <summary>
    /// A body under <paramref name="player"/> at identity — or standing alone, for null — with an
    /// Animator on a controller holding the float parameters named.
    /// </summary>
    private Animator Body(PlayerView player, string name, params string[] floats)
    {
        var body = new GameObject(name);

        if (player != null)
        {
            body.transform.SetParent(player.transform, false);
        }
        else
        {
            _created.Add(body);
        }

        var controller = new AnimatorController
        {
            name = $"AC_{name}TestDouble",
            hideFlags = HideFlags.HideAndDontSave,
        };

        _created.Add(controller);

        controller.AddLayer("Base Layer");

        foreach (string parameter in floats)
        {
            controller.AddParameter(parameter, AnimatorControllerParameterType.Float);
        }

        Animator animator = body.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        return animator;
    }

    private static void Dress(MonoBehaviour view, Animator animator) =>
        view.GetType().GetField("_animator", Private).SetValue(view, animator);

    /// <summary>
    /// Entries in the order given, duplicates included — which a
    /// <see cref="Dictionary{TKey,TValue}"/> cannot hold.
    /// </summary>
    private sealed class ListedLooks : IReadOnlyDictionary<ContentId, CharacterLook>
    {
        private readonly KeyValuePair<ContentId, CharacterLook>[] _entries;

        public ListedLooks(params KeyValuePair<ContentId, CharacterLook>[] entries)
        {
            _entries = entries;
        }

        public int Count => _entries.Length;

        public IEnumerable<ContentId> Keys
        {
            get
            {
                foreach (KeyValuePair<ContentId, CharacterLook> entry in _entries)
                {
                    yield return entry.Key;
                }
            }
        }

        public IEnumerable<CharacterLook> Values
        {
            get
            {
                foreach (KeyValuePair<ContentId, CharacterLook> entry in _entries)
                {
                    yield return entry.Value;
                }
            }
        }

        public CharacterLook this[ContentId key]
            => TryGetValue(key, out CharacterLook look) ? look : throw new KeyNotFoundException(key.Value);

        public bool ContainsKey(ContentId key) => TryGetValue(key, out _);

        public bool TryGetValue(ContentId key, out CharacterLook value)
        {
            foreach (KeyValuePair<ContentId, CharacterLook> entry in _entries)
            {
                if (entry.Key.Equals(key))
                {
                    value = entry.Value;

                    return true;
                }
            }

            value = default;

            return false;
        }

        public IEnumerator<KeyValuePair<ContentId, CharacterLook>> GetEnumerator()
        {
            foreach (KeyValuePair<ContentId, CharacterLook> entry in _entries)
            {
                yield return entry;
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
