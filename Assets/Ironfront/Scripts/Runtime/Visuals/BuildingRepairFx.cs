using Ironfront.Audio;
using Ironfront.Buildings;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Presentation of an in-place building repair (Milestone 009): welding sparks at random points on the structure and
    /// a periodic repair sound while <see cref="BuildingRepair"/> is repairing, only where the player can see it.
    /// Reads the repair state; never changes it. Added next to every <see cref="BuildingRepair"/>.
    /// </summary>
    public class BuildingRepairFx : MonoBehaviour
    {
        private BuildingRepair _repair;
        private Building _building;
        private float _sparkTimer, _soundTimer;

        private void Awake()
        {
            _repair = GetComponent<BuildingRepair>();
            _building = GetComponent<Building>();
        }

        private void Update()
        {
            if (_repair == null || _repair.State != BuildingRepair.Status.Repairing) return;
            if (!Vision.VisionSystem.PlayerCanSee(transform.position)) return;
            var size = _building != null && _building.Definition != null ? _building.Definition.FootprintSize : new Vector2(4f, 4f);
            _sparkTimer -= Time.deltaTime;
            if (_sparkTimer <= 0f)
            {
                _sparkTimer = Random.Range(0.15f, 0.35f);
                var local = new Vector3(Random.Range(-0.45f, 0.45f) * size.x, Random.Range(0.6f, 2.4f), Random.Range(-0.45f, 0.45f) * size.y);
                FxLibrary.RepairSparks(transform.TransformPoint(local));
            }
            _soundTimer -= Time.deltaTime;
            if (_soundTimer <= 0f)
            {
                _soundTimer = Random.Range(1.6f, 2.2f);
                GameAudio.Play(SoundId.RepairWork, transform.position);
            }
        }
    }
}
