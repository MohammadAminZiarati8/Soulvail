using System;
using Soulvail.Core.Content;
using Soulvail.Core.Ports;
using Soulvail.Game.Authoring;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Soulvail.Game.Presentation
{
    /// <summary>Three authored slots and a separate, guarded confirmation of the preview.</summary>
    public sealed class PathCardsView : MonoBehaviour
    {
        [SerializeField] private TMP_Text _labelTemplate;
        [SerializeField] private Sprite _panelSprite;
        [SerializeField] private Sprite _buttonSprite;
        private readonly Button[] _cards = new Button[3];
        private readonly TMP_Text[] _names = new TMP_Text[3];
        private readonly TMP_Text[] _descriptions = new TMP_Text[3];
        private readonly TMP_Text[] _gates = new TMP_Text[3];
        private readonly Transform[] _strips = new Transform[3];
        private Button _choose;
        private TMP_Text _chooseLabel;
        private Action<int> _preview;
        private Action _confirm;

        public bool ChooseEnabled => _choose != null && _choose.interactable;
        public Button Card(int index) => _cards[index];
        public string CardText(int index) => _names[index].text;
        public void Confirm() { if (ChooseEnabled) _confirm?.Invoke(); }

        public void Show(CharacterDefinition character, ContentCatalog catalog, SkillIconBook icons, Sprite placeholder,
            ILocalizer loc, int level, bool open, ContentId selected, float px,
            Action<int> preview, Action confirm)
        {
            _preview = preview;
            _confirm = confirm;
            Build();
            var root = (RectTransform)transform;
            float width = root.rect.width;
            for (int i = 0; i < 3; i++)
            {
                PathDefinition definition = character.PathSlots[i];
                PathSpec path = definition == null ? null : catalog.Path(new ContentId(definition.Id));
                _cards[i].interactable = path != null;
                _cards[i].image.color = path != null && path.Id == selected ? Palette.PlayerBlocked : Color.white;
                Place((RectTransform)_cards[i].transform, 0f, -i * 52f * px, width, 48f * px);
                _names[i].text = path == null ? loc.Get(new LocKey("ui.tree.coming-soon")) : loc.Get(path.NameKey);
                _descriptions[i].text = path == null ? string.Empty : loc.Get(path.DescriptionKey);
                int opens = catalog.Character(new ContentId(character.Id)).PathOpensAtLevel;
                _gates[i].text = level < opens
                    ? loc.Format(new LocKey("ui.tree.unlocks-at"), opens) : string.Empty;
                Place(_names[i].rectTransform, 10f * px, -3f * px, width * 0.64f, 20f * px);
                Place(_descriptions[i].rectTransform, 10f * px, -24f * px, width * 0.64f, 20f * px);
                Place(_gates[i].rectTransform, width * 0.72f, -25f * px, width * 0.25f, 20f * px);
                _names[i].fontSize = 14f * px;
                _descriptions[i].fontSize = 11f * px;
                _gates[i].fontSize = 11f * px;
                for (int j = 0; j < _strips[i].childCount; j++)
                    _strips[i].GetChild(j).gameObject.SetActive(false);
                if (path == null)
                    continue;
                for (int j = 0; j < path.Skills.Count; j++)
                {
                    Image icon;
                    if (j < _strips[i].childCount)
                        icon = _strips[i].GetChild(j).GetComponent<Image>();
                    else
                    {
                        var go = new GameObject("SkillIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                        go.transform.SetParent(_strips[i], false);
                        icon = go.GetComponent<Image>();
                        icon.raycastTarget = false;
                    }
                    ContentId id = path.Skills[j].Ranks.Tier(1)[0];
                    icon.sprite = icons?.For(id) ?? placeholder;
                    icon.color = icons?.TintFor(id) ?? Color.white;
                    icon.preserveAspect = true;
                    icon.gameObject.SetActive(true);
                    float side = Mathf.Min(20f * px, width * 0.26f / path.Skills.Count);
                    Place(icon.rectTransform, width * 0.72f + j * side, -3f * px, side, side);
                }
            }
            Place((RectTransform)_choose.transform, width * 0.25f, -158f * px, width * 0.5f, 34f * px);
            _choose.interactable = open && selected != default;
            _chooseLabel.text = loc.Get(new LocKey("ui.tree.path.choose"));
            _chooseLabel.fontSize = 15f * px;
            Place(_chooseLabel.rectTransform, 0f, 0f, width * 0.5f, 34f * px);
            _chooseLabel.alignment = TextAlignmentOptions.Center;
            gameObject.SetActive(true);
        }

        private void Build()
        {
            if (_choose != null)
                return;
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _cards[i] = MakeButton("PathCard", transform, _panelSprite);
                _cards[i].onClick.AddListener(() => { if (_cards[index].interactable) _preview?.Invoke(index); });
                _names[i] = Label(_cards[i].transform);
                _descriptions[i] = Label(_cards[i].transform);
                _gates[i] = Label(_cards[i].transform);
                var strip = new GameObject("SkillStrip", typeof(RectTransform));
                strip.transform.SetParent(_cards[i].transform, false);
                var rect = (RectTransform)strip.transform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                _strips[i] = strip.transform;
            }
            _choose = MakeButton("Choose", transform, _buttonSprite);
            _choose.onClick.AddListener(Confirm);
            _chooseLabel = Label(_choose.transform);
        }

        private TMP_Text Label(Transform parent)
        {
            TMP_Text text = Instantiate(_labelTemplate, parent);
            text.gameObject.SetActive(true);
            text.raycastTarget = false;
            text.color = Palette.HudText;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            return text;
        }

        private Button MakeButton(string label, Transform parent, Sprite sprite)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 8f;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            return button;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
