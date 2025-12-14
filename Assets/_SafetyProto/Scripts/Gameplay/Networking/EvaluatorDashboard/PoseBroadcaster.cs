using System;
using System.Collections;
using UnityEngine;

namespace SafetyProto.Gameplay.Networking.EvaluatorDashboard
{
    public class PoseBroadcaster : MonoBehaviour
    {
        [Header("Tracked Transforms")]
        public Transform hmd;
        public Transform leftHand;
        public Transform rightHand;
        public Transform[] ppeObjects;
        public string[] ppeIds;

        [Header("Settings")]
        [Tooltip("Pose send rate in Hz.")]
        public float sendRateHz = 10f;
        [Tooltip("If true, positions are world space; otherwise local space.")]
        public bool useWorldSpace = true;
        [Tooltip("Round coordinates to this decimal precision.")]
        public int decimalPrecision = 3;

        private EvaluatorWebSocketServer _server;
        private WaitForSeconds _wait;
        private string[] _cachedAttachments;

        public void Initialize(EvaluatorWebSocketServer server)
        {
            _server = server;
            RefreshWait();
            CachePpeAttachments();
        }

        private void OnEnable()
        {
            RefreshWait();
            CachePpeAttachments();
            StartCoroutine(SendLoop());
        }

        private void OnDisable()
        {
            StopAllCoroutines();
        }

        private IEnumerator SendLoop()
        {
            if (_wait == null)
            {
                RefreshWait();
            }

            while (enabled)
            {
                if (_server != null && _server.HasConnections)
                {
                    var json = BuildPoseJson();
                    if (!string.IsNullOrEmpty(json))
                    {
                        _server.BroadcastJson(json);
                    }
                }

                yield return _wait;
            }
        }

        private void RefreshWait()
        {
            var interval = sendRateHz > 0f ? 1f / sendRateHz : 0.1f;
            _wait = new WaitForSeconds(interval);
        }

        private string BuildPoseJson()
        {
            var envelope = new PoseEnvelope
            {
                eventType = "PoseFrame",
                payload = new PoseFrame
                {
                    timestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    hmd = BuildPoseSample(hmd),
                    leftHand = BuildPoseSample(leftHand),
                    rightHand = BuildPoseSample(rightHand),
                    ppe = BuildPpeSamples()
                }
            };

            return JsonUtility.ToJson(envelope);
        }

        private PoseSample BuildPoseSample(Transform t)
        {
            if (t == null)
            {
                return PoseSample.Identity;
            }

            var position = useWorldSpace ? t.position : t.localPosition;
            var rotation = useWorldSpace ? t.rotation : t.localRotation;

            return new PoseSample
            {
                px = Round(position.x),
                py = Round(position.y),
                pz = Round(position.z),
                qx = Round(rotation.x),
                qy = Round(rotation.y),
                qz = Round(rotation.z),
                qw = Round(rotation.w)
            };
        }

        private PpePose[] BuildPpeSamples()
        {
            if (ppeObjects == null || ppeObjects.Length == 0 || ppeIds == null || ppeIds.Length != ppeObjects.Length)
                return Array.Empty<PpePose>();

            var attachments = _cachedAttachments ?? Array.Empty<string>();
            var result = new PpePose[ppeObjects.Length];
            for (int i = 0; i < ppeObjects.Length; i++)
            {
                var id = string.IsNullOrEmpty(ppeIds[i]) ? $"ppe-{i}" : ppeIds[i];
                var pose = BuildPoseSample(ppeObjects[i]);
                var attachedTo = attachments.Length > i ? attachments[i] : string.Empty;
                result[i] = new PpePose { id = id, pose = pose, attachedTo = attachedTo };
            }
            return result;
        }

        private float Round(float value)
        {
            if (decimalPrecision < 0)
                return value;
            float factor = Mathf.Pow(10f, decimalPrecision);
            return Mathf.Round(value * factor) / factor;
        }

        private void CachePpeAttachments()
        {
            if (ppeObjects == null || ppeIds == null || ppeObjects.Length != ppeIds.Length)
            {
                _cachedAttachments = Array.Empty<string>();
                return;
            }

            _cachedAttachments = new string[ppeObjects.Length];
            for (int i = 0; i < ppeObjects.Length; i++)
            {
                var obj = ppeObjects[i];
                if (obj == null)
                {
                    _cachedAttachments[i] = string.Empty;
                    continue;
                }

                if (hmd != null && obj.IsChildOf(hmd)) _cachedAttachments[i] = "hmd";
                else if (leftHand != null && obj.IsChildOf(leftHand)) _cachedAttachments[i] = "leftHand";
                else if (rightHand != null && obj.IsChildOf(rightHand)) _cachedAttachments[i] = "rightHand";
                else _cachedAttachments[i] = string.Empty;
            }
        }

        [Serializable]
        private struct PoseEnvelope
        {
            public string eventType;
            public PoseFrame payload;
        }

        [Serializable]
        public struct PoseFrame
        {
            public long timestampMs;
            public PoseSample hmd;
            public PoseSample leftHand;
            public PoseSample rightHand;
            public PpePose[] ppe;
        }

        [Serializable]
        public struct PoseSample
        {
            public float px, py, pz;
            public float qx, qy, qz, qw;

            public static PoseSample Identity => new PoseSample
            {
                px = 0f, py = 0f, pz = 0f,
                qx = 0f, qy = 0f, qz = 0f, qw = 1f
            };
        }

        [Serializable]
        public struct PpePose
        {
            public string id;
            public PoseSample pose;
            public string attachedTo;
        }
    }
}
