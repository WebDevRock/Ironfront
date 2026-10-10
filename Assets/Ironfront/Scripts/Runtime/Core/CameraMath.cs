using UnityEngine;

namespace Ironfront.Core
{
    /// <summary>
    /// Pure helpers for the RTS camera.
    /// </summary>
    public static class CameraMath
    {
        /// <summary>
        /// Pan direction (-1..1 per axis) produced by the pointer resting within <paramref name="border"/>
        /// pixels of a screen edge. Returns zero when the pointer is outside the screen.
        /// </summary>
        public static Vector2 EdgePanDirection(Vector2 pointer, Vector2 screenSize, float border)
        {
            if (pointer.x < 0f || pointer.y < 0f || pointer.x > screenSize.x || pointer.y > screenSize.y)
                return Vector2.zero;

            Vector2 dir = Vector2.zero;
            if (pointer.x <= border) dir.x = -1f;
            else if (pointer.x >= screenSize.x - border) dir.x = 1f;
            if (pointer.y <= border) dir.y = -1f;
            else if (pointer.y >= screenSize.y - border) dir.y = 1f;
            return dir;
        }

        /// <summary>Applies scroll steps to a zoom distance and clamps it. Positive scroll zooms in.</summary>
        public static float ApplyZoom(float distance, float scroll, float step, float min, float max)
        {
            if (scroll > 0f) distance -= step;
            else if (scroll < 0f) distance += step;
            return Mathf.Clamp(distance, min, max);
        }

        /// <summary>Clamps a ground-plane position into XZ bounds.</summary>
        public static Vector3 ClampToBounds(Vector3 position, Vector2 min, Vector2 max)
        {
            position.x = Mathf.Clamp(position.x, min.x, max.x);
            position.z = Mathf.Clamp(position.z, min.y, max.y);
            return position;
        }

        /// <summary>Camera offset from its focus point for a given pitch/yaw (degrees) and distance.</summary>
        public static Vector3 OrbitOffset(float pitch, float yaw, float distance)
        {
            return Quaternion.Euler(pitch, yaw, 0f) * new Vector3(0f, 0f, -distance);
        }

        /// <summary>Converts a 2D pan input into a world-space XZ direction relative to camera yaw.</summary>
        public static Vector3 PanToWorld(Vector2 input, float yaw)
        {
            Vector3 local = new Vector3(input.x, 0f, input.y);
            if (local.sqrMagnitude > 1f) local.Normalize();
            return Quaternion.Euler(0f, yaw, 0f) * local;
        }
    }
}
