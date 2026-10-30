#nullable enable
using System.Text.RegularExpressions;

namespace SafetyProto.Domain.Sessions
{
    /// <summary>
    /// The participant ids the evaluator assigns: <c>P-07</c> for a study participant (the code
    /// on the key spreadsheet), <c>DEMO-03</c> for a demonstration. Sessions never fall back to
    /// a random id on this path, so every collected log can be matched to its paper kit.
    /// </summary>
    public static class ParticipantCode
    {
        public const string ParticipantPrefix = "P";
        public const string DemoPrefix = "DEMO";
        public const int MaxDigits = 3;

        private static readonly Regex Normalized = new Regex(@"^(P|DEMO)-\d{2,3}$", RegexOptions.CultureInvariant);
        private static readonly Regex Loose = new Regex(@"^(?:(P|DEMO)\s*-?\s*)?(\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsValid(string? id) => id != null && Normalized.IsMatch(id);

        public static bool IsDemo(string? id) => id != null && id.StartsWith(DemoPrefix + "-", System.StringComparison.Ordinal);

        /// <summary>
        /// Accepts what an evaluator types — "7", "07", "P07", "p-7", "DEMO 3" — and returns the
        /// normalized id ("P-07", "DEMO-03"). Bare digits are a study participant. On failure,
        /// <paramref name="error"/> is a Portuguese message for the evaluator.
        /// </summary>
        public static bool TryNormalize(string? raw, out string id, out string error)
        {
            id = string.Empty;
            var match = Loose.Match((raw ?? string.Empty).Trim());
            if (!match.Success)
            {
                error = "Código inválido: use o número do participante (ex.: P-07) ou DEMO-01.";
                return false;
            }

            string digits = match.Groups[2].Value.TrimStart('0');
            if (digits.Length == 0)
            {
                error = "O número do participante precisa ser maior que zero.";
                return false;
            }
            if (digits.Length > MaxDigits)
            {
                error = $"Use no máximo {MaxDigits} dígitos no código.";
                return false;
            }

            string prefix = match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : ParticipantPrefix;
            id = $"{prefix}-{digits.PadLeft(2, '0')}";
            error = string.Empty;
            return true;
        }
    }

    /// <summary>Who set the participant id: the <c>source</c> token of the session log.</summary>
    public static class ParticipantSources
    {
        public const string Dashboard = "dashboard";
        public const string Keypad = "keypad";
        public const string SystemKeyboard = "system_keyboard";
        public const string Skipped = "skipped";
        public const string Simulator = "simulator";
    }
}
