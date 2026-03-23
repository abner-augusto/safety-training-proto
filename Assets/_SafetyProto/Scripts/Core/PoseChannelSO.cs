using UnityEngine;

namespace SafetyProto.Core
{
    [CreateAssetMenu(fileName = "PoseChannel", menuName = "VRSafetyTraining/PoseChannel", order = 1)]
    public class PoseChannelSO : ScriptableObject
    {
        [Header("Head")]
        public Vector3 HeadPosition;
        public Quaternion HeadRotation = Quaternion.identity;

        [Header("Left Hand")]
        public Vector3 LeftHandPosition;
        public Quaternion LeftHandRotation = Quaternion.identity;

        [Header("Right Hand")]
        public Vector3 RightHandPosition;
        public Quaternion RightHandRotation = Quaternion.identity;

        [Header("PPE")]
        public PpeObjectPose[] PpeObjects;
        public string[] PpeIds;
    }

    [System.Serializable]
    public struct PpeObjectPose
    {
        public string Id;
        public Vector3 Position;
        public Quaternion Rotation;
        public string AttachedTo;
    }
}
