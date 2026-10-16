using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace SafetyProto.Tests.Editor
{
    public class SharedAssemblyIsEngineFreeTests
    {
        [Test]
        public void SharedDomainAssemblyReferencesNoUnityEngine()
        {
            var assembly = typeof(SafetyProto.Domain.Tasks.TaskManagerCore).Assembly;

            var offenders = assembly.GetReferencedAssemblies()
                .Select(a => a.Name ?? string.Empty)
                .Where(n => n.StartsWith("UnityEngine", StringComparison.Ordinal)
                         || n.StartsWith("UnityEditor", StringComparison.Ordinal))
                .ToList();

            Assert.IsEmpty(offenders,
                $"{assembly.GetName().Name} references engine assemblies: {string.Join(", ", offenders)}");
        }

        [Test]
        public void SharedCoreTypesComeFromTheSameAssemblyAsDomain()
        {
            var unityHost = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name == "UnityEngine.CoreModule");
            if (unityHost)
                Assert.Ignore("Unity host: Core and Domain are separate assemblies by design.");

            Assert.AreEqual(
                typeof(SafetyProto.Domain.Tasks.TaskManagerCore).Assembly,
                typeof(SafetyProto.Core.ViolationCodes).Assembly);
        }
    }
}
