using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SafetySimulation
{
    [DisallowMultipleComponent]
    public sealed class InputGuideRow : MonoBehaviour
    {
        [SerializeField, Tooltip("Assign separate child Image objects; use enough slots for grouped icons.")]
        private UnityEngine.UI.Image[] iconImages = Array.Empty<UnityEngine.UI.Image>();
        [SerializeField]
        private TMP_Text instructionText;
        [SerializeField, Tooltip("Optional separate label for controls when sprites are unavailable.")]
        private TMP_Text controlLabelText;

        private readonly Dictionary<UnityEngine.UI.Image, IconSizing> iconSizing =
            new Dictionary<UnityEngine.UI.Image, IconSizing>();

        private sealed class IconSizing
        {
            private readonly RectTransform rect;
            private readonly Vector2 originalSize;
            private UnityEngine.UI.LayoutElement layout;
            private readonly bool originalEnabled;
            private readonly bool originalIgnoreLayout;
            private readonly Vector2 originalMin;
            private readonly Vector2 originalPreferred;
            private readonly Vector2 originalFlexible;
            private readonly int originalPriority;

            public IconSizing(UnityEngine.UI.Image image)
            {
                rect = image.rectTransform;
                originalSize = rect.sizeDelta;
                layout = image.GetComponent<UnityEngine.UI.LayoutElement>();
                if (layout != null)
                {
                    originalEnabled = layout.enabled;
                    originalIgnoreLayout = layout.ignoreLayout;
                    originalMin = new Vector2(layout.minWidth, layout.minHeight);
                    originalPreferred = new Vector2(layout.preferredWidth, layout.preferredHeight);
                    originalFlexible = new Vector2(layout.flexibleWidth, layout.flexibleHeight);
                    originalPriority = layout.layoutPriority;
                }
            }

            public void Apply(Vector2? size)
            {
                if (size.HasValue)
                {
                    var value = size.Value;
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, value.x);
                    rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, value.y);
                    if (layout == null)
                    {
                        layout = rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    }

                    // Layout groups read these values instead of replacing the requested icon size.
                    layout.enabled = true;
                    layout.ignoreLayout = false;
                    layout.layoutPriority = Mathf.Max(1, originalPriority);
                    layout.minWidth = layout.preferredWidth = value.x;
                    layout.minHeight = layout.preferredHeight = value.y;
                    layout.flexibleWidth = layout.flexibleHeight = 0f;
                }
                else
                {
                    // Rows are reused; remove the previous action's size override.
                    rect.sizeDelta = originalSize;
                    if (layout != null)
                    {
                        layout.minWidth = originalMin.x;
                        layout.minHeight = originalMin.y;
                        layout.preferredWidth = originalPreferred.x;
                        layout.preferredHeight = originalPreferred.y;
                        layout.flexibleWidth = originalFlexible.x;
                        layout.flexibleHeight = originalFlexible.y;
                        layout.ignoreLayout = originalIgnoreLayout;
                        layout.layoutPriority = originalPriority;
                        layout.enabled = originalEnabled;
                    }
                }
            }
        }

        public void SetEntry(InputGuideEntry entry, InputGuideIcons iconMap)
        {
            var sprites = Array.Empty<Sprite>();
            Vector2? iconSize = null;
            var controlLabel = entry.Action.HasValue
                ? InputGuideResolver.GetControlLabel(entry.Action.Value)
                : string.Empty;
            if (entry.Action.HasValue && iconMap != null)
            {
                iconMap.TryGet(entry.Action.Value, out sprites, out controlLabel, out iconSize);
            }
            // Keep the current flashlight shortcut even if an icon asset has an older label.
            var flashlightToggle = entry.Action == InputGuideAction.EquipFlashlight;
            if (flashlightToggle)
                controlLabel = InputGuideResolver.GetControlLabel(InputGuideAction.EquipFlashlight);

            var displayed = 0;
            if (iconImages != null)
            {
                for (var i = 0; i < iconImages.Length; i++)
                {
                    var image = iconImages[i];
                    if (image == null)
                    {
                        continue;
                    }

                    var sprite = i < sprites.Length ? sprites[i] : null;
                    if (!iconSizing.TryGetValue(image, out var sizing))
                    {
                        sizing = new IconSizing(image);
                        iconSizing.Add(image, sizing);
                    }

                    sizing.Apply(iconSize);
                    image.sprite = sprite;
                    image.preserveAspect = true;
                    image.raycastTarget = false;
                    image.gameObject.SetActive(sprite != null);
                    if (sprite != null)
                    {
                        displayed++;
                    }
                }
            }

            // Keep the key label if a mapping or an Image slot is missing.
            var needsLabel = entry.Action.HasValue && (sprites.Length == 0 || displayed < sprites.Length);
            if (controlLabelText != null)
            {
                controlLabelText.text = needsLabel ? controlLabel : string.Empty;
                controlLabelText.gameObject.SetActive(needsLabel);
            }

            if (instructionText != null)
            {
                instructionText.text = needsLabel && controlLabelText == null
                    ? controlLabel + " — " + entry.Instruction
                    : entry.Instruction;
            }
        }
    }
}
