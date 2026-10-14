using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace SafetySimulation
{
    [CreateAssetMenu(menuName = "Safety/Equipment Catalog")]
    public sealed class EquipmentCatalog : ScriptableObject
    {
        [FormerlySerializedAs("Tools")]
        public ItemDefinition[] Items = Array.Empty<ItemDefinition>();
        public string[] RequiredPpeIds =
        {
            "helmet",
            "gloves",
            "footwear"
        };
        public ItemDefinition FindItem(string id)
        {
            foreach (var item in Items)
            {
                if (item != null && item.Id == id)
                {
                    return item;
                }
            }

            return null;
        }

        public bool TryValidate(out string reason)
        {
            if (Items == null || Items.Length == 0 || RequiredPpeIds == null || RequiredPpeIds.Length == 0)
            {
                reason = "Configure catalog items and required PPE IDs.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in Items)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id) || item.Prefab == null ||
                    !Enum.IsDefined(typeof(EquipmentCategory), item.Category))
                {
                    reason = "Catalog items need unique IDs, a category and a prefab.";
                    return false;
                }

                if (item.Category == EquipmentCategory.Held && !(item.Prefab is ToolBase))
                {
                    reason = $"Held item '{item.Id}' needs a ToolBase prefab.";
                    return false;
                }

                if (!item.Prefab.TryValidate(out reason))
                {
                    return false;
                }
            }

            ids.Clear();
            foreach (var id in RequiredPpeIds)
            {
                var item = FindItem(id);
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id) || item == null || item.Category != EquipmentCategory.Worn)
                {
                    reason = "Every required PPE ID must identify a unique worn catalog item.";
                    return false;
                }
            }

            reason = null;
            return true;
        }
    }
}
