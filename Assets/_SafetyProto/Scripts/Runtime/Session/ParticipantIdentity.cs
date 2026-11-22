using System;
using SafetyProto.Core.Logging;
using SafetyProto.Domain.Sessions;

namespace SafetyProto.Runtime.Session
{
    /// <summary>
    /// Holds the current participant's identifier, which stamps every logged event and reaches the
    /// evaluator dashboard: the study code the evaluator or the keypad set (<c>P-07</c>,
    /// <c>DEMO-03</c>), a random <c>P-XXXX</c> after "Pular" in the development popup, or a
    /// <c>SIM-</c> id for a simulated session. The app never asks for a name nor stores one.
    /// </summary>
    public static class ParticipantIdentity
    {
        /// <summary>Anonymized id used as the player id for all logged events. Null until set.</summary>
        public static string CurrentId { get; private set; }

        /// <summary>Who set <see cref="CurrentId"/> (a <see cref="ParticipantSources"/> token).</summary>
        public static string CurrentSource { get; private set; } = string.Empty;

        /// <summary>
        /// Uses the study's participant code (e.g. "P-07") as the id itself. The code is already
        /// pseudonymous: the key spreadsheet that resolves it lives with the researcher.
        /// </summary>
        public static string SetParticipantCode(string code, string source = ParticipantSources.Keypad)
        {
            CurrentId = (code ?? string.Empty).Trim();
            CurrentSource = source ?? string.Empty;
            SafetyLog.Info($"[ParticipantIdentity] Participante registrado como {CurrentId} ({CurrentSource}).");
            return CurrentId;
        }

        /// <summary>
        /// "Pular" in the development popup: a random <c>P-XXXX</c>, hexadecimal so it cannot be
        /// taken for a study code (<c>P-07</c>), and logged as <see cref="ParticipantSources.Skipped"/>.
        /// </summary>
        public static string SetAnonymousParticipant()
        {
            CurrentId = "P-" + Guid.NewGuid().ToString("N").Substring(0, 4).ToUpperInvariant();
            CurrentSource = ParticipantSources.Skipped;
            SafetyLog.Info($"[ParticipantIdentity] Identificação pulada; sessão registrada como {CurrentId}.");
            return CurrentId;
        }

        /// <summary>Forgets the participant, so the next session cannot inherit a finished one's id.</summary>
        public static void Clear()
        {
            CurrentId = null;
            CurrentSource = string.Empty;
        }

        /// <summary>
        /// Assigns a synthetic identity. The SIM- prefix is the MVP contract for excluding
        /// simulator data from participant data.
        /// </summary>
        public static string SetSimulatedParticipant()
        {
            string id = "SIM-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            CurrentId = id;
            CurrentSource = ParticipantSources.Simulator;
            SafetyLog.Info($"[ParticipantIdentity] Sessão simulada identificada como {id}.");
            return id;
        }
    }
}
