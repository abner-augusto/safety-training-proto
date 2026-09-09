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
        private const string GroupSummaryPath = "Assets/_SafetyProto/Prefabs/UI/SessionReport_GroupSummary.prefab";
        private const string GroupLabelPath = "Assets/_SafetyProto/Prefabs/UI/SessionReport_GroupLabel.prefab";

        private const string ScoreValuePath = "Background/RIghtPanel/ScoreValue";

        /// <summary>Fixed size chosen so 0, the scenario maximum and 9999 all fit unwrapped.</summary>
        private const float ScoreValueFontSize = 72f;

        private const int PopupUILayer = 16;

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
        public void FinishScreen_KeepsOnlyDetailsAndRestartTargets()
        {
            // No scrollbar handle: the details list is dragged directly with the ray, so the
            // scroll view's own graphic is the whole scrolling surface.
            AssertRaycastTargets(FinishScreenPath, new[]
            {
                "MainPanel/FooterActions/DetailsToggleButton",
                "MainPanel/FooterActions/RestartButton",
                "DetailsPanel/TaskScrollView",
            });
        }

        [Test]
        public void FinishScreen_EveryControlTargetGraphicStaysRaycastable()
        {
            AssertControlsCanBeHit(FinishScreenPath);
        }

        [Test]
        public void FinishScreen_HasTwoPanelGeometry()
        {
            var root = Load(FinishScreenPath);
            var rootRect = root.GetComponent<RectTransform>();

            Assert.AreEqual(new Vector2(1440f, 810f), rootRect.rect.size,
                "The root rect must enclose both panels at the target two-panel geometry.");

            var mainPanel = Find(root, "MainPanel").GetComponent<RectTransform>();
            var detailsPanel = Find(root, "DetailsPanel").GetComponent<RectTransform>();

            AssertRectApprox(mainPanel, new Vector2(821f, 583f), new Vector2(-216f, 16f));
            AssertRectApprox(detailsPanel, new Vector2(418f, 486f), new Vector2(432f, 16f));

            Assert.AreEqual(0f, mainPanel.localEulerAngles.y, 0.01f,
                "The reference layout must not carry an angled panel: one flat surface backs both cards.");
            Assert.AreEqual(0f, detailsPanel.localEulerAngles.y, 0.01f,
                "The reference layout must not carry an angled panel: one flat surface backs both cards.");

            Assert.AreEqual(1, root.GetComponentsInChildren<Canvas>(true).Length,
                "Exactly one Canvas backs both cards.");
            Assert.AreEqual(1, root.GetComponentsInChildren<GraphicRaycaster>(true).Length,
                "Exactly one GraphicRaycaster backs both cards.");
            Assert.IsNotEmpty(ComponentTypeNames(root, "PointableCanvas"),
                "The root must keep its PointableCanvas so the ray/poke surfaces still route pointer events.");
            Assert.AreEqual(2, ComponentTypeNames(root, "PlaneSurface").Length,
                "Both the ray and poke interaction subtrees keep their PlaneSurface.");
        }

        [Test]
        public void FinishScreen_PanelsUseStockSlicedMaterial()
        {
            var root = Load(FinishScreenPath);
            var roundedBoxOwners = new HashSet<string>(ComponentTypeNames(root, "RoundedBoxUIProperties"));

            foreach (var name in new[] { "MainPanel", "DetailsPanel" })
            {
                var image = Find(root, name).GetComponent<Image>();
                Assert.IsNotNull(image, $"Expected an Image on {name}.");
                Assert.AreEqual(Image.Type.Sliced, image!.type, $"{name} must use a 9-sliced sprite.");
                // Graphic.material falls back to a non-null defaultMaterial even when unset;
                // the authored value lives in the serialized m_Material field instead.
                var matProp = new SerializedObject(image).FindProperty("m_Material");
                Assert.IsNull(matProp.objectReferenceValue,
                    $"{name} must use the stock UI material, not a custom one.");
                Assert.IsFalse(roundedBoxOwners.Contains(name),
                    $"{name} must not carry RoundedBoxUIProperties; the sliced sprite already supplies the corners.");
            }
        }

        [Test]
        public void FinishScreen_NoFinishScreenImageUsesRoundedBoxMaterial()
        {
            var root = Load(FinishScreenPath);

            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                var mat = image.material;
                if (mat == null) continue;
                Assert.IsFalse(mat.name.Contains("RoundedBoxUI"),
                    $"{PathOf(image.transform, root.transform)} still references {mat.name}; " +
                    "the Quest baseline is fill-rate bound and this material is the expensive one.");
            }
        }

        [Test]
        public void FinishScreen_AllReportPrefabsUsePopupUILayer()
        {
            AssertAllOnLayer(FinishScreenPath);
            AssertAllOnLayer(TaskReportRowPath);
            AssertAllOnLayer(GroupSummaryPath);
            AssertAllOnLayer(GroupLabelPath);
        }

        // taskManager, gateValidator and audioSource are cross-scene references a prefab
        // asset cannot serialize; the scene supplies them as documented essential overrides.
        private static readonly HashSet<string> SceneWiredFields = new HashSet<string>
        {
            "taskManager", "gateValidator", "audioSource",
        };

        [Test]
        public void FinishScreen_SessionReportUIBindingsAreAllAssigned()
        {
            var root = Load(FinishScreenPath);
            var behaviour = root.GetComponentsInChildren<MonoBehaviour>(true)
                .FirstOrDefault(b => b != null && b.GetType().Name == "SessionReportUI");

            Assert.IsNotNull(behaviour, "Expected a SessionReportUI component on the finish-screen prefab.");

            var so = new SerializedObject(behaviour);
            var prop = so.GetIterator();
            bool enterChildren = true;
            var unassigned = new List<string>();

            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (prop.name == "m_Script") continue;
                if (SceneWiredFields.Contains(prop.name)) continue;
                if (prop.objectReferenceValue == null)
                    unassigned.Add(prop.name);
            }

            CollectionAssert.IsEmpty(unassigned,
                $"SessionReportUI has unassigned object references: {string.Join(", ", unassigned)}.");
        }

        [Test]
        public void TaskReportRow_HasNoRaycastTargets()
        {
            AssertRaycastTargets(TaskReportRowPath, System.Array.Empty<string>());
        }

        [Test]
        public void TaskReportRow_HasScoreBadgeAndAdviceContainer()
        {
            var root = Load(TaskReportRowPath);

            Assert.IsNotNull(root.transform.Find("SummaryRow/ScoreBadge"),
                "Expected a ScoreBadge under SummaryRow.");
            Assert.IsNotNull(root.transform.Find("AdviceContainer"),
                "Expected an AdviceContainer sibling of SummaryRow.");
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

        private static void AssertRectApprox(RectTransform rect, Vector2 size, Vector2 anchoredPosition)
        {
            Assert.AreEqual(size.x, rect.rect.width, 4f, $"{rect.name} width drifted beyond the allowed TMP tolerance.");
            Assert.AreEqual(size.y, rect.rect.height, 4f, $"{rect.name} height drifted beyond the allowed TMP tolerance.");
            Assert.AreEqual(anchoredPosition.x, rect.anchoredPosition.x, 4f, $"{rect.name} X position drifted.");
            Assert.AreEqual(anchoredPosition.y, rect.anchoredPosition.y, 4f, $"{rect.name} Y position drifted.");
        }

        private static void AssertAllOnLayer(string assetPath)
        {
            var root = Load(assetPath);
            var offenders = root.GetComponentsInChildren<Transform>(true)
                .Where(t => t.gameObject.layer != PopupUILayer)
                .Select(t => PathOf(t, root.transform))
                .ToArray();

            CollectionAssert.IsEmpty(offenders,
                $"Every object in {assetPath} must be on the PopupUI layer (16). Offenders: {string.Join(", ", offenders)}.");
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
