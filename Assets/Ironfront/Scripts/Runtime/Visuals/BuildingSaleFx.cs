using Ironfront.Audio;
using Ironfront.Buildings;
using Ironfront.Core.Economy;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Presentation of a building sale (Milestone 010): while dismantling, the visual model sinks into the ground in step
    /// with the progress, dust rises at the edges and dismantling clanks play; a cancelled sale restores the model. On
    /// completion the faction hears a credit chime and a final dust ring marks the cleared site. Reads
    /// <see cref="BuildingSale"/>; never changes it.
    /// </summary>
    public class BuildingSaleFx : MonoBehaviour
    {
        private const float SinkDepth = 3.5f;

        private BuildingSale _sale;
        private Building _building;
        private Transform _model;
        private Vector3 _modelRest;
        private float _dustTimer, _soundTimer;
        private bool _wasDismantling;

        private void Awake()
        {
            _sale = GetComponent<BuildingSale>();
            _building = GetComponent<Building>();
            _model = transform.Find("Model");
            if (_model != null) _modelRest = _model.localPosition;
        }

        private void OnEnable() => BuildingSale.AnySold += OnSold;
        private void OnDisable() => BuildingSale.AnySold -= OnSold;

        private void OnSold(Building b, int refund)
        {
            if (b != _building) return;
            var size = b.Definition != null ? b.Definition.FootprintSize : new Vector2(4f, 4f);
            if (Vision.VisionSystem.PlayerCanSee(transform.position)) FxLibrary.DustRing(transform.position, size);
            if (b.FactionId == MatchController.Current?.PlayerFactionId) GameAudio.Play(SoundId.SaleComplete, transform.position);
        }

        private void Update()
        {
            if (_sale == null) return;
            bool dismantling = _sale.State == SaleState.Dismantling;
            if (!dismantling)
            {
                if (_wasDismantling && _model != null) _model.localPosition = _modelRest; // cancelled
                _wasDismantling = false;
                return;
            }
            _wasDismantling = true;
            float p = _sale.Progress01;
            if (_model != null) _model.localPosition = _modelRest + Vector3.down * SinkDepth * p * p;

            if (!Vision.VisionSystem.PlayerCanSee(transform.position)) return;
            var size = _building != null && _building.Definition != null ? _building.Definition.FootprintSize : new Vector2(4f, 4f);
            _dustTimer -= Time.deltaTime;
            if (_dustTimer <= 0f)
            {
                _dustTimer = Random.Range(0.12f, 0.25f);
                float side = Random.value < 0.5f ? -0.5f : 0.5f;
                var local = Random.value < 0.5f
                    ? new Vector3(side * size.x, 0.2f, Random.Range(-0.5f, 0.5f) * size.y)
                    : new Vector3(Random.Range(-0.5f, 0.5f) * size.x, 0.2f, side * size.y);
                FxLibrary.Dust(transform.TransformPoint(local), Vector3.up * 1.2f + Random.insideUnitSphere * 0.5f, 1.4f);
                if (Random.value < 0.3f) FxLibrary.RepairSparks(transform.TransformPoint(local + Vector3.up * 1.5f));
            }
            _soundTimer -= Time.deltaTime;
            if (_soundTimer <= 0f)
            {
                _soundTimer = Random.Range(1.1f, 1.5f);
                GameAudio.Play(SoundId.Dismantle, transform.position);
            }
        }
    }
}
