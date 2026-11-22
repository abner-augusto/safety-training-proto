using NUnit.Framework;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class HeavyPieceGrabSolverTests
    {
        // The toeboard: 3.09 m long on X, 0.30 m high on Y (centred 5 cm low), handles 0.3 m off
        // the middle on its upper edge.
        private static readonly Bounds Board = new Bounds(new Vector3(0f, -0.05f, 0f), new Vector3(3.09f, 0.30f, 0.05f));
        private static readonly Vector3 HandleLocal = new Vector3(0.3f, 0.1f, 0f);
        private static readonly Vector3 OtherHandleLocal = new Vector3(-0.3f, 0.1f, 0f);
        private const float Dt = 1f / 72f;

        private static HeavyPieceGrabSolver NewSolver() =>
            new HeavyPieceGrabSolver(Vector3.right, Vector3.up, Board, HeavyPieceGrabSolver.Tuning.Default);

        private static Vector3 At(Pose piece, Vector3 local) => piece.position + piece.rotation * local;

        private static float? FloorAt(float height) => height;

        private static Pose HoldStill(HeavyPieceGrabSolver solver, Pose grip, float seconds, System.Func<Vector3, float?> floor)
        {
            Pose pose = default;
            for (float t = 0f; t < seconds; t += Dt) pose = solver.StepOneHand(grip, Dt, floor);
            return pose;
        }

        private static float LowestY(Pose piece)
        {
            float lowest = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = Board.center + Vector3.Scale(Board.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                lowest = Mathf.Min(lowest, At(piece, corner).y);
            }
            return lowest;
        }

        [Test]
        public void Begin_LeavesThePieceWhereItIs()
        {
            var piece = new Pose(new Vector3(1f, 0.63f, -0.85f), Quaternion.Euler(90f, 0f, 0f));
            var grip = new Pose(At(piece, HandleLocal), Quaternion.Euler(10f, 30f, 0f));
            var solver = NewSolver();

            solver.BeginOneHand(piece, grip);
            var held = solver.StepOneHand(grip, 0f, null);
            Assert.Less(Vector3.Distance(piece.position, held.position), 1e-4f);
            Assert.Less(Quaternion.Angle(piece.rotation, held.rotation), 0.05f);

            var second = new Pose(At(piece, OtherHandleLocal), Quaternion.identity);
            solver.BeginTwoHands(piece, grip, second);
            held = solver.StepTwoHands(grip, second, 0f);
            Assert.Less(Vector3.Distance(piece.position, held.position), 1e-4f);
            Assert.Less(Quaternion.Angle(piece.rotation, held.rotation), 0.05f);
        }

        [Test]
        public void OneHand_TheGripStaysOnTheHand()
        {
            var piece = new Pose(new Vector3(0f, 0.63f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);

            for (int i = 0; i < 120; i++)
            {
                grip.position += new Vector3(0.004f, 0.003f, -0.002f);
                grip.rotation *= Quaternion.Euler(0.5f, 1f, 2f);
                var held = solver.StepOneHand(grip, Dt, _ => 0f);
                Assert.Less(Vector3.Distance(At(held, HandleLocal), grip.position), 1e-4f, "step " + i);
            }
        }

        [Test]
        public void OneHand_TheFreeEndSagsOntoTheFloorAndRestsThere()
        {
            var piece = new Pose(new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);

            var held = HoldStill(solver, grip, 3f, _ => FloorAt(0f));

            Assert.AreEqual(0f, LowestY(held), 0.02f);
            Assert.Less(At(held, new Vector3(-1.545f, 0f, 0f)).y, grip.position.y - 0.3f, "the far end went down");
        }

        [Test]
        public void OneHand_WithNothingBelow_TheFreeEndHangsAtTheMaximumSag()
        {
            var piece = new Pose(new Vector3(0f, 22f, 0f), Quaternion.identity);
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);

            var held = HoldStill(solver, grip, 5f, _ => null);

            Vector3 towardsFarEnd = held.rotation * Vector3.left;
            float sag = Mathf.Asin(-towardsFarEnd.y) * Mathf.Rad2Deg;
            Assert.AreEqual(HeavyPieceGrabSolver.Tuning.Default.maxSagDegrees, sag, 1f);
        }

        [Test]
        public void OneHand_TheWristsRollNeverTurnsThePiece()
        {
            var piece = new Pose(new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            var still = NewSolver();
            var rolled = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            still.BeginOneHand(piece, grip);
            rolled.BeginOneHand(piece, grip);

            Pose a = default, b = default;
            for (int i = 0; i < 100; i++)
            {
                a = still.StepOneHand(grip, Dt, _ => 0f);
                b = rolled.StepOneHand(new Pose(grip.position, Quaternion.Euler(0f, 0f, i * 2f)), Dt, _ => 0f);
            }
            Assert.Less(Quaternion.Angle(a.rotation, b.rotation), 0.01f);
        }

        [Test]
        public void OneHand_LyingOrStanding_ItEndsUpHangingTheSameWay()
        {
            var lying = new Pose(new Vector3(0f, 0.9f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var standing = new Pose(new Vector3(0f, 0.9f, 0f), Quaternion.identity);
            var fromLying = NewSolver();
            var fromStanding = NewSolver();
            var gripLying = new Pose(At(lying, HandleLocal), Quaternion.identity);
            var gripStanding = new Pose(At(standing, HandleLocal), Quaternion.identity);
            fromLying.BeginOneHand(lying, gripLying);
            fromStanding.BeginOneHand(standing, gripStanding);

            var a = HoldStill(fromLying, gripLying, 4f, _ => null);
            var b = HoldStill(fromStanding, gripStanding, 4f, _ => null);

            Assert.Less(Quaternion.Angle(a.rotation, b.rotation), 1f);
            Assert.Greater(Vector3.Dot(a.rotation * Vector3.up, Vector3.ProjectOnPlane(Vector3.up, a.rotation * Vector3.right).normalized), 0.999f,
                "the handles' edge is up");
        }

        [Test]
        public void OneHand_MovingSideways_DragsTheFreeEndBehind()
        {
            var piece = new Pose(new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);
            var rested = HoldStill(solver, grip, 2f, _ => FloorAt(0f));
            float before = Yaw(rested.rotation * Vector3.right);

            Pose held = rested;
            for (int i = 0; i < 72; i++)
            {
                grip.position += new Vector3(0f, 0f, 0.5f / 72f);
                held = solver.StepOneHand(grip, Dt, _ => 0f);
            }

            float turned = Mathf.Abs(Mathf.DeltaAngle(before, Yaw(held.rotation * Vector3.right)));
            Assert.Greater(turned, 8f);
            Assert.Less(Vector3.Distance(At(held, HandleLocal), grip.position), 1e-4f);
        }

        [Test]
        public void OneHand_TheWristsYawSteersPartlyAndLate()
        {
            var piece = new Pose(new Vector3(0f, 22f, 0f), Quaternion.identity);
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);
            float before = Yaw(solver.StepOneHand(grip, Dt, _ => null).rotation * Vector3.right);

            var turnedGrip = new Pose(grip.position, Quaternion.Euler(0f, 40f, 0f));
            float afterOneFrame = Yaw(solver.StepOneHand(turnedGrip, Dt, _ => null).rotation * Vector3.right);
            var settled = HoldStill(solver, turnedGrip, 3f, _ => null);
            float turned = Mathf.DeltaAngle(before, Yaw(settled.rotation * Vector3.right));

            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(before, afterOneFrame)), 3f, "late");
            Assert.AreEqual(40f * HeavyPieceGrabSolver.Tuning.Default.wristYawShare, turned, 1f, "partly");
        }

        [Test]
        public void TwoHands_TheLongAxisFollowsTheHandsAndThePieceHangs()
        {
            var piece = new Pose(new Vector3(0f, 0.63f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var solver = NewSolver();
            var first = new Pose(At(piece, HandleLocal), Quaternion.identity);
            var second = new Pose(At(piece, OtherHandleLocal), Quaternion.identity);
            solver.BeginTwoHands(piece, first, second);

            Vector3 centre = (first.position + second.position) * 0.5f + Vector3.up * 0.4f;
            Vector3 line = Quaternion.Euler(0f, 30f, 10f) * (second.position - first.position);
            first = new Pose(centre - line * 0.5f, Quaternion.Euler(0f, 0f, 60f));
            second = new Pose(centre + line * 0.5f, Quaternion.Euler(0f, 0f, -60f));
            Pose held = default;
            for (float t = 0f; t < 2f; t += Dt) held = solver.StepTwoHands(first, second, Dt);

            Assert.Greater(Vector3.Dot(held.rotation * Vector3.left, line.normalized), 0.9999f);
            Vector3 hang = Vector3.ProjectOnPlane(Vector3.up, held.rotation * Vector3.right).normalized;
            Assert.Greater(Vector3.Dot(held.rotation * Vector3.up, hang), 0.999f);
            Assert.Less(Vector3.Distance(At(held, (HandleLocal + OtherHandleLocal) * 0.5f), centre), 1e-4f);
        }

        // Supports are sampled every 8 cm, so a sloping edge can dip into a support between two
        // samples by up to the slope times the gap, about a centimetre at 20 degrees.
        private const float SampledSupportTolerance = -0.02f;

        // Trestle tops at 0.6 m, 12 cm wide, 1.1 m either side of the middle; the floor elsewhere.
        private static float? Trestles(Vector3 p) => Mathf.Abs(Mathf.Abs(p.x) - 1.1f) < 0.06f ? 0.6f : 0f;

        // The lowest point of the piece's edges over each x where the support function stands,
        // minus the support height there: negative means the piece went into it. The solver treats
        // the piece as a plank, so its thickness (5 cm) is left out of the check.
        private static float WorstClearance(Pose piece, System.Func<Vector3, float?> support)
        {
            float worst = float.MaxValue;
            for (float along = -1.545f; along <= 1.545f; along += 0.02f)
            {
                var bottom = At(piece, new Vector3(along, Board.min.y, 0f));
                var floor = support(bottom);
                if (floor.HasValue) worst = Mathf.Min(worst, bottom.y - floor.Value);
            }
            return worst;
        }

        [Test]
        public void OneHand_OnTwoTrestles_ItRestsOnThemAndSagsOnlyOnceLifted()
        {
            var piece = new Pose(new Vector3(0f, 0.64f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);

            var held = HoldStill(solver, grip, 2f, Trestles);
            Assert.Less(Quaternion.Angle(piece.rotation, held.rotation), 12f, "lying across both, it stays put");

            for (int i = 0; i < 72; i++)
            {
                grip.position += Vector3.up * (0.5f / 72f);
                held = solver.StepOneHand(grip, Dt, Trestles);
                Assert.Greater(WorstClearance(held, Trestles), SampledSupportTolerance, "step " + i);
            }
            held = HoldStill(solver, grip, 2f, Trestles);

            Assert.Greater(WorstClearance(held, Trestles), SampledSupportTolerance);
            Assert.Less(At(held, new Vector3(-1.545f, 0f, 0f)).y, At(held, new Vector3(1.545f, 0f, 0f)).y - 0.2f, "the far end sank");
            Assert.Greater(Vector3.Dot(held.rotation * Vector3.up, Vector3.up), 0.8f, "it hangs from the handles");
        }

        [Test]
        public void OneHand_LyingOnATable_ItDoesNotSinkThroughToHang()
        {
            var piece = new Pose(new Vector3(0f, 0.64f, 0f), Quaternion.Euler(90f, 0f, 0f));
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);

            var held = HoldStill(solver, grip, 2f, _ => 0.6f);

            Assert.Greater(WorstClearance(held, _ => 0.6f), -0.01f);
            Assert.Less(Vector3.Dot(held.rotation * Vector3.up, Vector3.up), 0.3f, "it stays nearly flat");
        }

        [Test]
        public void OneHand_HeldStill_ItSettlesEvenOverAnEdge()
        {
            // A deck that ends at x = -1.1 with a bracket on its last 10 cm, then nothing.
            System.Func<Vector3, float?> deck = p => p.x > -1.0f ? 0f : p.x > -1.1f ? 0.25f : (float?)null;
            var piece = new Pose(new Vector3(0f, 0.75f, 0f), Quaternion.identity);
            var solver = NewSolver();
            var grip = new Pose(At(piece, HandleLocal), Quaternion.identity);
            solver.BeginOneHand(piece, grip);
            var settled = HoldStill(solver, grip, 3f, deck);

            float worstStep = 0f, drift = 0f;
            var previous = settled;
            for (int i = 0; i < 72; i++)
            {
                var held = solver.StepOneHand(grip, Dt, deck);
                worstStep = Mathf.Max(worstStep, Quaternion.Angle(previous.rotation, held.rotation));
                drift = Mathf.Max(drift, Quaternion.Angle(settled.rotation, held.rotation));
                previous = held;
            }
            Assert.Less(worstStep, 0.05f);
            Assert.Less(drift, 0.2f);
        }

        private static float Yaw(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
    }
}
