using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Eindwerk.PlayerMechanics
{
    /// <summary>
    /// Bestuurt de speler en de camera. De spelerpositie ligt bij de voeten.
    /// Alleen de lokale speler verwerkt invoer; surf wordt per map aangezet.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class CapsulePlayerController : MonoBehaviour
    {
        [Header("Verwijzingen")]
        [SerializeField] private Transform visual;
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private bool localControlEnabled;

        [Header("Beweging")]
        [SerializeField, Min(0.1f), Tooltip("Gewone loopsnelheid in m/s.")] private float walkSpeed = 4.5f;
        [SerializeField, Min(0.1f), Tooltip("Sprint in m/s. Bij surf gebruiken we de verhouding sprinten/lopen.")] private float sprintSpeed = 7.5f;
        [SerializeField, Min(0.1f)] private float crouchSpeed = 2.25f;
        [SerializeField, Min(0.1f)] private float acceleration = 24f;
        [SerializeField, Range(0f, 1f)] private float airControl = 0.4f;
        [SerializeField, Min(0.1f)] private float turnSpeed = 14f;
        [SerializeField] private float gravity = -24f;
        [SerializeField, Min(0.1f)] private float jumpHeight = 1.25f;
        [SerializeField, Range(0f, 0.3f)] private float coyoteTime = 0.12f;
        [SerializeField, Range(0f, 0.3f)] private float jumpBufferTime = 0.15f;
        [SerializeField, Min(0.1f)] private float terminalFallSpeed = 45f;

        [Header("CS-surf (per map)")]
        [SerializeField] private bool surfEnabled;
        [SerializeField, Min(1f), Tooltip("Gewone doelsnelheid op de grond bij surf, in m/s.")] private float surfWishSpeed = 8f;
        [SerializeField, Min(1f)] private float surfAirAcceleration = 150f;
        [SerializeField, Min(0.01f), Tooltip("Grens in de gekozen luchtrichting: 30 Source-eenheden/s = 0,762 m/s.")] private float surfAirWishSpeedCap = 0.762f;
        [SerializeField, Min(0f)] private float surfGroundFriction = 6f;
        [SerializeField, Min(1f), Tooltip("Zwaartekracht in m/s².")] private float surfGravity = 20.32f;
        [SerializeField, Min(1f)] private float surfJumpSpeed = 6.4f;
        [SerializeField, Min(1f), Tooltip("Veilige bovengrens: 3500 Source-eenheden/s = 88,9 m/s.")] private float surfMaximumSpeed = 88.9f;
        [SerializeField, Range(0f, 60f)] private float surfWalkableAngle = 46f;

        [Header("Botsingsvorm")]
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

        [Header("Terugzetten")]
        [SerializeField] private float fallResetHeight = -20f;

        private readonly Collider[] standingObstructions = new Collider[24];
        private readonly RaycastHit[] cameraObstructions = new RaycastHit[24];
        private readonly RaycastHit[] surfaceHits = new RaycastHit[24];
        private readonly RaycastHit[] movementHits = new RaycastHit[24];
        private readonly Vector3[] contactPlanes = new Vector3[8];
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
        private bool surfGrounded;
        private bool surfContact;
        private bool collectingSurfContacts;
        private int contactPlaneCount;
        private float defaultSlopeLimit;
        private float defaultStepOffset;
        private bool surfMotorConfigured;

        public bool IsCrouching => crouching;
        public bool IsGrounded => character != null && character.enabled && (surfEnabled ? surfGrounded : character.isGrounded);
        public float CurrentSpeed { get; private set; }
        public bool SurfEnabled => surfEnabled;
        public bool IsSurfing => surfEnabled && surfContact && !surfGrounded;
        public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
        public float SourceUnitsSpeed => CurrentSpeed / 0.0254f;
        public bool HasLocalControl => localControlEnabled;
        public bool CursorCaptured => localControlEnabled && Cursor.lockState == CursorLockMode.Locked;
        public bool IsFirstPerson => firstPerson;
        /// <summary>Controleert of de muis op een menu staat. Pixels tellen vanaf linksonder.</summary>
        public System.Func<Vector2, bool> PointerBlocked;

        private void Awake()
        {
            CacheReferences();
            if (!spawnPointSet)
                SetSpawnPoint(transform.position, transform.rotation);
            yaw = transform.eulerAngles.y;
            if (surfEnabled)
                ConfigureSurf();
            ApplyControlState(false);
        }

        private void Start()
        {
            ApplyControlState(localControlEnabled);
        }

        /// <summary>Koppelt het model, het draaipunt en de camera aan de speler.</summary>
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

        /// <summary>Zet invoer, botsingen en camera aan voor de lokale speler.</summary>
        public void SetLocalControl(bool enabledForThisPlayer)
        {
            if (localControlEnabled && !enabledForThisPlayer)
                ReleaseCursor();
            localControlEnabled = enabledForThisPlayer;
            CacheReferences();
            ApplyControlState(enabledForThisPlayer && Application.isPlaying);
        }

        /// <summary>Slaat de plek op waar R of een val de speler terugzet.</summary>
        public void SetSpawnPoint(Vector3 position, Quaternion rotation)
        {
            spawnPosition = position;
            spawnRotation = rotation;
            spawnPointSet = true;
        }

        /// <summary>Zet surf aan of uit en past de camera en botsingsinstellingen aan.</summary>
        public void ConfigureSurf(bool enabled = true)
        {
            CacheReferences();
            surfEnabled = enabled;
            if (enabled)
            {
                if (!surfMotorConfigured)
                {
                    defaultSlopeLimit = character.slopeLimit;
                    defaultStepOffset = character.stepOffset;
                    surfMotorConfigured = true;
                }
                // Onze code herkent grond tot 46°. Unity mag beweging langs steile rampen niet blokkeren.
                character.slopeLimit = 89f;
                character.stepOffset = 0f;
                character.minMoveDistance = 0f;
                firstPerson = true;
                minimumPitch = -89f;
                maximumPitch = 89f;
                if (playerCamera != null)
                {
                    playerCamera.fieldOfView = 90f;
                    playerCamera.farClipPlane = Mathf.Max(1800f, playerCamera.farClipPlane);
                }
            }
            else if (surfMotorConfigured)
            {
                character.slopeLimit = defaultSlopeLimit;
                character.stepOffset = defaultStepOffset;
                surfMotorConfigured = false;
            }
            surfGrounded = false;
            surfContact = false;
            contactPlaneCount = 0;
            SetVisualVisibility();
        }

        /// <summary>Verplaatst de speler direct en wist de snelheid en oude botsingen.</summary>
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
            surfGrounded = false;
            surfContact = false;
            contactPlaneCount = 0;
            yaw = rotation.eulerAngles.y;
            pitch = 12f;
        }

        /// <summary>Werkt het hurken van andere spelers bij.</summary>
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
                    if (surfEnabled ? keyboard.spaceKey.isPressed : keyboard.spaceKey.wasPressedThisFrame)
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
                    if (surfEnabled ? gamepad.buttonSouth.isPressed : gamepad.buttonSouth.wasPressedThisFrame)
                        jumpRequested = true;
                    if (gamepad.rightShoulder.wasPressedThisFrame)
                        ToggleView();
                }
                UpdateLook(mouse, gamepad);
            }

            // Zonder muisbesturing blijft zwaartekracht werken. Surf behoudt de opgebouwde snelheid.
            if (!inputAllowed && !surfEnabled)
                horizontalVelocity = Vector3.zero;
            SimulateMovement(movement, sprint, crouchRequested, jumpRequested, Time.deltaTime);
        }

        /// <summary>
        /// Berekent beweging met tijd in seconden en snelheid in m/s.
        /// De richting volgt de camera. Roep dit eenmaal per beeld aan.
        /// Tests kunnen dit zonder toetsenbord gebruiken; surf heeft de botsingsmeldingen nodig.
        /// </summary>
        public void SimulateMovement(Vector2 movement, bool sprint, bool crouchRequested,
            bool jumpPressed, float deltaTime)
        {
            CacheReferences();
            if (!localControlEnabled || !character.enabled || deltaTime <= 0f)
                return;
            if (surfEnabled)
            {
                SimulateSurf(movement, sprint, crouchRequested, jumpPressed, deltaTime);
                return;
            }
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

        private void SimulateSurf(Vector2 movement, bool sprintRequested, bool crouchRequested,
            bool jumpHeld, float deltaTime)
        {
            movement = Vector2.ClampMagnitude(movement, 1f);
            crouching = crouchRequested || (crouching && !CanStand());
            // Kleine stappen voorkomen gemiste botsingen bij hoge snelheid.
            float remaining = Mathf.Min(deltaTime, 0.1f);
            UpdateCapsuleHeight(remaining);
            Vector3 velocity = Velocity;
            Vector3 wish = Quaternion.Euler(0f, yaw, 0f) * new Vector3(movement.x, 0f, movement.y);
            // Shift verhoogt de doelsnelheid. Bestaande snelheid wordt niet direct aangepast.
            float sprintFactor = sprintRequested ? Mathf.Max(1f, sprintSpeed / Mathf.Max(0.1f, walkSpeed)) : 1f;
            float wishSpeed = surfWishSpeed * movement.magnitude * (crouching ? 0.34f : sprintFactor);
            Vector3 wishDirection = wish.sqrMagnitude > 0f ? wish.normalized : Vector3.zero;
            float groundDot = Mathf.Cos(surfWalkableAngle * Mathf.Deg2Rad);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            surfContact = false;
            int iterations = 0;
            while (remaining > 0.00001f && iterations++ < 64)
            {
                float step = Mathf.Min(remaining, Mathf.Min(1f / 128f, 0.20f / Mathf.Max(1f, velocity.magnitude)));
                remaining -= step;
                movementTime += step;
                bool hasSurface = ProbeSurfSurface(out Vector3 surfaceNormal);
                surfGrounded = hasSurface && surfaceNormal.y >= groundDot && velocity.y <= 0.1f;
                bool onRamp = hasSurface && surfaceNormal.y > 0.01f && surfaceNormal.y < groundDot
                    && Vector3.Dot(velocity, surfaceNormal) <= 0.25f;
                surfContact |= onRamp;
                if (surfGrounded)
                {
                    velocity.y = 0f;
                    if (jumpHeld)
                    {
                        velocity.y = surfJumpSpeed;
                        surfGrounded = false;
                    }
                    else
                    {
                        float speed = new Vector2(velocity.x, velocity.z).magnitude;
                        float reduced = Mathf.Max(0f, speed - Mathf.Max(2.54f, speed) * surfGroundFriction * step);
                        if (speed > 0f)
                        {
                            velocity.x *= reduced / speed;
                            velocity.z *= reduced / speed;
                        }
                        Accelerate(ref velocity, wishDirection, wishSpeed, wishSpeed, 10f, step);
                    }
                }
                if (!surfGrounded)
                {
                    // Alleen de snelheid in de gekozen luchtrichting is begrensd op 30 Source-eenheden/s.
                    // Daardoor blijft de totale opgebouwde snelheid behouden bij het bijsturen.
                    Accelerate(ref velocity, wishDirection, Mathf.Min(wishSpeed, surfAirWishSpeedCap),
                        wishSpeed, surfAirAcceleration, step);
                    velocity.y -= surfGravity * step * 0.5f;
                    if (onRamp)
                        ClipIntoPlane(ref velocity, surfaceNormal);
                }
                velocity = Vector3.ClampMagnitude(velocity, surfMaximumSpeed);
                contactPlaneCount = 0;
                collectingSurfContacts = true;
                Vector3 moveStart = transform.position;
                Vector3 displacement = velocity * step + (surfGrounded ? Vector3.down * (0.5f * step) : Vector3.zero);
                CollisionFlags collisions = character.Move(displacement);
                collectingSurfContacts = false;
                if (contactPlaneCount == 0 && (collisions & (CollisionFlags.Sides | CollisionFlags.Above)) != 0)
                    RecoverSurfContacts(moveStart, displacement);
                if (!surfGrounded)
                    velocity.y -= surfGravity * step * 0.5f;
                // Verwijder alleen snelheid het oppervlak in; glijden langs het oppervlak blijft mogelijk.
                for (int pass = 0; pass < 2; pass++)
                    for (int i = 0; i < contactPlaneCount; i++)
                        ClipIntoPlane(ref velocity, contactPlanes[i]);
                velocity = Vector3.ClampMagnitude(velocity, surfMaximumSpeed);
            }
            horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
            verticalVelocity = velocity.y;
            CurrentSpeed = horizontalVelocity.magnitude;
            // De surfmap regelt zelf het checkpoint en de valgrens.
        }

        /// <summary>Voegt alleen snelheid toe zolang de gekozen richting nog onder haar grens zit.</summary>
        private static void Accelerate(ref Vector3 velocity, Vector3 direction, float projectedSpeedCap,
            float inputSpeed, float rate, float deltaTime)
        {
            float addSpeed = projectedSpeedCap - Vector3.Dot(velocity, direction);
            if (addSpeed > 0f)
                velocity += direction * Mathf.Min(addSpeed, rate * inputSpeed * deltaTime);
        }

        private static void ClipIntoPlane(ref Vector3 velocity, Vector3 normal)
        {
            float intoSurface = Vector3.Dot(velocity, normal);
            if (intoSurface < 0f)
                velocity -= normal * intoSurface;
        }

        private bool ProbeSurfSurface(out Vector3 normal)
        {
            float radius = Mathf.Max(0.05f, character.radius - character.skinWidth);
            Vector3 origin = transform.position + Vector3.up * (character.radius + 0.12f);
            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, surfaceHits,
                character.skinWidth * 2f + 0.20f, obstructionMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity;
            normal = Vector3.up;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = surfaceHits[i];
                if (hit.collider == null || hit.transform.IsChildOf(transform) || hit.normal.y <= 0.01f)
                    continue;
                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    normal = hit.normal;
                }
            }
            return nearest < float.PositiveInfinity;
        }

        private void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (!surfEnabled || !collectingSurfContacts || hit.collider.isTrigger)
                return;
            AddSurfContact(hit.normal);
        }

        private void AddSurfContact(Vector3 normal)
        {
            if (contactPlaneCount < contactPlanes.Length)
                contactPlanes[contactPlaneCount++] = normal;
            float normalY = normal.y;
            if (normalY > 0.01f && normalY < Mathf.Cos(surfWalkableAngle * Mathf.Deg2Rad))
                surfContact = true;
        }

        private void RecoverSurfContacts(Vector3 moveStart, Vector3 displacement)
        {
            // Soms meldt Unity een botsing zonder de raakrichting door te geven.
            // Zoek die richting dan met een iets kleinere botsingsvorm.
            float distance = displacement.magnitude;
            if (distance < 0.00001f)
                return;
            float radius = Mathf.Max(0.05f, character.radius - character.skinWidth * 2f);
            Vector3 bottom = moveStart + Vector3.up * character.radius;
            Vector3 top = moveStart + Vector3.up * (character.height - character.radius);
            int count = Physics.CapsuleCastNonAlloc(bottom, top, radius, displacement / distance,
                movementHits, distance + character.skinWidth * 2f + 0.005f, obstructionMask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = movementHits[i];
                if (hit.collider != null && !hit.transform.IsChildOf(transform)
                    && Vector3.Dot(displacement, hit.normal) < 0f)
                    AddSurfContact(hit.normal);
            }
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

        /// <summary>Controleert of er boven de speler genoeg vrije ruimte is om recht te staan.</summary>
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
