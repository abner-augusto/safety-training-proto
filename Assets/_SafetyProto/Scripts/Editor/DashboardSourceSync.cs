#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// Mirrors the dashboard source files in <c>Tools/DashboardSrc/</c> into
    /// <c>Assets/_SafetyProto/Resources/Dashboard/*.txt</c> (Unity only imports
    /// .txt as TextAsset, which MiniHttpServer serves). Runs automatically
    /// before entering Play mode and before every build; also available via
    /// the SafetyProto menu. Generated files carry a DO-NOT-EDIT banner.
    /// </summary>
    public static class DashboardSourceSync
    {
        private const string SourceDir = "Tools/DashboardSrc";
        private const string TargetDir = "Assets/_SafetyProto/Resources/Dashboard";

        // source file -> (target file, banner comment)
        private static readonly (string source, string target, string banner)[] Files =
        {
            ("index.html", "index.txt", "<!-- GENERATED FILE — DO NOT EDIT. Source: Tools/DashboardSrc/index.html (menu: SafetyProto → Sync Dashboard Source) -->"),
            ("style.css",  "style.txt", "/* GENERATED FILE — DO NOT EDIT. Source: Tools/DashboardSrc/style.css (menu: SafetyProto → Sync Dashboard Source) */"),
            ("app.js",     "app.txt",   "// GENERATED FILE — DO NOT EDIT. Source: Tools/DashboardSrc/app.js (menu: SafetyProto → Sync Dashboard Source)"),
        };

        [InitializeOnLoadMethod]
        private static void RegisterPlayModeHook()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    Sync();
            };
        }

        [MenuItem("SafetyProto/Sync Dashboard Source")]
        public static void SyncFromMenu()
        {
            Sync(verbose: true);
        }

        public static void Sync(bool verbose = false)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            var sourceDir = Path.Combine(projectRoot, SourceDir);
            if (!Directory.Exists(sourceDir))
            {
                Debug.LogWarning($"[DashboardSourceSync] '{SourceDir}' not found; dashboard sync skipped.");
                return;
            }

            var updated = new List<string>();
            foreach (var (source, target, banner) in Files)
            {
                var sourcePath = Path.Combine(sourceDir, source);
                if (!File.Exists(sourcePath))
                {
                    Debug.LogWarning($"[DashboardSourceSync] Missing source '{SourceDir}/{source}'; target left untouched.");
                    continue;
                }

                var content = banner + "\n" + File.ReadAllText(sourcePath);
                var targetPath = Path.Combine(projectRoot, TargetDir, target);
                if (File.Exists(targetPath) && File.ReadAllText(targetPath) == content)
                    continue;

                File.WriteAllText(targetPath, content, new UTF8Encoding(false));
                updated.Add(target);
            }

            // Mirror Tools/DashboardSrc/js/*.js -> Resources/Dashboard/js/<name>.txt.
            // Resources/Dashboard/js/ is 100% generated, so deletions are mirrored too:
            // an orphaned .txt with no source .js counterpart is deleted (with its .meta).
            var deleted = new List<string>();
            var jsSourceDir = Path.Combine(sourceDir, "js");
            if (Directory.Exists(jsSourceDir))
            {
                var jsTargetDir = Path.Combine(projectRoot, TargetDir, "js");
                Directory.CreateDirectory(jsTargetDir);

                var sourceNames = new HashSet<string>();
                foreach (var jsPath in Directory.GetFiles(jsSourceDir, "*.js"))
                {
                    var name = Path.GetFileNameWithoutExtension(jsPath);
                    sourceNames.Add(name);

                    var banner = $"// GENERATED FILE — DO NOT EDIT. Source: Tools/DashboardSrc/js/{name}.js (menu: SafetyProto → Sync Dashboard Source)";
                    var content = banner + "\n" + File.ReadAllText(jsPath);
                    var targetPath = Path.Combine(jsTargetDir, name + ".txt");
                    if (File.Exists(targetPath) && File.ReadAllText(targetPath) == content)
                        continue;

                    File.WriteAllText(targetPath, content, new UTF8Encoding(false));
                    updated.Add($"js/{name}.txt");
                }

                foreach (var txtPath in Directory.GetFiles(jsTargetDir, "*.txt"))
                {
                    var name = Path.GetFileNameWithoutExtension(txtPath);
                    if (sourceNames.Contains(name))
                        continue;

                    AssetDatabase.DeleteAsset($"{TargetDir}/js/{name}.txt");
                    deleted.Add($"js/{name}.txt");
                }
            }

            if (updated.Count > 0 || deleted.Count > 0)
                AssetDatabase.Refresh();

            if (updated.Count > 0)
                Debug.Log($"[DashboardSourceSync] Updated {updated.Count} file(s): {string.Join(", ", updated)}");
            if (deleted.Count > 0)
                Debug.Log($"[DashboardSourceSync] Deleted {deleted.Count} orphan file(s): {string.Join(", ", deleted)}");
            if (updated.Count == 0 && deleted.Count == 0 && verbose)
                Debug.Log("[DashboardSourceSync] All dashboard files already in sync (0 files updated).");
        }

        /// <summary>Sync before every player build so the APK never ships a stale dashboard.</summary>
        private class PreBuildSync : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;
            public void OnPreprocessBuild(BuildReport report) => Sync();
        }
    }
}
