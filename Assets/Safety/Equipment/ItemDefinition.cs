using UnityEngine;

namespace SafetySimulation
{
    public enum EquipmentCategory
    {
        Held,
        Worn
    }

    [CreateAssetMenu(menuName = "Safety/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;
        public Sprite Icon;
        public EquippableItem Prefab;
        public EquipmentCategory Category;
        public Vector3 EquipPosition;
        public Vector3 EquipEuler;
    }
}
