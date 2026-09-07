using UnityEngine;
using UnityEngine.InputSystem;

namespace ARReveal
{
    /// <summary>
    /// Editor/desktop-testing-only free-fly camera - WASD (+ Q/E for down/up) to move,
    /// hold the right mouse button and drag to look around, left Shift to move faster.
    /// Lets you walk up to a tentacle in Play mode to test distance-based behaviour
    /// (TentacleController.ReachByDistance) without a real device or a working webcam
    /// pipeline.
    ///
    /// Attach directly to whichever camera is tagged MainCamera (the same one
    /// TentacleController's reach-toward-camera logic reads via Camera.main) - if this
    /// project has more than one Camera in the scene, only the one actually tagged
    /// MainCamera is what the distance check sees, so put this on that one specifically.
    ///
    /// Purely additive: only moves this transform while MoveEnabled is on, so it's
    /// safe to leave attached and switch off once something else (Zappar's own
    /// tracking, on a real device or once the in-editor webcam pipeline works) should
    /// be driving this transform instead.
    ///
    /// Uses the new Input System's direct device polling (Keyboard.current/
    /// Mouse.current) rather than the legacy Input class or a pre-built Input Actions
    /// asset - works regardless of whether this project's Active Input Handling is set
    /// to "Input System Package (New)" or "Both", with nothing else to configure.
    /// </summary>
    public class DebugFlyCamera : MonoBehaviour
    {
        [Tooltip("Master switch - off leaves this transform alone entirely.")]
        public bool MoveEnabled = true;

        [Header("Move")]
        public float MoveSpeed = 2f;
        public float FastMoveMultiplier = 3f;

        [Header("Look")]
        [Tooltip("Only look around while the right mouse button is held - keeps the mouse free for clicking UI/Inspector the rest of the time.")]
        public bool RequireRightMouseHeld = true;
        public float LookSpeed = 0.15f;
        public bool InvertY = false;

        private float _yaw;
        private float _pitch;
        private bool _anglesInitialised;

        private void Update()
        {
            if (!MoveEnabled) return;

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null) return;

            // Read the starting facing once, on first use, rather than assuming (0,0,0)
            // - this transform likely already has whatever rotation Zappar/the scene
            // authored it with.
            if (!_anglesInitialised)
            {
                Vector3 e = transform.eulerAngles;
                _yaw = e.y;
                _pitch = e.x;
                _anglesInitialised = true;
            }

            if (mouse != null && (!RequireRightMouseHeld || mouse.rightButton.isPressed))
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * LookSpeed;
                float dy = delta.y * LookSpeed * (InvertY ? 1f : -1f);
                _pitch = Mathf.Clamp(_pitch - dy, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            float speed = MoveSpeed * (kb.leftShiftKey.isPressed ? FastMoveMultiplier : 1f);
            Vector3 move = Vector3.zero;
            if (kb.wKey.isPressed) move += transform.forward;
            if (kb.sKey.isPressed) move -= transform.forward;
            if (kb.dKey.isPressed) move += transform.right;
            if (kb.aKey.isPressed) move -= transform.right;
            if (kb.eKey.isPressed) move += Vector3.up;
            if (kb.qKey.isPressed) move -= Vector3.up;

            transform.position += move.normalized * speed * Time.deltaTime;
        }
    }
}
