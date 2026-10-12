using System.IO;
using SafetyProto.Utils;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SafetyProto.Editor
{
    /// <summary>
    /// Stamps the git commit into <see cref="BuildCommit.AssetPath"/> so the player can report
    /// which code it was built from (the headset has no git). The file is removed after the build
    /// and is gitignored, so a failed build that leaves it behind never marks the tree dirty.
    /// </summary>
    internal sealed class BuildCommitStamp : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // Run before every other build processor: some (the Meta XR ones among them) rewrite
        // ProjectSettings and OVRBuildConfig for the duration of the build, which would read
        // as uncommitted changes.
        public int callbackOrder => int.MinValue;

        public void OnPreprocessBuild(BuildReport report)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
            // Query git before writing the stamp, so the stamp itself can't count as a change.
            string commit = BuildCommit.Describe(projectRoot);

            File.WriteAllText(Path.Combine(projectRoot, BuildCommit.AssetPath), commit);
            AssetDatabase.ImportAsset(BuildCommit.AssetPath, ImportAssetOptions.ForceSynchronousImport);

            if (string.IsNullOrEmpty(commit))
                Debug.LogWarning("[BuildCommitStamp] git unavailable; the build will not record its commit.");
            else if (commit.EndsWith("-dirty"))
                Debug.LogWarning($"[BuildCommitStamp] Build stamped with commit {commit}; uncommitted changes:\n" +
                                 BuildCommit.WorkingTreeChanges(projectRoot));
            else
                Debug.Log($"[BuildCommitStamp] Build stamped with commit {commit}.");
        }

        public void OnPostprocessBuild(BuildReport report) =>
            AssetDatabase.DeleteAsset(BuildCommit.AssetPath);
    }
}
