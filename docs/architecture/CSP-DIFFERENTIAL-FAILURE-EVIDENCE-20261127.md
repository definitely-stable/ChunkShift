# AUDIT-004 — CSP differential failure evidence

Status: IMPLEMENTED / exact-head hosted qualification pending. Refs #137 #144.
Existing C# fuzz failures retain exact mutated inputs, but Python comparators
previously logged mismatches only. Runner cleanup could destroy the bytes
required to reproduce a disagreement. This change is diagnostic, not a
change to CSP semantics, mutation distribution or acceptance verdicts.

Each of decode.py --compare, forward_apply.py --compare and decode.py
--compare-frames now retains exact already-read binary inputs on disagreement
or an unexpected decoder exception. Bundles live in failures/python-{kind}/
01..05 with case.csp/base.csm/base.bin or frame.zst/dictionary.bin and
failure.json written last. Metadata records original JSONL row/listing/line,
both verdicts, actual forward seed (zero-based checked count), reason or
traceback, Python/libzstd/platform and hosted run context, SHA256 and length
for every input. Absent input is null, not an empty file. Original filename
contains the C# seed (hex) and iteration. GITHUB_SHA is workflow context
(possibly the PR merge ref), not a claim of independently verified checkout.
Existing C# bundles remain untouched.

Bound: five slots per comparator per dump root, 16 MiB binary per bundle,
128 KiB metadata, 16 Ki characters diagnostic, at most 15 bundles / 240 MiB
binary plus metadata per runner. Repeated invocation does not overwrite
existing slots. Exceeding limits or artifact I/O failure logs an explicit
diagnostic and leaves the comparison failing; incomplete bundles have no
failure.json. No all-corpus artifact upload. Retention remains 90 days.
All three comparators run after successful C# fuzz, even if one fails.
Unexpected decoder exceptions are rethrown after attempting retention.
Input-listing parse/read failures are not binary mismatches and do not
promise a complete reproducer. No production data, dependencies or API changes.

Eight hosted negative-control tests exercise actual comparator mismatch paths
via injected verdicts, exact bytes/digests, forward seed, dictionary and
expected length, exception identity, matching verdict/no output, caps and
artifact I/O failure. Real differential fuzz matrix must still pass on x64
and ARM64; test assertions prove retention behavior, not a real decoder bug.
Next AUDIT-005: common per-mode apply oracle, output residue, allocation
budget and failure capture before alternate-output cleanup.

## Qualification correction — hosted run 45902381900

Both native jobs failed in negative controls before extended CSP fuzzing:
linux-x64 140191010012, linux-arm64 140191010362. Seven of eight tests passed;
`test_frame_mismatch_retains_dictionary_and_length` incorrectly instantiated
`Malformed("injected")`, although its real constructor takes `(rule, reason)`.
This was a test defect, not a product mismatch. The replacement uses an actual
malformed frame, exercises the real envelope parser and checks the retained
reason and original row. Two additional non-mocked controls use a committed
valid CSP fixture with independently pinned target SHA256 and an actual valid
libzstd frame. The CSP control intentionally supplies a wrong .NET listing
verdict, verifies stored bytes and replays them, then requires real forward
agreement. The frame control requires no failure artifact for an agreeing
verdict. Ten tests now require fresh exact-head hosted qualification. No
production decoder, acceptance rule, or evidence retention limit changed.
