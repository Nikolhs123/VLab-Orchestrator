using NUnit.Framework;
using UnityEngine;
using VLabOrchestrator.Runtime;

namespace VLabOrchestrator.Showcase.Tests
{
    public sealed class BenchmarkPresetTests
    {
        [Test]
        public void CanonicalBenchmarkProtocolIsCurrentPublicationVersion()
        {
            Assert.That(
                OrchestrationProjectConfig.CanonicalBenchmarkProtocolVersion,
                Is.EqualTo("1.4"));
        }

        [TestCase(OrchestrationBenchmarkScenario.BaselineFcfs, 3, 10f, 1f, 15f, true, true, 1f, 6f, 60f, true)]
        [TestCase(OrchestrationBenchmarkScenario.ContentionBurst, 5, 5f, 1f, 15f, false, true, 0.5f, 3f, 30f, true)]
        [TestCase(OrchestrationBenchmarkScenario.RapidHandoff, 10, 2.5f, 0.25f, 10f, true, true, 0.25f, 1.5f, 15f, true)]
        [TestCase(OrchestrationBenchmarkScenario.TransformReplication, 5, 8f, 1f, 15f, true, true, 0.5f, 6f, 90f, true)]
        [TestCase(OrchestrationBenchmarkScenario.OpenLoopStress, 20, 0.5f, 0.1f, 10f, false, false, 0f, 0.25f, 0f, false)]
        public void PaperPresetAppliesCompleteCanonicalProtocol(
            OrchestrationBenchmarkScenario scenario,
            int cycles,
            float holdSeconds,
            float cooldownSeconds,
            float timeoutSeconds,
            bool preQueue,
            bool manipulate,
            float manipulationStartSeconds,
            float manipulationDurationSeconds,
            float rotationDegrees,
            bool validateRotation)
        {
            var config = ScriptableObject.CreateInstance<OrchestrationProjectConfig>();
            try
            {
                config.benchmarkScenario = scenario;
                config.ApplySelectedPaperPreset();

                Assert.That(config.expectedParticipants, Is.EqualTo(2));
                Assert.That(config.repeatCount, Is.EqualTo(cycles));
                Assert.That(config.warmupSeconds, Is.EqualTo(5f));
                Assert.That(config.holdSeconds, Is.EqualTo(holdSeconds));
                Assert.That(config.cooldownSeconds, Is.EqualTo(cooldownSeconds));
                Assert.That(config.stepTimeoutSeconds, Is.EqualTo(timeoutSeconds));
                Assert.That(config.preQueueNextParticipant, Is.EqualTo(preQueue));
                Assert.That(config.manipulateControlledChild, Is.EqualTo(manipulate));
                Assert.That(config.manipulationStartDelaySeconds, Is.EqualTo(manipulationStartSeconds));
                Assert.That(config.manipulationDurationSeconds, Is.EqualTo(manipulationDurationSeconds));
                Assert.That(config.controlledRotationDelta.y, Is.EqualTo(rotationDegrees));
                Assert.That(config.validateFinalRotation, Is.EqualTo(validateRotation));
                Assert.That(config.ResolveScenarioId(), Does.StartWith($"S{(int)scenario + 1}_"));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

    }
}
