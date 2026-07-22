# Source

This directory is the SoftwareX `repo/src` boundary.

- `framework` contains the project-authored framework source snapshot.
- `backend-adapters` groups source that requires optional networking SDKs.
- `packages` contains the installable SDK-independent core package and five
  optional backend package boundaries.
- `showcase` contains the dependency-free playable Unity demonstration.

Vendor SDK source and original V-Lab media are deliberately excluded.

The showcase references the packages through local Unity Package Manager
dependencies. It does not duplicate the queue and policy implementation under
its `Assets` directory.
