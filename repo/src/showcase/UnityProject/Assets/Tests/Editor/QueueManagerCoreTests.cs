using NUnit.Framework;
using Thesis.Orchestration.Core;

namespace VLabOrchestrator.Showcase.Tests
{
    public sealed class QueueManagerCoreTests
    {
        private const string Instrument = "instrument";
        private QueueManagerCore queue;

        [SetUp]
        public void SetUp()
        {
            queue = new QueueManagerCore();
            Assert.That(queue.RegisterInstrument(Instrument), Is.True);
            Assert.That(queue.RegisterUser("a"), Is.True);
            Assert.That(queue.RegisterUser("b"), Is.True);
            Assert.That(queue.RegisterUser("c"), Is.True);
        }

        [Test]
        public void FirstRequestIsGranted()
        {
            QueueDecision decision = queue.RequestAccess("a", Instrument);

            Assert.That(decision.Type, Is.EqualTo(QueueDecisionType.Granted));
            Assert.That(queue.GetInstrumentState(Instrument).CurrentHolderUserId, Is.EqualTo("a"));
        }

        [Test]
        public void WaitingUsersRemainInFcfsOrder()
        {
            queue.RequestAccess("a", Instrument);
            QueueDecision second = queue.RequestAccess("b", Instrument);
            QueueDecision third = queue.RequestAccess("c", Instrument);

            Assert.That(second.Type, Is.EqualTo(QueueDecisionType.Queued));
            Assert.That(second.QueuePosition, Is.EqualTo(1));
            Assert.That(third.Type, Is.EqualTo(QueueDecisionType.Queued));
            Assert.That(third.QueuePosition, Is.EqualTo(2));
        }

        [Test]
        public void ReleasePromotesNextQueuedUser()
        {
            queue.RequestAccess("a", Instrument);
            queue.RequestAccess("b", Instrument);

            QueueDecision release = queue.ReleaseAccess("a", Instrument);

            Assert.That(release.Type, Is.EqualTo(QueueDecisionType.ReleasedAndGrantedNext));
            Assert.That(release.SecondaryUserId, Is.EqualTo("a"));
            Assert.That(release.UserId, Is.EqualTo("b"));
            Assert.That(queue.GetInstrumentState(Instrument).CurrentHolderUserId, Is.EqualTo("b"));
        }

        [Test]
        public void DuplicateQueueRequestIsDenied()
        {
            queue.RequestAccess("a", Instrument);
            queue.RequestAccess("b", Instrument);

            QueueDecision duplicate = queue.RequestAccess("b", Instrument);

            Assert.That(duplicate.Type, Is.EqualTo(QueueDecisionType.Denied));
            Assert.That(queue.GetQueuePosition("b", Instrument), Is.EqualTo(1));
        }

        [Test]
        public void NonHolderCannotReleaseInstrument()
        {
            queue.RequestAccess("a", Instrument);

            QueueDecision denied = queue.ReleaseAccess("b", Instrument);

            Assert.That(denied.Type, Is.EqualTo(QueueDecisionType.Denied));
            Assert.That(queue.GetInstrumentState(Instrument).CurrentHolderUserId, Is.EqualTo("a"));
        }

        [Test]
        public void QueuedUserCanBeRemovedWithoutChangingHolder()
        {
            queue.RequestAccess("a", Instrument);
            queue.RequestAccess("b", Instrument);

            QueueDecision removed = queue.RemoveQueuedUser("b", Instrument);

            Assert.That(removed.Type, Is.EqualTo(QueueDecisionType.RemovedFromQueue));
            Assert.That(queue.GetQueuePosition("b", Instrument), Is.EqualTo(-1));
            Assert.That(queue.GetInstrumentState(Instrument).CurrentHolderUserId, Is.EqualTo("a"));
        }
    }
}
