using System.Collections;
using System.Linq;
using Ironfront.CameraControl;
using Ironfront.Commands;
using Ironfront.Selection;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Ironfront.Tests
{
    /// <summary>
    /// Loads the real RTS_Sandbox scene and exercises selection, formation movement and pathing
    /// through the same entry points the input handlers use.
    /// </summary>
    public class RtsSandboxPlayModeTests
    {
        private const string SceneName = "RTS_Sandbox";

        private SelectionManager _selection;
        private UnitCommandController _commands;
        private Camera _camera;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
            yield return null;
            yield return null;

            _selection = Object.FindAnyObjectByType<SelectionManager>();
            _commands = Object.FindAnyObjectByType<UnitCommandController>();
            _camera = _selection != null ? _selection.ViewCamera : null;
        }

        /// <summary>The five Milestone 001 capsule units (the scene now also contains a harvester).</summary>
        private static SelectableUnit[] Scouts =>
            SelectableUnit.All.Where(u => u.name.StartsWith("Unit_")).OrderBy(u => u.name).ToArray();

        [Test]
        public void Scene_HasRequiredSystemsAndFiveScouts()
        {
            Assert.IsNotNull(_selection, "SelectionManager missing");
            Assert.IsNotNull(_commands, "UnitCommandController missing");
            Assert.IsNotNull(_camera, "Selection camera missing");
            Assert.IsNotNull(Object.FindAnyObjectByType<RtsCameraController>(), "RTS camera missing");
            Assert.AreEqual(5, Scouts.Length);
            Assert.IsTrue(SelectableUnit.All.All(u => u.GetComponent<NavMeshAgent>().isOnNavMesh),
                "All units must start on the baked NavMesh");
            Assert.IsTrue(SelectableUnit.All.All(u => u.SelectionIndicator != null && !u.SelectionIndicator.activeSelf));
        }

        [Test]
        public void ClickOnUnit_SelectsIt_AndShowsIndicator_ClickOnGroundClears()
        {
            var unit = Scouts[0];
            _selection.ClickAt(ScreenPos(unit), additive: false);

            Assert.AreEqual(1, _selection.Selection.Count);
            Assert.IsTrue(unit.IsSelected);
            Assert.IsTrue(unit.SelectionIndicator.activeSelf);

            _selection.ClickAt(ScreenPos(new Vector3(-12f, 0f, -14f)), additive: false);
            Assert.AreEqual(0, _selection.Selection.Count);
            Assert.IsFalse(unit.SelectionIndicator.activeSelf);
        }

        [Test]
        public void ShiftClick_TogglesUnits()
        {
            var a = Scouts[0];
            var b = Scouts[1];
            _selection.ClickAt(ScreenPos(a), false);
            _selection.ClickAt(ScreenPos(b), true);
            Assert.AreEqual(2, _selection.Selection.Count);

            _selection.ClickAt(ScreenPos(a), true);
            Assert.AreEqual(1, _selection.Selection.Count);
            Assert.IsTrue(_selection.Selection.Contains(b));
            Assert.IsFalse(a.SelectionIndicator.activeSelf);
        }

        [Test]
        public void ScreenRectangle_SelectsEveryUnitOnScreen()
        {
            var screen = new Rect(0, 0, Screen.width, Screen.height);
            var visible = SelectableUnit.All
                .Where(u => { var p = _camera.WorldToScreenPoint(u.transform.position); return p.z > 0 && screen.Contains(p); })
                .ToList();
            Assert.GreaterOrEqual(visible.Count, 5, "The start view should show at least the five scouts");

            _selection.SelectInScreenRect(screen, false);
            CollectionAssert.AreEquivalent(visible, _selection.Selection.Items);
            Assert.IsTrue(visible.All(u => u.SelectionIndicator.activeSelf));
        }

        [UnityTest]
        public IEnumerator MoveOrder_SpreadsUnitsIntoFormation_AroundObstacles()
        {
            _selection.Selection.Replace(Scouts);
            var root = GameObject.Find("Environment/Obstacles");
            Assert.IsNotNull(root, "Environment/Obstacles not found");
            var obstacles = root.GetComponentsInChildren<Collider>().Select(c => c.bounds).ToList();
            Assert.GreaterOrEqual(obstacles.Count, 5, "Expected at least five obstacles");

            // Target on the far side of the central wall from the unit start area.
            var target = new Vector3(20f, 0f, 22f);
            int accepted = _commands.IssueMove(target);
            Assert.AreEqual(5, accepted, "Every selected unit should accept the move order");

            var agents = Scouts.Select(u => u.GetComponent<NavMeshAgent>()).ToList();
            float timeout = 30f;
            while (timeout > 0f && !agents.All(Arrived))
            {
                foreach (var a in agents)
                    foreach (var b in obstacles)
                        Assert.IsFalse(InsideFootprint(b, a.transform.position, 0.2f),
                            $"{a.name} passed through an obstacle at {a.transform.position}");
                timeout -= Time.deltaTime;
                yield return null;
            }

            Assert.IsTrue(agents.All(Arrived), "Units did not arrive within 30s");

            var finals = agents.Select(a => a.transform.position).ToList();
            for (int i = 0; i < finals.Count; i++)
            {
                Assert.Less(Flat(finals[i] - target).magnitude, 5f, $"{agents[i].name} ended far from target");
                for (int j = i + 1; j < finals.Count; j++)
                    Assert.Greater(Flat(finals[i] - finals[j]).magnitude, 1.0f,
                        $"{agents[i].name} and {agents[j].name} stacked on the same point");
            }
        }

        private static bool Arrived(NavMeshAgent a) =>
            !a.pathPending && a.remainingDistance <= a.stoppingDistance + 0.1f && a.velocity.sqrMagnitude < 0.05f;

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // Footprint shrunk by a margin so units brushing an edge don't count as passing through.
        private static bool InsideFootprint(Bounds b, Vector3 p, float margin) =>
            p.x > b.min.x + margin && p.x < b.max.x - margin && p.z > b.min.z + margin && p.z < b.max.z - margin;

        private Vector2 ScreenPos(SelectableUnit unit) => ScreenPos(unit.transform.position);
        private Vector2 ScreenPos(Vector3 world) => _camera.WorldToScreenPoint(world);
    }
}
