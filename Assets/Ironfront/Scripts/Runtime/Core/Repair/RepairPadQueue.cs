using System.Collections.Generic;

namespace Ironfront.Core.Repair
{
    /// <summary>
    /// First-in, first-out queue of one Repair Pad (Milestone 009). Clients are identified by an integer id.
    /// <list type="bullet">
    /// <item>A client is queued at most once (duplicates refused).</item>
    /// <item>At most one client holds the bay (<see cref="Active"/>).</item>
    /// <item>The bay passes to the next client only after the previous one has <b>vacated</b> it: finishing or leaving
    /// marks it <see cref="Vacating"/> until <see cref="MarkVacated"/>; the caller also checks the bay is physically clear
    /// before calling <see cref="TryGrant"/>.</item>
    /// </list>
    /// Pure; the runtime pad decides when the bay is physically clear.
    /// </summary>
    public sealed class RepairPadQueue
    {
        public const int None = -1;

        private readonly List<int> _waiting = new List<int>();

        /// <summary>Client holding the bay (entering or being repaired), or <see cref="None"/>.</summary>
        public int Active { get; private set; } = None;
        /// <summary>Client that held the bay and is leaving it, or <see cref="None"/>. Blocks new grants.</summary>
        public int Vacating { get; private set; } = None;

        public IReadOnlyList<int> Waiting => _waiting;
        public int WaitingCount => _waiting.Count;
        /// <summary>Vehicles using the pad: waiting plus the one in the bay.</summary>
        public int Count => _waiting.Count + (Active != None ? 1 : 0);
        public bool BayFree => Active == None && Vacating == None;

        public bool Contains(int id) => id != None && (Active == id || _waiting.Contains(id));

        /// <summary>Adds a client to the back of the queue. Returns false if it is already queued or in the bay.</summary>
        public bool Enqueue(int id)
        {
            if (id == None || Contains(id)) return false;
            _waiting.Add(id);
            return true;
        }

        /// <summary>0-based place in the waiting line, -1 when not waiting (in the bay or not queued).</summary>
        public int PositionOf(int id) => _waiting.IndexOf(id);

        /// <summary>
        /// Gives the bay to the first waiting client if the bay is free. <paramref name="bayPhysicallyClear"/> is the
        /// runtime check that nothing else stands in the bay. Returns the granted id or <see cref="None"/>.
        /// </summary>
        public int TryGrant(bool bayPhysicallyClear)
        {
            if (!BayFree || !bayPhysicallyClear || _waiting.Count == 0) return None;
            Active = _waiting[0];
            _waiting.RemoveAt(0);
            return Active;
        }

        /// <summary>The active client is done (repaired, cancelled or gave up) and is leaving the bay.</summary>
        public void Release(int id)
        {
            if (id == None || Active != id) return;
            Active = None;
            Vacating = id;
        }

        /// <summary>The leaving client is out of the bay (or gone). New grants become possible.</summary>
        public void MarkVacated(int id)
        {
            if (id != None && Vacating == id) Vacating = None;
        }

        /// <summary>
        /// Removes a client wherever it is: a waiting client leaves the line (everyone behind moves up); the active client
        /// releases the bay into the vacating state; a vacating client is treated as gone (<paramref name="gone"/>) or
        /// still leaving. Returns true if anything changed.
        /// </summary>
        public bool Remove(int id, bool gone)
        {
            if (id == None) return false;
            if (_waiting.Remove(id)) return true;
            if (Active == id)
            {
                Active = None;
                Vacating = gone ? None : id;
                return true;
            }
            if (Vacating == id && gone)
            {
                Vacating = None;
                return true;
            }
            return false;
        }

        /// <summary>Forgets every client (pad destroyed).</summary>
        public List<int> Clear()
        {
            var all = new List<int>(_waiting);
            if (Active != None) all.Insert(0, Active);
            if (Vacating != None && !all.Contains(Vacating)) all.Add(Vacating);
            _waiting.Clear();
            Active = Vacating = None;
            return all;
        }
    }
}
