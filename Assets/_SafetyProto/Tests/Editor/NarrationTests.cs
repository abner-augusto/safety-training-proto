using NUnit.Framework;
using SafetyProto.Domain.Feedback;
using SafetyProto.Runtime.Feedback;
using SafetyProto.Runtime.Interaction;
using UnityEngine;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers how a narrated surface finds its clip (by scenario id or tip index, never by popup
    /// text), how a narrated warning's auto-close makes room for its line, and the info boards'
    /// view-cone rule.
    /// </summary>
    public class NarrationTests
    {
        private AudioClip _chinelo;
        private AudioClip _cinto;

        [SetUp]
        public void SetUp()
        {
            _chinelo = AudioClip.Create("chinelo", 2400, 1, 24000, false);
            _cinto = AudioClip.Create("cinto", 2400, 1, 24000, false);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_chinelo);
            Object.DestroyImmediate(_cinto);
        }

        [Test]
        public void DistractorKeys_AreTheScenarioIdPrefixed()
        {
            Assert.AreEqual("distractor:flip_flops", NarrationKeys.ForDistractor("flip_flops"));
            Assert.AreEqual("distractor:rubber_gloves", NarrationKeys.ForDistractor(" rubber_gloves "));
            Assert.AreEqual("report:report_damaged_safety_mesh", NarrationKeys.ForHazardReport("report_damaged_safety_mesh"));
        }

        [Test]
        public void AnEmptyId_HasNoKey()
        {
            Assert.AreEqual(string.Empty, NarrationKeys.ForDistractor(null));
            Assert.AreEqual(string.Empty, NarrationKeys.ForDistractor("  "));
            Assert.AreEqual(string.Empty, NarrationKeys.ForHazardReport(""));
        }

        [Test]
        public void Table_FindsTheClipOfEachDistractor()
        {
            var table = new NarrationClipTable();
            table.Add(NarrationKeys.ForDistractor("flip_flops"), _chinelo);
            table.Add(NarrationKeys.ForDistractor("abdominal_belt"), _cinto);

            Assert.AreSame(_chinelo, table.Find(NarrationKeys.ForDistractor("flip_flops")));
            Assert.AreSame(_cinto, table.Find(NarrationKeys.ForDistractor("abdominal_belt")));
        }

        [Test]
        public void Table_UnknownEmptyOrUnfilledKeys_ResolveToNull()
        {
            var table = new NarrationClipTable();
            table.Add(NarrationKeys.ForDistractor("flip_flops"), _chinelo);
            table.Add(NarrationKeys.PhaseCompleted, null);

            Assert.IsNull(table.Find(NarrationKeys.ForDistractor("helmet_no_chinstrap")));
            Assert.IsNull(table.Find(NarrationKeys.PhaseCompleted), "An empty slot degrades to silence.");
            Assert.IsNull(table.Find(null));
            Assert.IsNull(table.Find(""));
        }

        [Test]
        public void Table_KeysMatchExactly_NotByPrefix()
        {
            var table = new NarrationClipTable();
            table.Add("distractor:flip_flops", _chinelo);

            Assert.IsNull(table.Find("distractor:flip"));
            Assert.IsNull(table.Find("DISTRACTOR:FLIP_FLOPS"));
            Assert.AreSame(_chinelo, table.Find(" distractor:flip_flops "));
        }

        [Test]
        public void FinishTipClip_IsTheOneAtThePickedIndex()
        {
            var clips = new[] { _chinelo, null, _cinto };

            Assert.AreSame(_chinelo, NarrationClipTable.At(clips, 0));
            Assert.AreSame(_cinto, NarrationClipTable.At(clips, 2));
            Assert.IsNull(NarrationClipTable.At(clips, 1), "An empty slot degrades to silence.");
            Assert.IsNull(NarrationClipTable.At(clips, 3));
            Assert.IsNull(NarrationClipTable.At(clips, -1));
            Assert.IsNull(NarrationClipTable.At(null, 0));
        }

        [Test]
        public void NarratedWarning_StaysOpenUntilItsLineEnds()
        {
            Assert.AreEqual(18f, NarrationTiming.AutoCloseSeconds(6f, 0f, 17f, 1f), 1e-4f);
            Assert.AreEqual(21f, NarrationTiming.AutoCloseSeconds(6f, 3f, 17f, 1f), 1e-4f,
                "A line waiting behind a DICA still gets its full length.");
        }

        [Test]
        public void NarratedWarning_KeepsALongerAuthoredDelayOrManualClose()
        {
            Assert.AreEqual(30f, NarrationTiming.AutoCloseSeconds(30f, 0f, 10f, 1f), 1e-4f);
            Assert.AreEqual(0f, NarrationTiming.AutoCloseSeconds(0f, 0f, 10f, 1f), 1e-4f,
                "Zero means the popup closes by hand; narration must not turn it into a timer.");
            Assert.AreEqual(6f, NarrationTiming.AutoCloseSeconds(6f, 0f, 0f, 1f), 1e-4f);
        }

        [Test]
        public void ViewCone_SeesAPointAheadWithinRange()
        {
            var eye = new Vector3(0f, 1.6f, 0f);
            Assert.IsTrue(ViewCone.Contains(eye, Vector3.forward, new Vector3(0.3f, 1.6f, 2f), 20f, 4f));
        }

        [Test]
        public void ViewCone_IgnoresAPointOffToTheSide()
        {
            var eye = new Vector3(0f, 1.6f, 0f);
            // About 34 degrees off the head's forward: the board at spawn seen from the corner of the eye.
            Assert.IsFalse(ViewCone.Contains(eye, Vector3.forward, new Vector3(-0.96f, 1.6f, 1.47f), 20f, 4f));
            Assert.IsFalse(ViewCone.Contains(eye, Vector3.back, new Vector3(0f, 1.6f, 2f), 20f, 4f));
        }

        [Test]
        public void ViewCone_IgnoresAPointBeyondRange()
        {
            var eye = new Vector3(0f, 1.6f, 0f);
            Assert.IsFalse(ViewCone.Contains(eye, Vector3.forward, new Vector3(0f, 1.6f, 5f), 20f, 4f));
        }
    }
}
