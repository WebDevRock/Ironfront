using System.Collections.Generic;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Buildings
{
    /// <summary>Samples which enemy command structures each faction has seen (twice a second). Added by the match controller.</summary>
    public class TerritoryTracker : MonoBehaviour
    {
        private readonly List<int> _factions = new List<int>();
        private float _timer;

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 0.5f;
            var match = MatchController.Current;
            if (match == null || match.Setup == null) return;
            _factions.Clear();
            foreach (var f in match.Setup.Factions) _factions.Add(f.id);
            TerritorySystem.SampleSightings(_factions);
        }
    }
}
