using UnityEngine;

namespace SafetySimulation
{
    public abstract class EquippableItem : MonoBehaviour
    {
        public string ItemId
        {
            get;
            internal set;
        }

        public virtual bool TryValidate(out string reason)
        {
            reason = null;
            return true;
        }

        public abstract void Equip(ToolUseContext context);
        public abstract void Unequip();
    }
}
