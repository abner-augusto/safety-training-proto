using System.Reflection;
using NUnit.Framework;
using Oculus.Interaction.HandGrab;
using SafetyProto.Runtime.PPE;
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

        private static void Invoke(object target, string method) =>
            target.GetType().GetMethod(method, Private).Invoke(target, null);
    }
}
