using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Save;
using Soulvail.Game.Adapters;
using Soulvail.Game.Composition;
using UnityEngine;
using VContainer;

// Block namespace, deliberately — see the note in BootScope.cs: the Menu scene references this
// component, and a file-scoped namespace loads it as null (Traps §5).
namespace Soulvail.Game.Presentation
{
    /// <summary>
    /// DT-01's development start: pick a stage on the menu and begin there, with the XP the stages
    /// before it would have paid banked as picks, so the tree opens on the first frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Off in two ways.</b> Untick <see cref="_on"/> on the Menu scene's <c>TestStart</c> object
    /// and the panel never draws; a release build never draws it either
    /// (<see cref="Debug.isDebugBuild"/>). Clearing <c>MenuScope</c>'s Test Start field removes it
    /// from the scope altogether, and the menu is exactly what it was before DT-01.
    /// </para>
    /// <para>
    /// <b>IMGUI and English, because it is a tool rather than a screen</b> — <c>DebugOverlay</c>'s
    /// bargain. It lays nothing over the Clash-style layout and owns no <c>LocKey</c>.
    /// </para>
    /// <para>
    /// The run it starts is an ordinary resumed run: it saves to <c>run.json</c>, records bests and
    /// pays Shards on death, like any other.
    /// </para>
    /// </remarks>
    public sealed class TestStartPanel : MonoBehaviour
    {
        /// <summary>The portrait height the panel is laid out at; it scales with the screen.</summary>
        private const float ReferenceHeight = 914f;

        [Tooltip("Untick to hide the test start. It never shows in a release build either way.")]
        [SerializeField] private bool _on = true;

        [Tooltip("The stage the stepper opens on.")]
        [SerializeField] private int _stage = 10;

        private PendingRun _pending;
        private SceneLoader _loader;
        private ContentCatalog _catalog;
        private MenuPresenter _menu;
        private TestStart _start;
        private bool _starting;

        /// <summary>Whether the panel draws: the switch is on and this is a development build.</summary>
        public bool IsShown => _on && Debug.isDebugBuild;

        /// <exception cref="ArgumentNullException">A dependency is null.</exception>
        [Inject]
        public void Construct(PendingRun pending, SceneLoader loader, ContentCatalog catalog, MenuPresenter menu)
        {
            _pending = pending ?? throw new ArgumentNullException(nameof(pending));
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _menu = menu != null ? menu : throw new ArgumentNullException(nameof(menu));
            _start = new TestStart(catalog);
        }

        private void OnGUI()
        {
            if (!IsShown || _start is null || !_catalog.TryGetMode(_menu.SelectedMode, out ModeSpec mode))
            {
                return;
            }

            int last = Math.Min(mode.FinalStage, 999);
            _stage = Math.Clamp(_stage, mode.StartingStage, last);

            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            GUILayout.BeginArea(new Rect(8f, 8f, 230f, 80f), GUI.skin.box);
            GUILayout.Label("Test start (dev)");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("-5", GUILayout.Width(36f)))
            {
                _stage = Math.Max(mode.StartingStage, _stage - 5);
            }

            if (GUILayout.Button("-", GUILayout.Width(28f)))
            {
                _stage = Math.Max(mode.StartingStage, _stage - 1);
            }

            GUILayout.Label("Stage " + _stage, GUILayout.Width(64f));

            if (GUILayout.Button("+", GUILayout.Width(28f)))
            {
                _stage = Math.Min(last, _stage + 1);
            }

            if (GUILayout.Button("+5", GUILayout.Width(36f)))
            {
                _stage = Math.Min(last, _stage + 5);
            }

            GUILayout.EndHorizontal();

            GUI.enabled = !_starting;

            if (GUILayout.Button("Start"))
            {
                Begin(mode.Id);
            }

            GUI.enabled = true;
            GUILayout.EndArea();
            GUI.matrix = Matrix4x4.identity;
        }

        /// <summary>
        /// Hands the stage's snapshot to the Continue path and loads the Run scene.
        /// </summary>
        /// <remarks><c>async void</c> for <c>MenuPresenter.Play</c>'s reason: an event handler.</remarks>
        private async void Begin(ContentId modeId)
        {
            if (_starting)
            {
                return;
            }

            _starting = true;

            try
            {
                int seed = Environment.TickCount;
                RandomState random = new SeededRandom(seed).Capture();

                _pending.Resume(_start.At(modeId, _menu.SelectedCharacter, seed, random, _stage));

                await _loader.LoadAsync(SceneLoader.Run);
            }
            catch (Exception exception)
            {
                _starting = false;
                Debug.LogException(exception, this);
            }
        }
    }
}
