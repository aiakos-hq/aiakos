# Story review: slice 15-1

Reviewed at commit d56466d. Stories: 5. Check: ok.

## Findings

- [ ] S5 (context-gap): R9, E8 and T5 pack the CLI and install it into a temporary tool path, but the package keeps the id `Aiakos` and version `0.0.1-preview.1`, which is already published on nuget.org as the placeholder and may sit in the user's global packages folder, so the install can resolve the published or a cached package instead of the one just packed: the smoke then tests the old greeting or a stale build, and it writes into the owner's package cache. Fix: state in R9 and T5 that the smoke install uses only the temporary package folder as its source and its own packages folder (or a local-only version for the smoke pack), so that neither nuget.org nor a cached copy can be picked.
- [ ] S1 (context-gap): R1 says help and version "require no instance/path lookup" and R2 says an invalid instance "resolves to fixed usage error", so `--help` or `--version` with an invalid `AIAKOS_INSTANCE`, or with `--instance BAD`, has two answers. Fix: state the result of those four invocations in R2 and add them to E1.

## Not checked

- `System.CommandLine` 2.0.12 is listed on nuget.org (checked through the package index); its API was not tried, so R1 and R2 (no response-file expansion, a bare `-` as a positional, `--version` only at the root) were not run.
- `ResolvedRig.SpecHash`/`BindingHash`, `Diagnostic`, `Severity`, `DiagnosticFormatter.Format` and `RigLoader.Load(rigRoot, envPath)` exist on main with the shapes R4 to R8 use; AIK5010 and its wording agree with spec 0003's diagnostics table and spec 0007. Read, not compiled.
- The CI workflow only packs the CLI and does not invoke it, so C1's change of `-v` does not break a workflow step; read in `.github/workflows/ci.yml`.
- R3's rules (which commands refuse `--json` or the `dev` instance, the selectors of `down` and `send`) were not compared with spec 0007's command reference line by line.
- Nothing was built.
