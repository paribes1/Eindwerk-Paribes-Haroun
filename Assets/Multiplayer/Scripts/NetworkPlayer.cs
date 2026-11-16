using Eindwerk.PlayerMechanics;
using Unity.Netcode;
using UnityEngine;

namespace Eindwerk.Multiplayer
{
    [RequireComponent(typeof(NetworkObject), typeof(CapsulePlayerController))]
    public sealed class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private Transform capsuleVisual;

        private readonly NetworkVariable<bool> crouching = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        private CapsulePlayerController controller;

        public void Configure(Transform visual) => capsuleVisual = visual;

        public override void OnNetworkSpawn()
        {
            controller = GetComponent<CapsulePlayerController>();
            if (FindAnyObjectByType<KitsuneSurfCourse>() != null)
                controller.ConfigureSurf();
            controller.SetSpawnPoint(transform.position, transform.rotation);
            if (IsOwner)
            {
                MultiplayerSession session = FindAnyObjectByType<MultiplayerSession>();
                if (session != null)
                    controller.PointerBlocked = session.IsPointerOverMenu;
            }
            controller.SetLocalControl(IsOwner);
            crouching.OnValueChanged += OnCrouchChanged;
            if (!IsOwner)
                controller.SetReplicatedCrouch(crouching.Value);
            ApplyPlayerColor();
        }

        public override void OnNetworkDespawn()
        {
            crouching.OnValueChanged -= OnCrouchChanged;
            if (controller != null)
            {
                controller.SetLocalControl(false);
                controller.PointerBlocked = null;
            }
        }

        public override void OnGainedOwnership()
        {
            if (IsSpawned && controller != null)
                controller.SetLocalControl(true);
        }

        public override void OnLostOwnership()
        {
            if (controller != null)
                controller.SetLocalControl(false);
        }

        private void LateUpdate()
        {
            if (IsSpawned && IsOwner && controller != null && crouching.Value != controller.IsCrouching)
                crouching.Value = controller.IsCrouching;
        }

        private void OnCrouchChanged(bool previous, bool current)
        {
            if (!IsOwner && controller != null)
                controller.SetReplicatedCrouch(current);
        }

        private void ApplyPlayerColor()
        {
            Renderer visualRenderer = capsuleVisual != null
                ? capsuleVisual.GetComponent<Renderer>()
                : GetComponentInChildren<Renderer>();
            if (visualRenderer == null)
                return;

            Color color = Color.HSVToRGB((OwnerClientId * 0.173f + 0.52f) % 1f, 0.62f, 0.94f);
            var properties = new MaterialPropertyBlock();
            visualRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            visualRenderer.SetPropertyBlock(properties);
        }
    }
}
