using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SafetyProto.Core;

namespace SafetyProto.Tests.Editor
{
    public class ViolationCodesTests
    {
        private static List<string> AllCodeValues() =>
            typeof(ViolationCodes)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue()!)
                .ToList();

        [Test]
        public void AllCodesAreUniqueUpperSnakeCase()
        {
            var values = AllCodeValues();

            Assert.GreaterOrEqual(values.Count, 13);
            foreach (var value in values)
                Assert.IsTrue(Regex.IsMatch(value, "^[A-Z][A-Z0-9_]*$"), value);
            CollectionAssert.AllItemsAreUnique(values);
        }

        /// <summary>
        /// These strings are written into session logs and compared across scenario revisions,
        /// so a shipped value must never change.
        /// </summary>
        [Test]
        public void ShippedValuesNeverChange()
        {
            Assert.AreEqual("NO_ACTIVE_GROUP", ViolationCodes.NoActiveGroup);
            Assert.AreEqual("WRONG_ACTION", ViolationCodes.WrongAction);
            Assert.AreEqual("PREREQUISITE_PENDING", ViolationCodes.PrerequisitePending);
            Assert.AreEqual("PPE_MISSING", ViolationCodes.PpeMissing);
            Assert.AreEqual("TASK_NOT_PERFORMED", ViolationCodes.TaskNotPerformed);
            Assert.AreEqual("HAZARD_MISCLASSIFIED", ViolationCodes.HazardMisclassified);
            Assert.AreEqual("PPE_OUT_OF_ORDER", ViolationCodes.PpeOutOfOrder);
            Assert.AreEqual("ACTION_ID_MISSING", ViolationCodes.ActionIdMissing);
            Assert.AreEqual("ORDER_VIOLATION", ViolationCodes.OrderViolation);
            Assert.AreEqual("GATE_PENALTY", ViolationCodes.GatePenalty);
            Assert.AreEqual("GATE_FAILED", ViolationCodes.GateFailed);
            Assert.AreEqual("INSPECTION_INCOMPLETE", ViolationCodes.InspectionIncomplete);
            Assert.AreEqual("WRONG_PPE_SELECTED", ViolationCodes.WrongPpeSelected);
        }
    }
}
