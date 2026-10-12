using System.Collections.Generic;
using Ironfront.Core.Skirmish;
using UnityEngine;

namespace Ironfront.Data
{
    /// <summary>The skirmish content the setup menu offers: battlefields and computer difficulties (Milestone 008).</summary>
    [CreateAssetMenu(menuName = "Ironfront/Map Catalog", fileName = "MapCatalog")]
    public class MapCatalog : ScriptableObject
    {
        [SerializeField] private List<MapDefinition> maps = new List<MapDefinition>();
        [SerializeField] private List<AiDifficulty> difficulties = new List<AiDifficulty>();

        public IReadOnlyList<MapDefinition> Maps => maps;
        public IReadOnlyList<AiDifficulty> Difficulties => difficulties;

        public MapDefinition FindMap(string id)
        {
            foreach (var m in maps)
                if (m != null && m.Id == id) return m;
            return null;
        }

        public bool HasMap(string id) => FindMap(id) != null;

        public AiDifficulty FindDifficulty(DifficultyLevel level)
        {
            foreach (var d in difficulties)
                if (d != null && d.Level == level) return d;
            return null;
        }
    }
}
