using Ironfront.Audio;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Ironfront.UI
{
    /// <summary>Hover tick and click sound for a button (interactable buttons only; disabled ones stay silent).</summary>
    [RequireComponent(typeof(Selectable))]
    public class UiSound : MonoBehaviour, IPointerEnterHandler, IPointerClickHandler
    {
        private Selectable _selectable;

        private void Awake() => _selectable = GetComponent<Selectable>();

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_selectable != null && _selectable.IsInteractable()) GameAudio.PlayFlat(SoundId.UiHover);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            GameAudio.PlayFlat(_selectable != null && _selectable.IsInteractable() ? SoundId.UiClick : SoundId.UiError);
        }
    }
}
