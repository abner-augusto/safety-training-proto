using SafetyProto.Runtime.PPE;
using SafetyProto.Runtime.Scaffolding;
using UnityEngine;

namespace SafetyProto.Runtime.Feedback
{
    /// <summary>How many objects one recall sent home, by kind.</summary>
    public struct LooseObjectRecallResult
    {
        public int Ppe;
        public int Distractors;
        public int ScaffoldPieces;
        public int Other;
        /// <summary>Loose objects left alone because a hand was holding them.</summary>
        public int SkippedHeld;

        public int Returned => Ppe + Distractors + ScaffoldPieces + Other;

        /// <summary>Portuguese summary for the evaluator ("3 EPIs e 1 distrator devolvidos").</summary>
        public string Describe()
        {
            if (Returned == 0)
                return SkippedHeld > 0
                    ? $"Nenhum objeto devolvido; {Count(SkippedHeld, "objeto está", "objetos estão")} na mão do participante."
                    : "Nenhum objeto fora do lugar.";

            var parts = new System.Collections.Generic.List<string>(4);
            if (Ppe > 0) parts.Add(Count(Ppe, "EPI", "EPIs"));
            if (Distractors > 0) parts.Add(Count(Distractors, "distrator", "distratores"));
            if (ScaffoldPieces > 0) parts.Add(Count(ScaffoldPieces, "peça do andaime", "peças do andaime"));
            if (Other > 0) parts.Add(Count(Other, "outro objeto", "outros objetos"));
            string list = parts.Count == 1
                ? parts[0]
                : string.Join(", ", parts.GetRange(0, parts.Count - 1)) + " e " + parts[parts.Count - 1];
            string held = SkippedHeld > 0 ? $"; {Count(SkippedHeld, "ficou", "ficaram")} na mão do participante" : string.Empty;
            return $"{list} {(Returned == 1 ? "devolvido" : "devolvidos")} à posição inicial{held}.";
        }

        private static string Count(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
    }

    /// <summary>
    /// Sends every loose interactive object back to its home pose at once — the evaluator's
    /// recovery when the participant's hands knocked PPE out of reach and the automatic return
    /// did not bring it back. Leaves alone what is held, worn or installed, so the session state
    /// the participant built is untouched and nothing is scored.
    /// </summary>
    public static class LooseObjectRecall
    {
        // Objects resting on the table settle up to ~7 cm and ~10° off their authored pose under
        // physics; counting those would report "4 EPIs devolvidos" when one was knocked away.
        private const float HomePositionTolerance = 0.15f;
        private const float HomeAngleTolerance = 30f;

        public static LooseObjectRecallResult RecallAll()
        {
            var result = new LooseObjectRecallResult();
            foreach (var home in Object.FindObjectsByType<ReturnObjectHome>())
            {
                if (home == null || home.IsReturning || home.IsSecured) continue;

                if (home.IsHeld)
                {
                    result.SkippedHeld++;
                    continue;
                }
                if (IsAtHome(home)) continue;

                home.BeginReturnNow();

                var ppe = home.GetComponent<PPEItem>();
                if (home.GetComponent<ScaffoldPieceInstaller>() != null) result.ScaffoldPieces++;
                else if (ppe != null && ppe.isDistractor) result.Distractors++;
                else if (ppe != null) result.Ppe++;
                else result.Other++;
            }
            return result;
        }

        private static bool IsAtHome(ReturnObjectHome home)
        {
            var target = home.HomePose;
            var t = home.transform;
            return Vector3.Distance(t.position, target.position) <= HomePositionTolerance
                && Quaternion.Angle(t.rotation, target.rotation) <= HomeAngleTolerance;
        }
    }
}
