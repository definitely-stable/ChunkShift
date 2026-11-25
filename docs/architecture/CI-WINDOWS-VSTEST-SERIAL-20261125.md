# Hosted Windows VSTest startup reliability — 2026-11-25

Status: mitigation candidate in #485, not a proven root-cause fix.

Exact #485 head 4fe6db3c3e4b161051918ce6081a0be211b3761e:
CI run 45616661901 Windows original job 139213708144 and retry
139222347825 failed after Repository 198/198 PASS. Retry log starts CLI,
Patching, Repository and Core net8.0 test assemblies at 13:00:58 UTC;
Repository passes at 13:01:21; Core net10.0 passes 826 at 13:02:42;
VSTest reports a testhost connection timeout (90 seconds) at 13:02:57;
CLI 192 and Patching 1654 pass afterwards. Core net8.0 has no PASS in
that retry. Not an assertion failure, but cannot identify a definitive
resource or process-level cause from this log alone. Linux x64/ARM64,
Ubuntu and CodeQL/Heavy pass at that exact head. Package smokes were
skipped because their dependency failed. Repeated similar failures in
#482 and #484 resolved by retries; #485 retry did not.

Hypothesis: concurrent solution-level testhost startup and load contributes
to this intermittent failure. Limit Windows solution test invocation to
one MSBuild worker (-m:1) and disable parallel target-framework testing
(-p:TestTfmsInParallel=false). Keep the complete solution and framework
discovery, Release/no-build/no-restore, original 90-second connection
timeout, job timeout, all tests, xUnit collection settings, failure exit
codes and dependent smoke gates. No retries hidden in a successful job.
TRX logger with generated names; upload results even on failure (diagnostic,
not a substitute for successful test exit). Ubuntu coverage and Heavy
unchanged. Cost: possibly longer Windows job wall time.

References:
- https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test-vstest
- https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-command-line-reference

Acceptance: exact new head must pass CI/Heavy/CodeQL and applicable gates.
Inspect Windows original logs for Core net8.0 and net10.0, CLI, Patching,
Repository and both smoke jobs. Expected counts at this candidate:
826 + 826 + 192 + 1654 + 198 = 3696, no skipped tests. Confirm actual
nonoverlapping assembly execution from timestamps, do not infer stability
from one success; observe subsequent runs. If timeout persists, obtain
VSTest diagnostics and investigate startup/runner environment rather than
disable tests or repeatedly increase timeouts.
