using UnityEngine;

namespace Ironfront.Selection
{
    /// <summary>
    /// Draws the drag-selection rectangle using a UI RectTransform anchored to the bottom-left of an
    /// overlay canvas. Its graphic must not be a raycast target. Start the GameObject inactive.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class SelectionBoxView : MonoBehaviour
    {
        [SerializeField] private Canvas canvas;

        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            if (canvas == null) canvas = GetComponentInParent<Canvas>();
        }

        public void Show(Rect screenRect)
        {
            if (_rect == null) _rect = (RectTransform)transform;
            float scale = canvas != null && canvas.scaleFactor > 0f ? canvas.scaleFactor : 1f;
            _rect.anchoredPosition = screenRect.min / scale;
            _rect.sizeDelta = screenRect.size / scale;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
