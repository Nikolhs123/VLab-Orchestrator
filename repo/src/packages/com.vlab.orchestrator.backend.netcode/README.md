# Unity Netcode Adapter

Tested SDK: Unity Netcode for GameObjects `2.11.0`.

The project-authored production adapter is preserved under `ReferenceSource` as
`.cs.txt` files so the core package is independent of Netcode.

The final adapter assembly will use a Unity Package Manager version define and
compile only when Netcode for GameObjects is installed.
