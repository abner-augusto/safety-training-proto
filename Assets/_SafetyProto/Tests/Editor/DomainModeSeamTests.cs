#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using SafetyProto.Core;
using SafetyProto.Core.Interfaces;
using SafetyProto.Domain.Dashboard;
using SafetyProto.Domain.Safety;
using SafetyProto.Domain.Scoring;
using SafetyProto.Domain.Sessions;
using SafetyProto.Domain.Tasks;
using SafetyProto.Tests.Editor.Support;

namespace SafetyProto.Tests.Editor
{
    public class DomainModeSeamTests
    {
        private const string ForbiddenRead = "SessionModeState.Current";

        [Test]
        public void DomainNeverReadsSessionModeStateCurrent()
        {
            var domainDir = Path.Combine(FindRepoRoot(), "Assets", "_SafetyProto", "Scripts", "Domain");
            Assert.IsTrue(Directory.Exists(domainDir), $"Domain folder not found: {domainDir}");

            var offenders = new List<string>();
            foreach (var file in Directory.EnumerateFiles(domainDir, "*.cs", SearchOption.AllDirectories))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Contains(ForbiddenRead))
                        offenders.Add($"{file}:{i + 1}");
                }
            }

            Assert.IsEmpty(offenders,
                $"Domain code must receive the mode as a Func<SessionMode>, not read {ForbiddenRead}:\n" +
                string.Join("\n", offenders));
        }

        [Test]
        public void CoresRejectMissingModeSource()
        {
            var bus = new FakeEventBus();

            Assert.Throws<ArgumentNullException>(() =>
                new TaskManagerCore(bus, new ScoreService(), new List<ITaskGroup>(), null!));
            Assert.Throws<ArgumentNullException>(() =>
                new SafetyRuleEngineCore(bus, null!));
            Assert.Throws<ArgumentNullException>(() =>
                new SessionLoggerCore(bus, Path.GetTempPath(), SessionLoggerCore.SerializeIndentedOmittingDefaults, null!));
            Assert.Throws<ArgumentNullException>(() =>
                new DashboardEventRelay(bus, new FakeDashboardHost(), null!));
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ProjectSettings", "ProjectVersion.txt")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "Assets", "_SafetyProto")))
                    return dir.FullName;
                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found above the test directory.");
        }
    }
}
