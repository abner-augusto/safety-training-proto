using NUnit.Framework;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Networking.Dashboard;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class SceneLayoutGeometryTests
    {
        private const float Eps = 1e-4f;

        [Test]
        public void Marker_AxisAligned_OffsetsCentreAndKeepsSize()
        {
            var m = Matrix4x4.TRS(new Vector3(1f, 2f, 3f), Quaternion.identity, Vector3.one);
            var dto = SceneLayoutGeometry.Marker("g", "Guardrail", "install_guardrail", m,
                new Bounds(new Vector3(0.5f, 0f, 0f), new Vector3(2f, 1f, 4f)), 0.12f);

            Assert.AreEqual(1.5f, dto.px, Eps);
            Assert.AreEqual(2f, dto.py, Eps);
            Assert.AreEqual(3f, dto.pz, Eps);
            Assert.AreEqual(2f, dto.sx, Eps);
            Assert.AreEqual(1f, dto.sy, Eps);
            Assert.AreEqual(4f, dto.sz, Eps);
            Assert.AreEqual(1f, dto.qw, Eps);
            Assert.AreEqual("Guardrail", dto.kind);
            Assert.AreEqual("install_guardrail", dto.taskId);
        }

        [Test]
        public void Marker_Yawed_RotatesCentreAndKeepsLocalSize()
        {
            var rot = Quaternion.Euler(0f, 90f, 0f);
            var m = Matrix4x4.TRS(Vector3.zero, rot, Vector3.one);
            var dto = SceneLayoutGeometry.Marker("g", "Toeboard", "t", m,
                new Bounds(new Vector3(1f, 0f, 0f), new Vector3(2f, 1f, 4f)), 0f);

            Assert.AreEqual(0f, dto.px, Eps);
            Assert.AreEqual(-1f, dto.pz, Eps);
            Assert.AreEqual(2f, dto.sx, Eps);
            Assert.AreEqual(4f, dto.sz, Eps);
            Assert.AreEqual(rot.y, dto.qy, Eps);
            Assert.AreEqual(rot.w, dto.qw, Eps);
        }

        [Test]
        public void Marker_Scaled_ScalesCentreAndSize()
        {
            var m = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(2f, 1f, 0.5f));
            var dto = SceneLayoutGeometry.Marker("g", "Anchor", "t", m,
                new Bounds(new Vector3(1f, 0f, 2f), new Vector3(1f, 1f, 4f)), 0f);

            Assert.AreEqual(2f, dto.px, Eps);
            Assert.AreEqual(1f, dto.pz, Eps);
            Assert.AreEqual(2f, dto.sx, Eps);
            Assert.AreEqual(2f, dto.sz, Eps);
        }

        [Test]
        public void Marker_MinSize_ClampsThinAxesOnly()
        {
            var dto = SceneLayoutGeometry.Marker("i", "Irregularity", "t", Matrix4x4.identity,
                new Bounds(Vector3.zero, new Vector3(0.15f, 0.05f, 0f)), 0.12f);

            Assert.AreEqual(0.15f, dto.sx, Eps);
            Assert.AreEqual(0.12f, dto.sy, Eps);
            Assert.AreEqual(0.12f, dto.sz, Eps);
        }

        [Test]
        public void Floor_UsesTopFaceOfScaffoldDeck()
        {
            // Measured andaime.piso (Array_008): pivot and mesh bounds from the loaded scene.
            var m = Matrix4x4.TRS(new Vector3(-3.844f, 21.713f, 12.461f), Quaternion.identity, Vector3.one);
            var dto = SceneLayoutGeometry.Floor("andaime", m,
                new Bounds(new Vector3(1.599f, 0f, -0.003f), new Vector3(5.925f, 0.030f, 0.902f)));

            Assert.AreEqual("andaime", dto.id);
            Assert.AreEqual(-2.245f, dto.px, 1e-3f);
            Assert.AreEqual(21.728f, dto.py, 1e-3f);
            Assert.AreEqual(12.458f, dto.pz, 1e-3f);
            Assert.AreEqual(5.925f, dto.sx, Eps);
            Assert.AreEqual(0.902f, dto.sz, Eps);
            Assert.AreEqual(1f, dto.qw, Eps);
        }

        [Test]
        public void TryGetLocalBounds_BoxCollider_UsesCentreAndSize()
        {
            var go = new GameObject("box");
            try
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.5f, 0f);
                box.size = new Vector3(2f, 1f, 3f);

                Assert.IsTrue(SceneLayoutGeometry.TryGetLocalBounds(box, out var b));
                Assert.AreEqual(new Vector3(0f, 0.5f, 0f), b.center);
                Assert.AreEqual(new Vector3(2f, 1f, 3f), b.size);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TryGetLocalBounds_BareTransform_IsZeroSizePoint()
        {
            var go = new GameObject("point");
            try
            {
                Assert.IsTrue(SceneLayoutGeometry.TryGetLocalBounds(go.transform, out var b));
                Assert.AreEqual(Vector3.zero, b.center);
                Assert.AreEqual(Vector3.zero, b.size);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void TryGetLocalBounds_Null_ReturnsFalse()
        {
            Assert.IsFalse(SceneLayoutGeometry.TryGetLocalBounds(null, out _));
        }

        [Test]
        public void SceneLayoutDto_SerializesProtocolFieldNames()
        {
            var json = JsonUtility.ToJson(new SceneLayoutDto
            {
                floors = new[] { new SceneFloorDto { id = "andaime", sx = 1f, sz = 2f, qw = 1f } },
                markers = new[] { new SceneMarkerDto { id = "guardrail", kind = "Guardrail", taskId = "install_guardrail", qw = 1f } }
            });

            StringAssert.Contains("\"floors\":[{\"id\":\"andaime\"", json);
            StringAssert.Contains("\"sz\":2.0", json);
            StringAssert.Contains("\"kind\":\"Guardrail\"", json);
            StringAssert.Contains("\"taskId\":\"install_guardrail\"", json);
        }
    }
}
