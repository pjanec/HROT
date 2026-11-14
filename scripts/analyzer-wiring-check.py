#!/usr/bin/env python3
"""
CE-379 / CE-380 — gate the source-generator ANALYZER WIRING invariants.

WHY THIS EXISTS
---------------
CE-379: `dotnet build` was green for months while Visual Studio failed, because a
generator's dependencies were never SHIPPED -- the CLI's csc runs ON .NET 8 and handed
them over from its own shared framework, while VS's csc is .NET Framework-hosted and has
none of them. A green CLI build is therefore NOT evidence that the wiring is correct, and
no existing gate could see the difference.

CE-380: the follow-up sweep was TEXT-based and produced three false positives -- a csproj
matched because `OutputItemType="Analyzer"` appears in one of its COMMENTS, and because a
grep cannot tell an ORDINARY ProjectReference from an Analyzer one. This script parses the
XML, so it can.

THE TWO INVARIANTS, and they apply ONLY to `OutputItemType="Analyzer"` items
---------------------------------------------------------------------------
  (1) A MULTI-TARGETED project shipped as an Analyzer must be pinned with
      SetTargetFramework="TargetFramework=netstandard2.0". Otherwise nearest-TFM hands a
      net8.0 consumer the net8.0 flavour, which a .NET Framework host cannot load at all.

  (2) A consumer shipping an analyzer that depends on System.Text.Json must ship that
      package's WHOLE closure beside it as <Analyzer Include="$(Pkg...)"/>. A netstandard2.0
      library does not copy package assets to bin, so the DLL is simply absent; the loader
      then names exactly ONE missing assembly per attempt, which makes a partial list read
      as progress.

      SCOPED TO System.Text.Json ON PURPOSE, and this is a judgement worth stating. A blanket
      "every non-compiler package must be shipped" rule OVER-REPORTS: Hrot.Blueprints.Compiler
      also references System.Runtime.Loader, which is a facade the host resolves, and the VS
      build is green without it being shipped. An over-reporting gate gets switched off -- this
      repo has done exactly that before -- so this checks the one package MEASURED to be
      required (CE-379) and merely LISTS the rest.

  ORDINARY ProjectReferences are DELIBERATELY NOT CHECKED. There is no analyzer load
  context for them, so nearest-TFM picking net8.0 for a net8.0 consumer is correct.
  Conflating the two is precisely what CE-380 got wrong.

Exit 0 when clean, 1 with a per-site explanation otherwise.
"""
import os
import sys
import xml.etree.ElementTree as ET

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Packages the Roslyn HOST always provides -- shipping them would be redundant, and on some
# hosts actively harmful (two copies of the compiler API in one load context).
HOST_PROVIDED = ("Microsoft.CodeAnalysis", "Microsoft.CSharp", "System.Collections.Immutable")


def _norm(rel):
    """csproj paths are Windows-flavoured; make them usable on either host."""
    return rel.replace("\\", "/")


def _name(path):
    return os.path.basename(_norm(path)).replace(".csproj", "")


def _iter_csprojs():
    for root, dirs, files in os.walk(REPO):
        dirs[:] = [d for d in dirs if d not in ("obj", "bin", ".git", "node_modules")]
        for f in files:
            if f.endswith(".csproj"):
                yield os.path.join(root, f)


def _project_facts(path):
    """(is_multi_targeted, [non-host package refs]) for a project file.

    A MISSING file yields (None, []) rather than raising: ExtDeps demos carry references to
    projects that are not in this tree, and a wiring gate that crashes on them is a gate
    nobody runs.
    """
    if not os.path.exists(path):
        return None, []
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return None, []
    multi = any(e.tag.endswith("TargetFrameworks") and e.text and ";" in e.text
                for e in root.iter())
    pkgs = sorted({
        e.get("Include") for e in root.iter()
        if e.tag.endswith("PackageReference") and e.get("Include")
        and not e.get("Include").startswith(HOST_PROVIDED)
    })
    return multi, pkgs


def _analyzer_refs(path):
    """ONLY ProjectReferences carrying OutputItemType="Analyzer" -- never ordinary ones."""
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return []
    out = []
    for pr in root.iter():
        if not pr.tag.endswith("ProjectReference"):
            continue
        if pr.get("OutputItemType") != "Analyzer":
            continue
        inc = pr.get("Include") or ""
        target = os.path.normpath(os.path.join(os.path.dirname(path), _norm(inc)))
        out.append((_name(inc), target, pr.get("SetTargetFramework")))
    return out


def _shipped_analyzer_packages(path):
    """Bare <Analyzer Include="$(Pkg...)"/> items -- the package closure, if any."""
    try:
        root = ET.parse(path).getroot()
    except ET.ParseError:
        return set()
    return {
        os.path.basename(_norm(e.get("Include", ""))).replace(".dll", "")
        for e in root.iter()
        if e.tag.endswith("Analyzer") and e.get("Include")
    }


def main():
    # The analyzer load context contains EXACTLY the Analyzer items the consumer ships.
    # So the walk follows a project's references ONLY where the consumer also ships that
    # project as an Analyzer. Following ordinary references instead is what made the first
    # draft of this gate demand CycloneDDS and NLog -- assemblies that are not in the load
    # context at all, and whose absence breaks nothing.
    def packages_in_context(project_path, co_shipped, seen=None):
        seen = seen or set()
        key = os.path.normpath(project_path)
        if key in seen or not os.path.exists(key):
            return set()
        seen.add(key)
        _, pkgs = _project_facts(key)
        acc = set(pkgs)
        try:
            root = ET.parse(key).getroot()
        except ET.ParseError:
            return acc
        for pr in root.iter():
            if not pr.tag.endswith("ProjectReference"):
                continue
            tgt = os.path.normpath(os.path.join(os.path.dirname(key),
                                                _norm(pr.get("Include") or "")))
            if tgt in co_shipped:
                acc |= packages_in_context(tgt, co_shipped, seen)
        return acc

    failures = []
    unresolved = []
    informational = set()
    checked = 0
    for csproj in sorted(_iter_csprojs()):
        refs = _analyzer_refs(csproj)
        if not refs:
            continue
        shipped = _shipped_analyzer_packages(csproj)
        consumer = os.path.relpath(csproj, REPO)
        co_shipped = {os.path.normpath(t) for _, t, _ in refs}
        for name, target, stf in refs:
            checked += 1
            if not os.path.exists(target):
                # Reported, not silently skipped: an unresolvable analyzer reference cannot be
                # checked, and saying so is the honest outcome.
                unresolved.append(f"{consumer} -> '{name}' ({os.path.relpath(target, REPO)})")
                continue
            multi, _ = _project_facts(target)
            if multi and not stf:
                failures.append(
                    f"{consumer}\n"
                    f"    ships MULTI-TARGETED '{name}' as an Analyzer without SetTargetFramework.\n"
                    f"    Nearest-TFM will hand a net8.0 consumer the net8.0 flavour, which Visual\n"
                    f"    Studio's .NET Framework-hosted csc cannot load (CS8784/CS8785).\n"
                    f"    FIX: SetTargetFramework=\"TargetFramework=netstandard2.0\" on that reference.")
            pkgs = packages_in_context(target, co_shipped)
            if "System.Text.Json" in pkgs and "System.Text.Json" not in shipped:
                failures.append(
                    f"{consumer}\n"
                    f"    ships '{name}' as an Analyzer, and it depends on System.Text.Json, but the\n"
                    f"    package closure is not shipped beside it.\n"
                    f"    A netstandard2.0 library does not copy package assets to bin, so the DLL is\n"
                    f"    absent from the analyzer load context on a .NET Framework host (CS8785).\n"
                    f"    FIX: copy the 8-package block from Hrot.AI.Behaviors.csproj (CE-379) --\n"
                    f"         GeneratePathProperty=\"true\" ExcludeAssets=\"all\" + <Analyzer Include=...>\n"
                    f"         for the WHOLE closure; the loader names only one missing assembly at a time.")
            for other in sorted(p for p in pkgs if p != "System.Text.Json" and p not in shipped):
                informational.add(f"{consumer}: '{name}' -> {other} (not shipped; not known to be required)")

    if failures:
        print("ANALYZER WIRING CHECK FAILED\n")
        for f in failures:
            print("  " + f + "\n")
        print(f"{len(failures)} problem(s) across {checked} analyzer reference(s).")
        print("Verify any single consumer with:")
        print("  dotnet msbuild <consumer>.csproj -t:ResolveReferences -getItem:Analyzer")
        return 1

    if informational:
        print("INFO — package deps of shipped analyzers that are NOT shipped. Not a failure:")
        print("       only System.Text.Json is MEASURED to be required on a .NET Framework host")
        print("       (CE-379). These are listed so a future CS8785 has somewhere to start.")
        for i in sorted(informational):
            print("  " + i)
        print()

    if unresolved:
        print("NOTE — analyzer references whose project is not in this tree, so NOT checked:")
        for u in unresolved:
            print("  " + u)
        print()

    print(f"analyzer wiring OK — {checked - len(unresolved)} Analyzer project reference(s) checked, "
          f"multi-target pinning and package closures both satisfied.")
    print("(Ordinary ProjectReferences are deliberately not checked — CE-380.)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
