using System;
using System.Collections.Generic;

namespace Ironfront.Core.AI
{
    /// <summary>
    /// What a computer commander believes about where the enemy is (Milestone 008 addendum). It starts knowing only
    /// the map's candidate start sites (public map knowledge: the setup screen shows them to the player too), never
    /// which one the enemy took. Sites become <see cref="SiteState.Empty"/> when seen without enemy structures and
    /// <see cref="SiteState.EnemyBase"/> when enemy structures are remembered there. Every belief carries the time it
    /// was last confirmed, so current intelligence is distinguished from outdated observations
    /// (<see cref="StaleAfter"/>). Pure and deterministic.
    /// </summary>
    public sealed class EnemyIntel
    {
        public enum SiteState { Unexplored, Empty, EnemyBase }

        public sealed class Site
        {
            public int Id;
            public float X, Z;
            public SiteState State = SiteState.Unexplored;
            /// <summary>Game time the state was last confirmed (-1 = never).</summary>
            public float LastConfirmed = -1f;
        }

        /// <summary>A remembered enemy structure as the commander's perception reports it.</summary>
        public readonly struct Sighting
        {
            public readonly float X, Z;
            public readonly bool Headquarters;
            public readonly float LastSeen;

            public Sighting(float x, float z, bool headquarters, float lastSeen)
            {
                X = x;
                Z = z;
                Headquarters = headquarters;
                LastSeen = lastSeen;
            }
        }

        private readonly List<Site> _sites = new List<Site>();
        private readonly List<Sighting> _sightings = new List<Sighting>();

        /// <summary>Observations older than this are treated as outdated (still remembered, re-checked by scouts).</summary>
        public float StaleAfter = 180f;
        /// <summary>Structures within this distance of a candidate site mark it as the enemy base.</summary>
        public float SiteRadius = 35f;

        public IReadOnlyList<Site> Sites => _sites;
        public IReadOnlyList<Sighting> Sightings => _sightings;
        /// <summary>Game time the enemy base was first located (-1 = not yet).</summary>
        public float FirstDiscovery { get; private set; } = -1f;
        public bool EnemyLocated => _sightings.Count > 0;

        public EnemyIntel(IEnumerable<(int id, float x, float z)> candidateSites)
        {
            if (candidateSites != null)
                foreach (var (id, x, z) in candidateSites) _sites.Add(new Site { Id = id, X = x, Z = z });
        }

        /// <summary>
        /// Updates beliefs from this decision's observations.
        /// </summary>
        /// <param name="visibleNow">Is a point visible to the commander's faction right now?</param>
        /// <param name="remembered">Enemy structures the commander remembers (seen, not yet seen gone).</param>
        public void Observe(float now, Func<float, float, bool> visibleNow, IReadOnlyList<Sighting> remembered)
        {
            _sightings.Clear();
            if (remembered != null) _sightings.AddRange(remembered);
            if (_sightings.Count > 0 && FirstDiscovery < 0f) FirstDiscovery = now;

            foreach (var site in _sites)
            {
                float latest = -1f;
                foreach (var s in _sightings)
                    if (Sq(s.X - site.X) + Sq(s.Z - site.Z) <= SiteRadius * SiteRadius && s.LastSeen > latest) latest = s.LastSeen;
                if (latest >= 0f)
                {
                    site.State = SiteState.EnemyBase;
                    site.LastConfirmed = latest;
                }
                else if (visibleNow != null && visibleNow(site.X, site.Z))
                {
                    site.State = SiteState.Empty;
                    site.LastConfirmed = now;
                }
                else if (site.State == SiteState.EnemyBase)
                {
                    // Remembered structures are gone (seen destroyed) but the site itself is not in view: unknown again.
                    site.State = SiteState.Unexplored;
                }
            }
        }

        public bool IsCurrent(float lastSeen, float now) => lastSeen >= 0f && now - lastSeen <= StaleAfter;

        /// <summary>Is the best-known enemy position confirmed recently?</summary>
        public bool HasCurrentIntel(float now)
        {
            foreach (var s in _sightings) if (IsCurrent(s.LastSeen, now)) return true;
            return false;
        }

        /// <summary>
        /// Where the enemy most probably is: a remembered headquarters, else the most recently seen structure; null when
        /// nothing has been found.
        /// </summary>
        public (float x, float z)? KnownEnemyFocus()
        {
            Sighting? best = null;
            foreach (var s in _sightings)
            {
                if (best == null) { best = s; continue; }
                var b = best.Value;
                if (s.Headquarters && !b.Headquarters) best = s;
                else if (s.Headquarters == b.Headquarters && s.LastSeen > b.LastSeen) best = s;
            }
            return best == null ? ((float, float)?)null : (best.Value.X, best.Value.Z);
        }

        /// <summary>
        /// The next candidate site worth looking at from (x, z): the nearest unexplored one; once all are explored,
        /// the enemy base site with the oldest (outdated) confirmation. Null when every site is current.
        /// </summary>
        public Site NextSiteToInvestigate(float fromX, float fromZ, float now, Func<Site, bool> exclude = null)
        {
            Site best = null;
            float bestD = float.MaxValue;
            foreach (var s in _sites)
            {
                if (s.State != SiteState.Unexplored || (exclude != null && exclude(s))) continue;
                float d = Sq(s.X - fromX) + Sq(s.Z - fromZ);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best != null) return best;
            float oldest = float.MaxValue;
            foreach (var s in _sites)
            {
                if (s.State != SiteState.EnemyBase || IsCurrent(s.LastConfirmed, now) || (exclude != null && exclude(s))) continue;
                if (s.LastConfirmed < oldest) { oldest = s.LastConfirmed; best = s; }
            }
            return best;
        }

        /// <summary>Best guess of where to go to fight: the known enemy, else the nearest unexplored candidate site.</summary>
        public (float x, float z)? BestGuess(float fromX, float fromZ, float now)
        {
            var focus = KnownEnemyFocus();
            if (focus != null) return focus;
            var site = NextSiteToInvestigate(fromX, fromZ, now);
            return site == null ? ((float, float)?)null : (site.X, site.Z);
        }

        public int Count(SiteState state)
        {
            int n = 0;
            foreach (var s in _sites) if (s.State == state) n++;
            return n;
        }

        private static float Sq(float v) => v * v;
    }
}
