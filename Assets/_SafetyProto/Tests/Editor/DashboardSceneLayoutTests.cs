using NUnit.Framework;
using SafetyProto.Networking.Dashboard;
using UnityEngine;
using UnityEngine.TestTools;

namespace SafetyProto.Tests.Editor
{
    public class DashboardSceneLayoutTests
    {
        private GameObject _go = null!;

        [SetUp]
        public void Setup() => _go = new GameObject("layout-test");

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void BuildFrom_SkipsEntriesWithoutSource_AndWarns()
        {
            var floors = new[] { new DashboardSceneLayout.FloorEntry { id = "ghost", source = null } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("ghost"));
            var dto = DashboardSceneLayout.BuildFrom(floors, new DashboardSceneLayout.MarkerEntry[0], null);

            Assert.AreEqual(0, dto.floors.Length);
            Assert.AreEqual(0, dto.markers.Length);
        }

        [Test]
        public void BuildFrom_SkipsFloorWithoutArea_AndWarns()
        {
            // A bare Transform resolves to a point: no rectangle to stand on.
            var floors = new[] { new DashboardSceneLayout.FloorEntry { id = "flat", source = _go.transform } };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("flat"));
            var dto = DashboardSceneLayout.BuildFrom(floors, new DashboardSceneLayout.MarkerEntry[0], null);

            Assert.AreEqual(0, dto.floors.Length);
        }

        [Test]
        public void BuildFrom_SkinnedRendererWithRootBone_UsesTheRootBoneSpace()
        {
            var bone = new GameObject("bone").transform;
            bone.SetParent(_go.transform, false);
            bone.localPosition = new Vector3(0f, 5f, 0f);
            var skinned = _go.AddComponent<SkinnedMeshRenderer>();
            skinned.rootBone = bone;
            skinned.localBounds = new Bounds(Vector3.zero, new Vector3(2f, 0.2f, 2f));
            var floors = new[] { new DashboardSceneLayout.FloorEntry { id = "skinned", source = skinned } };

            var dto = DashboardSceneLayout.BuildFrom(floors, new DashboardSceneLayout.MarkerEntry[0], null);

            Assert.AreEqual(5.1f, dto.floors[0].py, 1e-4f);
        }

        [Test]
        public void Start_CapturesTheLayoutBeforeTheSceneMoves()
        {
            var box = _go.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 0.2f, 1f);
            var layout = _go.AddComponent<DashboardSceneLayout>();
            var so = new UnityEditor.SerializedObject(layout);
            var floors = so.FindProperty("floors");
            floors.arraySize = 1;
            floors.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue = "deck";
            floors.GetArrayElementAtIndex(0).FindPropertyRelative("source").objectReferenceValue = box;
            so.ApplyModifiedPropertiesWithoutUndo();

            typeof(DashboardSceneLayout).GetMethod("Start",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .Invoke(layout, null);
            _go.transform.rotation = Quaternion.Euler(30f, 0f, 0f);   // the collapse tilts the deck later

            Assert.AreEqual(1f, layout.Build().floors[0].qw, 1e-5f);
        }

        [Test]
        public void BuildFrom_MarkerWithoutMinSize_UsesDefault()
        {
            var markers = new[]
            {
                new DashboardSceneLayout.MarkerEntry
                {
                    id = "anchor", kind = DashboardMarkerKind.Anchor, taskId = "connect_lanyard",
                    source = _go.transform, minSize = 0f
                }
            };

            var dto = DashboardSceneLayout.BuildFrom(new DashboardSceneLayout.FloorEntry[0], markers, null);

            Assert.AreEqual(1, dto.markers.Length);
            Assert.AreEqual("Anchor", dto.markers[0].kind);
            Assert.AreEqual("connect_lanyard", dto.markers[0].taskId);
            Assert.AreEqual(DashboardSceneLayout.DefaultMinSize, dto.markers[0].sx, 1e-5f);
        }

        [Test]
        public void Waypoints_ExposeFloorAndMarkerIds_AndResolveStandPoints()
        {
            _go.transform.position = new Vector3(0f, 10f, 0f);
            var box = _go.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 0.2f, 1f);
            var spot = new GameObject("spot");
            spot.transform.SetParent(_go.transform, false);
            spot.transform.localPosition = new Vector3(1f, 1f, 2f);   // beyond the 1 m deck depth

            var layout = _go.AddComponent<DashboardSceneLayout>();
            var so = new UnityEditor.SerializedObject(layout);
            var floors = so.FindProperty("floors");
            floors.arraySize = 1;
            floors.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue = "deck";
            floors.GetArrayElementAtIndex(0).FindPropertyRelative("source").objectReferenceValue = box;
            var markers = so.FindProperty("markers");
            markers.arraySize = 1;
            markers.GetArrayElementAtIndex(0).FindPropertyRelative("id").stringValue = "spot";
            markers.GetArrayElementAtIndex(0).FindPropertyRelative("source").objectReferenceValue = spot.transform;
            so.ApplyModifiedPropertiesWithoutUndo();

            SafetyProto.Runtime.Simulation.ISimulationWaypoints waypoints = layout;

            CollectionAssert.AreEqual(new[] { "deck", "spot" }, waypoints.WaypointIds);
            Assert.IsTrue(waypoints.TryGetStandPoint("spot", out var stand, out var lookAt));
            Assert.AreEqual(10.1f, stand.y, 1e-4f);
            Assert.AreEqual(0.5f - SceneLayoutGeometry.StandInset, stand.z, 1e-4f);
            Assert.AreEqual(new Vector3(1f, 11f, 2f), lookAt);
        }

        [Test]
        public void BuildFrom_FloorFromBoxCollider_UsesTopFace()
        {
            _go.transform.position = new Vector3(0f, 10f, 0f);
            var box = _go.AddComponent<BoxCollider>();
            box.size = new Vector3(4f, 0.2f, 1f);
            var floors = new[] { new DashboardSceneLayout.FloorEntry { id = "deck", source = box } };

            var dto = DashboardSceneLayout.BuildFrom(floors, new DashboardSceneLayout.MarkerEntry[0], null);

            Assert.AreEqual(10.1f, dto.floors[0].py, 1e-4f);
            Assert.AreEqual(4f, dto.floors[0].sx, 1e-4f);
        }
    }
}
