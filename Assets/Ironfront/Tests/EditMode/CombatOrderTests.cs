using System.Collections.Generic;
using System.Linq;
using Ironfront.Core.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Ironfront.Tests
{
    /// <summary>Order transitions of <see cref="CombatOrderMachine"/> against a simulated body on a flat plane.</summary>
    public class CombatOrderTests
    {
        private class Target : ICombatTarget
        {
            public bool Alive = true;
            public Vector3 Pos;
            public bool IsAlive => Alive;
            public Vector3 Position => Pos;
            public string DisplayName => "target";
        }

        private class Body : ICombatBody
        {
            public Vector3 Pos;
            public float Speed = 5f;
            public bool Blocked;
            public Vector3? Dest;
            public readonly List<Target> Enemies = new List<Target>();
            public readonly HashSet<Target> NoSight = new HashSet<Target>();
            public readonly WeaponCooldown Cooldown = new WeaponCooldown(1f);
            public readonly List<(float time, Target target)> Shots = new List<(float, Target)>();
            public int MoveCalls, ChaseCalls, HaltCalls;
            public float Time;

            public Vector3 Position => Pos;
            public float DistanceTo(ICombatTarget t) => Vector3.Distance(Pos, t.Position);
            public bool HasLineOfSight(ICombatTarget t) => !NoSight.Contains((Target)t);

            public ICombatTarget FindTarget(float radius, ICombatTarget ignore) =>
                Enemies.Where(e => e.Alive && e != ignore && Vector3.Distance(Pos, e.Pos) <= radius)
                    .OrderBy(e => Vector3.Distance(Pos, e.Pos)).FirstOrDefault();

            public void MoveTo(Vector3 d) { Dest = d; MoveCalls++; }
            public void ChaseTo(ICombatTarget t) { Dest = t.Position; ChaseCalls++; }
            public void Halt() { Dest = null; HaltCalls++; }
            public bool HasArrived => Dest == null || Vector3.Distance(Pos, Dest.Value) < 0.05f;

            public bool TryFire(ICombatTarget t)
            {
                if (!Cooldown.TryFire()) return false;
                Shots.Add((Time, (Target)t));
                return true;
            }

            public void Step(float dt)
            {
                Time += dt;
                Cooldown.Tick(dt);
                if (Dest == null || Blocked) return;
                Pos = Vector3.MoveTowards(Pos, Dest.Value, Speed * dt);
            }
        }

        private const float Dt = 0.1f;
        private const float Range = 10f;

        private static CombatOrderMachine NewMachine(float range = Range, float acquisition = 14f) =>
            new CombatOrderMachine(CombatSettings.ForWeapon(range, acquisition, requiresLineOfSight: true));

        private static void Run(CombatOrderMachine m, Body b, float seconds, System.Action perTick = null)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                perTick?.Invoke();
                m.Tick(Dt, b);
                b.Step(Dt);
            }
        }

        // ---- Move ----

        [Test]
        public void Move_TravelsWithoutEngaging_ThenBecomesIdle()
        {
            var b = new Body();
            b.Enemies.Add(new Target { Pos = new Vector3(15f, 0f, 5f) }); // passes within range on the way
            var m = NewMachine();

            m.OrderMove(b, new Vector3(30f, 0f, 0f));
            Run(m, b, 3f);
            Assert.AreEqual(CombatOrderType.Move, m.Order);
            Assert.AreEqual(0, b.Shots.Count, "Move never fires");
            Assert.IsNull(m.Target);

            Run(m, b, 5f);
            Assert.AreEqual(CombatOrderType.Idle, m.Order);
            Assert.AreEqual(30f, b.Pos.x, 0.1f);
        }

        [Test]
        public void Idle_FiresAtEnemyInRange_WithoutMoving()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(8f, 0f, 0f) };
            b.Enemies.Add(enemy);
            var m = NewMachine();

            Run(m, b, 3f);
            Assert.Greater(b.Shots.Count, 0);
            Assert.AreEqual(Vector3.zero, b.Pos, "Idle units do not move");
            Assert.AreEqual(0, b.MoveCalls + b.ChaseCalls);
        }

        [Test]
        public void Idle_IgnoresEnemyOutOfRange()
        {
            var b = new Body();
            b.Enemies.Add(new Target { Pos = new Vector3(12f, 0f, 0f) });
            var m = NewMachine();
            Run(m, b, 3f);
            Assert.AreEqual(0, b.Shots.Count);
            Assert.AreEqual(Vector3.zero, b.Pos);
        }

        // ---- Attack ----

        [Test]
        public void Attack_ClosesIntoRange_Stops_AndFires()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(40f, 0f, 0f) };
            var m = NewMachine();

            m.OrderAttack(b, enemy);
            Run(m, b, 8f);

            float d = Vector3.Distance(b.Pos, enemy.Pos);
            Assert.LessOrEqual(d, Range, "Moved into range");
            Assert.Greater(d, Range - 2f, "Stopped near max range rather than driving up to the target");
            Assert.AreEqual(CombatActivity.Engaging, m.Activity);
            Assert.IsNull(b.Dest, "Halted while firing");
            Assert.Greater(b.Shots.Count, 0);
            Assert.IsTrue(b.Shots.All(s => s.target == enemy));
        }

        [Test]
        public void Attack_TargetDestroyed_EndsInIdle()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(5f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 1f);
            enemy.Alive = false;
            Run(m, b, 0.5f);

            Assert.AreEqual(CombatOrderType.Idle, m.Order);
            Assert.IsNull(m.Target);
            StringAssert.Contains("destroyed", m.Describe());
        }

        [Test]
        public void Attack_TargetMovesAway_UnitReapproaches()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(15f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 4f);
            Assert.AreEqual(CombatActivity.Engaging, m.Activity);
            int shotsBefore = b.Shots.Count;

            enemy.Pos = new Vector3(40f, 0f, 10f);
            Run(m, b, 1f);
            Assert.AreEqual(CombatActivity.Chasing, m.Activity);
            Run(m, b, 8f);
            Assert.AreEqual(CombatActivity.Engaging, m.Activity);
            Assert.LessOrEqual(Vector3.Distance(b.Pos, enemy.Pos), Range);
            Assert.Greater(b.Shots.Count, shotsBefore);
        }

        [Test]
        public void Engaging_TargetJustInsideMaxRange_NoStartStopJitter()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(20f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 5f);
            Assert.AreEqual(CombatActivity.Engaging, m.Activity);
            int chases = b.ChaseCalls;

            // Target drifts back and forth inside the hysteresis band (between stop distance and max range).
            float baseX = enemy.Pos.x;
            int i = 0;
            Run(m, b, 5f, () => enemy.Pos = new Vector3(baseX + ((i++ % 2 == 0) ? 0.4f : 0f), 0f, 0f));
            Assert.AreEqual(chases, b.ChaseCalls, "No new paths while the target stays within max range");
            Assert.AreEqual(CombatActivity.Engaging, m.Activity);
        }

        [Test]
        public void Chase_RepathIsThrottled_ForSmallTargetMovement()
        {
            var b = new Body { Speed = 1f };
            var enemy = new Target { Pos = new Vector3(60f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            // Target creeps 0.1 m per tick: re-paths only when it has moved > RepathDistance (2 m), at most every 0.5 s.
            Run(m, b, 10f, () => enemy.Pos += new Vector3(0f, 0f, 0.1f));
            Assert.LessOrEqual(b.ChaseCalls, 7, "About one re-path per 2 m of target movement");
            Assert.GreaterOrEqual(b.ChaseCalls, 3, "Still follows the moving target");
        }

        [Test]
        public void Attack_UnreachableTarget_GivesUpInsteadOfStickingForever()
        {
            var b = new Body { Blocked = true };
            var enemy = new Target { Pos = new Vector3(40f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 5f);

            Assert.AreEqual(CombatOrderType.Idle, m.Order);
            Assert.IsNull(m.Target);
            StringAssert.Contains("unreachable", m.Describe());
            Assert.Greater(b.HaltCalls, 0);
        }

        [Test]
        public void Attack_InRangeWithoutLineOfSight_DoesNotFire_ButRepositions()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(6f, 0f, 0f) };
            b.NoSight.Add(enemy);
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 1f);
            Assert.AreEqual(0, b.Shots.Count);
            Assert.AreEqual(CombatActivity.Chasing, m.Activity);
            Assert.Greater(b.ChaseCalls, 0);

            b.NoSight.Clear();
            Run(m, b, 1f);
            Assert.Greater(b.Shots.Count, 0);
        }

        [Test]
        public void FireRate_NeverExceedsCooldown()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(5f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 10f);
            Assert.GreaterOrEqual(b.Shots.Count, 9);
            for (int i = 1; i < b.Shots.Count; i++)
                Assert.GreaterOrEqual(b.Shots[i].time - b.Shots[i - 1].time, 1f - 1e-3f);
        }

        // ---- Attack-move ----

        [Test]
        public void AttackMove_EngagesThreatOnTheWay_ThenResumesAndArrives()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(20f, 0f, 8f) };
            b.Enemies.Add(enemy);
            var m = NewMachine();

            m.OrderAttackMove(b, new Vector3(40f, 0f, 0f));
            Run(m, b, 4f);
            Assert.AreSame(enemy, m.Target, "Enemy acquired en route");
            Assert.Greater(b.Shots.Count, 0);
            Assert.Less(b.Pos.x, 40f);

            enemy.Alive = false;
            Run(m, b, 1f);
            Assert.AreEqual(CombatOrderType.AttackMove, m.Order);
            Assert.AreEqual(CombatActivity.Moving, m.Activity, "Resumed advancing");

            Run(m, b, 10f);
            Assert.AreEqual(CombatOrderType.Idle, m.Order);
            Assert.AreEqual(40f, b.Pos.x, 0.1f);
        }

        [Test]
        public void AttackMove_UnreachableThreat_IsSkipped_AndAdvanceContinues()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(12f, 0f, 12f) };
            b.Enemies.Add(enemy);
            var m = NewMachine();
            m.OrderAttackMove(b, new Vector3(40f, 0f, 0f));
            Run(m, b, 1.5f); // enters the 14 m acquisition radius after ~1 s
            Assert.AreSame(enemy, m.Target);

            b.Blocked = true;                   // cannot get closer
            Run(m, b, 5f);
            Assert.IsNull(m.Target);
            b.Blocked = false;
            Run(m, b, 2f);
            Assert.IsNull(m.Target, "The unreachable enemy is not immediately re-acquired");
            Assert.AreEqual(CombatOrderType.AttackMove, m.Order);
        }

        // ---- Hold & Stop ----

        [Test]
        public void Hold_EngagesInRange_NeverPursues()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(20f, 0f, 0f) };
            b.Enemies.Add(enemy);
            var m = NewMachine();
            m.OrderHold(b);

            Run(m, b, 2f);
            Assert.AreEqual(0, b.Shots.Count, "Out of range: no fire");
            Assert.AreEqual(Vector3.zero, b.Pos, "...and no pursuit");

            enemy.Pos = new Vector3(8f, 0f, 0f);
            Run(m, b, 2f);
            Assert.Greater(b.Shots.Count, 0);
            int shots = b.Shots.Count;

            enemy.Pos = new Vector3(30f, 0f, 0f);
            Run(m, b, 3f);
            Assert.AreEqual(shots, b.Shots.Count, "Stops firing when the target leaves range");
            Assert.AreEqual(Vector3.zero, b.Pos);
            Assert.AreEqual(CombatOrderType.Hold, m.Order);
            Assert.AreEqual(0, b.MoveCalls + b.ChaseCalls);
        }

        [Test]
        public void Hold_DoesNotFireWithoutLineOfSight()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(5f, 0f, 0f) };
            b.Enemies.Add(enemy);
            b.NoSight.Add(enemy);
            var m = NewMachine();
            m.OrderHold(b);
            Run(m, b, 3f);
            Assert.AreEqual(0, b.Shots.Count);
        }

        [Test]
        public void Stop_CancelsAttack_AndHalts()
        {
            var b = new Body();
            var enemy = new Target { Pos = new Vector3(40f, 0f, 0f) };
            var m = NewMachine();
            m.OrderAttack(b, enemy);
            Run(m, b, 1f);
            Vector3 stoppedAt = b.Pos;

            m.OrderStop(b);
            Run(m, b, 3f);
            Assert.AreEqual(CombatOrderType.Idle, m.Order);
            Assert.IsNull(m.Target);
            Assert.AreEqual(stoppedAt, b.Pos);
            Assert.AreEqual("Idle", m.Describe());
        }

        [Test]
        public void Stop_CancelsMove()
        {
            var b = new Body();
            var m = NewMachine();
            m.OrderMove(b, new Vector3(50f, 0f, 0f));
            Run(m, b, 1f);
            m.OrderStop(b);
            Vector3 at = b.Pos;
            Run(m, b, 2f);
            Assert.AreEqual(at, b.Pos);
            Assert.AreEqual(CombatOrderType.Idle, m.Order);
        }

        [Test]
        public void NewOrder_ReplacesPrevious_AndRaisesOrderChanged()
        {
            var b = new Body();
            var m = NewMachine();
            var seen = new List<CombatOrderType>();
            m.OrderChanged += seen.Add;
            var enemy = new Target { Pos = new Vector3(30f, 0f, 0f) };

            m.OrderAttack(b, enemy);
            m.OrderMove(b, new Vector3(0f, 0f, 30f));
            Assert.IsNull(m.Target, "Move clears the attack target");
            m.OrderAttackMove(b, Vector3.one);
            m.OrderHold(b);
            m.OrderStop(b);
            CollectionAssert.AreEqual(new[]
            {
                CombatOrderType.Attack, CombatOrderType.Move, CombatOrderType.AttackMove, CombatOrderType.Hold, CombatOrderType.Idle,
            }, seen);
        }

        [Test]
        public void OrderAttack_OnDeadTarget_IsIgnored()
        {
            var b = new Body();
            var m = NewMachine();
            m.OrderAttack(b, new Target { Alive = false });
            Assert.AreEqual(CombatOrderType.Idle, m.Order);
            m.OrderAttack(b, null);
            Assert.AreEqual(CombatOrderType.Idle, m.Order);
        }
    }
}
