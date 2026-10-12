#if UNITY_EDITOR
#nullable enable
using System.Linq;
using Oculus.Interaction.Locomotion;
using UnityEngine;

namespace SafetyProto.Runtime.Simulation
{
    public enum SimulatedPosture
    {
        Standing,
        Crouched
    }

    /// <summary>
    /// Parks the headset-less Editor rig at a named waypoint with a chosen eye height. Without a
    /// headset the camera sits at the rig's feet and the Meta character controller falls through
    /// the 3 cm scaffold deck, so moving also switches locomotion off for the rest of Play Mode.
    /// </summary>
    public static class SimulatedPlayerMover
    {
        public const float StandingEyeHeight = 1.65f;
        /// <summary>Below the dashboard's 1.15 m crouch threshold.</summary>
        public const float CrouchedEyeHeight = 1.05f;

        public static bool TryParsePosture(string? value, out SimulatedPosture posture)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "standing":
                case "em_pe":
                case "em pé":
                    posture = SimulatedPosture.Standing;
                    return true;
                case "crouched":
                case "agachado":
                    posture = SimulatedPosture.Crouched;
                    return true;
                default:
                    posture = SimulatedPosture.Standing;
                    return false;
            }
        }

        public static ISimulationWaypoints? FindWaypoints() =>
            Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).OfType<ISimulationWaypoints>().FirstOrDefault();

        /// <summary>Moves the player to <paramref name="target"/>. On failure, <paramref name="error"/>
        /// is a Portuguese message for the simulator window.</summary>
        public static bool TryMove(string? target, SimulatedPosture posture, out string error)
        {
            error = string.Empty;
            var waypoints = FindWaypoints();
            if (waypoints == null)
            {
                error = "Nenhum DashboardSceneLayout na cena: não há destinos para mover o jogador.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(target) || !waypoints.TryGetStandPoint(target!.Trim(), out var stand, out var lookAt))
            {
                error = $"Destino '{target}' desconhecido ou sem piso ao alcance. Use: {string.Join(", ", waypoints.WaypointIds)}.";
                return false;
            }

            var camera = Camera.main;
            if (camera == null)
            {
                error = "Câmera principal não encontrada: o rig do jogador não pode ser movido.";
                return false;
            }

            DisableLocomotion();
            PlaceRig(camera.transform.root, camera.transform, stand, lookAt, posture);
            return true;
        }

        /// <summary>Turns <paramref name="rig"/> about the head so it faces <paramref name="lookAt"/>
        /// (heading kept when it equals the stand point), then shifts it so the head sits at the
        /// posture's eye height above <paramref name="standPoint"/>.</summary>
        public static void PlaceRig(Transform rig, Transform head, Vector3 standPoint, Vector3 lookAt, SimulatedPosture posture)
        {
            var desired = Vector3.ProjectOnPlane(lookAt - standPoint, Vector3.up);
            var current = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (desired.sqrMagnitude > 1e-6f && current.sqrMagnitude > 1e-6f)
                rig.RotateAround(head.position, Vector3.up, Vector3.SignedAngle(current, desired, Vector3.up));

            float eyeHeight = posture == SimulatedPosture.Crouched ? CrouchedEyeHeight : StandingEyeHeight;
            rig.position += standPoint + Vector3.up * eyeHeight - head.position;
        }

        private static void DisableLocomotion()
        {
            foreach (var locomotor in Object.FindObjectsByType<FirstPersonLocomotor>(FindObjectsSortMode.None))
                locomotor.enabled = false;
            foreach (var controller in Object.FindObjectsByType<Oculus.Interaction.Locomotion.CharacterController>(FindObjectsSortMode.None))
                controller.enabled = false;
        }
    }
}
#endif
