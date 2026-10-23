using UnityEngine;
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace SphereDescent
{
    /// <summary>
    /// Thin input layer so gameplay code works with either the legacy Input Manager or the Input System package.
    /// The project uses the Input System, which gives full keyboard + mouse and gamepad support. The legacy
    /// fallback only handles keyboard + mouse (plus left-stick movement and jump on a gamepad).
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
            if (Gamepad.current != null) v += Gamepad.current.leftStick.ReadValue() + Gamepad.current.dpad.ReadValue();
            return Vector2.ClampMagnitude(v, 1f);
        }

        public static bool GamepadConnected => Gamepad.current != null;

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

        public static bool MenuPressed() =>
            (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) ||
            (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);

        /// <summary>-1 / +1 when left / right was pressed this frame (arrow keys, D-pad), else 0.</summary>
        public static int MenuStep()
        {
            int step = 0;
            var k = Keyboard.current;
            if (k != null) step += (k.rightArrowKey.wasPressedThisFrame ? 1 : 0) - (k.leftArrowKey.wasPressedThisFrame ? 1 : 0);
            var g = Gamepad.current;
            if (g != null) step += (g.dpad.right.wasPressedThisFrame ? 1 : 0) - (g.dpad.left.wasPressedThisFrame ? 1 : 0);
            return step;
        }

        public static bool ConfirmPressed() =>
            (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame)) ||
            (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);

        /// <summary>Any key, mouse button or gamepad button pressed this frame.</summary>
        public static bool AnyPressed()
        {
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) return true;
            var m = Mouse.current;
            if (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame)) return true;
            var g = Gamepad.current;
            return g != null && (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame ||
                                 g.buttonNorth.wasPressedThisFrame || g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame ||
                                 g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame);
        }

        /// <summary>Which number key (1-9) was pressed this frame, or 0 for none.</summary>
        public static int NumberPressed()
        {
            var k = Keyboard.current;
            if (k == null) return 0;
            for (int n = 1; n <= 9; n++)
                if (k[Key.Digit1 + (n - 1)].wasPressedThisFrame || k[Key.Numpad1 + (n - 1)].wasPressedThisFrame) return n;
            return 0;
        }
#else
        public static Vector2 Move() =>
            Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);

        public static bool GamepadConnected => false;

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
        public static bool MenuPressed() => Input.GetKeyDown(KeyCode.M);

        /// <summary>-1 / +1 when left / right was pressed this frame, else 0.</summary>
        public static int MenuStep() =>
            (Input.GetKeyDown(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKeyDown(KeyCode.LeftArrow) ? 1 : 0);

        public static bool ConfirmPressed() => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);

        /// <summary>Any key, mouse button or gamepad button pressed this frame.</summary>
        public static bool AnyPressed() => Input.anyKeyDown;

        /// <summary>Which number key (1-9) was pressed this frame, or 0 for none.</summary>
        public static int NumberPressed()
        {
            for (int n = 1; n <= 9; n++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + (n - 1)) || Input.GetKeyDown(KeyCode.Keypad1 + (n - 1))) return n;
            return 0;
        }
#endif
    }
}
