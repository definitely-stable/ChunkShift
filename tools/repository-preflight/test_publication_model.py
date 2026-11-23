"""Deterministic state-machine probes for the proposed R0 storage contract.

These tests validate only the abstract model and future implementation obligations;
they must not be presented as storage crash/durability or concurrency evidence.
"""
import random
import unittest

from publication_model import (
    ConflictingContent,
    InvalidChunk,
    InvalidPublication,
    RepositoryModel,
)


def sealed(model, physical, key, fingerprint=None):
    model.stage(physical, key, key, 4, 4, fingerprint or key)
    model.verify(physical)
    model.seal(physical)


class RepositoryPublicationModelTests(unittest.TestCase):
    def test_unverified_and_unsealed_objects_are_not_publishable(self):
        m = RepositoryModel()
        m.stage("p1", "sha256:abc", "sha256:abc", 4, 4, "v1")
        with self.assertRaises(InvalidPublication):
            m.seal("p1")
        with self.assertRaises(InvalidPublication):
            m.prepare("main", ("p1",))
        m.verify("p1")
        with self.assertRaises(InvalidPublication):
            m.prepare("main", ("p1",))
        self.assertFalse(m.can_read(m.refs["main"], "sha256:abc"))
        m.assert_reachable_integrity()

    def test_expected_key_includes_hash_suite_and_exact_length(self):
        m = RepositoryModel()
        m.stage("bad-suite", "sha256:abc", "blake3:abc", 4, 4, "x")
        with self.assertRaises(InvalidChunk):
            m.verify("bad-suite")
        m.stage("bad-size", "sha256:def", "sha256:def", 4, 3, "y")
        with self.assertRaises(InvalidChunk):
            m.verify("bad-size")
        self.assertEqual(m.refs["main"], "g0")
        m.assert_reachable_integrity()

    def test_pack_identity_cannot_be_overwritten(self):
        m = RepositoryModel()
        sealed(m, "p1", "sha256:a")
        with self.assertRaises(InvalidPublication):
            m.stage("p1", "sha256:b", "sha256:b", 4, 4, "b")

    def test_sealed_pack_and_candidate_generation_are_not_visible_until_cas(self):
        m = RepositoryModel()
        sealed(m, "p1", "sha256:a")
        base, proposed = m.prepare("main", ("p1",))
        self.assertFalse(m.can_read(m.refs["main"], "sha256:a"))
        self.assertEqual(base, "g0")
        self.assertTrue(m.publish("main", base, proposed))
        self.assertTrue(m.can_read(m.refs["main"], "sha256:a"))
        m.assert_reachable_integrity()

    def test_duplicate_ingest_is_logically_idempotent(self):
        m = RepositoryModel()
        sealed(m, "p1", "sha256:a", "same")
        parent, one = m.prepare("main", ("p1",))
        self.assertTrue(m.publish("main", parent, one))
        sealed(m, "p2", "sha256:a", "same")
        parent, two = m.prepare("main", ("p2",))
        self.assertTrue(m.publish("main", parent, two))
        self.assertEqual(dict(m.generations[two].locations)["sha256:a"], "p1")
        self.assertIn("p2", m.gc())
        m.assert_reachable_integrity()

    def test_claimed_same_key_with_different_bytes_rejected(self):
        m = RepositoryModel()
        sealed(m, "p1", "sha256:a", "valid")
        parent, generation = m.prepare("main", ("p1",))
        self.assertTrue(m.publish("main", parent, generation))
        sealed(m, "p2", "sha256:a", "different")
        with self.assertRaises(ConflictingContent):
            m.prepare("main", ("p2",))
        self.assertTrue(m.can_read(m.refs["main"], "sha256:a"))

    def test_stale_writer_rebases_without_losing_winner(self):
        m = RepositoryModel()
        sealed(m, "p1", "sha256:a")
        sealed(m, "p2", "sha256:b")
        parent_a, gen_a = m.prepare("main", ("p1",))
        parent_b, gen_b = m.prepare("main", ("p2",))
        self.assertTrue(m.publish("main", parent_a, gen_a))
        self.assertFalse(m.publish("main", parent_b, gen_b))
        # A losing but still-active writer must not lose its sealed pack to GC.
        self.assertNotIn("p2", m.gc())
        self.assertFalse(m.can_read(m.refs["main"], "sha256:b"))
        new_parent, rebased = m.prepare("main", ("p2",))
        self.assertTrue(m.publish("main", new_parent, rebased))
        self.assertTrue(m.can_read(rebased, "sha256:a"))
        self.assertTrue(m.can_read(rebased, "sha256:b"))
        m.assert_reachable_integrity()

    def test_reader_pinning_delays_retirement(self):
        m = RepositoryModel()
        sealed(m, "old", "sha256:a")
        base, gen = m.prepare("main", ("old",))
        self.assertTrue(m.publish("main", base, gen))
        reader = m.pin("main")
        base, next_gen = m.prepare("main", removals=("sha256:a",))
        self.assertTrue(m.publish("main", base, next_gen))
        self.assertNotIn("old", m.gc())
        self.assertTrue(m.can_read(reader, "sha256:a"))
        m.unpin(reader)
        self.assertIn("old", m.gc())
        self.assertFalse(m.can_read(next_gen, "sha256:a"))

    def test_multiple_named_refs_are_gc_roots(self):
        m = RepositoryModel()
        sealed(m, "p1", "sha256:a")
        parent, gen = m.prepare("main", ("p1",))
        self.assertTrue(m.publish("main", parent, gen))
        m.refs["release"] = gen
        parent, removed = m.prepare("main", removals=("sha256:a",))
        self.assertTrue(m.publish("main", parent, removed))
        self.assertNotIn("p1", m.gc())
        self.assertTrue(m.can_read(m.refs["release"], "sha256:a"))
        m.assert_reachable_integrity()

    def test_active_writer_pins_staging_and_sealed_objects_until_abort(self):
        m = RepositoryModel()
        m.stage("staging", "sha256:one", "sha256:one", 4, 4, "one")
        self.assertNotIn("staging", m.gc())
        m.verify("staging")
        m.seal("staging")
        self.assertNotIn("staging", m.gc())
        m.abandon("staging")
        self.assertIn("staging", m.gc())
        m.assert_reachable_integrity()

    def test_orphans_are_safe_and_restart_does_not_publish_them(self):
        m = RepositoryModel()
        sealed(m, "unreferenced", "sha256:a")
        m.stage("partial", "sha256:b", "sha256:b", 4, 4, "b")
        parent, candidate = m.prepare("main", ("unreferenced",))
        m.crash()
        self.assertNotIn("partial", m.packs)
        self.assertEqual(m.refs["main"], "g0")
        self.assertFalse(m.can_read(m.refs["main"], "sha256:a"))
        self.assertIn("unreferenced", m.gc())
        with self.assertRaises(InvalidPublication):
            m.publish("main", parent, candidate)

    def test_crash_before_or_after_cas_never_exposes_partial_view(self):
        for fault_boundary in ("stage", "verify", "seal", "prepare", "cas"):
            with self.subTest(fault_boundary=fault_boundary):
                m = RepositoryModel()
                m.stage("p1", "sha256:a", "sha256:a", 4, 4, "a")
                if fault_boundary != "stage":
                    m.verify("p1")
                if fault_boundary in ("seal", "prepare", "cas"):
                    m.seal("p1")
                if fault_boundary in ("prepare", "cas"):
                    parent, candidate = m.prepare("main", ("p1",))
                if fault_boundary == "cas":
                    self.assertTrue(m.publish("main", parent, candidate))
                m.crash()
                self.assertEqual(
                    m.can_read(m.refs["main"], "sha256:a"),
                    fault_boundary == "cas",
                )
                m.assert_reachable_integrity()

    def test_deterministic_interleavings_preserve_reachable_integrity(self):
        for seed in range(32):
            with self.subTest(seed=seed):
                rng = random.Random(seed)
                m = RepositoryModel()
                for step in range(60):
                    key = f"sha256:{rng.randrange(8)}"
                    pack = f"pack-{step}"
                    sealed(m, pack, key)
                    parent, candidate = m.prepare("main", (pack,))
                    # Some candidates are intentionally left uncommitted.
                    if rng.randrange(5) != 0:
                        self.assertTrue(m.publish("main", parent, candidate))
                    else:
                        m.abandon(pack)
                    if rng.randrange(7) == 0:
                        m.gc()
                    m.assert_reachable_integrity()
                # All writes are same-content for each symbolic key.
                self.assertLessEqual(len(m.generations[m.refs["main"]].locations), 8)


if __name__ == "__main__":
    unittest.main()
