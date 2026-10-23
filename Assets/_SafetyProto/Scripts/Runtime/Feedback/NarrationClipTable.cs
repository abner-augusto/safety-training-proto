using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>
    /// Inspector-authored map from a narration key (see <c>Domain.Feedback.NarrationKeys</c>) to its
    /// voice-over clip. A missing key or an empty slot resolves to null, so the caller can degrade to
    /// a silent popup and log it.
    /// </summary>
    [Serializable]
    public sealed class NarrationClipTable
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("Chave da narração (ex.: 'fase_concluida', 'distractor:flip_flops', 'report:<taskId>').")]
            public string key;
            public AudioClip clip;
        }

        [SerializeField] private List<Entry> entries = new();

        public IReadOnlyList<Entry> Entries => entries;

        public void Add(string key, AudioClip clip) => entries.Add(new Entry { key = key, clip = clip });

        /// <summary>The clip for <paramref name="key"/>, or null for an empty key, an unknown key or an empty slot.</summary>
        public AudioClip Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            var wanted = key.Trim();
            foreach (var entry in entries)
                if (entry != null && entry.clip != null && string.Equals(entry.key?.Trim(), wanted, StringComparison.Ordinal))
                    return entry.clip;
            return null;
        }

        /// <summary>The clip at <paramref name="index"/>, or null when the index is out of range or the slot is empty.</summary>
        public static AudioClip At(IReadOnlyList<AudioClip> clips, int index)
        {
            if (clips == null || index < 0 || index >= clips.Count) return null;
            return clips[index] != null ? clips[index] : null;
        }
    }
}
