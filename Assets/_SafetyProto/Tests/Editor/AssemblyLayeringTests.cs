using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Pins the project-assembly reference matrix documented in ARCHITECTURE.md section 2.
    /// Update the matrix and the document together when a layering change is intended.
    /// </summary>
    public class AssemblyLayeringTests
    {
        private const string Prefix = "SafetyProto.";
        private const string EventBus = "SafetyProto.EventBus.Core";
        private const string Domain = "SafetyProto.Domain.Core";
        private const string Utils = "SafetyProto.Utils.Unity";
        private const string Runtime = "SafetyProto.Runtime.Unity";
        private const string Networking = "SafetyProto.Networking.Unity";
        private const string Ui = "SafetyProto.UI.Unity";
        private const string Editor = "SafetyProto.Editor";

        private static readonly Dictionary<string, string[]> Allowed = new Dictionary<string, string[]>
        {
            [EventBus] = new string[0],
            [Domain] = new[] { EventBus },
            [Utils] = new[] { EventBus, Domain },
            [Runtime] = new[] { EventBus, Domain, Utils },
            [Networking] = new[] { EventBus, Domain, Utils, Runtime },
            [Ui] = new[] { EventBus, Domain, Utils, Runtime },
            [Editor] = new[] { EventBus, Utils, Runtime, Domain, Ui },
        };

        private sealed class Asmdef
        {
            public string[] ProjectReferences = new string[0];
            public bool NoEngineReferences;
        }

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

        private static Dictionary<string, Asmdef> LoadAsmdefs()
        {
            var scripts = Path.Combine(RepoRoot(), "Assets", "_SafetyProto", "Scripts");
            var result = new Dictionary<string, Asmdef>();
            foreach (var path in Directory.GetFiles(scripts, "*.asmdef", SearchOption.AllDirectories))
            {
                var json = JObject.Parse(File.ReadAllText(path));
                var name = json["name"]?.ToString() ?? Path.GetFileNameWithoutExtension(path);
                var references = json["references"] as JArray;
                result[name] = new Asmdef
                {
                    ProjectReferences = references == null
                        ? new string[0]
                        : references.Select(r => r.ToString())
                            .Where(r => r.StartsWith(Prefix))
                            .ToArray(),
                    NoEngineReferences = json["noEngineReferences"]?.Value<bool>() ?? false,
                };
            }
            return result;
        }

        [Test]
        public void ExactlySevenProjectAssembliesExist()
        {
            CollectionAssert.AreEquivalent(Allowed.Keys, LoadAsmdefs().Keys);
        }

        [Test]
        public void EveryAssemblyReferencesOnlyItsAllowedProjectAssemblies()
        {
            foreach (var pair in LoadAsmdefs())
            {
                Assert.IsTrue(Allowed.ContainsKey(pair.Key), "Unknown assembly " + pair.Key);
                var allowed = Allowed[pair.Key];
                var unexpected = pair.Value.ProjectReferences.Except(allowed).ToArray();
                var missing = allowed.Except(pair.Value.ProjectReferences).ToArray();
                Assert.IsEmpty(unexpected,
                    pair.Key + " references assemblies outside its allowed set: " + string.Join(", ", unexpected));
                Assert.IsEmpty(missing,
                    pair.Key + " no longer references: " + string.Join(", ", missing));
            }
        }

        [Test]
        public void DomainCoreHasNoEngineReferences()
        {
            var withoutEngine = LoadAsmdefs().Where(p => p.Value.NoEngineReferences).Select(p => p.Key).ToArray();
            CollectionAssert.AreEquivalent(new[] { Domain }, withoutEngine);
        }

        [Test]
        public void RuntimeNeverReferencesNetworkingOrUi()
        {
            var references = LoadAsmdefs()[Runtime].ProjectReferences;
            CollectionAssert.DoesNotContain(references, Networking);
            CollectionAssert.DoesNotContain(references, Ui);
        }

        [Test]
        public void NetworkingAndUiDoNotReferenceEachOther()
        {
            var asmdefs = LoadAsmdefs();
            CollectionAssert.DoesNotContain(asmdefs[Networking].ProjectReferences, Ui);
            CollectionAssert.DoesNotContain(asmdefs[Ui].ProjectReferences, Networking);
        }

        [Test]
        public void NoReferenceCycles()
        {
            var asmdefs = LoadAsmdefs();
            var done = new HashSet<string>();
            var path = new List<string>();

            void Visit(string name)
            {
                if (done.Contains(name))
                    return;
                Assert.IsFalse(path.Contains(name), "Reference cycle: " + string.Join(" -> ", path.Append(name)));
                if (!asmdefs.TryGetValue(name, out var asmdef))
                    return;
                path.Add(name);
                foreach (var reference in asmdef.ProjectReferences)
                    Visit(reference);
                path.RemoveAt(path.Count - 1);
                done.Add(name);
            }

            foreach (var name in asmdefs.Keys)
                Visit(name);
        }
    }
}
