namespace Ironfront.Core.AI
{
    /// <summary>
    /// Which known enemy structure an attacking army should go for. Production is hit first (it stops
    /// reinforcements), then the headquarters (the victory condition), then income and power. Nearer
    /// targets win when priorities are similar. Enemy units are engaged by the units' own target selection
    /// (armed units first) along the way.
    /// </summary>
    public static class TargetPriorities
    {
        public static float StructureWeight(string definitionId)
        {
            switch (definitionId)
            {
                case "vehicle_factory": return 3f;
                case "headquarters": return 2.6f;
                case "refinery": return 2f;
                case "power_plant": return 1.5f;
                case "research_centre": return 1.8f;
                // Defences protect everything else: take them out first when they are near.
                case "at_turret": return 2.2f;
                case "guard_tower": return 1.6f;
                default: return 1f;
            }
        }

        /// <summary>Higher is better. Distance in metres from the army.</summary>
        public static float Score(string definitionId, float distance) => StructureWeight(definitionId) / (1f + distance / 40f);
    }
}
