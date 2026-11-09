"""PATCH-DICT-001 D0: bounded, SHA-256-verified run-topology oracle foundation.

This is a *synthetic* lab-only structural oracle.  Its injected cost callback
is NOT a CSP physical-byte oracle; no real evaluation/adoption is performed.
"""

from __future__ import annotations

from dataclasses import dataclass
from hashlib import sha256
from itertools import combinations
from typing import Callable, Sequence

MAX_BASE_BYTES = 8 * 1024 * 1024
MAX_BASE_CHUNKS = 64
MAX_CHUNK_BYTES = 256 * 1024
MAX_DICTIONARY_BYTES = 1 * 1024 * 1024
MAX_CANDIDATES = 8
MAX_RUNS = 16
MAX_REFERENCES = 32
CSP_V1_MAX_REFERENCES = 4  # CSP-V1-CANDIDATE section 5.2
ZSTD_TRAINED_MAGIC = bytes.fromhex("37a430ec")


@dataclass(frozen=True)
class BaseChunk:
    ordinal: int
    offset: int
    data: bytes
    sha256_hex: str


@dataclass(frozen=True, order=True)
class Run:
    start: int
    count: int

    @property
    def end(self) -> int:
        return self.start + self.count  # exclusive


@dataclass(frozen=True)
class Plan:
    runs: tuple[Run, ...]


@dataclass(frozen=True)
class ScoredPlan:
    plan: Plan
    score: int
    dictionary_bytes: int
    referenced_chunks: int

    @property
    def csp_v1_compatible(self) -> bool:
        # CSP v1 stores up to four ordered base ChunkIds, not run ordinals.
        return self.referenced_chunks <= CSP_V1_MAX_REFERENCES


@dataclass(frozen=True)
class OracleResult:
    best_contiguous: ScoredPlan
    best_any: ScoredPlan
    evaluated: int


class VerifiedBase:
    """Owns an immutable, single-file SHA-256-suite synthetic CSM occurrence view."""

    def __init__(self, chunks: Sequence[BaseChunk]) -> None:
        if not 1 <= len(chunks) <= MAX_BASE_CHUNKS:
            raise ValueError("base chunk count outside D0 bounds")
        expected_offset = 0
        validated = []
        for i, chunk in enumerate(chunks):
            if type(chunk.ordinal) is not int or chunk.ordinal != i:
                raise ValueError("noncanonical base record ordinal")
            if type(chunk.offset) is not int or chunk.offset != expected_offset:
                raise ValueError("base must have contiguous zero-origin offsets")
            if type(chunk.data) is not bytes or not 1 <= len(chunk.data) <= MAX_CHUNK_BYTES:
                raise ValueError("invalid or mutable base chunk payload")
            if type(chunk.sha256_hex) is not str or len(chunk.sha256_hex) != 64:
                raise ValueError("missing canonical base SHA-256")
            if sha256(chunk.data).hexdigest() != chunk.sha256_hex:
                raise ValueError("base occurrence SHA-256 mismatch")
            expected_offset += len(chunk.data)
            if expected_offset > MAX_BASE_BYTES:
                raise ValueError("D0 base exceeds maximum bytes")
            validated.append(chunk)
        self.chunks = tuple(validated)

    def materialize(self, plan: Plan, byte_budget: int = MAX_DICTIONARY_BYTES) -> bytes:
        if type(byte_budget) is not int or not 1 <= byte_budget <= MAX_DICTIONARY_BYTES:
            raise ValueError("dictionary byte budget outside D0 bounds")
        if not 1 <= len(plan.runs) <= MAX_RUNS:
            raise ValueError("run count outside D0 bounds")
        pieces: list[bytes] = []
        end = -1
        references = 0
        total = 0
        for run in plan.runs:
            if (type(run.start) is not int or type(run.count) is not int or
                    run.start < 0 or run.count <= 0 or run.end > len(self.chunks)):
                raise ValueError("base run outside verified occurrence set")
            if run.start <= end:
                raise ValueError("runs overlap, touch, or are unordered")
            end = run.end
            references += run.count
            if references > MAX_REFERENCES:
                raise ValueError("too many referenced base chunks")
            for chunk in self.chunks[run.start:run.end]:
                # Validate again at the read boundary, not merely at construction.
                if sha256(chunk.data).hexdigest() != chunk.sha256_hex:
                    raise ValueError("base occurrence digest changed")
                total += len(chunk.data)
                if total > byte_budget:
                    raise ValueError("dictionary byte budget exceeded")
                pieces.append(chunk.data)
        dictionary = b"".join(pieces)
        if dictionary.startswith(ZSTD_TRAINED_MAGIC):
            raise ValueError("raw dictionary starts with zstd trained-dictionary magic")
        return dictionary


def bounded_oracle(
    base: VerifiedBase,
    candidates: Sequence[Run],
    cost: Callable[[Plan, bytes], int],
    *,
    byte_budget: int = MAX_DICTIONARY_BYTES,
    max_runs: int = MAX_RUNS,
) -> OracleResult:
    """Exact selection over a SMALL declared pool; not whole-base/global optimality.

    The caller owns the physical/synthetic cost semantics; no wall-clock inputs
    are permitted as a canonical score.  The selector is deterministic even if
    proposal ordering changes.  A single-run plan is the matched-budget control.
    """
    if not isinstance(base, VerifiedBase) or not callable(cost):
        raise TypeError("verified base and deterministic cost callback required")
    if type(byte_budget) is not int or not 1 <= byte_budget <= MAX_DICTIONARY_BYTES:
        raise ValueError("byte budget outside D0 limits")
    if type(max_runs) is not int or not 1 <= max_runs <= MAX_RUNS:
        raise ValueError("run limit outside D0 limits")
    if not 1 <= len(candidates) <= MAX_CANDIDATES:
        raise ValueError("candidate count outside D0 limits")
    if any(type(r) is not Run for r in candidates):
        raise ValueError("noncanonical run candidate")
    pool = sorted(candidates)
    if len(set(pool)) != len(pool):
        raise ValueError("duplicate candidate")
    for r in pool:
        if r.start < 0 or r.count <= 0 or r.end > len(base.chunks):
            raise ValueError("candidate outside verified base")

    best_single: ScoredPlan | None = None
    best_any: ScoredPlan | None = None
    evaluated = 0
    for count in range(1, min(max_runs, len(pool)) + 1):
        for subset in combinations(pool, count):
            if any(a.end >= b.start for a, b in zip(subset, subset[1:])):
                continue  # overlap / adjacent runs cannot be canonical together
            if sum(r.count for r in subset) > MAX_REFERENCES:
                continue
            plan = Plan(subset)
            try:
                dictionary = base.materialize(plan, byte_budget)
            except ValueError as exc:
                # Only byte-budget and raw zstd-magic exclusions can be skipped.
                # Every other exception is an invalid source/implementation.
                if str(exc) not in (
                    "dictionary byte budget exceeded",
                    "raw dictionary starts with zstd trained-dictionary magic",
                ):
                    raise
                continue
            score = cost(plan, dictionary)
            if type(score) is not int or score < 0 or score > (1 << 63) - 1:
                raise ValueError("cost must be an exact nonnegative Int64")
            candidate = ScoredPlan(plan, score, len(dictionary), sum(r.count for r in subset))
            evaluated += 1
            key = lambda x: (x.score, len(x.plan.runs), x.dictionary_bytes,
                             x.referenced_chunks, x.plan.runs)
            if best_any is None or key(candidate) < key(best_any):
                best_any = candidate
            if count == 1 and (best_single is None or key(candidate) < key(best_single)):
                best_single = candidate
    if best_single is None or best_any is None:
        raise ValueError("no eligible same-budget contiguous baseline")
    return OracleResult(best_single, best_any, evaluated)
