using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace SphereDescent
{
    /// <summary>
    /// Thin input layer so gameplay code works with either the legacy Input Manager or the Input System package.
    /// Keyboard + mouse and gamepad are both supported.
    /// </summary>
    public static class GameInput
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        public static Vector2 Move()
        {
            Vector2 v = Vector2.zero;
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) v.y += 1f;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) v.y -= 1f;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) v.x += 1f;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) v.x -= 1f;
            }
            if (Gamepad.current != null) v += Gamepad.current.leftStick.ReadValue();
            return Vector2.ClampMagnitude(v, 1f);
        }

        /// <summary>Camera orbit input in degrees for this frame (x = yaw, y = pitch).</summary>
        public static Vector2 Look(bool mouseActive, float mouseSensitivity, float keySpeed)
        {
            Vector2 v = Vector2.zero;
            if (mouseActive && Mouse.current != null) v += Mouse.current.delta.ReadValue() * (mouseSensitivity * 0.1f);
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.qKey.isPressed) v.x -= keySpeed * Time.deltaTime;
                if (k.eKey.isPressed) v.x += keySpeed * Time.deltaTime;
            }
            if (Gamepad.current != null) v += Gamepad.current.rightStick.ReadValue() * (keySpeed * Time.deltaTime);
            return v;
        }

        public static bool JumpPressed() =>
            (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        public static bool RespawnPressed() =>
            (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame);

        public static bool SkipWorldPressed() => Keyboard.current != null && Keyboard.current.nKey.wasPressedThisFrame;
        public static bool UnlockCursorPressed() => Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        public static bool LockCursorPressed() => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
        public static Vector2 Move() =>
            Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);

        /// <summary>Camera orbit input in degrees for this frame (x = yaw, y = pitch).</summary>
        public static Vector2 Look(bool mouseActive, float mouseSensitivity, float keySpeed)
        {
            Vector2 v = Vector2.zero;
            if (mouseActive) v += new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * mouseSensitivity;
            if (Input.GetKey(KeyCode.Q)) v.x -= keySpeed * Time.deltaTime;
            if (Input.GetKey(KeyCode.E)) v.x += keySpeed * Time.deltaTime;
            return v;
        }

        public static bool JumpPressed() => Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.JoystickButton0);
        public static bool RespawnPressed() => Input.GetKeyDown(KeyCode.R);
        public static bool SkipWorldPressed() => Input.GetKeyDown(KeyCode.N);
        public static bool UnlockCursorPressed() => Input.GetKeyDown(KeyCode.Escape);
        public static bool LockCursorPressed() => Input.GetMouseButtonDown(0);
#endif
    }
}
