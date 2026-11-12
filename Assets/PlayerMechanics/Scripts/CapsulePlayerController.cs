using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Eindwerk.PlayerMechanics
{
    /// <summary>
    /// Reusable capsule movement and camera, independent of networking and game rules.
    /// The root is at the feet. Only the local owner enables input and its camera.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CapsulePlayerController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Transform visual;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private bool localControlEnabled;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float walkSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float sprintSpeed = 7.5f;
        [SerializeField, Min(0.1f)] private float crouchSpeed = 2.25f;
        [SerializeField, Min(0.1f)] private float acceleration = 24f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.4f;
        [SerializeField, Min(0.1f)] private float turnSpeed = 14f;
        [SerializeField] private float gravity = -24f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.25f;
        [SerializeField, Range(0f, 0.3f)] private float coyoteTime = 0.12f;
        [SerializeField, Range(0f, 0.3f)] private float jumpBufferTime = 0.15f;
        [SerializeField, Min(0.1f)] private float terminalFallSpeed = 45f;

        [Header("Capsule")]
        [SerializeField, Min(1f)] private float standingHeight = 2f;
        [SerializeField, Min(0.8f)] private float crouchingHeight = 1.15f;
        [SerializeField, Min(0.1f)] private float crouchTransitionSpeed = 5f;
        [SerializeField] private LayerMask obstructionMask = ~0;

        [Header("Camera")]
        [SerializeField] private bool firstPerson;
        [SerializeField, Range(0.01f, 1f)] private float mouseSensitivity = 0.12f;
        [SerializeField, Min(1f)] private float gamepadLookSpeed = 130f;
        [SerializeField, Range(-89f, 0f)] private float minimumPitch = -70f;
        [SerializeField, Range(0f, 89f)] private float maximumPitch = 75f;
        [SerializeField, Min(0.5f)] private float thirdPersonDistance = 4f;
        [SerializeField, Range(0.05f, 0.4f)] private float cameraCollisionRadius = 0.18f;
        [SerializeField] private LayerMask cameraCollisionMask = ~0;

        [Header("Reset")]
        [SerializeField] private float fallResetHeight = -20f;

        private readonly Collider[] standingObstructions = new Collider[24];
        private readonly RaycastHit[] cameraObstructions = new RaycastHit[24];
        private CharacterController character;
        private Renderer[] visualRenderers;
        private AudioListener cameraListener;
        private Vector3 visualStandingScale = Vector3.one;
        private Vector3 visualStandingPosition = Vector3.up;
        private Vector3 horizontalVelocity;
        private Vector3 spawnPosition;
        private Quaternion spawnRotation;
        private float verticalVelocity;
        private float yaw;
        private float pitch = 12f;
        private float lastGroundedTime = float.NegativeInfinity;
        private float lastJumpPressedTime = float.NegativeInfinity;
        private float movementTime;
        private float displayedHeight = 2f;
        private bool referencesCached;
        private bool crouching;
        private bool spawnPointSet;

        public bool IsCrouching => crouching;
        public bool IsGrounded => character != null && character.enabled && character.isGrounded;
        public float CurrentSpeed { get; private set; }
        public bool HasLocalControl => localControlEnabled;
        public bool CursorCaptured => localControlEnabled && Cursor.lockState == CursorLockMode.Locked;
        public bool IsFirstPerson => firstPerson;
        /// <summary>Optional UI hit test in bottom-left screen pixel coordinates.</summary>
        public System.Func<Vector2, bool> PointerBlocked;

        private void Awake()
        {
            CacheReferences();
            if (!spawnPointSet)
                SetSpawnPoint(transform.position, transform.rotation);
            yaw = transform.eulerAngles.y;
            ApplyControlState(false);
        }

        private void Start()
        {
            ApplyControlState(localControlEnabled);
        }

        /// <summary>Assign the capsule model, an eye pivot, and a child camera.</summary>
        public void Configure(Transform capsuleVisual, Transform pivot, Camera viewCamera)
        {
            visual = capsuleVisual;
            cameraPivot = pivot;
            playerCamera = viewCamera;
            referencesCached = false;
            CacheReferences();
            ApplyControlState(false);
            UpdateCapsuleVisual(standingHeight);
        }

        /// <summary>Called by an offline scene or by a network ownership callback.</summary>
        public void SetLocalControl(bool enabledForThisPlayer)
        {
            if (localControlEnabled && !enabledForThisPlayer)
                ReleaseCursor();
            localControlEnabled = enabledForThisPlayer;
            CacheReferences();
            ApplyControlState(enabledForThisPlayer && Application.isPlaying);
        }

        public void SetSpawnPoint(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
            spawnPointSet = true;
        }

        /// <summary>Moves without CharacterController collision resistance and clears momentum.</summary>
        public void Warp(Vector3 position, Quaternion rotation)
        {
            CacheReferences();
            bool wasEnabled = character.enabled;
            character.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            character.enabled = wasEnabled;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            CurrentSpeed = 0f;
            lastGroundedTime = float.NegativeInfinity;
            lastJumpPressedTime = float.NegativeInfinity;
            yaw = rotation.eulerAngles.y;
            pitch = 12f;
        }

        /// <summary>Remote players only need their capsule presentation updated.</summary>
        public void SetReplicatedCrouch(bool isCrouching)
        {
            if (!localControlEnabled)
                crouching = isCrouching;
        }

        public void CaptureCursor()
        {
            if (!localControlEnabled || !Application.isPlaying || !Application.isFocused)
                return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void ReleaseCursor()
        {
            if (!localControlEnabled)
                return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void CacheReferences()
        {
            if (referencesCached)
                return;
            character = GetComponent<CharacterController>();
            if (visual != null)
            {
                visualStandingScale = visual.localScale;
                visualStandingPosition = visual.localPosition;
                visualRenderers = visual.GetComponentsInChildren<Renderer>(true);
            }
            else
                visualRenderers = new Renderer[0];
            cameraListener = playerCamera != null ? playerCamera.GetComponent<AudioListener>() : null;
            displayedHeight = standingHeight;
            referencesCached = true;
        }

        private void ApplyControlState(bool captureCursor)
        {
            if (character != null)
                character.enabled = localControlEnabled;
            if (playerCamera != null)
                playerCamera.enabled = localControlEnabled;
            if (cameraListener != null)
                cameraListener.enabled = localControlEnabled;
            SetVisualVisibility();
            if (captureCursor)
                CaptureCursor();
        }

        private void Update()
        {
            if (!localControlEnabled || character == null || !character.enabled)
                return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Gamepad gamepad = Gamepad.current;
            bool releasePressed = (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                || (gamepad != null && gamepad.startButton.wasPressedThisFrame);
            if (releasePressed)
                ReleaseCursor();
            else if (!CursorCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame
                && !PointerOverInterface(mouse.position.ReadValue()))
                CaptureCursor();

            bool inputAllowed = CursorCaptured && Application.isFocused;
            Vector2 movement = Vector2.zero;
            bool sprint = false;
            bool crouchRequested = false;
            bool jumpRequested = false;
            if (inputAllowed)
            {
                if (keyboard != null)
                {
                    movement.x += Axis(keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed,
                        keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed);
                    movement.y += Axis(keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed,
                        keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed);
                    sprint = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
                    crouchRequested = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;
                    if (keyboard.spaceKey.wasPressedThisFrame)
                        jumpRequested = true;
                    if (keyboard.vKey.wasPressedThisFrame)
                        ToggleView();
                    if (keyboard.rKey.wasPressedThisFrame)
                        Warp(spawnPosition, spawnRotation);
                }
                if (gamepad != null)
                {
                    movement += gamepad.leftStick.ReadValue();
                    sprint |= gamepad.leftStickButton.isPressed;
                    crouchRequested |= gamepad.rightStickButton.isPressed;
                    if (gamepad.buttonSouth.wasPressedThisFrame)
                        jumpRequested = true;
                    if (gamepad.rightShoulder.wasPressedThisFrame)
                        ToggleView();
                }
                UpdateLook(mouse, gamepad);
            }

            // Keep gravity active while the pointer is free, but stop horizontal input.
            if (!inputAllowed)
                horizontalVelocity = Vector3.zero;
            SimulateMovement(movement, sprint, crouchRequested, jumpRequested, Time.deltaTime);
        }

        /// <summary>
        /// Input-independent movement step, also usable by automated physics checks.
        /// Direction is relative to the camera yaw; call once per frame from one source.
        /// Disable this MonoBehaviour if another input source calls this method directly.
        /// </summary>
        public void SimulateMovement(Vector2 movement, bool sprint, bool crouchRequested,
            bool jumpPressed, float deltaTime)
        {
            CacheReferences();
            if (!localControlEnabled || !character.enabled || deltaTime <= 0f)
                return;
            movementTime += deltaTime;
            if (jumpPressed)
                lastJumpPressedTime = movementTime;
            movement = Vector2.ClampMagnitude(movement, 1f);
            crouching = crouchRequested || (crouching && !CanStand());
            UpdateCapsuleHeight(deltaTime);
            if (character.isGrounded && verticalVelocity <= 0f)
            {
                lastGroundedTime = movementTime;
                verticalVelocity = -2f;
            }
            if (!crouching && movementTime - lastJumpPressedTime <= jumpBufferTime
                && movementTime - lastGroundedTime <= coyoteTime)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                lastJumpPressedTime = float.NegativeInfinity;
                lastGroundedTime = float.NegativeInfinity;
            }

            Quaternion planarView = Quaternion.Euler(0f, yaw, 0f);
            Vector3 desiredDirection = planarView * new Vector3(movement.x, 0f, movement.y);
            float speed = crouching ? crouchSpeed : (sprint ? sprintSpeed : walkSpeed);
            float control = character.isGrounded ? 1f : airControl;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, desiredDirection * speed,
                acceleration * control * deltaTime);
            if (firstPerson || desiredDirection.sqrMagnitude > 0.001f)
            {
                float desiredYaw = firstPerson ? yaw : Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(0f, desiredYaw, 0f),
                    1f - Mathf.Exp(-turnSpeed * deltaTime));
            }
            verticalVelocity = Mathf.Max(verticalVelocity + gravity * deltaTime, -terminalFallSpeed);
            CollisionFlags collisions = character.Move((horizontalVelocity + Vector3.up * verticalVelocity) * deltaTime);
            if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
                verticalVelocity = 0f;
            Vector3 actualVelocity = character.velocity;
            CurrentSpeed = new Vector2(actualVelocity.x, actualVelocity.z).magnitude;
            if (transform.position.y < fallResetHeight)
                Warp(spawnPosition, spawnRotation);
        }

        private void LateUpdate()
        {
            if (!localControlEnabled)
            {
                displayedHeight = Mathf.MoveTowards(displayedHeight,
                    crouching ? crouchingHeight : standingHeight, crouchTransitionSpeed * Time.deltaTime);
                UpdateCapsuleVisual(displayedHeight);
                return;
            }
            if (cameraPivot == null || playerCamera == null)
                return;

            cameraPivot.localPosition = new Vector3(0f, displayedHeight - 0.35f, 0f);
            cameraPivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
            float distance = firstPerson ? 0f : CameraDistanceWithCollision();
            playerCamera.transform.localPosition = new Vector3(0f, 0f, -distance);
            playerCamera.transform.localRotation = Quaternion.identity;
        }

        private void UpdateLook(Mouse mouse, Gamepad gamepad)
        {
            Vector2 look = mouse != null ? mouse.delta.ReadValue() * mouseSensitivity : Vector2.zero;
            if (gamepad != null)
                look += gamepad.rightStick.ReadValue() * gamepadLookSpeed * Time.deltaTime;
            yaw += look.x;
            pitch = Mathf.Clamp(pitch - look.y, minimumPitch, maximumPitch);
        }

        private void UpdateCapsuleHeight(float deltaTime)
        {
            displayedHeight = Mathf.MoveTowards(displayedHeight,
                crouching ? crouchingHeight : standingHeight, crouchTransitionSpeed * deltaTime);
            character.height = displayedHeight;
            character.center = Vector3.up * (displayedHeight * 0.5f);
            UpdateCapsuleVisual(displayedHeight);
        }

        private void UpdateCapsuleVisual(float height)
        {
            if (visual == null)
                return;
            Vector3 scale = visualStandingScale;
            scale.y *= height / standingHeight;
            visual.localScale = scale;
            Vector3 position = visualStandingPosition;
            position.y = height * 0.5f;
            visual.localPosition = position;
        }

        private bool CanStand()
        {
            float radius = Mathf.Max(0.05f, character.radius - 0.02f);
            Vector3 bottom = transform.position + Vector3.up * (radius + 0.04f);
            Vector3 top = transform.position + Vector3.up * (standingHeight - radius);
            int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, standingObstructions,
                obstructionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
                if (!standingObstructions[i].transform.IsChildOf(transform))
                    return false;
            return count < standingObstructions.Length;
        }

        private float CameraDistanceWithCollision()
        {
            Vector3 direction = -cameraPivot.forward;
            float result = thirdPersonDistance;
            int count = Physics.SphereCastNonAlloc(cameraPivot.position, cameraCollisionRadius, direction,
                cameraObstructions, thirdPersonDistance, cameraCollisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = cameraObstructions[i];
                if (!hit.transform.IsChildOf(transform))
                    result = Mathf.Min(result, Mathf.Max(0.15f, hit.distance - 0.05f));
            }
            return result;
        }

        private void ToggleView()
        {
            firstPerson = !firstPerson;
            SetVisualVisibility();
        }

        private void SetVisualVisibility()
        {
            if (visualRenderers == null)
                return;
            foreach (Renderer modelRenderer in visualRenderers)
                if (modelRenderer != null)
                    modelRenderer.enabled = !(localControlEnabled && firstPerson);
        }

        private static float Axis(bool positive, bool negative)
        {
            return (positive ? 1f : 0f) - (negative ? 1f : 0f);
        }

        private bool PointerOverInterface(Vector2 pointerPosition)
        {
            return (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                || (PointerBlocked != null && PointerBlocked(pointerPosition));
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                ReleaseCursor();
        }

        private void OnDisable()
        {
            ReleaseCursor();
            if (playerCamera != null)
                playerCamera.enabled = false;
            if (cameraListener != null)
                cameraListener.enabled = false;
        }

        private void OnEnable()
        {
            if (referencesCached)
                ApplyControlState(localControlEnabled && Application.isPlaying);
        }

        private void OnValidate()
        {
            gravity = Mathf.Min(-0.01f, gravity);
            sprintSpeed = Mathf.Max(walkSpeed, sprintSpeed);
            crouchingHeight = Mathf.Clamp(crouchingHeight, 0.8f, standingHeight);
        }
    }
}
