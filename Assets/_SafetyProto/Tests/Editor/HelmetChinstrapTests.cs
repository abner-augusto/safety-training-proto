using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime.PPE;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class HelmetChinstrapTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _helmet;
        private GameObject _table;
        private HelmetChinstrap _strap;
        private HelmetChinstrap.Strap[] _chains;

        [SetUp]
        public void SetUp()
        {
            _helmet = PrefabUtility.LoadPrefabContents("Assets/_SafetyProto/Prefabs/PPE/Safety_Helmet.prefab");
            _helmet.transform.position = new Vector3(100f, 10f, 100f);
            _strap = _helmet.GetComponent<HelmetChinstrap>();
            Assert.That(_strap, Is.Not.Null);
            _chains = (HelmetChinstrap.Strap[])typeof(HelmetChinstrap).GetField("straps", Private).GetValue(_strap);
            _table = new GameObject("ChinstrapTestTable");
            _table.transform.position = new Vector3(100f, 9.881f, 100f);
            _table.AddComponent<BoxCollider>().size = new Vector3(2f, 0.1f, 2f);
            Physics.SyncTransforms();
            Invoke("Awake");
        }

        [TearDown]
        public void TearDown()
        {
            if (_strap != null) Invoke("OnDestroy");
            if (_helmet != null) PrefabUtility.UnloadPrefabContents(_helmet);
            if (_table != null) Object.DestroyImmediate(_table);
        }

        [Test]
        public void LooseStraps_SettleAboveTableWithoutStretching()
        {
            Step(180);
            foreach (var chain in _chains)
            {
                for (int i = 1; i < chain.bones.Length; i++)
                {
                    Assert.That(chain.bones[i].position.y, Is.GreaterThanOrEqualTo(9.940f), "Tape penetrated the tabletop.");
                    float rest = Vector3.Distance(chain.fittedPositions[i - 1], chain.fittedPositions[i]);
                    Assert.That(Vector3.Distance(chain.bones[i - 1].position, chain.bones[i].position),
                        Is.LessThan(rest * 1.3f), "A contact stretched the tape excessively.");
                }
            }
            var renderer = _helmet.GetComponentInChildren<SkinnedMeshRenderer>();
            var baked = new Mesh();
            try
            {
                // Compensate for the FBX renderer's unit-conversion scale before TransformPoint.
                renderer.BakeMesh(baked, true);
                foreach (var vertex in baked.vertices)
                    Assert.That(renderer.transform.TransformPoint(vertex).y, Is.GreaterThanOrEqualTo(9.931f),
                        "Rendered tape or buckle penetrated the table.");
            }
            finally { Object.DestroyImmediate(baked); }
        }

        [Test]
        public void AnimatedReturn_CarriesTheTapeAndClearsVelocityAtArrival()
        {
            var home = _helmet.GetComponent<SafetyProto.Runtime.Feedback.ReturnObjectHome>();
            var returning = home.GetType().GetField("_returning", Private);
            Step(20);
            returning.SetValue(home, true);
            for (int i = 0; i < 8; i++)
            {
                _helmet.transform.position += new Vector3(0.12f, 0.04f, 0f);
                Step(1);
                foreach (var chain in _chains)
                    foreach (var bone in chain.bones)
                        Assert.That(Vector3.Distance(bone.position, _helmet.transform.position), Is.LessThan(0.4f));
            }
            returning.SetValue(home, false);
            Step(1);
            foreach (var chain in _chains)
                foreach (var bone in chain.bones)
                    Assert.That(Vector3.Distance(bone.position, _helmet.transform.position), Is.LessThan(0.4f));
        }

        [Test]
        public void Teleport_DiscardsVelocityAndRebuildsNearHelmet()
        {
            Step(20);
            _helmet.transform.position += new Vector3(3f, 2f, -1f);
            _helmet.transform.rotation = Quaternion.Euler(25f, 140f, 15f);
            Step(2);
            foreach (var chain in _chains)
                foreach (var bone in chain.bones)
                    Assert.That(Vector3.Distance(bone.position, _helmet.transform.position), Is.LessThan(0.4f));
        }

        [Test]
        public void SegmentContact_DetectsThinObstacleBetweenEndpoints()
        {
            var type = typeof(HelmetChinstrap).Assembly.GetType("SafetyProto.Runtime.PPE.ChinstrapCollision");
            object collision = typeof(HelmetChinstrap).GetField("_collision", Private).GetValue(_strap);
            _table.transform.position = new Vector3(100f, 11f, 100f);
            _table.GetComponent<BoxCollider>().size = new Vector3(0.005f, 0.2f, 0.2f);
            Physics.SyncTransforms();
            object[] args = { new Vector3(99.95f, 11f, 100f), new Vector3(100.05f, 11f, 100f), false };
            Assert.That((bool)type.GetMethod("Resolve").Invoke(collision, args), Is.True,
                "Both endpoints are outside the obstacle, but the segment crosses it.");
            Assert.That((Vector3)args[0], Is.Not.EqualTo(new Vector3(99.95f, 11f, 100f)));
        }

        [Test]
        public void MountingBone_FollowsHelmetWithoutPhysicsOrContactRotation()
        {
            Step(180);
            for (int frame = 0; frame < 12; frame++)
            {
                _helmet.transform.position += new Vector3(0.001f, 0.002f, 0f);
                _helmet.transform.rotation = Quaternion.Euler(frame, frame * 2f, -frame);
                Invoke("LateUpdate");
                foreach (var chain in _chains)
                {
                    Assert.That(Vector3.Distance(chain.bones[0].position,
                        _helmet.transform.TransformPoint(chain.fittedPositions[0])), Is.LessThan(0.00001f));
                    Assert.That(Quaternion.Angle(chain.bones[0].rotation,
                        _helmet.transform.rotation * chain.fittedRotations[0]), Is.LessThan(0.01f));
                }
            }
        }

        [Test]
        public void WornStraps_HideImmediatelyAndResumeOnRemoval()
        {
            var slotObject = new GameObject("ChinstrapTestSlot");
            try
            {
                slotObject.AddComponent<BoxCollider>();
                var slot = slotObject.AddComponent<PPESnapSlot>();
                var snap = _helmet.GetComponent<PPESnapItem>();
                typeof(PPESnapItem).GetField("_currentSlot", Private).SetValue(snap, slot);
                typeof(PPESnapItem).GetField("_isSnapped", Private).SetValue(snap, true);
                _helmet.transform.SetPositionAndRotation(new Vector3(100f, 11f, 100f), Quaternion.Euler(10f, 35f, 0f));
                Invoke("LateUpdate");
                Assert.That(_strap.IsFitted, Is.True);
                var renderer = _helmet.GetComponentInChildren<SkinnedMeshRenderer>();
                Assert.That(renderer.enabled, Is.False);
                Step(2);
                Assert.That(renderer.enabled, Is.False);
                typeof(PPESnapItem).GetField("_isSnapped", Private).SetValue(snap, false);
                Invoke("LateUpdate");
                Assert.That(renderer.enabled, Is.True);
                foreach (var chain in _chains)
                    foreach (var bone in chain.bones)
                        Assert.That(Vector3.Distance(bone.position, _helmet.transform.position), Is.LessThan(0.4f));
                Step(20);
                Assert.That(_strap.IsFitted, Is.False);
                Assert.That(Vector3.Distance(_chains[0].bones[12].position,
                    _helmet.transform.TransformPoint(_chains[0].fittedPositions[12])), Is.GreaterThan(0.02f));
            }
            finally { Object.DestroyImmediate(slotObject); }
        }

        private void Step(int count)
        {
            for (int i = 0; i < count; i++) Invoke("FixedUpdate");
            Invoke("LateUpdate");
        }

        private void Invoke(string method) => typeof(HelmetChinstrap).GetMethod(method, Private).Invoke(_strap, null);
    }
}
