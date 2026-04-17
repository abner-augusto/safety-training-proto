using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace SafetyProto.Tests.Editor.Support
{
    public static class EventBusAssertions
    {
        public static List<T> EventsOf<T>(this FakeEventBus bus)
        {
            return bus.PublishedEvents
                .Where(e => e.payload is T)
                .Select(e => (T)e.payload)
                .ToList();
        }

        public static void AssertPublishCount<T>(this FakeEventBus bus, int expected)
        {
            var actual = bus.EventsOf<T>().Count;
            Assert.AreEqual(expected, actual,
                $"Expected {expected} events of type {typeof(T).Name}, got {actual}.");
        }
    }
}
