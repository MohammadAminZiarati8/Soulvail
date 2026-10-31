using System.Collections.Generic;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Core.Run;
using Soulvail.Game.Adapters;
using Soulvail.Game.Authoring;
using Soulvail.Game.Controls;
using UnityEngine;
using VContainer;

namespace Soulvail.Game.Presentation
{
    /// <summary>Minimal skill HUD: ordered actives beside the roll and untappable corner passives.</summary>
    public sealed class SkillArc : MonoBehaviour
    {
        [SerializeField] private SkillArcButton _template;
        [SerializeField] private RectTransform _roll;
        [SerializeField] private bool _leftHanded;
        private readonly List<SkillArcButton> _actives = new List<SkillArcButton>();
        private readonly List<SkillArcButton> _passives = new List<SkillArcButton>();
        private readonly Dictionary<ContentId, string> _levels = new Dictionary<ContentId, string>();
        private IRunSession _session;
        private ContentCatalog _catalog;
        private SkillIconBook _icons;
        private CharacterLookBook _looks;
        private SkillArcInput _input;
        private ILocalizer _localizer;
        private ContentId _character;
        private ContentId _path;
        private ContentId _volley;

        public IReadOnlyList<SkillArcButton> Actives => _actives;
        public IReadOnlyList<SkillArcButton> Passives => _passives;

        [Inject]
        public void Construct(IRunSession session, ContentCatalog catalog, SkillIconBook icons,
            CharacterLookBook looks, SkillArcInput input, ILocalizer localizer)
        {
            _session = session;
            _catalog = catalog;
            _icons = icons;
            _looks = looks;
            _input = input;
            _localizer = localizer;
            Refresh();
        }

        private void Update() => Refresh();

        public void Refresh()
        {
            RunState state = _session?.State;
            if (state is null || _template == null)
            {
                return;
            }

            if (_character != state.CharacterId || _path != state.PathId)
            {
                Rebuild(state);
            }

            float px = PixelsPerDp();
            for (int i = 0; i < _actives.Count; i++)
            {
                Place(_actives[i].transform as RectTransform,
                    HudLayout.ArcCentreDp(i, _actives.Count), HudLayout.ArcSizeDp, px);
                Draw(_actives[i], state);
                _actives[i].StepHold(Time.unscaledTime);
            }

            for (int i = 0; i < _passives.Count; i++)
            {
                Place(_passives[i].transform as RectTransform, HudLayout.PassiveCentreDp(i), HudLayout.PassiveSizeDp, px);
                Draw(_passives[i], state);
            }

            if (_roll != null)
            {
                Place(_roll, new Vector2(-HudLayout.RollRightDp, HudLayout.RollBottomDp), 72f, px);
            }
        }

        private void Rebuild(RunState state)
        {
            Clear(_actives);
            Clear(_passives);
            _levels.Clear();
            _character = state.CharacterId;
            _path = state.PathId;
            _volley = _looks.For(state.CharacterId).VolleyNode;
            if (_catalog.TryGetTreeFor(state.CharacterId, out SkillTreeSpec tree))
            {
                // Volley is authored as a shot-driven passive, but occupies the first active circle.
                if (_volley != default)
                {
                    Add(_catalog.Skill(_volley), 1, true);
                }

                foreach (SkillBranchSpec branch in tree.Branches)
                {
                    for (int tier = 1; tier <= branch.TierCount; tier++)
                    {
                        foreach (ContentId id in branch.Tier(tier))
                        {
                            AddRoot(_catalog.Skill(id), 1);
                        }
                    }
                }
            }

            if (_path != default && _catalog.TryGetPath(_path, out PathSpec path))
            {
                foreach (PathSkillSpec skill in path.Skills)
                {
                    AddRoot(_catalog.Skill(skill.Ranks.Tier(1)[0]), skill.UnlockLevel);
                }
            }
        }

        private void AddRoot(SkillSpec spec, int level)
        {
            if (spec.Id != _volley && (spec.Kind == SkillKind.Active || spec.Kind == SkillKind.Passive))
            {
                Add(spec, level, spec.Kind == SkillKind.Active);
            }
        }

        private void Add(SkillSpec spec, int level, bool active)
        {
            if (active && _actives.Count == HudLayout.MaxArcSkills)
            {
                return;
            }

            SkillArcButton button = Instantiate(_template, transform);
            button.name = spec.Id.Value;
            button.Bind(spec.Id, _input, active, _icons.For(spec.Id), _icons.TintFor(spec.Id));
            button.gameObject.SetActive(true);
            (active ? _actives : _passives).Add(button);
            _levels[spec.Id] = _localizer.Format(new LocKey("ui.tree.unlocks-at"), level);
        }

        private void Draw(SkillArcButton button, RunState state)
        {
            bool taken = false;
            for (int i = 0; i < state.TakenNodeIds.Count; i++)
            {
                taken |= state.TakenNodeIds[i] == button.Id;
            }

            int index = -1;
            for (int i = 0; i < state.OwnedActiveCount; i++)
            {
                if (state.SkillIdAt(i) == button.Id)
                {
                    index = i;
                    break;
                }
            }

            bool volley = button.Id == _volley;
            float fraction = index >= 0 ? state.SkillCooldownFraction(index) : 0f;
            float seconds = index >= 0 ? state.SkillCooldownSeconds(index) * fraction : 0f;
            bool ready = volley ? state.IsVolleyReady : index >= 0 && state.IsSkillReady(index);
            bool auto = taken && (volley || index >= 0) && state.IsAutoCast(button.Id);
            button.Draw(taken, state.UnlockLevelOf(button.Id), state.Level, fraction, seconds,
                ready, auto, _levels[button.Id], _icons.TintFor(button.Id),
                volley ? state.VolleyShotsTowardNext : 0, volley ? state.VolleyEvery : 0);
        }

        private void Place(RectTransform rect, Vector2 positionDp, float sizeDp, float px)
        {
            Vector2 anchor = new Vector2(_leftHanded ? 0f : 1f, 0f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * (sizeDp * px);
            rect.anchoredPosition = new Vector2(_leftHanded ? -positionDp.x : positionDp.x, positionDp.y) * px;
        }

        private float PixelsPerDp()
        {
            var canvas = GetComponentInParent<Canvas>();
            return StickShaper.PixelsPerDp(Screen.dpi) / (canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f);
        }

        private void Clear(List<SkillArcButton> buttons)
        {
            foreach (SkillArcButton button in buttons)
            {
                button.gameObject.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(button.gameObject);
                }
                else
                {
                    DestroyImmediate(button.gameObject);
                }
            }

            buttons.Clear();
        }
    }
}
