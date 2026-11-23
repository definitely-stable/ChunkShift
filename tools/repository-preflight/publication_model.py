"""R0 executable *abstract* Repository publication model.

This is a specification oracle, NOT the production Repository implementation.
Logical identity is represented by symbolic keys/fingerprints rather than hashes.
No filesystem, power-loss, object-store or byte-format guarantee is exercised here.
"""
from dataclasses import dataclass, replace


class InvalidChunk(ValueError):
    pass


class ConflictingContent(ValueError):
    pass


class InvalidPublication(ValueError):
    pass


@dataclass(frozen=True)
class Candidate:
    key: str
    observed_key: str
    expected_length: int
    observed_length: int
    fingerprint: str
    verified: bool = False
    sealed: bool = False


@dataclass(frozen=True)
class Generation:
    parent: str | None
    locations: tuple[tuple[str, str], ...]


class RepositoryModel:
    """Small immutable-generation model with one or more named CAS refs."""

    def __init__(self):
        self.packs: dict[str, Candidate] = {}
        self.generations: dict[str, Generation] = {
            "g0": Generation(None, ())
        }
        self.refs: dict[str, str] = {"main": "g0"}
        self.pins: dict[str, int] = {}
        self.next_generation = 1

    def stage(self, pack_id: str, key: str, observed_key: str,
              expected_length: int, observed_length: int,
              fingerprint: str) -> None:
        if pack_id in self.packs:
            raise InvalidPublication("physical pack IDs must never be overwritten")
        if expected_length < 0 or observed_length < 0:
            raise InvalidChunk("negative length")
        self.packs[pack_id] = Candidate(
            key, observed_key, expected_length, observed_length, fingerprint
        )

    def verify(self, pack_id: str) -> None:
        candidate = self.packs[pack_id]
        if candidate.key != candidate.observed_key:
            raise InvalidChunk("observed HashSuiteId+ChunkId differs")
        if candidate.expected_length != candidate.observed_length:
            raise InvalidChunk("observed logical length differs")
        self.packs[pack_id] = replace(candidate, verified=True)

    def seal(self, pack_id: str) -> None:
        candidate = self.packs[pack_id]
        if not candidate.verified:
            raise InvalidPublication("cannot seal unverified bytes")
        self.packs[pack_id] = replace(candidate, sealed=True)

    def prepare(self, ref: str, additions: tuple[str, ...] = (),
                removals: tuple[str, ...] = ()) -> tuple[str, str]:
        """Prepare a complete candidate generation; does NOT publish it."""
        parent = self.refs[ref]
        locations = dict(self.generations[parent].locations)
        for key in removals:
            locations.pop(key, None)
        for pack_id in additions:
            candidate = self.packs[pack_id]
            if not candidate.sealed or not candidate.verified:
                raise InvalidPublication("prepared generation requires sealed pack")
            previous_id = locations.get(candidate.key)
            if previous_id is not None:
                previous = self.packs[previous_id]
                if previous.fingerprint != candidate.fingerprint:
                    raise ConflictingContent("same claimed key, different bytes")
                # Logical dedup: retain the already-published primary location.
                continue
            locations[candidate.key] = pack_id
        generation_id = f"g{self.next_generation}"
        self.next_generation += 1
        self.generations[generation_id] = Generation(
            parent, tuple(sorted(locations.items()))
        )
        return parent, generation_id

    def publish(self, ref: str, expected: str, proposed: str) -> bool:
        """CAS is the sole logical visibility point."""
        if self.refs[ref] != expected:
            return False
        generation = self.generations[proposed]
        if generation.parent != expected:
            raise InvalidPublication("proposal based on wrong parent")
        for key, pack_id in generation.locations:
            candidate = self.packs.get(pack_id)
            if candidate is None or not candidate.sealed or not candidate.verified:
                raise InvalidPublication("missing or unsealed committed pack")
            if candidate.key != key:
                raise InvalidPublication("logical key mismatch")
        self.refs[ref] = proposed
        return True

    def pin(self, ref: str) -> str:
        generation = self.refs[ref]
        self.pins[generation] = self.pins.get(generation, 0) + 1
        return generation

    def unpin(self, generation: str) -> None:
        if self.pins[generation] == 1:
            del self.pins[generation]
        else:
            self.pins[generation] -= 1

    def can_read(self, generation: str, key: str) -> bool:
        pack_id = dict(self.generations[generation].locations).get(key)
        return (
            pack_id is not None
            and pack_id in self.packs
            and self.packs[pack_id].verified
            and self.packs[pack_id].sealed
        )

    def gc(self) -> set[str]:
        """Retain packs in every named ref and active reader view."""
        retained_generations = set(self.refs.values()) | set(self.pins)
        retained_packs = {
            pack_id
            for generation in retained_generations
            for _, pack_id in self.generations[generation].locations
        }
        deleted = set(self.packs) - retained_packs
        for pack_id in deleted:
            del self.packs[pack_id]
        return deleted

    def crash(self) -> None:
        """Abstract process restart: refs survive; unsealed staging is removed.

        Sealed-but-unreferenced packs and unpublished generations can survive.
        This is NOT a durability simulation or a filesystem crash test.
        """
        self.pins.clear()
        for pack_id in list(self.packs):
            if not self.packs[pack_id].sealed:
                del self.packs[pack_id]

    def assert_reachable_integrity(self) -> None:
        """Check every published ref and every pinned reader generation."""
        for generation in set(self.refs.values()) | set(self.pins):
            for key, _ in self.generations[generation].locations:
                assert self.can_read(generation, key), (generation, key)
