using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Pins which world-space UI graphics may take part in pointer raycasting.
    /// Every enabled <see cref="Graphic.raycastTarget"/> is a candidate the Meta XR
    /// pointer tests each frame, and a decorative child that keeps the flag also
    /// intercepts events aimed at the control behind it.
    ///
    /// Assertions name exact hierarchy paths rather than counts: a count-only
    /// contract still passes when a required control loses its target graphic and
    /// an unrelated image gains one.
    /// </summary>
    public class UiPrefabPerformanceTests
    {
        private const string ChecklistPath = "Assets/_SafetyProto/Prefabs/UI/TaskChecklistPanel.prefab";
        private const string PopupPath = "Assets/_SafetyProto/Prefabs/UI/PopupCanvas_new.prefab";
        private const string FinishScreenPath = "Assets/_SafetyProto/Prefabs/UI/FinishScreenPanel.prefab";
        private const string TaskReportRowPath = "Assets/_SafetyProto/Prefabs/UI/TaskReport_Row.prefab";
        private const string ImprovementRowPath = "Assets/_SafetyProto/Prefabs/UI/Improvement_Row.prefab";

        private const string ScoreValuePath = "Background/RIghtPanel/ScoreValue";

        /// <summary>Fixed size chosen so 0, the scenario maximum and 9999 all fit unwrapped.</summary>
        private const float ScoreValueFontSize = 72f;

        [Test]
        public void Checklist_HasNoRaycastTargets()
        {
            AssertRaycastTargets(ChecklistPath, System.Array.Empty<string>());
        }

        [Test]
        public void Checklist_HasNoPointerModules()
        {
            var root = Load(ChecklistPath);

            Assert.IsNull(root.GetComponentInChildren<GraphicRaycaster>(true),
                "The checklist is display-only; a GraphicRaycaster puts it back into pointer processing.");
            Assert.IsEmpty(ComponentTypeNames(root, "PointableCanvas"),
                "The checklist has no Selectable, so a PointableCanvas forwards pointer events nothing consumes.");
            Assert.IsEmpty(ComponentTypeNames(root, "PlaneSurface"),
                "PlaneSurface only exists to give the removed PointableCanvas a hit surface.");
        }

        [Test]
        public void Checklist_CanvasGroupDoesNotBlockRaycasts()
        {
            var group = Load(ChecklistPath).GetComponent<CanvasGroup>();

            Assert.IsNotNull(group, "SessionEndFader drives this CanvasGroup's alpha; do not remove it.");
            Assert.IsFalse(group!.blocksRaycasts,
                "A blocking CanvasGroup makes the whole panel a pointer occluder even with every raycastTarget off.");
        }

        [Test]
        public void Popup_KeepsOnlyControlTargetGraphics()
        {
            AssertRaycastTargets(PopupPath, new[]
            {
                "Unity Canvas/Background/ActionButtonRoot/Button/Image",
                "Unity Canvas/Background/BtnCloseShadow/BtnClose",
                "Unity Canvas/Background/SkipButtonRoot/Button/Image",
            });
        }

        [Test]
        public void Popup_EveryControlTargetGraphicStaysRaycastable()
        {
            AssertControlsCanBeHit(PopupPath);
        }

        [Test]
        public void FinishScreen_KeepsOnlyScrollAndRestartTargets()
        {
            AssertRaycastTargets(FinishScreenPath, new[]
            {
                "Background/Footer/ImprovementList/ImprovementListScrollView",
                "Background/Footer/ImprovementList/ImprovementListScrollView/Scrollbar Vertical/Sliding Area/Handle",
                "Background/RestartButton",
                "Background/TaskBreakdownSection/TaskScrollView",
                "Background/TaskBreakdownSection/TaskScrollView/Scrollbar Vertical/Sliding Area/Handle",
            });
        }

        [Test]
        public void FinishScreen_EveryControlTargetGraphicStaysRaycastable()
        {
            AssertControlsCanBeHit(FinishScreenPath);
        }

        [Test]
        public void TaskReportRow_HasNoRaycastTargets()
        {
            AssertRaycastTargets(TaskReportRowPath, System.Array.Empty<string>());
        }

        [Test]
        public void ImprovementRow_HasNoRaycastTargets()
        {
            AssertRaycastTargets(ImprovementRowPath, System.Array.Empty<string>());
        }

        [Test]
        public void ScoreValue_UsesAFixedFontSize()
        {
            var score = Find(Load(ChecklistPath), ScoreValuePath);
            var text = score.GetComponent<TextMeshProUGUI>();

            Assert.IsNotNull(text, $"Expected a TextMeshProUGUI at {ScoreValuePath}.");
            Assert.IsFalse(text!.enableAutoSizing,
                "ScoreHUD writes a short integer into a fixed-width panel, so the AutoSize binary search " +
                "runs on every score change for a size that never needs to move.");
            Assert.AreEqual(ScoreValueFontSize, text.fontSize, 0.01f);
            Assert.AreEqual(TextWrappingModes.NoWrap, text.textWrappingMode,
                "Wrapping would let a four-digit score break across two lines once AutoSize can no longer shrink it.");
        }

        private static void AssertRaycastTargets(string assetPath, IReadOnlyList<string> expected)
        {
            var root = Load(assetPath);

            var actual = root.GetComponentsInChildren<Graphic>(true)
                .Where(g => g.raycastTarget)
                .Select(g => PathOf(g.transform, root.transform))
                .OrderBy(p => p, System.StringComparer.Ordinal)
                .ToArray();

            CollectionAssert.AreEqual(expected.OrderBy(p => p, System.StringComparer.Ordinal).ToArray(), actual,
                $"Raycast targets in {assetPath} drifted from the pinned set.");
        }

        private static void AssertControlsCanBeHit(string assetPath)
        {
            var root = Load(assetPath);

            foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                var owner = PathOf(selectable.transform, root.transform);
                var target = selectable.targetGraphic;

                Assert.IsNotNull(target, $"{owner} has no target graphic, so nothing routes a pointer to it.");
                Assert.IsTrue(target!.raycastTarget,
                    $"{owner} points at {PathOf(target.transform, root.transform)}, which is no longer raycastable.");
            }
        }

        private static GameObject Load(string assetPath)
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            Assert.IsNotNull(root, $"Expected a prefab at {assetPath}.");
            return root!;
        }

        private static Transform Find(GameObject root, string path)
        {
            var found = root.transform.Find(path);
            Assert.IsNotNull(found, $"Expected {path} under {root.name}.");
            return found!;
        }

        private static string PathOf(Transform target, Transform root) =>
            AnimationUtility.CalculateTransformPath(target, root);

        private static string[] ComponentTypeNames(GameObject root, string typeName) =>
            root.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && c.GetType().Name == typeName)
                .Select(c => PathOf(c.transform, root.transform))
                .ToArray();
    }
}
