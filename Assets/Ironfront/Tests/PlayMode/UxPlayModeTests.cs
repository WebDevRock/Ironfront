using System;
using System.Collections;
using System.Linq;
using Ironfront.CameraControl;
using Ironfront.Combat;
using Ironfront.Commands;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.Selection;
using Ironfront.UI;
using Ironfront.Units;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 005 usability features in the real RTS_Sandbox scene: retaliation, control groups,
    /// notifications, minimap, tooltips, help overlay and FX pooling. Test units are spawned in the open
    /// south-east of the map (as in <see cref="CombatPlayModeTests"/>).
    /// </summary>
    public class UxPlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;

        private MatchController _match;
        private SelectionManager _selection;
        private UnitDefinition _light, _heavy;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("RTS_Sandbox", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _match = MatchController.Current;
            _selection = Object.FindAnyObjectByType<SelectionManager>();
            var factory = _match.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            _light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
            _heavy = factory.ProducibleUnits.First(u => u.Id == "heavy_tank");
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        private static CombatUnit SpawnTank(UnitDefinition def, Vector3 position, int faction)
        {
            var go = Object.Instantiate(def.Prefab, position, Quaternion.identity);
            go.GetComponent<FactionMember>().FactionId = faction;
            return go.GetComponent<CombatUnit>();
        }

        private static IEnumerator WaitFor(Func<bool> condition, float seconds, bool unscaled = false)
        {
            float t = 0f;
            while (!condition() && t < seconds)
            {
                t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }
        }

        private static Vector2 ScreenPointOf(RectTransform rt, Vector2 uv)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners); // screen-space overlay: world corners are screen pixels
            return new Vector2(Mathf.Lerp(corners[0].x, corners[2].x, uv.x), Mathf.Lerp(corners[0].y, corners[2].y, uv.y));
        }

        // ---------- Retaliation ----------

        [UnityTest]
        public IEnumerator IdleUnit_ShotFromOutsideItsRange_ReturnsFire()
        {
            Time.timeScale = 4f;
            // Light tank: weapon range 11. The enemy heavy (range 13) attacks from 13 m, outside the light's range,
            // so before Milestone 005 the light tank sat idle and died without firing.
            var victim = SpawnTank(_light, new Vector3(30f, 0f, -45f), Player);
            var attacker = SpawnTank(_heavy, new Vector3(30f, 0f, -27f), Enemy);
            yield return null;
            attacker.OrderAttack(victim.GetComponent<Health>());

            bool sawRetaliation = false;
            yield return WaitFor(() =>
            {
                sawRetaliation |= victim.Machine.IsRetaliating;
                return victim.ShotsFired > 0 && attacker.GetComponent<Health>().Model.IsDamaged;
            }, 25f);

            Assert.IsTrue(victim.GetComponent<Health>().Model.IsDamaged, "the enemy did attack");
            Assert.IsTrue(sawRetaliation, "the idle unit switched to returning fire");
            Assert.Greater(victim.ShotsFired, 0, "the attacked unit fired back");
            Assert.IsTrue(attacker.GetComponent<Health>().Model.IsDamaged, "and hit its attacker");
            Assert.Greater(FxPool.Reused, 0, "projectiles / impact effects come from the pool");
        }

        [UnityTest]
        public IEnumerator AttackMoveOntoAStructureCentre_GoesToItsEdge_InsteadOfBeingIgnored()
        {
            // Regression (balance study): a destination deep inside a carved footprint failed the NavMesh search,
            // the order silently ended and an AI attacker stood idle forever.
            Time.timeScale = 4f;
            var hq = Buildings.Building.All.First(b => b.FactionId == Player && b.Definition != null && b.Definition.Id == "headquarters");
            var tank = SpawnTank(_light, new Vector3(30f, 0f, -45f), Player);
            yield return null;
            float start = Vector3.Distance(tank.transform.position, hq.transform.position);

            tank.OrderAttackMove(hq.transform.position);
            yield return null;
            Assert.AreNotEqual(Core.Combat.CombatOrderType.Idle, tank.Machine.Order, "the order is accepted");
            yield return WaitFor(() => tank.Machine.Order == Core.Combat.CombatOrderType.Idle, 30f);

            float end = Vector3.Distance(tank.transform.position, hq.transform.position);
            Assert.Less(end, 12f, $"the tank reached the structure (start {start:0} m, end {end:0} m)");
        }

        // ---------- Control groups ----------

        [UnityTest]
        public IEnumerator ControlGroups_AssignRecall_AndDropDestroyedUnits()
        {
            var groups = Object.FindAnyObjectByType<ControlGroupInput>();
            Assert.IsNotNull(groups, "ControlGroupInput is in the scene");
            var a = SpawnTank(_light, new Vector3(30f, 0f, -45f), Player).GetComponent<SelectableUnit>();
            var b = SpawnTank(_light, new Vector3(34f, 0f, -45f), Player).GetComponent<SelectableUnit>();
            yield return null;

            _selection.Selection.Replace(new[] { a, b });
            groups.Assign(2);
            _selection.Selection.Clear();
            groups.Recall(2, focusCamera: false);
            CollectionAssert.AreEquivalent(new[] { a, b }, _selection.Selection.Items);
            Assert.IsTrue(NotificationCenter.Current.Recent.Any(m => m.Contains("Group 2")), "assignment is announced");

            var cam = Object.FindAnyObjectByType<RtsCameraController>();
            groups.Recall(2, focusCamera: true);
            Assert.AreEqual(32f, cam.transform.position.x, 0.5f, "double-tap centres the camera on the group");

            Object.Destroy(b.gameObject);
            yield return null;
            _selection.Selection.Clear();
            groups.Recall(2, focusCamera: false);
            CollectionAssert.AreEqual(new[] { a }, _selection.Selection.Items);
        }

        // ---------- Notifications ----------

        [UnityTest]
        public IEnumerator AttackOnBase_PostsThrottledAlert_AndSpaceTargetIsRecorded()
        {
            var center = NotificationCenter.Current;
            Assert.IsNotNull(center);
            var hq = Health.All.First(h => h.IsStructure && h.FactionId == Player);

            hq.ApplyDamage(10f, Enemy);
            yield return null;
            hq.ApplyDamage(10f, Enemy);
            yield return null;

            Assert.AreEqual(1, center.Recent.Count(m => m.Contains("base is under attack")), "repeated hits are throttled");
            Assert.IsTrue(center.LastAlertPosition.HasValue);
            Assert.Less(Vector3.Distance(center.LastAlertPosition.Value, hq.transform.position), 0.01f);
            var feed = GameObject.Find("NotificationFeed").transform;
            Assert.Greater(feed.childCount, 0, "the alert is visible in the feed");

            var victim = SpawnTank(_light, new Vector3(30f, 0f, -45f), Player).GetComponent<Health>();
            yield return null;
            victim.ApplyDamage(1e6f, Enemy);
            yield return null;
            Assert.IsTrue(center.Recent.Any(m => m.StartsWith("Unit lost")));
        }

        // ---------- Minimap ----------

        [UnityTest]
        public IEnumerator Minimap_ShowsUnits_LeftClickMovesCamera_RightClickMovesSelection()
        {
            var minimap = Object.FindAnyObjectByType<Minimap>();
            Assert.IsNotNull(minimap);
            var unit = SpawnTank(_light, new Vector3(30f, 0f, -45f), Player);
            yield return null;
            int redraws = minimap.Redraws;
            yield return WaitFor(() => minimap.Redraws > redraws, 1f, unscaled: true);
            Assert.Greater(minimap.Redraws, redraws, "redrawn on a timer");

            var tex = minimap.Texture;
            var uv = Core.MinimapMath.WorldToUv(unit.transform.position, minimap.WorldRect);
            var px = tex.GetPixel((int)(uv.x * tex.width), (int)(uv.y * tex.height));
            Color player = _match.Setup.ColorOf(Player);
            Assert.Less(Mathf.Abs(px.r - player.r) + Mathf.Abs(px.g - player.g) + Mathf.Abs(px.b - player.b), 0.05f,
                "the unit is drawn in the player's colour at its position");

            var raw = minimap.GetComponent<UnityEngine.UI.RawImage>().rectTransform;
            var es = EventSystem.current;
            var cam = Object.FindAnyObjectByType<RtsCameraController>();

            var target = minimap.UvToWorld(new Vector2(0.25f, 0.75f));
            var click = new PointerEventData(es) { button = PointerEventData.InputButton.Left, position = ScreenPointOf(raw, new Vector2(0.25f, 0.75f)) };
            ExecuteEvents.Execute(minimap.gameObject, click, ExecuteEvents.pointerDownHandler);
            Assert.AreEqual(target.x, cam.transform.position.x, 0.6f, "left click centres the camera");
            Assert.AreEqual(target.z, cam.transform.position.z, 0.6f);

            _selection.Selection.Replace(new[] { unit.GetComponent<SelectableUnit>() });
            var dest = new Vector3(30f, 0f, -25f);
            var duv = Core.MinimapMath.WorldToUv(dest, minimap.WorldRect);
            var right = new PointerEventData(es) { button = PointerEventData.InputButton.Right, position = ScreenPointOf(raw, duv) };
            ExecuteEvents.Execute(minimap.gameObject, right, ExecuteEvents.pointerDownHandler);
            Time.timeScale = 4f;
            float start = Vector3.Distance(unit.transform.position, dest);
            yield return WaitFor(() => Vector3.Distance(unit.transform.position, dest) < 2.5f, 8f);
            Assert.Less(Vector3.Distance(unit.transform.position, dest), Mathf.Min(3f, start), "right click ordered a move there");
        }

        // ---------- Tooltips & help ----------

        [UnityTest]
        public IEnumerator BuildButtonTooltip_ShowsCostAndStatus_AndF1HelpToggles()
        {
            var view = TooltipView.Current;
            Assert.IsNotNull(view, $"TooltipView.Current is null; views in scene {Object.FindObjectsByType<TooltipView>().Length}, " +
                                   $"scene {SceneManager.GetActiveScene().name}");
            var button = GameObject.Find("Build_power_plant") ?? GameObject.FindObjectsByType<TooltipTrigger>().First().gameObject;
            ExecuteEvents.Execute(button, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            yield return WaitFor(() => view.IsVisible, 2f, unscaled: true);
            Assert.IsTrue(view.IsVisible);
            StringAssert.Contains("$", view.ShownText);
            ExecuteEvents.Execute(button, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
            Assert.IsFalse(view.IsVisible);

            var help = Object.FindAnyObjectByType<HelpOverlay>();
            Assert.IsFalse(help.IsVisible, "hidden at start");
            help.Toggle();
            Assert.IsTrue(help.IsVisible);
            help.Toggle();
            Assert.IsFalse(help.IsVisible);
        }

        // ---------- Damage feedback ----------

        [UnityTest]
        public IEnumerator HealthBar_FlashesOnHit()
        {
            var unit = SpawnTank(_light, new Vector3(30f, 0f, -45f), Player);
            yield return null;
            var bar = unit.GetComponent<HealthBar>();
            Assert.IsFalse(bar.IsFlashing);
            unit.GetComponent<Health>().ApplyDamage(20f, Enemy);
            Assert.IsTrue(bar.IsFlashing);
            yield return null;
            Assert.IsTrue(bar.IsVisible, "a damaged unit shows its bar");
            yield return WaitFor(() => !bar.IsFlashing, 1f);
            Assert.IsFalse(bar.IsFlashing, "the flash is brief");
        }
    }
}
