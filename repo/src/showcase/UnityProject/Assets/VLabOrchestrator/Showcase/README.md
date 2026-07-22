# Showcase Runtime

`OrchestrationShowcase` consumes the authored scene, instrument definition,
participant definitions, and editable materials. If those references are
missing, it can still create a primitive fallback environment at runtime.

The showcase intentionally depends only on the backend-independent queue core.
It is not a substitute for networked integration tests.
