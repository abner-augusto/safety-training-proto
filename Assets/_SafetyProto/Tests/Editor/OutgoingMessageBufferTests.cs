using System.Threading;
using NUnit.Framework;
using SafetyProto.Networking.Dashboard;

namespace SafetyProto.Tests.Editor
{
    public class OutgoingMessageBufferTests
    {
        [Test]
        public void ReliableCapacityRejectsOverflowWithoutEviction()
        {
            using var buffer = new OutgoingMessageBuffer(2);
            var first = new byte[] { 1 };
            var second = new byte[] { 2 };
            var third = new byte[] { 3 };
            Assert.IsTrue(buffer.TryEnqueue(first, false));
            Assert.IsTrue(buffer.TryEnqueue(second, false));
            Assert.IsFalse(buffer.TryEnqueue(third, false));
            Assert.IsTrue(buffer.TryDequeue(out var actual));
            Assert.AreSame(first, actual);
            Assert.IsTrue(buffer.TryDequeue(out actual));
            Assert.AreSame(second, actual);
        }

        [Test]
        public void PoseCoalescesWithoutTouchingReliableFifo()
        {
            using var buffer = new OutgoingMessageBuffer(1);
            var reliable = new byte[] { 1 };
            var oldPose = new byte[] { 2 };
            var latestPose = new byte[] { 3 };
            Assert.IsTrue(buffer.TryEnqueue(reliable, false));
            Assert.IsTrue(buffer.TryEnqueue(oldPose, true));
            Assert.IsTrue(buffer.TryEnqueue(latestPose, true));
            Assert.IsTrue(buffer.TryDequeue(out var actual));
            Assert.AreSame(reliable, actual);
            Assert.IsTrue(buffer.TryDequeue(out actual));
            Assert.AreSame(latestPose, actual);
            Assert.IsFalse(buffer.TryDequeue(out _));
        }

        [Test]
        public void CompleteWakesWaiterAndStopsNewMessages()
        {
            using var buffer = new OutgoingMessageBuffer(1);
            using var cancellation = new CancellationTokenSource(2000);
            buffer.Complete();
            Assert.DoesNotThrow(() => buffer.Wait(cancellation.Token));
            Assert.IsFalse(buffer.TryEnqueue(new byte[] { 1 }, false));
        }
    }
}
