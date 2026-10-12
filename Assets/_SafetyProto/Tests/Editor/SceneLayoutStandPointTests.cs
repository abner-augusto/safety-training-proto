using NUnit.Framework;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Networking.Dashboard;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class SceneLayoutStandPointTests
    {
        private const float Eps = 1e-3f;

        // Measured in SafetyTraining: andaime.piso (Array_008) and the four scaffold spots.
        private static readonly SceneFloorDto Deck = new SceneFloorDto
            { id = "andaime", px = -2.246f, py = 21.728f, pz = 12.458f, sx = 5.925f, sz = 0.902f, qw = 1f };
        private static readonly SceneFloorDto Yard = new SceneFloorDto
            { id = "canteiro", px = 0.336f, py = 0.05f, pz = 0.661f, sx = 7.328f, sz = 5.178f, qw = 1f };

        private static SceneMarkerDto Marker(string id, float x, float y, float z) =>
            new SceneMarkerDto { id = id, px = x, py = y, pz = z, sx = 0.12f, sy = 0.12f, sz = 0.12f, qw = 1f };

        private static SceneLayoutDto Layout(params SceneMarkerDto[] markers) =>
            new SceneLayoutDto { floors = new[] { Yard, Deck }, markers = markers };

        [Test]
        public void Floor_StandsAtTopFaceCentre_WithoutFacing()
        {
            Assert.IsTrue(SceneLayoutGeometry.TryGetStandPoint(Layout(), "andaime", out var stand, out var lookAt));

            Assert.AreEqual(-2.246f, stand.x, Eps);
            Assert.AreEqual(21.728f, stand.y, Eps);
            Assert.AreEqual(12.458f, stand.z, Eps);
            Assert.AreEqual(stand, lookAt);
        }

        [Test]
        public void MarkerPastDeckEdge_IsPulledInsideTheDeck_AndFaced()
        {
            var toeboard = Marker("toeboard", -0.753f, 21.775f, 11.98f);   // 3 cm outside the deck edge

            Assert.IsTrue(SceneLayoutGeometry.TryGetStandPoint(Layout(toeboard), "toeboard", out var stand, out var lookAt));

            Assert.AreEqual(-0.753f, stand.x, Eps);
            Assert.AreEqual(21.728f, stand.y, Eps);
            Assert.AreEqual(12.458f - 0.451f + SceneLayoutGeometry.StandInset, stand.z, Eps);
            Assert.AreEqual(new Vector3(-0.753f, 21.775f, 11.98f), lookAt);
        }

        [Test]
        public void MarkerInsideTheFloor_KeepsItsPlanPosition()
        {
            var spot = Marker("spot", 1.0f, 1.0f, 1.0f);

            Assert.IsTrue(SceneLayoutGeometry.TryGetStandPoint(Layout(spot), "spot", out var stand, out _));

            Assert.AreEqual(1.0f, stand.x, Eps);
            Assert.AreEqual(0.05f, stand.y, Eps);
            Assert.AreEqual(1.0f, stand.z, Eps);
        }

        [Test]
        public void YawedFloor_ClampsInItsOwnAxes()
        {
            // 4 x 1 m floor turned 90 degrees: its long side runs along world Z.
            var rot = Quaternion.Euler(0f, 90f, 0f);
            var floor = new SceneFloorDto { id = "f", sx = 4f, sz = 1f, qx = rot.x, qy = rot.y, qz = rot.z, qw = rot.w };
            var layout = new SceneLayoutDto { floors = new[] { floor }, markers = new[] { Marker("m", 1.0f, 0.5f, 1.5f) } };

            Assert.IsTrue(SceneLayoutGeometry.TryGetStandPoint(layout, "m", out var stand, out _));

            Assert.AreEqual(0.5f - SceneLayoutGeometry.StandInset, stand.x, Eps);
            Assert.AreEqual(1.5f, stand.z, Eps);
        }

        [Test]
        public void MarkerPicksTheHighestFloorNotAboveIt()
        {
            var high = new SceneFloorDto { id = "high", py = 10f, sx = 4f, sz = 4f, qw = 1f };
            var low = new SceneFloorDto { id = "low", py = 0f, sx = 4f, sz = 4f, qw = 1f };
            var layout = new SceneLayoutDto { floors = new[] { low, high }, markers = new[] { Marker("m", 0f, 11f, 0f) } };

            Assert.IsTrue(SceneLayoutGeometry.TryGetStandPoint(layout, "m", out var stand, out _));

            Assert.AreEqual(10f, stand.y, Eps);
        }

        [Test]
        public void MarkerFarFromAnyFloor_HasNoStandPoint()
        {
            Assert.IsFalse(SceneLayoutGeometry.TryGetStandPoint(Layout(Marker("far", 50f, 0f, 50f)), "far", out _, out _));
        }

        [Test]
        public void UnknownId_HasNoStandPoint()
        {
            Assert.IsFalse(SceneLayoutGeometry.TryGetStandPoint(Layout(), "nope", out _, out _));
        }
    }
}
