using System.Collections.Generic;
using UnityEngine;

namespace Ironfront.Core
{
    /// <summary>
    /// RTS selection rules, independent of input devices and scene objects.
    /// </summary>
    public static class SelectionRules
    {
        /// <summary>
        /// Single click. <paramref name="clicked"/> is null when the click hit no selectable.
        /// Without the additive modifier: select only the clicked item, or clear on empty ground.
        /// With the additive modifier: toggle the clicked item; empty clicks leave the selection untouched.
        /// </summary>
        public static void ApplyClick<T>(SelectionSet<T> selection, T clicked, bool additive) where T : class
        {
            if (clicked == null)
            {
                if (!additive) selection.Clear();
                return;
            }

            if (additive)
                selection.Toggle(clicked);
            else
                selection.Replace(new[] { clicked });
        }

        /// <summary>
        /// Rectangle selection. Replaces the selection, or adds to it when additive.
        /// </summary>
        public static void ApplyBox<T>(SelectionSet<T> selection, IEnumerable<T> inBox, bool additive) where T : class
        {
            if (additive)
                selection.AddRange(inBox);
            else
                selection.Replace(inBox);
        }

        /// <summary>True once the pointer has travelled far enough from the press point to count as a drag.</summary>
        public static bool IsDrag(Vector2 pressPosition, Vector2 currentPosition, float thresholdPixels)
        {
            return (currentPosition - pressPosition).sqrMagnitude >= thresholdPixels * thresholdPixels;
        }

        /// <summary>Normalised rect spanning two arbitrary corners.</summary>
        public static Rect RectFromCorners(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>
        /// Whether a projected screen point (z = depth in front of camera) lies inside the rect.
        /// Points behind the camera are rejected.
        /// </summary>
        public static bool IsInsideBox(Rect box, Vector3 screenPoint)
        {
            return screenPoint.z > 0f && box.Contains(new Vector2(screenPoint.x, screenPoint.y), true);
        }
    }
}
