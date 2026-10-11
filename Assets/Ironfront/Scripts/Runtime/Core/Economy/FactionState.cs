using Ironfront.Core.Research;

namespace Ironfront.Core.Economy
{
    /// <summary>
    /// All economic state owned by one faction. Nothing here assumes the faction is the human player,
    /// so AI factions can be added later with their own instance.
    /// </summary>
    public sealed class FactionState
    {
        public int Id { get; }
        public string DisplayName { get; }
        public bool IsHumanControlled { get; }

        public ResourceWallet Wallet { get; }
        public PowerGrid Power { get; } = new PowerGrid();
        public StructureRoster Structures { get; } = new StructureRoster();
        /// <summary>Technology for this match only (Milestone 007).</summary>
        public ResearchState Research { get; } = new ResearchState();

        public FactionState(int id, string displayName, bool isHumanControlled, int startingCredits)
        {
            Id = id;
            DisplayName = displayName;
            IsHumanControlled = isHumanControlled;
            Wallet = new ResourceWallet(startingCredits);
        }

        public void RegisterStructure(string structureId, int powerOutput, int powerDrain)
        {
            Structures.Add(structureId);
            Power.Add(powerOutput, powerDrain);
        }

        public void UnregisterStructure(string structureId, int powerOutput, int powerDrain)
        {
            if (!Structures.Has(structureId)) return;
            Structures.Remove(structureId);
            Power.Remove(powerOutput, powerDrain);
        }
    }
}
