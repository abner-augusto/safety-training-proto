using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// Builds the fallback 3D AudioSource for components whose authored source is missing.
    /// The distances differ per component, so they are parameters, not shared constants.
    /// </summary>
    public static class AudioSourceDefaults
    {
        /// <summary>
        /// Returns <paramref name="current"/> if set, else an AudioSource already on the host,
        /// else a new spatialized one-shot source with the given distance range.
        /// </summary>
        public static AudioSource EnsureSpatial(
            Component host, AudioSource current, float minDistance, float maxDistance)
        {
            if (current != null) return current;

            var source = host.GetComponent<AudioSource>();
            if (source != null) return source;

            source = host.gameObject.AddComponent<AudioSource>();
            source.spatialBlend = 1f;
            source.spatialize = true;
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            return source;
        }
    }
}
