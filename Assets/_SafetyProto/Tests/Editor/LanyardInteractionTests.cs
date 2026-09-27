using System.Reflection;
using NUnit.Framework;
using Oculus.Interaction.HandGrab;
using SafetyProto.Runtime.PPE;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class LanyardInteractionTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void GrabLock_DisablesEveryPoseAndRestoresOnlyPreviouslyEnabledPoses()
        {
            var root = new GameObject("Equipment");
            root.SetActive(false);
            try
            {
                root.AddComponent<BoxCollider>();
                var item = root.AddComponent<PPESnapItem>();
                var poses = new HandGrabInteractable[3];
                for (int i = 0; i < poses.Length; i++)
                {
                    var child = new GameObject("Pose");
                    child.transform.SetParent(root.transform);
                    poses[i] = child.AddComponent<HandGrabInteractable>();
                }
                poses[2].enabled = false;
                var tip = new GameObject("IndependentTip");
                tip.transform.SetParent(root.transform);
                tip.AddComponent<Rigidbody>();
                var tipGrab = tip.AddComponent<HandGrabInteractable>();
                Invoke(item, "Awake");

                item.SetGrabEnabled(false);
                item.SetGrabEnabled(false);
                foreach (var pose in poses) Assert.That(pose.enabled, Is.False);
                Assert.That(tipGrab.enabled, Is.True);

                item.SetGrabEnabled(true);
                Assert.That(poses[0].enabled && poses[1].enabled, Is.True);
                Assert.That(poses[2].enabled, Is.False);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void Rope_ExcludesSegmentsFromDisabledCapsuleAfterBodyMoves(int direction)
        {
            var root = new GameObject("RopeTest");
            try
            {
                var ropeObject = new GameObject("Rope");
                ropeObject.transform.SetParent(root.transform);
                var rope = ropeObject.AddComponent<VerletLanyard>();
                var body = new GameObject("Body");
                body.transform.SetParent(root.transform);
                var capsule = body.AddComponent<CapsuleCollider>();
                capsule.direction = direction;
                capsule.radius = 0.15f;
                capsule.height = 0.6f;
                capsule.enabled = false;
                body.transform.rotation = Quaternion.Euler(10f, 25f, 5f);
                body.transform.localScale = new Vector3(1.1f, 0.9f, 1.2f);
                var start = new GameObject("Start");
                start.transform.SetParent(root.transform);
                start.transform.position = new Vector3(-0.6f, 0f, 0f);
                var end = new GameObject("End");
                end.transform.SetParent(root.transform);
                end.transform.position = new Vector3(0.6f, 0f, 0f);
                var serialized = new SerializedObject(rope);
                serialized.FindProperty("bodyCollider").objectReferenceValue = capsule;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Invoke(rope, "Awake");
                rope.SetStartAnchor(start.transform);
                rope.SetEndAnchor(end.transform);
                rope.RopeLength = 1.6f;
                rope.ResetSimulation();

                for (int frame = 0; frame < 120; frame++)
                {
                    Invoke(rope, "FixedUpdate");
                    body.transform.position = new Vector3(0f, Mathf.Sin(frame * 0.07f) * 0.04f, 0f);
                    Invoke(rope, "LateUpdate");
                    AssertOutside(ropeObject.GetComponent<LineRenderer>(), capsule);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void AssertOutside(LineRenderer line, CapsuleCollider capsule)
        {
            Vector3 scale = capsule.transform.lossyScale;
            int direction = capsule.direction;
            float radius = capsule.radius * Mathf.Max(scale[(direction + 1) % 3], scale[(direction + 2) % 3]);
            float half = Mathf.Max(0f, capsule.height * scale[direction] * 0.5f - radius);
            Vector3 axis = direction == 0 ? Vector3.right : direction == 1 ? Vector3.up : Vector3.forward;
            axis = capsule.transform.TransformDirection(axis);
            Vector3 center = capsule.transform.TransformPoint(capsule.center);
            for (int i = 0; i < line.positionCount - 1; i++)
            {
                for (int sample = 0; sample <= 10; sample++)
                {
                    Vector3 point = Vector3.Lerp(line.GetPosition(i), line.GetPosition(i + 1), sample / 10f);
                    Vector3 relative = point - center;
                    float distance = (relative - axis * Mathf.Clamp(Vector3.Dot(relative, axis), -half, half)).magnitude;
                    Assert.That(distance, Is.GreaterThanOrEqualTo(radius - 0.001f), "A rendered segment entered the body.");
                }
            }
        }

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, Private).Invoke(target, null);
    }
}
