using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Ironfront.InputBindings
{
    /// <summary>
    /// Access to the "RTS" action map of the project-wide Input System actions
    /// (Assets/InputSystem_Actions.inputactions). Keeps all bindings in the project's input asset.
    /// </summary>
    public static class RtsInput
    {
        public const string MapName = "RTS";

        private static InputActionMap _map;

        public static InputAction Pan => Get("Pan");
        public static InputAction Zoom => Get("Zoom");
        public static InputAction Point => Get("Point");
        public static InputAction Select => Get("Select");
        public static InputAction Command => Get("Command");
        public static InputAction Additive => Get("Additive");
        public static InputAction Cancel => Get("Cancel");
        public static InputAction AttackMove => Get("AttackMove");
        public static InputAction Stop => Get("Stop");
        public static InputAction Hold => Get("Hold");
        public static InputAction ToggleAiDebug => Get("ToggleAiDebug");

        public static Vector2 PointerPosition => Point?.ReadValue<Vector2>() ?? Vector2.zero;
        public static bool AdditiveHeld => Additive != null && Additive.IsPressed();

        public static InputActionMap Map
        {
            get
            {
                if (_map == null)
                {
                    var asset = InputSystem.actions;
                    _map = asset != null ? asset.FindActionMap(MapName, false) : null;
                    if (_map == null)
                        Debug.LogError($"[Ironfront] Input action map '{MapName}' not found in project-wide actions.");
                }
                if (_map != null && !_map.enabled) _map.Enable();
                return _map;
            }
        }

        /// <summary>True when the pointer is over a raycast-target UI element.</summary>
        public static bool PointerOverUI()
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject();
        }

        private static InputAction Get(string name) => Map?.FindAction(name, false);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _map = null;
    }
}
