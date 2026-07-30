// Assets/_SafetyProto/Tests/Editor/PhaseControllerAnchorTests.cs
//
// Unity EditMode only. Covers Step 2 of plans/026-evaluator-recenter-playspace.md:
// PhaseController.CurrentAnchor (IRecenterAnchorProvider) must return the pre-transition
// (canteiro) anchor before the phase transition executes, and the post-transition (andaime)
// anchor afterwards. Fields are set via reflection and Start() is deliberately never invoked —
// CurrentAnchor only reads already-serialized state, so exercising PhaseController's full Start()
// lifecycle (EventBus, TaskManager, popup wiring) would be unrelated setup cost for this case.
using System.Reflection;
using NUnit.Framework;
using SafetyProto.Runtime;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    public class PhaseControllerAnchorTests
    {
        [Test]
        public void CurrentAnchor_ReturnsCanteiroBeforeTransition_AndaimeAfter()
        {
            var go = new GameObject("PhaseControllerUnderTest");
            Transform canteiro = null;
            Transform andaime = null;
            try
            {
                var pc = go.AddComponent<PhaseController>();
                canteiro = new GameObject("Canteiro").transform;
                andaime = new GameObject("Andaime").transform;

                SetPrivateField(pc, "startPointCanteiro", canteiro);
                SetPrivateField(pc, "spawnPointAndaime", andaime);

                var provider = (IRecenterAnchorProvider)pc;

                Assert.AreSame(canteiro, provider.CurrentAnchor,
                    "before the transition, CurrentAnchor must be the canteiro start point");

                SetPrivateField(pc, "_transitionExecuted", true);

                Assert.AreSame(andaime, provider.CurrentAnchor,
                    "after the transition, CurrentAnchor must be the andaime spawn point");
            }
            finally
            {
                if (canteiro != null) Object.DestroyImmediate(canteiro.gameObject);
                if (andaime != null) Object.DestroyImmediate(andaime.gameObject);
                Object.DestroyImmediate(go);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(field, $"Expected private field '{fieldName}' on {target.GetType().Name}");
            field.SetValue(target, value);
        }
    }
}
