using System.Collections.Generic;
using NUnit.Framework;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers the three rules that decide whether a head ray counts as gaze: the hit must be on the
    /// GazeTarget layer, within range, and not behind solid geometry.
    /// </summary>
    public class HeadGazeSourceTests
    {
        private const int GazeTargetLayer = 17;   // "GazeTarget"
        private const int DefaultLayer = 0;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private HeadGazeSource _source;

        private class RecordingTarget : MonoBehaviour, IGazeTarget
        {
            public int GazedFrames;
            public void OnGazed(float deltaTime) => GazedFrames++;
        }

        [SetUp]
        public void SetUp()
        {
            var host = new GameObject("HeadGazeSource");
            _spawned.Add(host);
            _source = host.AddComponent<HeadGazeSource>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        private RecordingTarget SpawnBox(Vector3 position, int layer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _spawned.Add(go);
            go.transform.position = position;
            go.layer = layer;
            Physics.SyncTransforms();
            return go.AddComponent<RecordingTarget>();
        }

        [Test]
        public void ResolvesTargetOnGazeLayerWithinRange()
        {
            var target = SpawnBox(new Vector3(0f, 0f, 2f), GazeTargetLayer);

            var resolved = _source.ResolveTarget(new Ray(Vector3.zero, Vector3.forward));

            Assert.AreSame(target, resolved);
        }

        [Test]
        public void IgnoresTargetBeyondMaxDistance()
        {
            SpawnBox(new Vector3(0f, 0f, _source.MaxDistance + 2f), GazeTargetLayer);

            var resolved = _source.ResolveTarget(new Ray(Vector3.zero, Vector3.forward));

            Assert.IsNull(resolved, "A tear seen from the ground must not be reportable.");
        }

        [Test]
        public void IgnoresTargetOccludedBySolidGeometry()
        {
            SpawnBox(new Vector3(0f, 0f, 2f), GazeTargetLayer);
            SpawnBox(new Vector3(0f, 0f, 1f), DefaultLayer);   // scaffold tube in the way

            var resolved = _source.ResolveTarget(new Ray(Vector3.zero, Vector3.forward));

            Assert.IsNull(resolved, "Looking through the scaffold structure must not count as gaze.");
        }

        [Test]
        public void ResolvesNothingWhenRayMissesEverything()
        {
            SpawnBox(new Vector3(0f, 0f, 2f), GazeTargetLayer);

            var resolved = _source.ResolveTarget(new Ray(Vector3.zero, Vector3.back));

            Assert.IsNull(resolved);
        }
    }
}
