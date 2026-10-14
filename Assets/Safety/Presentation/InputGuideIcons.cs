using System;
using UnityEngine;

namespace SafetySimulation
{
    [CreateAssetMenu(menuName = "Safety/Input Guide Icons")]
    public sealed class InputGuideIcons : ScriptableObject
    {
        [Serializable]
        public sealed class Mapping
        {
            public InputGuideAction Action;
            [Tooltip("One icon, or several icons for a group such as WASD or mouse movement.")]
            public Sprite[] Icons = Array.Empty<Sprite>();
            [Tooltip("Optional override for the text shown when an icon is missing.")]
            public string ControlLabel;
            [Tooltip("Override the size of each icon in this action's mapping.")]
            public bool OverrideIconSize;
            [Tooltip("Width and height in Canvas UI units. Sprite aspect ratio is preserved inside this box.")]
            public Vector2 IconSize = new Vector2(36f, 36f);
        }

        [SerializeField]
        private Mapping[] mappings =
        {
            new Mapping { Action = InputGuideAction.Move },
            new Mapping { Action = InputGuideAction.Look },
            new Mapping { Action = InputGuideAction.Interact },
            new Mapping { Action = InputGuideAction.EquipFlashlight },
            new Mapping { Action = InputGuideAction.EquipCurrentMeter },
            new Mapping { Action = InputGuideAction.Ascend },
            new Mapping { Action = InputGuideAction.Descend },
            new Mapping { Action = InputGuideAction.Use },
            new Mapping { Action = InputGuideAction.Extend }
        };

        public bool TryGet(InputGuideAction action, out Sprite[] icons, out string controlLabel)
        {
            return TryGet(action, out icons, out controlLabel, out _);
        }

        public bool TryGet(InputGuideAction action, out Sprite[] icons, out string controlLabel,
            out Vector2? iconSize)
        {
            controlLabel = InputGuideResolver.GetControlLabel(action);
            icons = Array.Empty<Sprite>();
            iconSize = null;
            if (mappings == null)
            {
                return false;
            }

            foreach (var mapping in mappings)
            {
                if (mapping == null || mapping.Action != action)
                {
                    continue;
                }

                icons = mapping.Icons ?? Array.Empty<Sprite>();
                if (mapping.OverrideIconSize)
                {
                    iconSize = new Vector2(Mathf.Max(1f, mapping.IconSize.x), Mathf.Max(1f, mapping.IconSize.y));
                }
                if (!string.IsNullOrWhiteSpace(mapping.ControlLabel))
                {
                    controlLabel = mapping.ControlLabel;
                }

                return true;
            }

            return false;
        }
    }
}
