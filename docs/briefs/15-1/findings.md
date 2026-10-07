# Story review: slice 15-1

Reviewed at commit 0673557. Stories: 5. Check: ok.

## Findings

- [x] S5 (context-gap): R9, E8 and T5 pack the CLI and install it into a temporary tool path, but the package keeps the id `Aiakos` and version `0.0.1-preview.1`, which is already published on nuget.org as the placeholder and may sit in the user's global packages folder, so the install can resolve the published or a cached package instead of the one just packed: the smoke then tests the old greeting or a stale build, and it writes into the owner's package cache. Fix: state in R9 and T5 that the smoke install uses only the temporary package folder as its source and its own packages folder (or a local-only version for the smoke pack), so that neither nuget.org nor a cached copy can be picked.
- [x] S1 (context-gap): R1 says help and version "require no instance/path lookup" and R2 says an invalid instance "resolves to fixed usage error", so `--help` or `--version` with an invalid `AIAKOS_INSTANCE`, or with `--instance BAD`, has two answers. Fix: state the result of those four invocations in R2 and add them to E1.

Author resolution: R9/E8/T5 now require local-only NuGet.Config with cleared sources, explicit
version and private packages/http-cache/tool/home paths. R1/R2/E1 give all four invalid-instance
help/version invocations the help/version exit0 result; only instance-value validation is skipped.

Round 2 (amendment 0673557, private guidance under R2 for story 15-1-1). Each line below was run
against System.CommandLine 2.0.12 on a tree built as the guidance describes.

- [x] S1 (context-gap): the guidance says "let System.CommandLine recognize tokens and reject unknown/extra tokens", but the library gives an option-like token to a free positional or to a scalar option without any error: `up --dry` parses with rig-dir `--dry`, `up --version` and `capture --version` parse as an ordinary `up`/`capture` (so "--version with a selected subcommand is invalid" is not delivered), `up --env --dry-run` gives env `--dry-run`, `up --seat --fresh` gives seat `--fresh`, and `--instance --help --help` returns help. Fix: state in the guidance that the implementation must itself refuse a positional or option value that begins with `-` and was not given after `--` (the literal `-` of `send` excepted), and that the author's probe needs a command with a positional to show it.
- [x] S1 (context-gap): the guidance gives a no-op action to the root only, but the `instance` group has the same library error: `instance --help` and `instance` both carry `Required command was not provided.`, and with the root action in place `--json` alone parses with no error and no command. Fix: extend the sentence to every command that has subcommands, and state the result for a root or group invocation that has options but neither a subcommand nor help/version.
- [x] S1 (context-gap): R2 forbids response-file expansion, but it is on by default in 2.0.12 (`@nofile up` gives `Response file not found 'nofile'.`, so `send impl @notes` would read a file) and the guidance does not mention it. Fix: add to the guidance that the parser configuration must turn the response-file token replacer off.

Confirmed by the same run, no finding: custom zero-arity `--help`/`-h` and root-only `--version`
after `root.Options.Clear()`; zero minimum arity keeps `capture --help` valid; `--version up` is
detectable from the selected command; duplicates are visible (`IdentifierTokenCount`, or a library
error for a scalar); `HelpAction.Invoke` renders help from a parse result with the output writer
set, without the built-in HelpOption. The amendment adds or changes no requirement of R1/R2.
`GetValue` throws `InvalidOperationException` for an option that has a parse error, so values may
be read only after `Errors` is checked. Not run: the acceptance tests (not read), the real tree.

Author round-2 resolution: R2 guidance now requires no-op actions for all command groups,
root-options-only refusal, instance group help, parsed option/positional value rejection before
-- (literal send - excepted), occurrence-aware provenance, duplicate alias counts, parse-error
check before GetValue, and ResponseFileTokenReplacer=null on every parse. These clarify
existing R1/R2 outcomes; public surface, items, split and acceptance sources are unchanged.

## Not checked

- `System.CommandLine` 2.0.12 is listed on nuget.org (checked through the package index); its API was not tried, so R1 and R2 (no response-file expansion, a bare `-` as a positional, `--version` only at the root) were not run.
- `ResolvedRig.SpecHash`/`BindingHash`, `Diagnostic`, `Severity`, `DiagnosticFormatter.Format` and `RigLoader.Load(rigRoot, envPath)` exist on main with the shapes R4 to R8 use; AIK5010 and its wording agree with spec 0003's diagnostics table and spec 0007. Read, not compiled.
- The CI workflow only packs the CLI and does not invoke it, so C1's change of `-v` does not break a workflow step; read in `.github/workflows/ci.yml`.
- R3's rules (which commands refuse `--json` or the `dev` instance, the selectors of `down` and `send`) were not compared with spec 0007's command reference line by line.
- Nothing was built.

Architect, round 1: the diff b876e99..90aa95b was read and both resolutions are accepted; no findings are open. Not run: that `dotnet tool install` succeeds with a single local source and empty private caches.
