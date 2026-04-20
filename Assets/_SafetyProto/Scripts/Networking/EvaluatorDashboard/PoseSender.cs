using System;
using System.Collections;
using System.Text;
using SafetyProto.Core;
using UnityEngine;

namespace SafetyProto.Networking.Dashboard
{
    internal class PoseSender
    {
        private readonly PoseChannelSO _channel;
        private readonly EvaluatorWebSocketServer _server;
        private readonly WaitForSeconds _wait;
        private readonly int _decimalPrecision;
        private readonly int _scaleFactor;
        private readonly StringBuilder _jsonBuilder = new StringBuilder(1024);
        private byte[] _utf8Buffer = new byte[2048];

        public PoseSender(PoseChannelSO channel, EvaluatorWebSocketServer server, float sendRateHz, int decimalPrecision)
        {
            _channel = channel;
            _server = server;
            _decimalPrecision = decimalPrecision < 0 ? 0 : decimalPrecision;

            var interval = sendRateHz > 0f ? 1f / sendRateHz : 0.1f;
            _wait = new WaitForSeconds(interval);

            _scaleFactor = 1;
            for (int i = 0; i < _decimalPrecision; i++)
                _scaleFactor *= 10;
        }

        public IEnumerator SendLoop()
        {
            while (true)
            {
                if (_server.HasConnections && _channel != null)
                {
                    SendPoseFrame();
                }

                yield return _wait;
            }
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

            AppendPoseSampleJson(sb, "hmd", _channel.HeadPosition, _channel.HeadRotation);
            sb.Append(',');
            AppendPoseSampleJson(sb, "leftHand", _channel.LeftHandPosition, _channel.LeftHandRotation);
            sb.Append(',');
            AppendPoseSampleJson(sb, "rightHand", _channel.RightHandPosition, _channel.RightHandRotation);
            sb.Append(',');

            sb.Append("\"ppe\":[");
            AppendPpeJson(sb, _channel.PpeObjects);
            sb.Append(']');

            sb.Append("}}");
        }

        private void AppendPoseSampleJson(StringBuilder sb, string key, Vector3 position, Quaternion rotation)
        {
            sb.Append('\"').Append(key).Append("\":{");

            sb.Append("\"px\":"); AppendFloat(sb, position.x); sb.Append(',');
            sb.Append("\"py\":"); AppendFloat(sb, position.y); sb.Append(',');
            sb.Append("\"pz\":"); AppendFloat(sb, position.z); sb.Append(',');
            sb.Append("\"qx\":"); AppendFloat(sb, rotation.x); sb.Append(',');
            sb.Append("\"qy\":"); AppendFloat(sb, rotation.y); sb.Append(',');
            sb.Append("\"qz\":"); AppendFloat(sb, rotation.z); sb.Append(',');
            sb.Append("\"qw\":"); AppendFloat(sb, rotation.w);
            sb.Append('}');
        }

        private void AppendPpeJson(StringBuilder sb, PpeObjectPose[] ppeObjects)
        {
            if (ppeObjects == null || ppeObjects.Length == 0)
                return;

            for (int i = 0; i < ppeObjects.Length; i++)
            {
                if (i > 0)
                    sb.Append(',');

                var ppe = ppeObjects[i];

                sb.Append("{\"id\":\"");
                AppendJsonString(sb, ppe.Id ?? string.Empty);
                sb.Append("\",\"pose\":{");

                sb.Append("\"px\":"); AppendFloat(sb, ppe.Position.x); sb.Append(',');
                sb.Append("\"py\":"); AppendFloat(sb, ppe.Position.y); sb.Append(',');
                sb.Append("\"pz\":"); AppendFloat(sb, ppe.Position.z); sb.Append(',');
                sb.Append("\"qx\":"); AppendFloat(sb, ppe.Rotation.x); sb.Append(',');
                sb.Append("\"qy\":"); AppendFloat(sb, ppe.Rotation.y); sb.Append(',');
                sb.Append("\"qz\":"); AppendFloat(sb, ppe.Rotation.z); sb.Append(',');
                sb.Append("\"qw\":"); AppendFloat(sb, ppe.Rotation.w);

                sb.Append("},\"attachedTo\":\"");
                AppendJsonString(sb, ppe.AttachedTo ?? string.Empty);
                sb.Append("\"}");
            }
        }

        private void AppendFloat(StringBuilder sb, float value)
        {
            long scaled = (long)Mathf.Round(value * _scaleFactor);
            bool negative = scaled < 0;
            if (negative) scaled = -scaled;

            long integer = _scaleFactor > 1 ? scaled / _scaleFactor : scaled;
            long frac = _scaleFactor > 1 ? scaled % _scaleFactor : 0;

            if (negative) sb.Append('-');
            sb.Append(integer);

            if (_decimalPrecision <= 0)
                return;

            sb.Append('.');
            long divisor = _scaleFactor / 10;
            for (int i = 0; i < _decimalPrecision; i++)
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
