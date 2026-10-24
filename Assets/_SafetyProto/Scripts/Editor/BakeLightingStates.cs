using System.IO;
using System.Linq;
using SafetyProto.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// Bakes the active scene twice for <see cref="BakedLightingSwitch"/>: first with its lights
    /// disabled, copying those lightmaps and probes to a <c>LightsOff</c> folder, then with them
    /// enabled, leaving that bake as the scene's own. A plain Generate Lighting only refreshes
    /// the lights-on set and leaves the off set on a stale layout.
    /// </summary>
    public static class BakeLightingStates
    {
        private const string OffFolderName = "LightsOff";

        private static BakedLightingSwitch _switch;
        private static string _layoutAfterOffBake;

        [MenuItem("SafetyProto/Bake Lighting States")]
        public static void Bake()
        {
            if (Lightmapping.isRunning || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[BakeLightingStates] A bake or Play Mode is already running.");
                return;
            }

            _switch = Object.FindFirstObjectByType<BakedLightingSwitch>(FindObjectsInactive.Include);
            if (_switch == null)
            {
                Debug.LogWarning("[BakeLightingStates] The open scene has no BakedLightingSwitch.");
                return;
            }

            SetLightsEnabled(false);
            Lightmapping.bakeCompleted += OnOffBakeCompleted;
            if (!Lightmapping.BakeAsync())
            {
                Lightmapping.bakeCompleted -= OnOffBakeCompleted;
                SetLightsEnabled(true);
                Debug.LogWarning("[BakeLightingStates] Unity refused to start the lights-off bake.");
                return;
            }
            Debug.Log("[BakeLightingStates] Baking with the lights off (1/2).");
        }

        private static void OnOffBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnOffBakeCompleted;
            CopyOffSet();
            _layoutAfterOffBake = DescribeLayout();

            SetLightsEnabled(true);
            Lightmapping.bakeCompleted += OnOnBakeCompleted;
            if (!Lightmapping.BakeAsync())
            {
                Lightmapping.bakeCompleted -= OnOnBakeCompleted;
                Debug.LogWarning("[BakeLightingStates] Unity refused to start the lights-on bake.");
                return;
            }
            Debug.Log("[BakeLightingStates] Baking with the lights on (2/2).");
        }

        private static void OnOnBakeCompleted()
        {
            Lightmapping.bakeCompleted -= OnOnBakeCompleted;
            // The switch swaps textures by lightmap index, so both bakes must pack every
            // renderer identically. Packing is deterministic for an unchanged scene.
            if (DescribeLayout() != _layoutAfterOffBake)
                Debug.LogError("[BakeLightingStates] The two bakes packed the lightmaps differently; the " +
                               "lights-off set will not line up. Bake again without editing the scene in between.");

            EditorSceneManager.MarkSceneDirty(_switch.gameObject.scene);
            EditorSceneManager.SaveScene(_switch.gameObject.scene);
            Debug.Log("[BakeLightingStates] Done: both lighting states baked and assigned.");
        }

        private static void SetLightsEnabled(bool enabled)
        {
            foreach (var light in _switch.SwitchedLights.Where(l => l != null))
            {
                // A light only contributes to the bake when both it and its GameObject are on.
                light.gameObject.SetActive(true);
                light.enabled = enabled;
                EditorUtility.SetDirty(light);
            }
        }

        private static void CopyOffSet()
        {
            var scene = _switch.gameObject.scene;
            var sceneDir = Path.Combine(Path.GetDirectoryName(scene.path), scene.name).Replace('\\', '/');
            var offDir = $"{sceneDir}/{OffFolderName}";
            if (AssetDatabase.IsValidFolder(offDir))
                AssetDatabase.DeleteAsset(offDir);
            AssetDatabase.CreateFolder(sceneDir, OffFolderName);

            var lightmaps = LightmapSettings.lightmaps;
            var colors = new Texture2D[lightmaps.Length];
            var dirs = new Texture2D[lightmaps.Length];
            for (int i = 0; i < lightmaps.Length; i++)
            {
                colors[i] = CopyTexture(lightmaps[i].lightmapColor, offDir);
                dirs[i] = CopyTexture(lightmaps[i].lightmapDir, offDir);
            }

            LightProbes probes = null;
            if (LightmapSettings.lightProbes != null)
            {
                probes = Object.Instantiate(LightmapSettings.lightProbes);
                AssetDatabase.CreateAsset(probes, $"{offDir}/LightProbes.asset");
            }
            AssetDatabase.SaveAssets();

            var so = new SerializedObject(_switch);
            AssignArray(so.FindProperty("offLightmapColors"), colors);
            AssignArray(so.FindProperty("offLightmapDirs"), dirs);
            so.FindProperty("offLightProbes").objectReferenceValue = probes;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Texture2D CopyTexture(Texture2D source, string folder)
        {
            if (source == null)
                return null;
            var sourcePath = AssetDatabase.GetAssetPath(source);
            var targetPath = $"{folder}/{Path.GetFileName(sourcePath)}";
            AssetDatabase.CopyAsset(sourcePath, targetPath);
            return AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
        }

        private static void AssignArray(SerializedProperty property, Object[] values)
        {
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        private static string DescribeLayout()
        {
            return string.Join(";", Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.InstanceID)
                .Where(r => r.lightmapIndex >= 0)
                .Select(r => $"{r.lightmapIndex}:{r.lightmapScaleOffset}"));
        }
    }
}
