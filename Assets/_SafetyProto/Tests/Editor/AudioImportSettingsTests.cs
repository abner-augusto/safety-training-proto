using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Asset-contract fixture for plan 038 Phase C: click1.mp3 drives a fully
    /// spatial AudioSource, so the second decoded channel is discarded at
    /// playback time. This pins the importer to mono so the asset does not
    /// silently regress back to stereo.
    /// </summary>
    public class AudioImportSettingsTests
    {
        private const string ClipPath = "Assets/_SafetyProto/Art/SFX/UI/click1.mp3";

        [Test]
        public void Click1_Importer_IsForcedToMono()
        {
            var importer = AssetImporter.GetAtPath(ClipPath) as AudioImporter;

            Assert.IsNotNull(importer, $"Expected an AudioImporter at {ClipPath}.");
            Assert.IsTrue(importer!.forceToMono, "click1.mp3 should be imported with Force To Mono enabled.");
        }

        [Test]
        public void Click1_Clip_HasOneChannel()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipPath);

            Assert.IsNotNull(clip, $"Expected an AudioClip at {ClipPath}.");
            Assert.AreEqual(1, clip!.channels, "click1.mp3 should decode to a single channel.");
        }
    }
}
