#nullable enable
using System.Collections.Generic;
using SafetyProto.Core;
using SafetyProto.Core.Events;

namespace SafetyProto.Tests.Editor.Support
{
    /// <summary>
    /// Projects a <see cref="FakeEventBus"/>'s recorded publish log into an ordered list of
    /// compact string tokens, so a test can assert the exact event SEQUENCE (not just counts).
    /// Engine-independent (no UnityEngine).
    /// </summary>
    public static class EventTimeline
    {
        /// <summary>Full ordered token stream for every recorded event.</summary>
        public static List<string> Tokens(this FakeEventBus bus)
        {
            var tokens = new List<string>(bus.PublishedEvents.Count);
            foreach (var (_, payload) in bus.PublishedEvents)
            {
                tokens.Add(Describe(payload));
            }
            return tokens;
        }

        /// <summary>
        /// Ordered token stream reduced to the session/group milestone events — the stable
        /// "spine" of a run, ignoring the finer PPE/action/score chatter. Handy for asserting
        /// lifecycle ordering without pinning every intermediate event.
        /// </summary>
        public static List<string> MilestoneTokens(this FakeEventBus bus)
        {
            var tokens = new List<string>();
            foreach (var (_, payload) in bus.PublishedEvents)
            {
                switch (payload)
                {
                    case SessionStartedEventArgs:
                        tokens.Add("SessionStarted");
                        break;
                    case TaskGroupEventArgs g:
                        tokens.Add($"Group:{g.Phase}:{g.Group?.groupName}");
                        break;
                    case SessionCompletedEventArgs:
                        tokens.Add("SessionCompleted");
                        break;
                    case SessionEndedEventArgs:
                        tokens.Add("SessionEnded");
                        break;
                }
            }
            return tokens;
        }

        private static string Describe(object payload)
        {
            return payload switch
            {
                SessionStartedEventArgs => "SessionStarted",
                SessionCompletedEventArgs => "SessionCompleted",
                SessionEndedEventArgs => "SessionEnded",
                TaskGroupEventArgs g => $"Group:{g.Phase}:{g.Group?.groupName}",
                TaskEventArgs t => $"Task:{t.Phase}:{t.Task?.taskName}",
                PPEStateChangedEventArgs p => $"PPE:{p.PpeType}:{(p.IsWearing ? "on" : "off")}",
                ActionAttemptedEvent a => $"Action:{a.ActionId}",
                SafetyViolationEventArgs v => $"Violation:{v.ViolationCode}",
                ScoreChangedEventArgs s => $"Score:{(s.Delta >= 0 ? "+" : "")}{s.Delta}",
                _ => payload.GetType().Name
            };
        }
    }
}
