using System;
using System.Collections;
using System.Text;
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
        private string[] _cachedPpeIds;
        private readonly StringBuilder _jsonBuilder = new StringBuilder(1024);
        private byte[] _utf8Buffer = new byte[2048];
        private int _scaleFactor = 1000;

        public void Initialize(EvaluatorWebSocketServer server)
        {
            _server = server;
            RefreshWait();
            RefreshNumericFormatting();
            CachePpeMetadata();
        }

        private void OnEnable()
        {
            RefreshWait();
            RefreshNumericFormatting();
            CachePpeMetadata();
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
                    SendPoseFrame();
                }

                yield return _wait;
            }
        }

        private void RefreshWait()
        {
            var interval = sendRateHz > 0f ? 1f / sendRateHz : 0.1f;
            _wait = new WaitForSeconds(interval);
        }

        private void SendPoseFrame()
        {
            _jsonBuilder.Clear();
            AppendPoseFrameJson(_jsonBuilder);

            var json = _jsonBuilder.ToString();
            EnsureUtf8Capacity(json);
            var byteCount = Encoding.UTF8.GetBytes(json, 0, json.Length, _utf8Buffer, 0);
            _server.BroadcastUtf8(_utf8Buffer, byteCount);
        }

        private void AppendPoseFrameJson(StringBuilder sb)
        {
            sb.Append("{\"eventType\":\"PoseFrame\",\"payload\":{");
            sb.Append("\"timestampMs\":").Append(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()).Append(',');

            AppendPoseSampleJson(sb, "hmd", hmd);
            sb.Append(',');
            AppendPoseSampleJson(sb, "leftHand", leftHand);
            sb.Append(',');
            AppendPoseSampleJson(sb, "rightHand", rightHand);
            sb.Append(',');

            sb.Append("\"ppe\":[");
            AppendPpeJson(sb);
            sb.Append(']');

            sb.Append("}}");
        }

        private void AppendPoseSampleJson(StringBuilder sb, string key, Transform t)
        {
            sb.Append('\"').Append(key).Append("\":{");
            if (t == null)
            {
                sb.Append("\"px\":0,\"py\":0,\"pz\":0,");
                sb.Append("\"qx\":0,\"qy\":0,\"qz\":0,\"qw\":1}");
                return;
            }

            var position = useWorldSpace ? t.position : t.localPosition;
            var rotation = useWorldSpace ? t.rotation : t.localRotation;

            sb.Append("\"px\":"); AppendFloat(sb, position.x); sb.Append(',');
            sb.Append("\"py\":"); AppendFloat(sb, position.y); sb.Append(',');
            sb.Append("\"pz\":"); AppendFloat(sb, position.z); sb.Append(',');
            sb.Append("\"qx\":"); AppendFloat(sb, rotation.x); sb.Append(',');
            sb.Append("\"qy\":"); AppendFloat(sb, rotation.y); sb.Append(',');
            sb.Append("\"qz\":"); AppendFloat(sb, rotation.z); sb.Append(',');
            sb.Append("\"qw\":"); AppendFloat(sb, rotation.w);
            sb.Append('}');
        }

        private void AppendPpeJson(StringBuilder sb)
        {
            if (ppeObjects == null || ppeObjects.Length == 0 || _cachedPpeIds == null || _cachedPpeIds.Length != ppeObjects.Length)
                return;

            var attachments = _cachedAttachments;
            for (int i = 0; i < ppeObjects.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');

                sb.Append("{\"id\":\"");
                AppendJsonString(sb, _cachedPpeIds[i]);
                sb.Append("\",\"pose\":{");

                var t = ppeObjects[i];
                if (t == null)
                {
                    sb.Append("\"px\":0,\"py\":0,\"pz\":0,");
                    sb.Append("\"qx\":0,\"qy\":0,\"qz\":0,\"qw\":1");
                }
                else
                {
                    var position = useWorldSpace ? t.position : t.localPosition;
                    var rotation = useWorldSpace ? t.rotation : t.localRotation;

                    sb.Append("\"px\":"); AppendFloat(sb, position.x); sb.Append(',');
                    sb.Append("\"py\":"); AppendFloat(sb, position.y); sb.Append(',');
                    sb.Append("\"pz\":"); AppendFloat(sb, position.z); sb.Append(',');
                    sb.Append("\"qx\":"); AppendFloat(sb, rotation.x); sb.Append(',');
                    sb.Append("\"qy\":"); AppendFloat(sb, rotation.y); sb.Append(',');
                    sb.Append("\"qz\":"); AppendFloat(sb, rotation.z); sb.Append(',');
                    sb.Append("\"qw\":"); AppendFloat(sb, rotation.w);
                }

                sb.Append("},\"attachedTo\":\"");
                AppendJsonString(sb, (attachments != null && attachments.Length > i) ? attachments[i] : string.Empty);
                sb.Append("\"}");
            }
        }

        private void RefreshNumericFormatting()
        {
            var precision = decimalPrecision < 0 ? 0 : decimalPrecision;
            _scaleFactor = 1;
            for (int i = 0; i < precision; i++)
                _scaleFactor *= 10;
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

                if (hmd != null && obj.IsChildOf(hmd)) _cachedAttachments[i] = "hmd";
                else if (leftHand != null && obj.IsChildOf(leftHand)) _cachedAttachments[i] = "leftHand";
                else if (rightHand != null && obj.IsChildOf(rightHand)) _cachedAttachments[i] = "rightHand";
                else _cachedAttachments[i] = string.Empty;
            }
        }

        private void AppendFloat(StringBuilder sb, float value)
        {
            int precision = decimalPrecision < 0 ? 0 : decimalPrecision;

            long scaled = (long)Mathf.Round(value * _scaleFactor);
            bool negative = scaled < 0;
            if (negative) scaled = -scaled;

            long integer = _scaleFactor > 1 ? scaled / _scaleFactor : scaled;
            long frac = _scaleFactor > 1 ? scaled % _scaleFactor : 0;

            if (negative) sb.Append('-');
            sb.Append(integer);

            if (precision <= 0)
                return;

            sb.Append('.');
            long divisor = _scaleFactor / 10;
            for (int i = 0; i < precision; i++)
            {
                int digit = divisor > 0 ? (int)(frac / divisor) : (int)frac;
                sb.Append((char)('0' + digit));
                if (divisor > 0)
                {
                    frac %= divisor;
                    divisor /= 10;
                }
            }
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '\"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
        }

        private void EnsureUtf8Capacity(string json)
        {
            var required = Encoding.UTF8.GetByteCount(json);
            if (_utf8Buffer.Length < required)
            {
                _utf8Buffer = new byte[required];
            }
        }
    }
}
