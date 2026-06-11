using System;
using System.Collections.Generic;
using System.IO;
using SafetyProto.Core.Logging;
using UnityEngine;

namespace SafetyProto.Runtime.Session
{
    /// <summary>
    /// Holds the current participant's anonymized identifier (e.g. "P-7F3A") and, privately, maps
    /// it back to their first name. The identifier is what stamps every logged event and reaches
    /// the evaluator dashboard — anonymized data. The real first name is written only to a private
    /// file on the device (persistentDataPath/participants_private.json), never to the session
    /// exports nor the LAN dashboard, so the mapping can be resolved later if needed.
    /// </summary>
    public static class ParticipantIdentity
    {
        private const string MapFileName = "participants_private.json";

        /// <summary>Anonymized id used as the player id for all logged events. Null until set.</summary>
        public static string CurrentId { get; private set; }

        /// <summary>The participant's first name (empty if skipped). Kept in memory + private file only.</summary>
        public static string CurrentName { get; private set; } = string.Empty;

        [Serializable]
        private class Entry
        {
            public string id;
            public string name;
            public string utc;
        }

        [Serializable]
        private class Map
        {
            public List<Entry> entries = new List<Entry>();
        }

        private static string MapPath => Path.Combine(Application.persistentDataPath, MapFileName);

        /// <summary>
        /// Assigns a fresh anonymized id for this participant and records the (optional) name in the
        /// private on-device map. Returns the generated id.
        /// </summary>
        public static string SetParticipant(string firstName)
        {
            string name = (firstName ?? string.Empty).Trim();
            var map = Load();
            string id = GenerateUniqueId(map);

            CurrentId = id;
            CurrentName = name;

            map.entries.Add(new Entry { id = id, name = name, utc = DateTime.UtcNow.ToString("o") });
            Save(map);

            SafetyLog.Info($"[ParticipantIdentity] Participante registrado como {id} (nome guardado em arquivo privado).");
            return id;
        }

        private static string GenerateUniqueId(Map map)
        {
            for (int attempt = 0; attempt < 1000; attempt++)
            {
                string candidate = "P-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
                bool exists = false;
                foreach (var e in map.entries)
                {
                    if (e != null && e.id == candidate) { exists = true; break; }
                }
                if (!exists) return candidate;
            }
            // Astronomically unlikely fallback — widen to 8 chars.
            return "P-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        }

        private static Map Load()
        {
            try
            {
                if (File.Exists(MapPath))
                {
                    string json = File.ReadAllText(MapPath);
                    var map = JsonUtility.FromJson<Map>(json);
                    if (map != null && map.entries != null) return map;
                }
            }
            catch (Exception ex)
            {
                SafetyLog.Warning($"[ParticipantIdentity] Falha ao ler o mapa privado: {ex.Message}");
            }
            return new Map();
        }

        private static void Save(Map map)
        {
            try
            {
                File.WriteAllText(MapPath, JsonUtility.ToJson(map, true));
            }
            catch (Exception ex)
            {
                SafetyLog.Warning($"[ParticipantIdentity] Falha ao salvar o mapa privado: {ex.Message}");
            }
        }
    }
}
