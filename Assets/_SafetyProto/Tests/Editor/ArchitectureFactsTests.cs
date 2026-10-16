using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Events;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Pins the machine-checkable facts ARCHITECTURE.md states (sections 2, 4, 6 and 7).
    /// When one of these fails, update the document and the test together.
    /// </summary>
    public class ArchitectureFactsTests
    {
        // The .sln is git-ignored, so the tracked Unity project files mark the root instead.
        private static string RepoRoot()
        {
            var start = TestContext.CurrentContext.TestDirectory;
            for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ProjectSettings", "ProjectVersion.txt"))
                    && Directory.Exists(Path.Combine(dir.FullName, "Assets", "_SafetyProto")))
                    return dir.FullName;
            }
            Assert.Fail("Repository root not found walking up from " + start);
            return start;
        }

        private static string ReadRepoFile(params string[] segments)
        {
            var path = Path.Combine(new[] { RepoRoot() }.Concat(segments).ToArray());
            Assert.IsTrue(File.Exists(path), "Missing file: " + path);
            return File.ReadAllText(path);
        }

        private static Type[] BusPayloadTypes()
        {
            return typeof(IEventPayload).Assembly.GetTypes()
                .Where(t => t.IsPublic && t.IsValueType && !t.IsEnum
                    && (t.Name.EndsWith("EventArgs", StringComparison.Ordinal)
                        || t.Name.EndsWith("Event", StringComparison.Ordinal))
                    && t.Name != "TaskOutcome")
                .ToArray();
        }

        [Test]
        public void PpeTypeOrdinalsAreFrozenAndTwoIsReserved()
        {
            var actual = Enum.GetValues(typeof(PPEType)).Cast<PPEType>()
                .Select(v => v + "=" + (int)v)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            var expected = new[]
            {
                "None=0", "Helmet=1", "Goggles=3", "Harness=4", "Vest=5", "Boots=6",
                "GloveLeft=7", "GloveRight=8",
            }.OrderBy(s => s, StringComparer.Ordinal).ToArray();

            CollectionAssert.AreEqual(expected, actual);
            Assert.IsFalse(Enum.GetValues(typeof(PPEType)).Cast<int>().Contains(2),
                "Value 2 is reserved: serialized assets depend on it staying unused");
        }

        [Test]
        public void TaskStateMembersAreFrozen()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "NotStarted", "InProgress", "CompletedSuccess", "CompletedSuccessButUnsafe",
                    "NotPerformed",
                },
                Enum.GetNames(typeof(TaskState)));
        }

        [Test]
        public void RiskLevelThresholdsMatchTheDoc()
        {
            Assert.AreEqual(1, (int)RiskLevel.Trivial);
            Assert.AreEqual(5, (int)RiskLevel.Intolerable);
            Assert.AreEqual(RiskLevel.Substantial, RiskLevels.EliminatoryThreshold);
            Assert.AreEqual(RiskLevel.Tolerable, RiskLevels.IncidentalChargeTier);
        }

        [Test]
        public void EveryBusPayloadStructImplementsIEventPayload()
        {
            var payloads = BusPayloadTypes();
            Assert.GreaterOrEqual(payloads.Length, 16);

            var offenders = payloads
                .Where(t => !typeof(IEventPayload).IsAssignableFrom(t))
                .Select(t => t.Name)
                .ToArray();
            Assert.IsEmpty(offenders,
                "Payload types that do not implement IEventPayload: " + string.Join(", ", offenders));
        }

        [Test]
        public void ProtocolTableListsEveryPayloadType()
        {
            var doc = ReadRepoFile("ARCHITECTURE.md");
            var missing = BusPayloadTypes()
                .Select(t => t.Name)
                .Where(name => !doc.Contains("`" + name + "`"))
                .ToArray();
            Assert.IsEmpty(missing,
                "ARCHITECTURE.md section 4 does not list: " + string.Join(", ", missing));
        }

        [Test]
        public void SharedCsprojGlobsTheDomain()
        {
            var csproj = ReadRepoFile("Tools", "SafetyProto.Shared", "SafetyProto.Shared.csproj");
            StringAssert.Contains(@"Scripts\Domain\**\*.cs", csproj);
        }

        [Test]
        public void ArchitectureDocCarriesAVerificationStamp()
        {
            var head = string.Join("\n", ReadRepoFile("ARCHITECTURE.md").Split('\n').Take(10));
            StringAssert.IsMatch("Verified against commit `[0-9a-f]{7}`", head);
        }
    }
}
