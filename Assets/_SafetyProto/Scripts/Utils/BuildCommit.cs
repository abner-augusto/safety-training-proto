using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace SafetyProto.Utils
{
    /// <summary>
    /// The git commit the running code was built from, e.g. "35c7f99" or "35c7f99-dirty" when
    /// the working tree had uncommitted changes. A player reads the value stamped into
    /// <c>Resources/build_commit.txt</c> at build time; the Editor asks git directly. Empty when
    /// neither source is available.
    /// </summary>
    public static class BuildCommit
    {
        /// <summary>Resources path (no extension) of the file the build stamps.</summary>
        public const string ResourceName = "build_commit";

        /// <summary>Asset path of the stamped file, relative to the project root.</summary>
        public const string AssetPath = "Assets/_SafetyProto/Resources/" + ResourceName + ".txt";

        private static string _cached;

        public static string Current
        {
            get
            {
                if (_cached != null) return _cached;
#if UNITY_EDITOR
                _cached = Describe(Directory.GetParent(Application.dataPath)!.FullName);
#else
                var asset = Resources.Load<TextAsset>(ResourceName);
                _cached = asset != null ? asset.text.Trim() : string.Empty;
#endif
                return _cached;
            }
        }

#if UNITY_EDITOR
        /// <summary>Short HEAD hash of the repository at <paramref name="repoRoot"/>, suffixed
        /// "-dirty" when <c>git status</c> reports changes. Empty when git is unavailable.</summary>
        public static string Describe(string repoRoot)
        {
            string hash = RunGit(repoRoot, "rev-parse --short HEAD");
            if (string.IsNullOrEmpty(hash)) return string.Empty;

            string status = RunGit(repoRoot, "status --porcelain");
            return string.IsNullOrEmpty(status) ? hash : hash + "-dirty";
        }

        private static string RunGit(string repoRoot, string arguments)
        {
            try
            {
                var info = new ProcessStartInfo("git", arguments)
                {
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var process = Process.Start(info);
                if (process == null) return string.Empty;
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);
                return process.ExitCode == 0 ? output.Trim() : string.Empty;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
#endif

        // Domain reload is disabled on entering Play Mode, so a cached hash would survive a commit.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _cached = null;
    }
}
