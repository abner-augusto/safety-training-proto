using UnityEngine;

namespace SafetyProto.Runtime.PPE
{
    /// <summary>One-way contact queries for a visual strap, including its full segment thickness.</summary>
    internal sealed class ChinstrapCollision
    {
        private readonly Transform _owner;
        private readonly CapsuleCollider _probe;
        private readonly Collider[] _overlaps = new Collider[64];
        private readonly RaycastHit[] _hits = new RaycastHit[64];
        private readonly int _mask;
        private readonly float _radius;

        public Vector3 ContactNormal { get; private set; }

        public ChinstrapCollision(Transform owner, CapsuleCollider probe, int mask, float radius)
        {
            _owner = owner;
            _probe = probe;
            _mask = mask;
            _radius = radius;
        }

        private bool Accept(Collider collider) => collider != null &&
            !collider.transform.IsChildOf(_owner) && collider != _probe;

        public Vector3 Sweep(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.00001f) return to;
            int count = Physics.SphereCastNonAlloc(from, _radius, delta / distance, _hits,
                distance, _mask, QueryTriggerInteraction.Ignore);
            float nearest = distance;
            for (int i = 0; i < count; i++)
                if (Accept(_hits[i].collider)) nearest = Mathf.Min(nearest, _hits[i].distance);
            return nearest < distance
                ? from + delta / distance * Mathf.Max(0f, nearest - 0.0002f)
                : to;
        }

        public bool Resolve(ref Vector3 a, ref Vector3 b, bool pinA)
        {
            bool contact = false;
            ContactNormal = Vector3.zero;
            int count = Physics.OverlapCapsuleNonAlloc(a, b, _radius, _overlaps, _mask,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider other = _overlaps[i];
                if (!Accept(other)) continue;
                Vector3 delta = b - a;
                _probe.radius = _radius;
                _probe.height = delta.magnitude + 2f * _radius;
                Quaternion rotation = delta.sqrMagnitude > 0.0000001f
                    ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity;
                if (!Physics.ComputePenetration(_probe, (a + b) * 0.5f, rotation,
                        other, other.transform.position, other.transform.rotation,
                        out Vector3 direction, out float depth)) continue;
                Vector3 correction = direction * (depth + 0.0002f);
                if (!pinA) a += correction;
                b += correction * (pinA ? 2f : 1f);
                ContactNormal += direction;
                contact = true;
            }
            return contact;
        }
    }
}
