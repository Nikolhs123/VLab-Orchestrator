using System;
using System.Collections.Generic;
using System.Linq;
using Thesis.Orchestration.Core.Models;

namespace Thesis.Orchestration.Core.Policies
{
    /// <summary>
    /// Defines the contract for all allocation policies.
    /// A policy decides which queued user should receive access next.
    /// </summary>
    public interface IAllocationPolicy
    {
        string PolicyName { get; }

        /// <summary>
        /// Selects the next user who should receive access to the instrument.
        /// Returns null if no suitable user exists.
        /// </summary>
        string SelectNextUser(
            InstrumentStateModel instrumentState,
            IReadOnlyDictionary<string, UserStateModel> allUsers);
    }

    /// <summary>
    /// Initial FCFS policy implementation.
    /// This reflects the current queue order without additional prioritization.
    /// </summary>
    public class FCFSPolicy : IAllocationPolicy
    {
        public string PolicyName => "First-Come-First-Served";

        public string SelectNextUser(
            InstrumentStateModel instrumentState,
            IReadOnlyDictionary<string, UserStateModel> allUsers)
        {
            if (instrumentState == null || instrumentState.WaitingQueue == null || instrumentState.WaitingQueue.Count == 0)
                return null;

            // FCFS simply picks the first queued user.
            return instrumentState.WaitingQueue.Peek();
        }
    }

    /// <summary>
    /// Central policy engine that holds the currently active policy
    /// and delegates next-user selection to it.
    /// </summary>
    public class PolicyEngineCore
    {
        public IAllocationPolicy CurrentPolicy { get; private set; }

        public PolicyEngineCore(IAllocationPolicy initialPolicy = null)
        {
            CurrentPolicy = initialPolicy ?? new FCFSPolicy();
        }

        /// <summary>
        /// Replaces the current allocation policy.
        /// </summary>
        public void SetPolicy(IAllocationPolicy newPolicy)
        {
            if (newPolicy == null)
                throw new ArgumentNullException(nameof(newPolicy));

            CurrentPolicy = newPolicy;
        }

        /// <summary>
        /// Returns the next user chosen by the active policy.
        /// This does not modify queue state; it only makes a selection decision.
        /// </summary>
        public string SelectNextUser(
            InstrumentStateModel instrumentState,
            IReadOnlyDictionary<string, UserStateModel> allUsers)
        {
            if (CurrentPolicy == null)
                throw new InvalidOperationException("No allocation policy is currently assigned.");

            return CurrentPolicy.SelectNextUser(instrumentState, allUsers);
        }
    }
}