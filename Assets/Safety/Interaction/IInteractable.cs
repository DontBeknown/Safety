namespace SafetySimulation
{
    public interface IInteractable
    {
        string Prompt
        {
            get;
        }

        bool CanInteract(InteractionContext context, out string reason);
        void Interact(InteractionContext context);
    }
}
