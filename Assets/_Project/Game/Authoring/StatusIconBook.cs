using Soulvail.Core.Content;
using UnityEngine;

namespace Soulvail.Game.Authoring
{
    /// <summary>Status id → icon for the HUD's status row (MK-05); unknown ids get the fallback.</summary>
    [CreateAssetMenu(menuName = "Soulvail/Status Icon Book", fileName = "StatusIconBook")]
    public sealed class StatusIconBook : ScriptableObject
    {
        [SerializeField] private string[] _ids = new string[0];
        [SerializeField] private Sprite[] _icons = new Sprite[0];
        [SerializeField] private Sprite _fallback;

        /// <summary>The status's icon, the fallback for an id with none. Never throws.</summary>
        public Sprite For(ContentId id)
        {
            int count = Mathf.Min(_ids.Length, _icons.Length);
            for (int i = 0; i < count; i++)
            {
                if (_icons[i] != null && _ids[i] == id.Value)
                {
                    return _icons[i];
                }
            }

            return _fallback;
        }

        /// <summary>Authoring and tests: replaces the table.</summary>
        public void Set(string[] ids, Sprite[] icons, Sprite fallback)
        {
            _ids = ids ?? new string[0];
            _icons = icons ?? new Sprite[0];
            _fallback = fallback;
        }
    }
}
