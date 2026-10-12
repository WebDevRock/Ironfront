using System;

namespace Ironfront.Core.Skirmish
{
    public enum DifficultyLevel
    {
        Easy,
        Normal,
        Hard,
    }

    public enum StartingResources
    {
        Low,
        Standard,
        High,
    }

    public enum VictoryMode
    {
        /// <summary>Default (M001–M007): a faction is defeated when its headquarters is destroyed.</summary>
        Headquarters,
        /// <summary>A faction is eliminated when it has no production structure and no armed unit left.</summary>
        Annihilation,
    }

    /// <summary>
    /// One skirmish configuration as chosen in the setup menu. Plain data: copied, never shared, so a match cannot
    /// change the settings of the next one. <see cref="Resolve"/> turns it into concrete match parameters.
    /// </summary>
    [Serializable]
    public sealed class SkirmishSettings
    {
        public const string DefaultMapId = "dustfall_plains";
        public const int RandomStart = -1;
        /// <summary>0 = a new seed for every match start; any other value reproduces the same spawn choice.</summary>
        public const int NewSeedEachMatch = 0;

        public string MapId = DefaultMapId;
        public DifficultyLevel Difficulty = DifficultyLevel.Normal;
        public StartingResources Resources = StartingResources.Standard;
        /// <summary>The player's start slot id, or <see cref="RandomStart"/>. The computer's slot is never chosen by the player.</summary>
        public int StartSlot = RandomStart;
        public VictoryMode Victory = VictoryMode.Headquarters;
        /// <summary>Spawn-selection seed (tests and reproduction); <see cref="NewSeedEachMatch"/> in normal play.</summary>
        public int Seed = NewSeedEachMatch;

        public static SkirmishSettings Default => new SkirmishSettings();

        public SkirmishSettings Clone() => (SkirmishSettings)MemberwiseClone();

        public static int CreditsFor(StartingResources level)
        {
            switch (level)
            {
                case StartingResources.Low: return 1000;
                case StartingResources.High: return 4000;
                default: return 2000;
            }
        }

        public static string Describe(StartingResources level) => $"{level} ({CreditsFor(level)} credits)";

        /// <summary>
        /// A copy with every field valid: unknown enum values fall back to the defaults, an unknown map id to
        /// <paramref name="fallbackMapId"/> (when <paramref name="isKnownMap"/> rejects it) and a negative start slot
        /// other than Random to Random.
        /// </summary>
        public SkirmishSettings Sanitised(Func<string, bool> isKnownMap = null, string fallbackMapId = DefaultMapId)
        {
            var s = Clone();
            if (string.IsNullOrEmpty(s.MapId) || (isKnownMap != null && !isKnownMap(s.MapId))) s.MapId = fallbackMapId;
            if (!Enum.IsDefined(typeof(DifficultyLevel), s.Difficulty)) s.Difficulty = DifficultyLevel.Normal;
            if (!Enum.IsDefined(typeof(StartingResources), s.Resources)) s.Resources = StartingResources.Standard;
            if (!Enum.IsDefined(typeof(VictoryMode), s.Victory)) s.Victory = VictoryMode.Headquarters;
            if (s.StartSlot < RandomStart) s.StartSlot = RandomStart;
            return s;
        }

        /// <summary>
        /// Concrete parameters for one match on a map with the given spawn table. The spawn pair is drawn with
        /// <paramref name="seed"/> (<see cref="SpawnSelection.Choose"/>), so the same seed reproduces the same starts.
        /// </summary>
        public ResolvedSkirmish Resolve(int seed, SpawnTable spawns)
        {
            var s = Sanitised();
            if (!SpawnSelection.Choose(spawns, s.StartSlot, seed, out int player, out int enemy))
                player = enemy = -1;
            return new ResolvedSkirmish(s.MapId, s.Difficulty, CreditsFor(s.Resources), player, enemy, s.Victory, seed);
        }

        public override string ToString() =>
            $"{MapId}, {Difficulty}, {Resources}, start {(StartSlot == RandomStart ? "random" : StartSlot.ToString())}, {Victory}, seed {Seed}";

        public override bool Equals(object obj) =>
            obj is SkirmishSettings o && o.MapId == MapId && o.Difficulty == Difficulty && o.Resources == Resources && o.StartSlot == StartSlot
            && o.Victory == Victory && o.Seed == Seed;

        public override int GetHashCode() =>
            (MapId ?? "").GetHashCode() ^ ((int)Difficulty << 2) ^ ((int)Resources << 5) ^ ((StartSlot + 1) << 8) ^ ((int)Victory << 13) ^ (Seed * 31);
    }

    /// <summary>The parameters one match actually runs with.</summary>
    public readonly struct ResolvedSkirmish
    {
        public readonly string MapId;
        public readonly DifficultyLevel Difficulty;
        public readonly int StartingCredits;
        /// <summary>Start slot ids of the human player (faction 1) and the computer (faction 2); -1 = none valid.</summary>
        public readonly int PlayerSlot, EnemySlot;
        public readonly VictoryMode Victory;
        /// <summary>The seed the spawn pair was drawn with (recorded for reproduction).</summary>
        public readonly int Seed;

        public ResolvedSkirmish(string mapId, DifficultyLevel difficulty, int credits, int playerSlot, int enemySlot, VictoryMode victory, int seed)
        {
            MapId = mapId;
            Difficulty = difficulty;
            StartingCredits = credits;
            PlayerSlot = playerSlot;
            EnemySlot = enemySlot;
            Victory = victory;
            Seed = seed;
        }

        public bool HasSpawns => PlayerSlot >= 0 && EnemySlot >= 0 && PlayerSlot != EnemySlot;
    }
}
