# Test Plan

The playable showcase provides an immediate manual conformance demonstration.
Six repeatable edit-mode tests for the FCFS queue core are included under:

`repo/src/showcase/UnityProject/Assets/Tests/Editor`

Before public release, testing will also include:

- Export-schema tests
- Runtime bootstrap tests
- Generic shared-object binding tests
- One installation and integration test path per optional backend
- Robustness tests for late join, disconnect, simultaneous requests, and clean
  session termination

Final publication measurements are not substitutes for automated software tests.
