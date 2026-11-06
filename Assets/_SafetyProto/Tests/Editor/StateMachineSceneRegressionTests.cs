using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime;
using SafetyProto.Runtime.Analytics;
using SafetyProto.Runtime.PPE;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Scene-side state transitions that must stay unreachable. Unity-only: they touch
    /// MonoBehaviours, unlike <see cref="StateMachineRegressionTests"/>.
    /// </summary>
    public class StateMachineSceneRegressionTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        // Re-grabbing a locked carabiner unhooks it and must announce that, or the fall
        // controller keeps gravity suspended while the participant walks around unanchored.
        [Test]
        public void Unlock_WhileTheCarabinerIsHeld_AnnouncesTheUnlock()
        {
            var go = new GameObject("LanyardTip");
            try
            {
                var lanyard = go.AddComponent<RetractableLanyardController>();
                typeof(RetractableLanyardController).GetField("state", Private)!
                    .SetValue(lanyard, RetractableLanyardController.LanyardState.Locked);
                typeof(RetractableLanyardController).GetField("_isGrabbed", Private)!.SetValue(lanyard, true);
                int unlocks = 0;
                lanyard.onLanyardUnlocked.AddListener(() => unlocks++);

                lanyard.Unlock();

                Assert.AreEqual(1, unlocks);
            }
            finally { Object.DestroyImmediate(go); }
        }

        // A scene reload in the middle of a recenter kills the coroutine; destroying the service
        // must still lift the process-wide pose gate, or the dashboard viewport stays frozen.
        [Test]
        public void RecenterDestroyedMidSequence_LiftsThePoseGate()
        {
            var go = new GameObject("Recenter");
            var anchor = new GameObject("Anchor");
            try
            {
                // The gate is process-wide: another fixture may have left it raised.
                DashboardGate.PoseBroadcastSuspended = false;
                var service = go.AddComponent<RecenterService>();
                var sequence = service.RecenterTo(anchor.transform,
                    new RecenterOptions { SuspendPoseBroadcast = true, UseGroundProbe = true, HoldBlackDuration = 1f });
                while (!DashboardGate.PoseBroadcastSuspended && sequence.MoveNext()) { }
                Assert.IsTrue(service.IsBusy && DashboardGate.PoseBroadcastSuspended,
                    "precondition: the sequence is in flight and holds the gate");

                typeof(RecenterService).GetMethod("OnDestroy", Private)!.Invoke(service, null);

                Assert.IsFalse(DashboardGate.PoseBroadcastSuspended);
            }
            finally
            {
                DashboardGate.PoseBroadcastSuspended = false;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(anchor);
            }
        }
    }
}
