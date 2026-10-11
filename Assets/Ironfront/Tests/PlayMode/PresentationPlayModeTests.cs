using System.Collections;
using System.Linq;
using Ironfront.Audio;
using Ironfront.Buildings;
using Ironfront.Combat;
using Ironfront.Construction;
using Ironfront.Data;
using Ironfront.Match;
using Ironfront.UI;
using Ironfront.Visuals;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ironfront.Tests
{
    /// <summary>
    /// Milestone 006 presentation in the real RTS_Match scene: effects and sounds follow gameplay events, wrecks
    /// and construction animations clean up after themselves and never change gameplay, and the pause menu
    /// pauses. These check wiring and lifetimes; how it looks and sounds still needs a human.
    /// </summary>
    public class PresentationPlayModeTests
    {
        private const int Player = 1;
        private const int Enemy = 2;
        private MatchController _match;
        private UnitDefinition _light;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            SceneManager.LoadScene("RTS_Match", LoadSceneMode.Single);
            yield return null;
            yield return null;
            _match = MatchController.Current;
            foreach (var ai in Object.FindObjectsByType<AI.EnemyCommander>()) ai.enabled = false; // quiet, deterministic map
            var factory = _match.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            _light = factory.ProducibleUnits.First(u => u.Id == "light_tank");
        }

        [TearDown]
        public void RestoreTime() => Time.timeScale = 1f;

        private CombatUnit Tank(Vector3 p, int faction) =>
            FactionSpawner.Spawn(_light.Prefab, p, Quaternion.LookRotation(faction == Player ? Vector3.forward : Vector3.back), faction)
                .GetComponent<CombatUnit>();

        private static IEnumerator Until(System.Func<bool> condition, float seconds)
        {
            float end = Time.time + seconds;
            while (!condition() && Time.time < end) yield return null;
        }

        [UnityTest]
        public IEnumerator Shot_PlaysFlashSmokeAndSound_AndRecoilsTheBarrel()
        {
            Assert.IsNotNull(GameAudio.Current, "the match scene has a GameAudio");
            var shooter = Tank(new Vector3(35f, 0f, -5f), Player);
            Tank(new Vector3(35f, 0f, 3f), Enemy);
            var barrel = shooter.transform.Find("TurretPivot/Barrel");
            Object.FindAnyObjectByType<CameraControl.RtsCameraController>().Focus(new Vector3(35f, 0f, -1f)); // effects are view-culled
            Vector3 rest = barrel.localPosition;
            int emitted = FxLibrary.Emitted, played = GameAudio.Played;
            Time.timeScale = 2f;
            yield return Until(() => shooter.ShotsFired > 0, 10f);
            Assert.Greater(shooter.ShotsFired, 0, "the tanks engage each other");
            yield return null;
            Assert.Greater(FxLibrary.Emitted, emitted, "muzzle particles emitted on Fired");
            Assert.Greater(GameAudio.Played, played, "firing sound played");
            Assert.Less(barrel.localPosition.z, rest.z - 0.01f, "barrel kicked back");
            shooter.enabled = false; // no further shots: the recoil must settle by itself
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(rest.z, barrel.localPosition.z, 0.02f, "barrel returned");
        }

        [UnityTest]
        public IEnumerator VehicleDeath_LeavesColliderFreeWreck_ThatRemovesItself()
        {
            var tank = Tank(new Vector3(35f, 0f, -5f), Enemy);
            yield return null;
            var health = tank.GetComponent<Health>();
            health.ApplyDamage(health.Max * 10f, Player);
            yield return null;
            var wreck = Object.FindObjectsByType<WreckBurner>().SingleOrDefault();
            Assert.IsNotNull(wreck, "a wreck replaces the destroyed tank");
            Assert.IsEmpty(wreck.GetComponentsInChildren<Collider>(), "wrecks never block, get selected or get targeted");
            Assert.IsNull(wreck.GetComponent<Health>());
            Assert.Greater(wreck.GetComponentsInChildren<MeshRenderer>().Length, 0);
            Time.timeScale = 4f;
            yield return Until(() => wreck == null, 15f);
            Assert.IsTrue(wreck == null, "the wreck sinks and is destroyed after ~10 s");
        }

        [UnityTest]
        public IEnumerator PlacedStructure_IsFunctionalAtOnce_WhileItsModelRisesIntoPlace()
        {
            var placement = Object.FindAnyObjectByType<BuildingPlacementController>();
            var power = _match.Setup.ConstructionOptions.First(d => d.Id == "power_plant");
            yield return new WaitForSeconds(1f); // structures present at scene load appear instantly; later ones rise
            int before = _match.PlayerFaction.Power.Produced;
            Assert.IsTrue(placement.TryPlace(power, new Vector3(-43f, 0f, -26f), Player, out var placed, out var reason), reason);
            yield return null;
            Assert.AreEqual(before + power.PowerOutput, _match.PlayerFaction.Power.Produced, "power counts immediately (unchanged rules)");
            var model = placed.transform.Find("Model");
            Assert.Less(model.localPosition.y, -0.5f, "model starts below ground");
            Assert.IsTrue(placed.GetComponent<ConstructionFx>().IsBuilding);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(0f, model.localPosition.y, 1e-4f, "model settles at ground level");
            Assert.IsFalse(placed.GetComponent<ConstructionFx>().IsBuilding);
        }

        [UnityTest]
        public IEnumerator HeavyDamage_TurnsOnSmoke_OnlyBelowHalfHealth()
        {
            var tank = Tank(new Vector3(35f, 0f, -5f), Player);
            Object.FindAnyObjectByType<CameraControl.RtsCameraController>().Focus(tank.transform.position);
            yield return null;
            var health = tank.GetComponent<Health>();
            var smoke = tank.GetComponent<DamageFx>();
            health.ApplyDamage(health.Max * 0.3f, Enemy);
            Assert.IsFalse(smoke.enabled, "70 % health: no smoke");
            health.ApplyDamage(health.Max * 0.35f, Enemy);
            Assert.IsTrue(smoke.enabled, "below half: smoking");
            int emitted = FxLibrary.Emitted;
            yield return new WaitForSeconds(0.6f);
            Assert.Greater(FxLibrary.Emitted, emitted, "smoke particles while damaged");
        }

        [UnityTest]
        public IEnumerator SameSoundSpam_IsCappedByTheVoiceLimiter()
        {
            yield return null;
            int played = GameAudio.Played, limited = GameAudio.Limited;
            var where = Camera.main.transform.position + Camera.main.transform.forward * 30f;
            for (int i = 0; i < 20; i++) GameAudio.Play(SoundId.LightCannon, where);
            Assert.LessOrEqual(GameAudio.Played - played, 6, "at most the configured voices start");
            Assert.Greater(GameAudio.Limited, limited, "the rest are refused instead of piling up");
        }

        [UnityTest]
        public IEnumerator GameMenu_PausesTheMatch_AndResumeRestoresTime()
        {
            var menu = Object.FindAnyObjectByType<GameMenu>();
            Assert.IsNotNull(menu, "pause menu present in the match");
            Time.timeScale = 2f;
            menu.Open();
            Assert.IsTrue(menu.IsOpen);
            Assert.AreEqual(0f, Time.timeScale);
            yield return null;
            menu.Close();
            Assert.IsFalse(menu.IsOpen);
            Assert.AreEqual(2f, Time.timeScale, "previous speed restored");
        }

        [Test]
        public void Definitions_HaveGeneratedIcons()
        {
            foreach (var b in _match.Setup.ConstructionOptions) Assert.IsNotNull(b.Icon, b.DisplayName);
            var factory = _match.Setup.ConstructionOptions.First(d => d.Id == "vehicle_factory");
            foreach (var u in factory.ProducibleUnits) Assert.IsNotNull(u.Icon, u.DisplayName);
        }
    }
}
