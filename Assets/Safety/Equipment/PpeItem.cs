namespace SafetySimulation
{
    public sealed class PpeItem : EquippableItem
    {
        public bool IsEquipped
        {
            get;
            private set;
        }

        public override void Equip(ToolUseContext context)
        {
            IsEquipped = true;
        }

        public override void Unequip()
        {
            IsEquipped = false;
        }
    }
}
