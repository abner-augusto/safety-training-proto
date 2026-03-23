using System;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Core
{
    public class PoseReporter : MonoBehaviour
    {
        [Header("Tracked Transforms")]
        [SerializeField] private Transform headTransform;
        [SerializeField] private Transform leftHandTransform;
        [SerializeField] private Transform rightHandTransform;

        [Header("PPE Tracking")]
        [SerializeField] private Transform[] ppeObjects;
        [SerializeField] private string[] ppeIds;

        [Header("Settings")]
        [Tooltip("If true, positions are world space; otherwise local space.")]
        [SerializeField] private bool useWorldSpace = true;

        [Header("Channel")]
        [SerializeField] private PoseChannelSO poseChannel;

        private string[] _cachedPpeIds;
        private string[] _cachedAttachments;

        private void Start()
        {
            if (poseChannel == null)
            {
                SafetyLog.Warning("[PoseReporter] PoseChannelSO not assigned.", this);
                enabled = false;
                return;
            }
            CachePpeMetadata();
        }

        private void LateUpdate()
        {
            if (poseChannel == null)
                return;

            if (headTransform != null)
            {
                poseChannel.HeadPosition = useWorldSpace ? headTransform.position : headTransform.localPosition;
                poseChannel.HeadRotation = useWorldSpace ? headTransform.rotation : headTransform.localRotation;
            }

            if (leftHandTransform != null)
            {
                poseChannel.LeftHandPosition = useWorldSpace ? leftHandTransform.position : leftHandTransform.localPosition;
                poseChannel.LeftHandRotation = useWorldSpace ? leftHandTransform.rotation : leftHandTransform.localRotation;
            }

            if (rightHandTransform != null)
            {
                poseChannel.RightHandPosition = useWorldSpace ? rightHandTransform.position : rightHandTransform.localPosition;
                poseChannel.RightHandRotation = useWorldSpace ? rightHandTransform.rotation : rightHandTransform.localRotation;
            }

            WritePpeData();
        }

        private void WritePpeData()
        {
            if (ppeObjects == null || ppeObjects.Length == 0)
            {
                poseChannel.PpeObjects = null;
                return;
            }

            var poses = poseChannel.PpeObjects;
            if (poses == null || poses.Length != ppeObjects.Length)
            {
                poses = new PpeObjectPose[ppeObjects.Length];
            }

            for (int i = 0; i < ppeObjects.Length; i++)
            {
                var t = ppeObjects[i];
                var id = (_cachedPpeIds != null && _cachedPpeIds.Length > i) ? _cachedPpeIds[i] : $"ppe-{i}";
                var attachment = (_cachedAttachments != null && _cachedAttachments.Length > i) ? _cachedAttachments[i] : string.Empty;

                if (t != null)
                {
                    poses[i] = new PpeObjectPose
                    {
                        Id = id,
                        Position = useWorldSpace ? t.position : t.localPosition,
                        Rotation = useWorldSpace ? t.rotation : t.localRotation,
                        AttachedTo = attachment
                    };
                }
                else
                {
                    poses[i] = new PpeObjectPose
                    {
                        Id = id,
                        Rotation = Quaternion.identity,
                        AttachedTo = attachment
                    };
                }
            }

            poseChannel.PpeObjects = poses;
        }

        private void CachePpeMetadata()
        {
            if (ppeObjects == null || ppeObjects.Length == 0)
            {
                _cachedAttachments = Array.Empty<string>();
                _cachedPpeIds = Array.Empty<string>();
                return;
            }

            _cachedAttachments = new string[ppeObjects.Length];
            _cachedPpeIds = new string[ppeObjects.Length];

            bool hasIds = ppeIds != null && ppeIds.Length == ppeObjects.Length;
            for (int i = 0; i < ppeObjects.Length; i++)
            {
                var obj = ppeObjects[i];
                var rawId = hasIds ? ppeIds[i] : null;
                _cachedPpeIds[i] = string.IsNullOrEmpty(rawId) ? $"ppe-{i}" : rawId;

                if (obj == null)
                {
                    _cachedAttachments[i] = string.Empty;
                    continue;
                }

                if (headTransform != null && obj.IsChildOf(headTransform)) _cachedAttachments[i] = "hmd";
                else if (leftHandTransform != null && obj.IsChildOf(leftHandTransform)) _cachedAttachments[i] = "leftHand";
                else if (rightHandTransform != null && obj.IsChildOf(rightHandTransform)) _cachedAttachments[i] = "rightHand";
                else _cachedAttachments[i] = string.Empty;
            }
        }
    }
}
