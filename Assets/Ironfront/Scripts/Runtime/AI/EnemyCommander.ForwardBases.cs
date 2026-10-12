using System.Collections.Generic;
using System.Linq;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Core.AI;
using Ironfront.Core.Construction;
using Ironfront.Core.Economy;
using Ironfront.Data;
using Ironfront.Economy;
using Ironfront.Match;
using Ironfront.Units;
using UnityEngine;

namespace Ironfront.AI
{
    /// <summary>
    /// Milestone 010 part of the computer commander: construction territory, forward bases and building sales, with the
    /// same rules, prices and timings as the player.
    /// <list type="bullet">
    /// <item>Refineries are only planned at ore fields with a site inside its own territory.</item>
    /// <item>When the next field lies outside, <see cref="ForwardBaseRules"/> decides whether to buy a Mobile Command
    /// Vehicle. The vehicle drives, with an escort, to an explored field away from known enemy structures, and deploys
    /// through <see cref="MobileCommandVehicle.Deploy"/>. Positions and sites are retried a bounded number of times, and
    /// failed sites are remembered.</item>
    /// <item>Forward bases get a guard tower; obsolete refineries and lost-cause forward bases are sold.</item>
    /// </list>
    /// It knows only what it has explored and seen, and gets no free vehicles or instant deployments.
    /// </summary>
    public partial class EnemyCommander
    {
        private sealed class McvTask
        {
            public Vector3 Site;
            public bool HasSite;
            public OreDeposit Target;
            public int Positions, Sites, Nudges;
            public float SiteSince;
            public float GaveUpUntil = -1f;
            public bool Parked;
        }

        private UnitDefinition _mcvDef;
        private BuildingDefinition _forwardDef;
        private readonly Dictionary<MobileCommandVehicle, McvTask> _mcvTasks = new Dictionary<MobileCommandVehicle, McvTask>();
        private readonly HashSet<CombatUnit> _escorts = new HashSet<CombatUnit>();
        private readonly List<Vector3> _failedForwardSites = new List<Vector3>();
        private float _lastMcvLoss = -999f;
        private bool _deployHooked;

        public int McvsProduced { get; private set; }
        public int McvsLost { get; private set; }
        public int ForwardBasesEstablished { get; private set; }
        /// <summary>Time.time when the first forward base was established (-1 = none), like <see cref="EnemyIntel.FirstDiscovery"/>.</summary>
        public float FirstForwardBaseTime { get; private set; } = -1f;
        public int DeployAttempts { get; private set; }
        public int DeployFailures { get; private set; }
        public int StructuresSold { get; private set; }
        public int SaleRefunds { get; private set; }
        public IReadOnlyCollection<CombatUnit> Escorts => _escorts;

        private void ResolveForwardDefinitions()
        {
            _mcvDef = _factoryDef.ProducibleUnits.FirstOrDefault(u => u != null && u.Id == "mcv");
            var vehicle = _mcvDef != null && _mcvDef.Prefab != null ? _mcvDef.Prefab.GetComponent<MobileCommandVehicle>() : null;
            _forwardDef = vehicle != null ? vehicle.DeploysInto : null;
            if (_forwardDef == null) _mcvDef = null; // no deployable content: the rules stay off
            if (!_deployHooked)
            {
                MobileCommandVehicle.AnyDeployed += OnAnyDeployed;
                _deployHooked = true;
            }
        }

        private void OnDestroy()
        {
            if (_deployHooked) MobileCommandVehicle.AnyDeployed -= OnAnyDeployed;
            _deployHooked = false;
        }

        private void OnAnyDeployed(MobileCommandVehicle m, Building centre)
        {
            if (centre == null || centre.FactionId != factionId) return;
            ForwardBasesEstablished++;
            if (FirstForwardBaseTime < 0f) FirstForwardBaseTime = Time.time;
            _mcvTasks.Remove(m);
            Record($"Forward base established at ({centre.transform.position.x:0},{centre.transform.position.z:0})");
        }

        // ---------- Territory knowledge for the snapshot ----------

        private bool IsForwardBase(Building b) => b != null && b.Definition != null && b.Definition.Id == MatchFlowController.ForwardCommandId;

        /// <summary>Could a refinery for this deposit stand inside our territory (deposit within reach of an anchor)?</summary>
        private bool RefinerySiteInTerritory(Vector3 deposit)
        {
            foreach (var a in TerritorySystem.Map.AnchorsOf(factionId))
                if ((new Vector2(deposit.x, deposit.z) - a.Centre).magnitude <= a.Radius + 4f) return true;
            return false;
        }

        /// <summary>Explored, reachable fields that no refinery of ours serves yet.</summary>
        private List<OreDeposit> UnclaimedDeposits(Vector3 home)
        {
            var refineries = OwnStructures().Where(b => b.Definition.Id == "refinery").Select(b => b.transform.position).ToList();
            return OreDeposit.All.Where(d => !d.IsDepleted && Ironfront.Vision.VisionSystem.IsExplored(factionId, d.transform.position)
                                             && refineries.All(r => (r - d.transform.position).sqrMagnitude > 18f * 18f)
                                             && GroundDistance(home, d) < float.MaxValue).ToList();
        }

        private float NearestKnownEnemy(Vector3 p)
        {
            float best = float.MaxValue;
            foreach (var s in _perception.Structures)
                best = Mathf.Min(best, Vector3.Distance(s.Position, p));
            return best;
        }

        private float KnownEnemyBaseDistance(Vector3 p)
        {
            if (_intel == null || !_intel.EnemyLocated) return float.MaxValue;
            float best = float.MaxValue;
            foreach (var s in _intel.Sightings)
                if (s.Headquarters) best = Mathf.Min(best, Vector3.Distance(new Vector3(s.X, 0f, s.Z), p));
            return best;
        }

        private bool AcceptableForwardTarget(Vector3 home, OreDeposit d)
        {
            var p = d.transform.position;
            if (_failedForwardSites.Any(f => (f - p).sqrMagnitude < 30f * 30f)) return false;
            return ForwardBaseRules.AcceptableSite(NearestKnownEnemy(p), Vector3.Distance(home, p), KnownEnemyBaseDistance(p));
        }

        private void FillTerritorySnapshot(ref CommanderSnapshot s, Vector3 home)
        {
            var unclaimed = UnclaimedDeposits(home);
            s.RefineryBlockedByTerritory = unclaimed.Count > 0 && !unclaimed.Any(d => RefinerySiteInTerritory(d.transform.position));
            var targets = _mcvDef == null ? new List<OreDeposit>()
                : unclaimed.Where(d => !RefinerySiteInTerritory(d.transform.position) && AcceptableForwardTarget(home, d)).ToList();
            s.ExpansionTargetKnown = targets.Count > 0;
            var refineries = OwnStructures().Where(b => b.Definition.Id == "refinery").Select(b => b.FrontPoint).ToList();
            if (refineries.Count == 0) refineries.Add(home);
            s.ExpansionHaulDistance = targets.Count == 0 ? 0f
                : targets.Min(d => refineries.Min(r => Vector3.Distance(r, d.transform.position)));
            foreach (var b in OwnStructures())
            {
                if (!IsForwardBase(b)) continue;
                s.ForwardBases++;
                var p = b.transform.position;
                bool defended = OwnStructures().Any(o => (o.Definition.Id == "guard_tower" || o.Definition.Id == "at_turret")
                                                         && (o.transform.position - p).sqrMagnitude < 30f * 30f);
                if (!defended) s.UndefendedForwardBases++;
            }
            foreach (var m in MobileCommandVehicle.All)
                if (m != null && m.FactionId == factionId && m.Health != null && m.Health.IsAlive) s.Mcvs++;
            foreach (var f in VehicleFactory.All)
            {
                if (f.FactionId != factionId) continue;
                foreach (var item in f.Queue.Items)
                    if (_mcvDef != null && item.Id == _mcvDef.Id) s.QueuedMcv++;
            }
            s.McvLosses = McvsLost;
            s.McvOnCooldown = Time.time - _lastMcvLoss < ForwardBaseRules.LossCooldown;
        }

        // ---------- MCV travel and deployment ----------

        private void ManageForwardBases(List<CombatUnit> army)
        {
            _escorts.RemoveWhere(u => u == null || !u.GetComponent<Health>().IsAlive);
            foreach (var gone in _mcvTasks.Keys.Where(k => k == null).ToList()) _mcvTasks.Remove(gone);
            var mine = MobileCommandVehicle.All.Where(m => m != null && m.FactionId == factionId && m.Health != null && m.Health.IsAlive).ToList();
            if (mine.Count == 0)
            {
                _escorts.Clear(); // they rejoin the army at the next decision
                return;
            }

            var home = Home();
            foreach (var m in mine)
            {
                if (!_mcvTasks.TryGetValue(m, out var t))
                {
                    t = new McvTask();
                    _mcvTasks[m] = t;
                    McvsProduced++;
                    m.Health.Died += _ =>
                    {
                        McvsLost++;
                        _lastMcvLoss = Time.time;
                        if (t.HasSite) _failedForwardSites.Add(t.Site); // dangerous ground: do not send the next one there
                        Record("Mobile Command Vehicle lost");
                    };
                    Record("Mobile Command Vehicle ready");
                }

                if (m.IsDeploying)
                {
                    Escort(m, army, guard: true);
                    continue;
                }
                if (t.GaveUpUntil > Time.time) continue;
                if (t.GaveUpUntil > 0f) { t.GaveUpUntil = -1f; t.Sites = 0; }

                if (t.HasSite && Time.time - t.SiteSince > ForwardBaseRules.TravelTimeout) FailSite(m, t, "travel timeout");
                if (!t.HasSite && !ChooseForwardSite(m, t, home != null ? home.transform.position : m.transform.position))
                {
                    if (!t.Parked && m.TryGetComponent<UnitMover>(out var park)) { park.MoveTo(RallyPoint); t.Parked = true; }
                    continue;
                }

                var mover = m.GetComponent<UnitMover>();
                var flat = m.transform.position - t.Site;
                flat.y = 0f;
                if (flat.magnitude > 3f)
                {
                    var agent = mover.Agent;
                    bool heading = agent != null && agent.hasPath && (agent.destination - t.Site).sqrMagnitude < 9f;
                    if (!heading) mover.MoveTo(t.Site);
                    Escort(m, army, guard: false);
                    continue;
                }

                DeployAttempts++;
                var result = m.Deploy();
                if (result == DeployCheck.Valid)
                {
                    Record($"Deploying forward base at ({t.Site.x:0},{t.Site.z:0})");
                    continue;
                }
                if (result == DeployCheck.UnitsInTheWay && t.Nudges < 4)
                {
                    // Own units (often the escort) on the footprint: move them off and try again, like a player would.
                    t.Nudges++;
                    ClearFootprint(m, t.Site);
                    continue;
                }
                DeployFailures++;
                t.Positions++;
                Record($"Deploy refused: {DeployProcess.Describe(result)}");
                if (t.Positions >= ForwardBaseRules.PositionsPerSite || !NextDeployPosition(m, t)) FailSite(m, t, "no valid position");
            }
        }

        private void FailSite(MobileCommandVehicle m, McvTask t, string why)
        {
            Record($"Forward site ({t.Site.x:0},{t.Site.z:0}) abandoned: {why}");
            _failedForwardSites.Add(t.Site);
            t.HasSite = false;
            t.Positions = 0;
            t.Sites++;
            if (t.Sites >= ForwardBaseRules.SitesPerVehicle)
            {
                t.GaveUpUntil = Time.time + ForwardBaseRules.LossCooldown; // try again later with fresh sites
                t.Parked = false;
                if (m.TryGetComponent<UnitMover>(out var mover)) mover.MoveTo(RallyPoint);
            }
        }

        private bool ChooseForwardSite(MobileCommandVehicle m, McvTask t, Vector3 home)
        {
            if (_forwardDef == null) return false;
            var targets = UnclaimedDeposits(home).Where(d => !RefinerySiteInTerritory(d.transform.position) && AcceptableForwardTarget(home, d))
                .OrderBy(d => GroundDistance(home, d)).Take(3);
            foreach (var d in targets)
            {
                var p = d.transform.position;
                var towardsHome = (home - p);
                towardsHome.y = 0f;
                var centre = p + (towardsHome.sqrMagnitude > 1f ? towardsHome.normalized : Vector3.forward) * 16f;
                if (!FindDeployPosition(m, centre, home, out var site)) continue;
                t.Site = site;
                t.Target = d;
                t.HasSite = true;
                t.SiteSince = Time.time;
                t.Positions = 0;
                t.Nudges = 0;
                t.Parked = false;
                Record($"Forward base site chosen near ore at ({p.x:0},{p.z:0})");
                return true;
            }
            return false;
        }

        /// <summary>A deployable centre near <paramref name="around"/>, using the same deployment validation as the player.</summary>
        private bool FindDeployPosition(MobileCommandVehicle m, Vector3 around, Vector3 home, out Vector3 site)
        {
            site = default;
            int checks = 0;
            foreach (var c in SiteSearch.Ranked(around, home, 0f, 14f, 2f))
            {
                if (++checks > 120) break;
                if (_failedForwardSites.Any(f => (f - c).sqrMagnitude < 6f * 6f)) continue;
                if (m.CheckSite(c, includeUnits: false) != DeployCheck.Valid) continue;
                site = c;
                return true;
            }
            return false;
        }

        private bool NextDeployPosition(MobileCommandVehicle m, McvTask t)
        {
            _failedForwardSites.Add(t.Site); // this exact spot failed
            var home = Home();
            if (!FindDeployPosition(m, t.Site, home != null ? home.transform.position : t.Site, out var next)) return false;
            t.Site = next;
            t.SiteSince = Time.time;
            return true;
        }

        /// <summary>Orders own armed units standing on the planned footprint to move clear of it.</summary>
        private void ClearFootprint(MobileCommandVehicle m, Vector3 site)
        {
            foreach (var u in OwnArmy())
            {
                var d = u.transform.position - site;
                d.y = 0f;
                if (d.magnitude > 9f) continue;
                var away = site + (d.sqrMagnitude > 0.01f ? d.normalized : Vector3.right) * 13f;
                Order(u, c => c.OrderMove(away));
            }
        }

        /// <summary>Keeps up to <see cref="ForwardBaseRules.EscortSize"/> army units with the vehicle (travel) or around it (deploying).</summary>
        private void Escort(MobileCommandVehicle m, List<CombatUnit> army, bool guard)
        {
            if (State == StrategicState.Defend) return; // the base comes first
            foreach (var u in army.Where(u => !_attackers.Contains(u) && !IsArtillery(u))
                         .OrderBy(u => (u.transform.position - m.transform.position).sqrMagnitude))
            {
                if (_escorts.Count >= ForwardBaseRules.EscortSize) break;
                _escorts.Add(u);
            }
            int i = 0;
            foreach (var u in _escorts)
            {
                float ang = (i++ * 120f + 30f) * Mathf.Deg2Rad;
                // Outside the future centre's 9 m footprint, so the escort never blocks the deployment.
                var spot = m.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (guard ? 13f : 10f);
                if ((u.transform.position - spot).sqrMagnitude > (guard ? 25f : 64f))
                    Order(u, c => c.OrderAttackMove(spot));
            }
        }

        // ---------- Sales ----------

        private void ManageSales(List<CombatUnit> army, List<Health> threats)
        {
            var own = OwnStructures().ToList();
            int refineries = own.Count(b => b.Definition.Id == "refinery");
            foreach (var b in own)
            {
                if (b.Sale == null || b.Sale.IsDismantling || !b.Definition.Sellable) continue;
                bool sell = false;
                string why = "";
                if (b.Definition.Id == "refinery")
                {
                    var p = b.FrontPoint;
                    // Harvesters haul from any field to the nearest refinery: it stays useful while ore remains within reach.
                    bool ore = OreDeposit.All.Any(d => !d.IsDepleted && (d.transform.position - p).sqrMagnitude <= ForwardBaseRules.ObsoleteRadius * ForwardBaseRules.ObsoleteRadius);
                    if (SaleRules.AiShouldSellRefinery(ore, refineries)) { sell = true; why = "ore exhausted"; }
                }
                else if (IsForwardBase(b))
                {
                    var h = b.GetComponent<Health>();
                    var p = b.transform.position;
                    bool attacked = threats.Any(t => t != null && (t.transform.position - p).sqrMagnitude < 30f * 30f);
                    int near = army.Count(u => (u.transform.position - p).sqrMagnitude < 35f * 35f);
                    if (SaleRules.AiShouldSellForwardBase(h.Current / h.Max, attacked, near)) { sell = true; why = "about to be lost"; }
                    else
                    {
                        float reach = b.Definition.TerritoryRadius + 4f;
                        float oreReach = ForwardBaseRules.ObsoleteRadius;
                        bool ore = OreDeposit.All.Any(d => !d.IsDepleted && (d.transform.position - p).sqrMagnitude <= oreReach * oreReach);
                        int factories = own.Count(o => o.Definition.Id == "vehicle_factory" && (o.transform.position - p).sqrMagnitude <= reach * reach);
                        if (SaleRules.AiShouldSellObsoleteForwardBase(ore, factories)) { sell = true; why = "its ore is exhausted"; }
                    }
                }
                if (!sell || !b.Sale.Begin(out _)) continue;
                if (b.Definition.Id == "refinery") refineries--;
                StructuresSold++;
                SaleRefunds += b.Sale.EstimatedRefund;
                Record($"Selling {b.Definition.DisplayName}: {why} (refund about {b.Sale.EstimatedRefund})");
            }
        }
    }
}
