using Ironfront.Audio;
using Ironfront.Buildings;
using Ironfront.Match;
using UnityEngine;

namespace Ironfront.Visuals
{
    /// <summary>
    /// Construction feedback for structures placed during play: the visual model rises out of the ground with a
    /// dust ring, then settles with a second burst and the completion sound. Structures present when the scene
    /// loads appear immediately. The building is fully functional from the moment it is placed (unchanged
    /// gameplay); only the "Model" child moves.
    /// </summary>
    [RequireComponent(typeof(Building))]
    public class ConstructionFx : MonoBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField] private float duration = 1.6f;
        [SerializeField] private float depth = 4f;

        private float _t = -1f;
        private Vector2 _footprint;

        /// <summary>True while the rise animation plays.</summary>
        public bool IsBuilding => _t >= 0f;

        private void Start()
        {
            if (model == null || Time.timeSinceLevelLoad < 0.5f) return;
            var b = GetComponent<Building>();
            _footprint = b.Definition != null ? b.Definition.FootprintSize : new Vector2(4f, 4f);
            _t = 0f;
            model.localPosition = Vector3.down * depth;
            FxLibrary.DustRing(transform.position, _footprint);
            if (FactionMember.Of(this) == MatchController.Current?.PlayerFactionId)
                GameAudio.Play(SoundId.PlaceStructure, transform.position);
        }

        private void Update()
        {
            if (_t < 0f) return;
            _t += Time.deltaTime / duration;
            float e = 1f - Mathf.Pow(1f - Mathf.Clamp01(_t), 3f);
            model.localPosition = Vector3.down * depth * (1f - e);
            if (_t < 1f) return;
            model.localPosition = Vector3.zero;
            _t = -1f;
            FxLibrary.DustRing(transform.position, _footprint);
            if (FactionMember.Of(this) == MatchController.Current?.PlayerFactionId)
                GameAudio.Play(SoundId.ConstructionComplete, transform.position);
            enabled = false;
        }
    }
}
