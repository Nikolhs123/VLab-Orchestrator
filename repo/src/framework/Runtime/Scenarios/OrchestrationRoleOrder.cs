using System;

namespace Thesis.Orchestration.Runtime.Scenarios
{
    // Which participant plays the scenario role User1. By default it is the master client (the
    // host on the server-authoritative backends); "-vlabRoleOrder swapped" makes the other
    // participant User1, so that a campaign can balance the order of the two participants.
    public static class OrchestrationRoleOrder
    {
        private const string Argument = "-vlabRoleOrder";

        public static bool IsSwapped => string.Equals(Value, "swapped", StringComparison.OrdinalIgnoreCase);

        public static string Name => IsSwapped ? "swapped" : "host-first";

        private static string Value
        {
            get
            {
                string[] arguments = Environment.GetCommandLineArgs();
                for (int i = 0; i < arguments.Length - 1; i++)
                {
                    if (string.Equals(arguments[i], Argument, StringComparison.OrdinalIgnoreCase))
                        return arguments[i + 1];
                }

                return string.Empty;
            }
        }
    }
}
