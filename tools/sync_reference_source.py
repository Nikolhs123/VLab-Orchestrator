"""Keep backend package ReferenceSource/*.cs.txt identical to the canonical adapters.

The canonical adapter source is repo/src/backend-adapters/<Backend>. The Unity packages under
repo/src/packages/com.vlab.orchestrator.backend.<backend>/ReferenceSource store a copy as .cs.txt so
the packages import without the vendor SDK. SoftwareXRuntimeInstaller materializes the canonical
tree, not these copies, so any difference is silent drift.

  py tools/sync_reference_source.py --check   # exit 1 and list differences
  py tools/sync_reference_source.py           # rewrite drifted copies, create missing ones

Missing copies are created with a TextScriptImporter .meta (new GUID). Copies without a canonical
counterpart are only reported. Line endings are normalized to LF.
"""
import argparse
import pathlib
import sys
import uuid

META = "fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"

ROOT = pathlib.Path(__file__).resolve().parents[1]
ADAPTERS = ROOT / "repo" / "src" / "backend-adapters"
PACKAGES = ROOT / "repo" / "src" / "packages"
BACKENDS = {"pun2": "PUN2", "fusion": "Fusion", "netcode": "Netcode", "fishnet": "FishNet", "mirror": "Mirror"}
# Mirrors SoftwareXRuntimeInstaller.ExcludedAdapterFiles: private V-Lab types, not published.
EXCLUDED = {"PUN2/StandaloneShared/ImmersionOilNetworkStateAdapter.cs", "PUN2/StandaloneShared/SlideNetworkStateAdapter.cs"}


def read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    drifted, missing_copy, orphan_copy = [], [], []
    for package, folder in BACKENDS.items():
        canonical_root = ADAPTERS / folder
        reference_root = PACKAGES / f"com.vlab.orchestrator.backend.{package}" / "ReferenceSource"
        for canonical in sorted(canonical_root.rglob("*.cs")):
            relative = canonical.relative_to(canonical_root).as_posix()
            if f"{folder}/{relative}" in EXCLUDED:
                continue
            copy = reference_root / (relative + ".txt")
            if not copy.exists():
                missing_copy.append((copy, canonical))
            elif read(copy) != read(canonical):
                drifted.append((copy, canonical))
        for copy in sorted(reference_root.rglob("*.cs.txt")):
            relative = copy.relative_to(reference_root).as_posix()[: -len(".txt")]
            if not (canonical_root / relative).exists():
                orphan_copy.append(copy)

    for copy, _ in drifted:
        print(f"DRIFT   {copy.relative_to(ROOT).as_posix()}")
    for copy, _ in missing_copy:
        print(f"MISSING {copy.relative_to(ROOT).as_posix()}")
    for copy in orphan_copy:
        print(f"ORPHAN  {copy.relative_to(ROOT).as_posix()}")

    if args.check:
        clean = not drifted and not missing_copy and not orphan_copy
        print("ReferenceSource matches canonical adapters." if clean else "ReferenceSource differs from canonical adapters.")
        return 0 if clean else 1

    for copy, canonical in drifted:
        copy.write_bytes(read(canonical).encode("utf-8"))
    for copy, canonical in missing_copy:
        copy.parent.mkdir(parents=True, exist_ok=True)
        copy.write_bytes(read(canonical).encode("utf-8"))
        meta = copy.with_name(copy.name + ".meta")
        if not meta.exists():
            meta.write_bytes(META.format(guid=uuid.uuid4().hex).encode("utf-8"))
    print(f"Rewrote {len(drifted)} drifted copies; created {len(missing_copy)} missing copies. Orphans: {len(orphan_copy)}.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
