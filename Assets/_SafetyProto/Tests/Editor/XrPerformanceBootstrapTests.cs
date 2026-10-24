using NUnit.Framework;
using SafetyProto.Runtime;

namespace SafetyProto.Tests.Editor
{
    /// <summary>
    /// Covers headset identification for the Quest 2 performance profile: the Android codename
    /// decides when present, the model name is the fallback, and newer headsets are not matched.
    /// </summary>
    public class XrPerformanceBootstrapTests
    {
        [TestCase("hollywood", "Oculus Quest", ExpectedResult = true)]
        [TestCase(null, "Oculus Quest 2", ExpectedResult = true)]
        [TestCase("eureka", "Oculus Quest 3", ExpectedResult = false)]
        [TestCase("panther", "Oculus Quest 3S", ExpectedResult = false)]
        [TestCase("seacliff", "Oculus Quest Pro", ExpectedResult = false)]
        [TestCase(null, null, ExpectedResult = false)]
        public bool IsQuest2(string androidDevice, string deviceModel) =>
            XrPerformanceBootstrap.IsQuest2(androidDevice, deviceModel);
    }
}
