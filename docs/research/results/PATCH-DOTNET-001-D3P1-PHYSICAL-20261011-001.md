# PATCH-DOTNET-001 D3P1: real physical CSP calibration — TypeRef-only STOP (2026-10-11)

EvidenceId: `PATCH-DOTNET-001/D3P1-PHYSICAL-20261011-001`
Verdict: **PHYSICAL_CANDIDATE_RECONSTRUCTION_PASS / TYPE_REF_ONLY_D3P1_STOP_ON_CALIBRATION / ZERO_SELECTED_SAVINGS / NO_HOLDOUT_VERDICT**.
Owner: [issue #221](https://github.com/definitely-stable/ChunkShift/issues/221).
Protocol (committed before implementation/measurements): [D3P1 physical pilot](../../../benchmarks/PATCH-DOTNET-001-D3-PHYSICAL-PILOT-PROTOCOL.md).
Prerequisite: [D3-O calibration upper bound](PATCH-DOTNET-001-D3-O-CALIBRATION-20261011-001.md).

## Exact real evidence

[GitHub-hosted run #38123812844](https://github.com/definitely-stable/ChunkShift/actions/runs/38123812844) **SUCCESS**, head `0067f7c37eda3db2e0c6f58a001f3c99ddae09a5`. Ubuntu 24.04 / .NET 10 Release. All sources remain six SHA-256-pinned PowerShell 7.5.3→7.5.4 application archives (Linux/Windows framework-dependent, Windows self-contained); frozen H0 reference and strict scanner were obtained from already independently audited run #38123052019. SDK/Desktop holdout was **not measured or downloaded for patches**.

- [D3P1 raw physical artifact #11695974097](https://github.com/definitely-stable/ChunkShift/actions/runs/38123812844/artifacts/11695974097) ZIP SHA-256 `f27a98829dc0abc3c7dac03e32cd9b383bae8e7219b78d38bb62dbffea8ce729`. ZIP independently downloaded; all **125/125** SHA256SUMS entries checked against actual bytes and all **123** raw `.d3p` binary envelopes checked independently for exact artifact SHA-256, fixed 24-byte `D3P1` framing, CSP member length, exact target CDN1 inverse map length, target source SHA-256 and sorted/valid original token offsets. Exactly **126** ZIP members: 123 candidate envelopes, physical pilot JSON, independent replay JSON and SHA256SUMS.
- Exact 1,242/1,242 changed file identity keys independently joined against H0 and scanner records; no duplicate or missing source row. The GitHub-hosted pilot re-scanned all 123 previous successes using the same unchanged A0 TypeRef prototype, constructed real normalized-base→normalized-target CSP files with default encoder, extracted them from the persisted research envelope, applied with production CSP applier, then independently inverted `CDN1` to SHA-256-verify **every original target**. No failed normalization/apply/roundtrip concealed with fallback.
- Original A1-v2 SHA: `v2-pairs.json` = `5ca65f09717c70913d324a3946b3b9b714c0f8f3466b0578ba9f52cb06dc89d9`. Frozen H0 evidence SHA = `f0e4ff2e47905f22772f1642b105d6732b955070393eded3bf43c5b39ac82cee`. Frozen D3 scanner evidence SHA = `9c2e6beb1f1086e9cd95e37d512950e355334d78e41be11718c415bccdf95b84`. No adaptive parameter changes.

## All-file physical cost, real measured results

| Correlated deployment | Full H0 bytes | Selected min(H0,D3P1) bytes | Measured savings | Unconditional D3P1-vs-H0 extra bytes |
| --- | ---: | ---: | ---: | ---: |
| PowerShell Linux x64 framework-dependent | 1,922,166 | 1,922,166 | 0 | +166,432 |
| PowerShell Windows x64 framework-dependent | 2,045,059 | 2,045,059 | 0 | +178,286 |
| PowerShell Windows x64 self-contained | 12,062,127 | 12,062,127 | 0 | +22,760 |
| **ALL 1,242 changed files** | **16,029,352** | **16,029,352** | **0 / 0.0000%** | **+367,478** |

Unconditional D3P1 lane (use it on every one of 123 verified candidates, H0 for other rows) = **16,396,830 bytes**, +367,478 B (+2.2925%). The preregistered `min(H0,D3P1)` size gate selected **0 out of 123** D3 envelopes; **all 1,242** rows remained H0. This is a real, observed **zero savings**, not a hypothesis.

For the 123 eligible changed pairs alone:
- H0 physical CSP bytes: **2,172,190 B**.
- Physical CSP over normalized pairs: **2,162,832 B**, a gross payload-only reduction of **9,358 B**.
- Raw uncompressed `CDN1` target inverse maps: **373,884 B**.
- 24-byte envelope headers: **2,952 B**.
- Fully accounted candidate physical size: **2,539,668 B**, **367,478 B worse** than H0 for these same 123 target files.
- Only **four** candidate CSP payloads are even smaller than their original H0 payloads before charging inverse metadata: `System.Management.Automation.dll` win-fdd 3,310 B / linux-fdd 2,714 B and `Newtonsoft.Json.dll` linux-fdd 1,674 B / win-fdd 1,660 B. All other candidate CSP payloads have no improvement.

### A decisive refined bound for the frozen design

For the **fixed A0 TypeRef-only normalized streams and fixed default CSP encoder settings**, suppose the inverse sidecar, envelope header, base normalization material, and all other format overhead were magically **zero bytes**. The most that the observed normalized CSP *payload* can save with per-file fallback is then:

```text
sum(max(0, H0_CSP_i − canonical_CSP_i)) over the 123 paired candidates
   = 9,358 bytes
9,358 / 16,029,352 = 0.0583804% of the same full calibration baseline
```

This **tighter, observed-payload-conditioned upper bound** is far below the predeclared 5% full-population physical savings gate. It is conditional on the **unchanged token grammar, canonicalization, CSP policy and this calibration population**; it does not bound hypothetical better D2/D4/D5 normalization, a redesigned candidate selector/encoder, other payload grammars, or future populations. The earlier impossible zero-*payload* upper bound of 11.4704% was correctly permissive, but is now superseded for this fixed experiment by the observed normalized CSP payload lengths.

## Research decision

**STOP / REJECT the current TypeRef-only D3P1+raw-CDN1 candidate as a physical patch-size optimization on PowerShell calibration**, without opening the SDK/Desktop sealed evaluation. The experiment has a rigorous negative stop criterion, not a general rejection of CLR-aware patching.

Do not spend further holdout budget or production engineering effort trying to compress only the existing inverse map: even **free** inverse metadata cannot bridge 0.0584% to 5% with this unchanged normalized CSP payload. D3P1 must remain lab-only and must not be promoted to a persisted CSP format. Production CSP v1, Default encoding, PublicAPI, release branch and publication repository are unchanged.

**Next productive branch of research:** independent, preregistered *operand coverage and cost* profiling over IL method/member references, TypeDef/MemberRef/MethodSpec tokens, metadata table/index/heap changes (D2), and a distinct version-aware ReadyToRun (D5) study. Before writing another encoder, count transformable fields, verify independently reversible identities and model exact sidecar vs physical CSP payload opportunity, **calibration only**. Seek additional independent applications/NuGet strata for eventual general decisions, and keep SDK/Desktop evaluation sealed.
