namespace Ironfront.Audio
{
    /// <summary>Every sound event in the game. Clips are assigned per id in the <see cref="SoundBank"/>.</summary>
    public enum SoundId
    {
        SelectAck,
        MoveAck,
        AttackAck,
        LightCannon,
        HeavyCannon,
        Impact,
        ExplosionSmall,
        ExplosionLarge,
        StructureDestroyed,
        PlaceStructure,
        ConstructionComplete,
        UnitReady,
        Delivery,
        UiClick,
        UiHover,
        UiError,
        Alert,
        Victory,
        Defeat,
        MachineGun,
        RocketLaunch,
        ResearchComplete,
        // Milestone 009
        RepairWork,
        RepairComplete,
        // Milestone 010
        Dismantle,
        SaleComplete,
        DeployStart,
        BaseEstablished,
        TerritoryLost,
    }

    /// <summary>Mixer category; each has its own volume setting.</summary>
    public enum SoundCategory
    {
        Effects,
        Interface,
        Voice,
        Ambience,
    }
}
