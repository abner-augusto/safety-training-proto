using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime.PPE;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class HandSurfaceLimiterTests
    {
        private const float Radius = 0.012f;
        private const int SurfaceLayer = 31;
        private static readonly Vector3 Origin = new Vector3(1000f, 1000f, 1000f);
        private readonly List<GameObject> _objects = new List<GameObject>();
        private HandSurfaceLimiter _limiter;

        [SetUp]
        public void SetUp()
        {
            GameObject hand = CreateObject("Hand surface limiter test");
            hand.SetActive(false);
            _limiter = hand.AddComponent<HandSurfaceLimiter>();
            SetField("surfaceLayers", (LayerMask)(1 << SurfaceLayer));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
        }

        [Test]
        public void DiagonalContact_PreservesLateralMotion()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(4f, 1f, 4f));

            Vector3 result = Slide(new Vector3(0f, 0.1f, 0f), new Vector3(0.2f, -0.1f, 0.15f));

            Assert.That(result.x, Is.EqualTo(0.2f).Within(0.0005f));
            Assert.That(result.z, Is.EqualTo(0.15f).Within(0.0005f));
            Assert.That(result.y, Is.InRange(Radius - 0.0005f, Radius + 0.002f));
        }

        [Test]
        public void SustainedPressure_ContinuesSlidingAcrossFrames()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(4f, 1f, 4f));
            Vector3 position = new Vector3(0f, Radius + 0.001f, 0f);

            for (int frame = 1; frame <= 20; frame++)
            {
                position = Slide(position, new Vector3(frame * 0.02f, -0.05f, 0f));
                Assert.That(position.x, Is.EqualTo(frame * 0.02f).Within(0.0005f));
                Assert.That(position.y, Is.GreaterThanOrEqualTo(Radius - 0.0005f));
            }
        }

        [Test]
        public void TableAndWall_StopsAcrossCornerAndSlidesAlongIt()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(4f, 1f, 4f));
            CreateBox(new Vector3(0.7f, 0.5f, 0f), new Vector3(1f, 2f, 4f));

            Vector3 result = Slide(new Vector3(0f, 0.1f, 0f), new Vector3(0.4f, -0.2f, 0.3f));

            Assert.That(result.x, Is.InRange(0.2f - Radius - 0.003f, 0.2f - Radius + 0.0005f));
            Assert.That(result.y, Is.GreaterThanOrEqualTo(Radius - 0.0005f));
            Assert.That(result.z, Is.EqualTo(0.3f).Within(0.0005f));
        }

        [Test]
        public void MovingAwayFromSurface_ReachesTrackedTarget()
        {
            CreateBox(new Vector3(0f, -0.5f, 0f), new Vector3(4f, 1f, 4f));
            Vector3 target = new Vector3(0.1f, 0.2f, 0f);

            Assert.That(Vector3.Distance(Slide(new Vector3(0f, Radius + 0.001f, 0f), target), target),
                Is.LessThan(0.0005f));
        }

        [Test]
        public void FastMotionThroughThinWall_IsSweptBeforeSliding()
        {
            CreateBox(Vector3.zero, new Vector3(0.01f, 4f, 4f));

            Vector3 result = Slide(new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0.2f, 0f));

            Assert.That(result.x, Is.LessThanOrEqualTo(-0.005f - Radius + 0.0005f));
            Assert.That(result.y, Is.EqualTo(0.2f).Within(0.0005f));
        }

        [TestCase("trigger")]
        [TestCase("rigidbody")]
        [TestCase("ignoredRoot")]
        [TestCase("layer")]
        public void ExcludedSurface_DoesNotConstrainMotion(string exclusion)
        {
            BoxCollider surface = CreateBox(Vector3.zero, new Vector3(0.01f, 4f, 4f));
            if (exclusion == "trigger") surface.isTrigger = true;
            if (exclusion == "rigidbody") surface.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            if (exclusion == "ignoredRoot") SetField("ignoredRoot", surface.transform);
            if (exclusion == "layer") surface.gameObject.layer = 0;
            Vector3 target = new Vector3(0.15f, 0.2f, 0f);

            Assert.That(Vector3.Distance(Slide(new Vector3(-0.15f, 0f, 0f), target), target),
                Is.LessThan(0.0005f));
        }

        private Vector3 Slide(Vector3 start, Vector3 target)
        {
            Physics.SyncTransforms();
            return (Vector3)typeof(HandSurfaceLimiter)
                .GetMethod("SlideProbe", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_limiter, new object[] { Origin + start, Origin + target, Radius }) - Origin;
        }

        private BoxCollider CreateBox(Vector3 center, Vector3 size)
        {
            GameObject surface = CreateObject("Hand contact surface");
            surface.transform.position = Origin + center;
            surface.layer = SurfaceLayer;
            BoxCollider collider = surface.AddComponent<BoxCollider>();
            collider.size = size;
            return collider;
        }

        private GameObject CreateObject(string name)
        {
            var instance = new GameObject(name);
            _objects.Add(instance);
            return instance;
        }

        private void SetField(string field, object value)
        {
            typeof(HandSurfaceLimiter).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_limiter, value);
        }
    }
}
