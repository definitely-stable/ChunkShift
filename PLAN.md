# ChunkShift v0.1: полный архитектурный план проекта

## 0. Короткая формула проекта

**ChunkShift** — это .NET SDK для content-defined chunking, deterministic manifest identity, manifest-to-manifest diff и проверки файла против manifest identity.

Главная идея:

```text
Define what changed. Process only that.
```

На русском:

```text
Определи, что изменилось. Обрабатывай только это.
```

Важно: ChunkShift v0.1 — **не backup-сервер**, **не sync-клиент**, **не chunk store**, **не object storage**, **не restore engine**, **не encryption layer** и **не ASP.NET upload protocol**.

ChunkShift v0.1 — это **детерминированное ядро manifest/diff/verify**, на котором позже можно построить backup, sync, resumable upload, local-first storage, RAG incremental indexing, artifact cache и другие системы.

---

# 1. Финальная продуктовая рамка

## 1.1. Что ChunkShift делает в v0.1

ChunkShift v0.1 должен уметь:

```text
1. Читать конечный byte stream.
2. Разбивать его на chunks.
3. Для default profile использовать FastCDC/Gear candidate profile.
4. Для baseline использовать FixedSize stable profile.
5. Считать SHA-256 hash каждого chunk.
6. Создавать ManifestIdentity.
7. Создавать ManifestEnvelope.
8. Считать IdentityHash через Binary Canonical Identity v1.
9. Сохранять JSON diagnostic/interchange manifest.
10. Читать JSON manifest обратно.
11. Валидировать manifest invariants.
12. Сравнивать два manifest identity.
13. Выдавать logical и unique diff metrics.
14. Выдавать optional ordered delta sequence.
15. Проверять файл против manifest identity.
16. Давать CLI: manifest, diff, verify, inspect, profiles.
```

---

## 1.2. Что ChunkShift v0.1 не делает

ChunkShift v0.1 не должен пытаться быть всем сразу.

Не входит в v0.1:

```text
1. Production IChunkStore.
2. Restore from chunk store.
3. Store-aware DeltaPlan.
4. Upload plan.
5. BytesToUpload / MissingFromStore.
6. ASP.NET resumable upload protocol.
7. Compression.
8. Encryption.
9. Manifest signing.
10. Secure cross-user deduplication.
11. Rabin CDC.
12. UltraCDC.
13. SuperCDC.
14. SeqCDC.
15. VectorCDC.
16. SQLite index.
17. Cloud storage adapters.
18. Directory sync engine.
```

---

## 1.3. Главный product risk

Нельзя продавать ChunkShift v0.1 как:

```text
production archive-grade dedup SDK
```

Правильная формулировка:

```text
ChunkShift v0.1 is a deterministic manifest/diff/verify SDK for .NET,
with immutable candidate profile IDs, Binary Canonical Identity,
SHA-256 Hash256 chunk identity, strict validation invariants,
and manifest-to-manifest diff semantics.
```

Стабильное archive-grade обещание начинается только с v1.0, когда FastCDC profile будет заморожен.

---

# 2. Название, пакеты, namespace

## 2.1. Название

```text
ChunkShift
```

## 2.2. Репозиторий

```text
chunkshift
```

## 2.3. CLI

```bash
chunkshift
```

## 2.4. NuGet packages v0.1

```text
ChunkShift
ChunkShift.Cli
```

`ChunkShift` — основной SDK package.

`ChunkShift.Cli` — standalone dotnet tool. Он не предназначен для reference из пользовательских SDK-проектов.

---

## 2.5. Future extension packages

После v0.1:

```text
ChunkShift.Hashing.Blake3
ChunkShift.Storage.FileSystem
ChunkShift.Storage.Sqlite
ChunkShift.Storage.Compression
ChunkShift.AspNetCore
ChunkShift.Experimental.UltraCdc
ChunkShift.Experimental.SuperCdc
ChunkShift.Experimental.SeqCdc
ChunkShift.Experimental.VectorCdc
```

Правило:

```text
Core-типы после v1.0 не должны переезжать между пакетами.
```

---

# 3. Target frameworks и support policy

## 3.1. Target frameworks

```xml
<TargetFrameworks>net8.0;net10.0</TargetFrameworks>
```

## 3.2. Почему так

```text
net8.0  — transitional adoption target.
net10.0 — primary LTS target.
```

## 3.3. Support policy

```text
net8.0 support follows Microsoft .NET 8 lifecycle.

Dropping net8.0 before v1.0 is allowed only if explicitly documented in release notes,
because v0.x has no stable API promise.

Dropping net8.0 after v1.0 requires a major version.

net10.0 is the primary LTS target.
```

## 3.4. LangVersion

Не использовать:

```xml
<LangVersion>latest</LangVersion>
```

Использовать pinned SDK через `global.json`.

Пример:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

В `.csproj` не указывать `LangVersion`, если нет конкретной причины.

---

# 4. Главные архитектурные принципы

## 4.1. Stable identity не равен byte-for-byte JSON

Неправильная старая формула:

```text
Same bytes + same profile = same byte-for-byte JSON manifest.
```

Правильная формула:

```text
Same bytes + same identity options + same profile
= same chunk sequence + same IdentityHash.
```

Почему: JSON manifest содержит `CreatedUtc`, `CreatedBy`, tool version, comments, source name и другие volatile-поля. Они не могут быть частью stable identity.

---

## 4.2. Manifest делится на Identity и Envelope

```text
ChunkManifest
  ManifestIdentity   // deterministic identity
  ManifestEnvelope   // volatile diagnostic/operational metadata
  Metadata           // extension/user metadata
```

`IdentityHash` считается только от `ManifestIdentity`.

`ManifestEnvelope` и `Metadata` не входят в `IdentityHash`.

---

## 4.3. JSON не является canonical identity source

JSON v0.1 — это:

```text
diagnostic/interchange format
```

IdentityHash считается не от JSON, а от:

```text
Binary Canonical Identity v1
```

---

## 4.4. ProfileId immutable всегда

Даже если profile имеет status `candidate`, опубликованный `ProfileId` нельзя менять.

Нельзя:

```text
fastcdc.gear.candidate1.64k сегодня = GearTable A
fastcdc.gear.candidate1.64k завтра = GearTable B
```

Нужно:

```text
fastcdc.gear.candidate1.64k
fastcdc.gear.candidate2.64k
fastcdc.gear.candidate3.64k
```

---

## 4.5. v0.1 FastCDC profile — candidate, не stable

В v0.1:

```text
fastcdc.gear.candidate1.64k
```

В v1.0:

```text
fastcdc.gear.v1.64k
```

Stable archive-grade compatibility начинается только с v1.0.

---

# 5. Profile stability policy

## 5.1. Stability levels

```text
Experimental:
  Not for persisted manifests.
  Intended for benchmarks and research only.
  Explicit opt-in required.

Candidate:
  Published ProfileId is immutable.
  May be superseded by another candidate or stable profile.
  Not recommended for archive-grade long-term compatibility.

Stable:
  Archive-grade compatibility promise.
  Same input bytes + same identity options + same profile
  produce the same chunk sequence and IdentityHash.
```

---

## 5.2. Что требует нового ProfileId

Любое изменение ниже требует новый `ProfileId`:

```text
1. Gear table.
2. Gear table encoding.
3. Masks.
4. Normalization boundary.
5. Rolling step.
6. Min/avg/max chunk sizes.
7. EOF behavior.
8. Empty stream behavior.
9. Forced max cut behavior.
10. Integer arithmetic.
11. Boundary pseudocode.
12. BoundaryKind semantics.
13. Two-byte rolling vs one-byte rolling.
```

---

# 6. Canonical profile specification

## 6.1. Файлы профиля

Для FastCDC candidate1:

```text
profiles/fastcdc-gear-candidate1-64k.profile.json
profiles/fastcdc-gear-candidate1-64k.gear.bin
docs/specs/profiles/fastcdc-gear-candidate1-64k.md
```

Назначение:

```text
.profile.json — normative profile artifact.
.gear.bin     — raw Gear table artifact.
.md           — human-readable explanation.
```

Markdown не участвует в hash profile identity.

---

## 6.2. SpecSha256

```text
SpecSha256 = SHA-256(exact normalized profile artifact bytes)
```

Правила artifact bytes:

```text
- UTF-8 without BOM.
- LF line endings.
- No trailing whitespace.
- Final newline included.
- File content exactly as committed.
```

Это намеренно означает: изменение normative profile artifact bytes меняет `SpecSha256`.

---

## 6.3. GearTableSha256

```text
GearTableSha256 = SHA-256(raw 256 * UInt64 little-endian bytes)
```

Файл:

```text
profiles/fastcdc-gear-candidate1-64k.gear.bin
```

Без реальных 256 значений Gear table профиль не существует.

---

## 6.4. Рекомендуемая генерация Gear table

Для candidate1 можно использовать deterministic generator:

```text
seed = "ChunkShift-FastCDC-Gear-Candidate1-64K-Table"
table[i] = first UInt64 little-endian value from SHA-256(seed || byte(i))
```

Генератор должен лежать в repo:

```text
eng/tools/GearTableGenerator
```

Важно:

```text
Сгенерированный .gear.bin — source of truth.
Генератор — reproducibility evidence.
```

---

# 7. FastCDC/Gear candidate1 profile

## 7.1. ProfileId

```text
fastcdc.gear.candidate1.64k
```

## 7.2. Parameters

```text
minSize: 16 KiB
averageSize: 64 KiB
maxSize: 256 KiB
rollingStepBytes: 1
content hash: SHA-256
```

## 7.3. Profile JSON shape

```json
{
  "profileId": "fastcdc.gear.candidate1.64k",
  "chunkerId": "fastcdc.gear",
  "stability": "candidate",
  "minSize": 16384,
  "averageSize": 65536,
  "maxSize": 262144,
  "rollingStepBytes": 1,
  "normalization": {
    "boundary": 65536,
    "normalMask": "0x000000000000ffff",
    "largeMask": "0x0000000000007fff"
  },
  "gearTable": {
    "id": "chunkshift.gear.candidate1",
    "format": "uint64-le-256",
    "sha256": "<gear-table-sha256>"
  },
  "eofBehavior": "emit-final-region-if-length-greater-than-zero",
  "emptyStreamBehavior": "zero-chunks",
  "integerArithmetic": "unchecked-uint64",
  "specVersion": 1
}
```

---

## 7.4. Boundary pseudocode

```text
fingerprint = 0
chunkStart = current absolute offset
n = 0

while byte available:
    b = read byte

    fingerprint = unchecked((fingerprint << 1) + Gear[b])
    n++

    if n < MinSize:
        continue

    if n >= MaxSize:
        emit ForcedMaxSize
        reset fingerprint and n
        continue

    if n < NormalizationBoundary:
        if (fingerprint & NormalMask) == 0:
            emit ContentDefined
            reset fingerprint and n
            continue
    else:
        if (fingerprint & LargeMask) == 0:
            emit ContentDefined
            reset fingerprint and n
            continue

on EOF:
    if n > 0:
        emit EndOfStream
```

## 7.5. Что не входит в candidate1

```text
- two-byte rolling;
- SIMD;
- VectorCDC;
- SeqCDC;
- Rabin;
- keyed CDC;
- encryption-aware chunking.
```

Если two-byte rolling появится позже — это новый `ProfileId`.

---

# 8. Fixed-size baseline profile

Fixed-size baseline можно сделать stable уже в v0.1, потому что его поведение простое и проверяемое.

## 8.1. ProfileId

```text
fixed.v1.64k
```

## 8.2. Profile JSON

```json
{
  "profileId": "fixed.v1.64k",
  "chunkerId": "fixed",
  "stability": "stable",
  "chunkSize": 65536,
  "eofBehavior": "emit-final-region-if-length-greater-than-zero",
  "emptyStreamBehavior": "zero-chunks",
  "specVersion": 1
}
```

## 8.3. Behavior

```text
- Empty input emits zero chunks.
- Full chunks have BoundaryKind.FixedSize.
- Final partial chunk has BoundaryKind.EndOfStream.
- Chunk size is exactly 65536 except final partial chunk.
```

---

# 9. Hash model

## 9.1. v0.1 hash algorithm

v0.1 поддерживает только:

```text
SHA-256
```

BLAKE3 не входит в v0.1 compatibility promise.

---

## 9.2. Hash256

`Hash256` — raw 256-bit digest value.

Он не хранит `HashAlgorithmId`.

`HashAlgorithmId` хранится один раз на уровне:

```text
ManifestIdentity.ChunkHashAlgorithm
```

---

## 9.3. Hash256 API

```csharp
public readonly struct Hash256 : IEquatable<Hash256>
{
    private readonly ulong _a;
    private readonly ulong _b;
    private readonly ulong _c;
    private readonly ulong _d;

    public bool IsDefault => _a == 0 && _b == 0 && _c == 0 && _d == 0;

    public static Hash256 FromBytes(ReadOnlySpan<byte> bytes);

    public static bool TryParseHexLower(
        ReadOnlySpan<char> hex,
        out Hash256 value);

    public void CopyTo(Span<byte> destination);

    public bool TryCopyTo(Span<byte> destination);

    public string ToHexLower();

    public bool TryFormatHexLower(Span<char> destination);

    public bool Equals(Hash256 other);

    public bool FixedTimeEquals(Hash256 other);

    public override bool Equals(object? obj);

    public override int GetHashCode();

    public static bool operator ==(Hash256 left, Hash256 right);

    public static bool operator !=(Hash256 left, Hash256 right);
}
```

---

## 9.4. Hash256 rules

```text
1. No per-instance byte[] allocation.
2. No HashAlgorithmId inside Hash256.
3. default(Hash256) is invalid/uninitialized.
4. All-zero digest is reserved and rejected by public constructors.
5. Public APIs reject default(Hash256).
6. FromBytes requires exactly 32 bytes.
7. Internal mapping uses explicit BinaryPrimitives little-endian reads.
8. CopyTo/TryCopyTo reproduces original standard 32-byte digest sequence.
9. GetHashCode uses all four UInt64 values.
10. No cached _hashCode field.
```

Почему нельзя `_hashCode` field:

```text
При десериализации/инициализации struct можно получить _hashCode = 0,
и HashSet/Dictionary деградируют до O(n).
```

---

# 10. Identifier model

## 10.1. Почему не enum

Не использовать enum для:

```text
HashAlgorithmId
ChunkerId
ChunkingProfileId
ManifestSchemaVersion
```

Причина: enum плохо расширяется third-party providers.

---

## 10.2. ID value object

```csharp
public readonly struct HashAlgorithmId : IEquatable<HashAlgorithmId>
{
    public string Value { get; }

    public bool IsDefault { get; }

    public HashAlgorithmId(string value);

    public override string ToString();

    public bool Equals(HashAlgorithmId other);

    public override bool Equals(object? obj);

    public override int GetHashCode();
}
```

---

## 10.3. ID grammar

```text
- ASCII only.
- lowercase only.
- allowed chars: a-z, 0-9, dot, dash, underscore.
- must start with a-z or 0-9.
- max length: 128.
- comparison: StringComparer.Ordinal.
```

Примеры:

```text
sha256
fastcdc.gear
fastcdc.gear.candidate1.64k
chunkshift.manifest.v1
fixed.v1.64k
```

---

# 11. ChunkingProfileRef

## 11.1. Назначение

`ManifestIdentity` хранит lightweight reference на profile, а не весь profile JSON.

```csharp
public readonly record struct ChunkingProfileRef(
    ChunkerId ChunkerId,
    ChunkingProfileId ProfileId,
    Hash256 SpecSha256);
```

---

## 11.2. Правила

```text
1. ProfileId identifies the profile.
2. SpecSha256 binds manifest to canonical profile spec.
3. ChunkerId is diagnostic/routing metadata.
4. Full profile is resolved through profile registry.
5. ProfileId + SpecSha256 is compatibility key.
6. ChunkerId must agree with resolved profile.
7. Unknown profile IDs are rejected for normal operations.
8. Inspection-only mode may allow unknown profile IDs.
9. Same ProfileId with different SpecSha256 is incompatible.
```

---

# 12. Chunk model

## 12.1. ChunkRegion

Временная streaming-структура без content hash.

```csharp
public readonly record struct ChunkRegion(
    long Index,
    long Offset,
    int Length,
    ChunkBoundaryKind BoundaryKind);
```

Почему `long Index`, а не `int`:

```text
int даёт примерно 32 TiB при min chunk 16 KiB, не 32 PiB.
Для future huge datasets long безопаснее.
Offset всё равно long.
Экономия 4 байт не стоит ограничения.
```

---

## 12.2. ChunkDescriptor

Manifest entry с content hash.

```csharp
public readonly record struct ChunkDescriptor(
    long Index,
    long Offset,
    int Length,
    Hash256 Hash,
    ChunkBoundaryKind BoundaryKind);
```

---

## 12.3. ChunkContentKey

Diff identity key.

```csharp
public readonly record struct ChunkContentKey(
    Hash256 Hash,
    int Length);
```

Diff использует `(Hash, Length)`, а не только `Hash`.

Причины:

```text
1. Hash collision практически невозможна, но SDK не должен опираться на “невозможно”.
2. Corrupted/malicious manifest может указать одинаковый hash с разной длиной.
3. Unique byte metrics требуют однозначной длины.
```

---

## 12.4. ChunkBoundaryKind

```csharp
public enum ChunkBoundaryKind : byte
{
    ContentDefined = 1,
    ForcedMaxSize = 2,
    EndOfStream = 3,
    FixedSize = 4
}
```

---

## 12.5. BoundaryKind precedence

```text
1. ForcedMaxSize — если cut случился из-за MaxSize до наблюдения EOF.
2. ContentDefined — если cut случился из-за CDC boundary condition.
3. FixedSize — если cut случился по fixed-size profile.
4. EndOfStream — только final region, если EOF достигнут до другого cut condition.
```

Fixed-size profile:

```text
- full-size chunks: FixedSize
- final partial chunk: EndOfStream
```

---

# 13. Manifest model

## 13.1. Главная идея

Manifest разделяется на:

```text
ManifestIdentity
ManifestEnvelope
Metadata
```

Только `ManifestIdentity` входит в `IdentityHash`.

---

## 13.2. Почему ManifestIdentity не должен быть record с IReadOnlyList

Плохая модель:

```csharp
public sealed record ManifestIdentity
{
    public required IReadOnlyList<ChunkDescriptor> Chunks { get; init; }
}
```

Проблема:

```csharp
var chunks = new List<ChunkDescriptor>();
var identity = new ManifestIdentity { Chunks = chunks };

chunks.Add(...); // identity изменилась после создания
```

Это ломает `IdentityHash`.

---

## 13.3. ManifestIdentity должен быть immutable-by-construction

Рекомендуемая модель:

```csharp
public sealed class ManifestIdentity
{
    private readonly ImmutableArray<ChunkDescriptor> _chunks;

    public ManifestSchemaVersion SchemaVersion { get; }

    public ChunkingProfileRef Profile { get; }

    public HashAlgorithmId ChunkHashAlgorithm { get; }

    public long ContentLength { get; }

    public IReadOnlyList<ChunkDescriptor> Chunks => _chunks;

    public Hash256? WholeContentHash { get; }

    public Hash256 IdentityHash { get; }

    private ManifestIdentity(
        ManifestSchemaVersion schemaVersion,
        ChunkingProfileRef profile,
        HashAlgorithmId chunkHashAlgorithm,
        long contentLength,
        ImmutableArray<ChunkDescriptor> chunks,
        Hash256? wholeContentHash,
        Hash256 identityHash)
    {
        SchemaVersion = schemaVersion;
        Profile = profile;
        ChunkHashAlgorithm = chunkHashAlgorithm;
        ContentLength = contentLength;
        _chunks = chunks;
        WholeContentHash = wholeContentHash;
        IdentityHash = identityHash;
    }

    public static ManifestIdentity Create(
        ManifestSchemaVersion schemaVersion,
        ChunkingProfileRef profile,
        HashAlgorithmId chunkHashAlgorithm,
        long contentLength,
        IEnumerable<ChunkDescriptor> chunks,
        Hash256? wholeContentHash)
    {
        var immutableChunks = chunks.ToImmutableArray();

        ManifestIdentityValidator.Validate(
            profile,
            contentLength,
            immutableChunks,
            wholeContentHash);

        var identityHash = IdentityHashV1.Compute(
            schemaVersion,
            profile,
            chunkHashAlgorithm,
            contentLength,
            immutableChunks,
            wholeContentHash);

        return new ManifestIdentity(
            schemaVersion,
            profile,
            chunkHashAlgorithm,
            contentLength,
            immutableChunks,
            wholeContentHash,
            identityHash);
    }
}
```

---

## 13.4. Record equality caveat

Нельзя полагаться на record-generated equality для:

```text
ManifestIdentity
ChunkManifest
```

Семантическая identity:

```text
IdentityHash
```

Structural comparison — только через explicit comparer/validator.

---

## 13.5. ManifestEnvelope

```csharp
public sealed record ManifestEnvelope
{
    public required DateTimeOffset CreatedUtc { get; init; }

    public required ManifestCreatedBy CreatedBy { get; init; }
}
```

`Envelope` не входит в `IdentityHash`.

---

## 13.6. ManifestCreatedBy

```csharp
public sealed record ManifestCreatedBy
{
    public required string Tool { get; init; }

    public required string Package { get; init; }

    public required string Version { get; init; }

    public string? CommitSha { get; init; }
}
```

`CommitSha` — diagnostic field.

---

## 13.7. ChunkManifest

```csharp
public sealed class ChunkManifest
{
    public ManifestIdentity Identity { get; }

    public ManifestEnvelope Envelope { get; }

    public IReadOnlyDictionary<string, string> Metadata { get; }

    public ChunkManifest(
        ManifestIdentity identity,
        ManifestEnvelope envelope,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        Identity = identity;
        Envelope = envelope;
        Metadata = metadata is null
            ? new Dictionary<string, string>()
            : new Dictionary<string, string>(metadata);
    }
}
```

---

# 14. Manifest validation invariants

`ManifestIdentity` должен валидироваться при создании и при чтении manifest.

## 14.1. Основные invariants

```text
1. ContentLength >= 0.
2. Chunk list immutable after identity creation.
3. Empty content => Chunks.Count == 0.
4. Non-empty content => Chunks.Count > 0.
5. Chunk indexes are zero-based and contiguous.
6. Chunks[i].Index == i.
7. Chunk offsets are contiguous.
8. Chunks[0].Offset == 0.
9. Chunks[i].Offset == Chunks[i-1].Offset + Chunks[i-1].Length.
10. Chunk lengths > 0.
11. Chunk length <= profile max size.
12. Sum(chunk.Length) == ContentLength.
13. Last chunk end offset == ContentLength.
14. Chunk hash != default(Hash256).
15. WholeContentHash, if present, != default(Hash256).
16. IdentityHash from serialized input must equal recomputed IdentityHash.
17. BoundaryKind must be valid for active profile.
18. FixedSize profile cannot emit ContentDefined.
19. FastCDC profile cannot emit FixedSize.
20. EndOfStream may appear only on final chunk.
21. ForcedMaxSize chunks must have Length == Profile.MaxSize.
22. Candidate/experimental profile usage must follow options policy.
23. Same ProfileId with different SpecSha256 is invalid/incompatible.
```

---

## 14.2. Empty stream behavior

Для empty content:

```text
ContentLength = 0
Chunks.Count = 0
WholeContentHash, if present = SHA-256(empty)
```

---

## 14.3. Invalid manifest handling

Invalid manifest reader должен выбрасывать:

```csharp
ChunkShiftManifestFormatException
```

или возвращать structured read result, если выбран non-throwing API.

---

# 15. Binary Canonical Identity v1

## 15.1. Цель

`IdentityHash` не считается от JSON.

Он считается от:

```text
ChunkShift Binary Canonical Identity v1
```

---

## 15.2. Formula

```text
IdentityHashV1 = SHA-256(BinaryCanonicalIdentityV1 without IdentityHash)
```

---

## 15.3. Важное ограничение

Binary Canonical Identity v1 — это формат для identity hashing.

Это не обязательно тот же формат, что будущий binary manifest file v0.2.

---

## 15.4. Binary rules

```text
Magic:
  ASCII "CSID"

Purpose:
  ASCII "identity"

Canonical version:
  u16 little-endian, value 1

String:
  u32 little-endian byte length
  UTF-8 bytes
  null not representable
  maximum length 4096 bytes unless specified otherwise

IDs:
  ASCII lowercase
  grammar: [a-z0-9][a-z0-9._-]{0,127}

Integers:
  little-endian
  signed fields must be non-negative

Hash256:
  exactly 32 bytes in standard digest byte order

profileSpecSha256:
  exactly 32 raw bytes, not hex string

Enum:
  u8 numeric value defined by ChunkShift spec
```

---

## 15.5. Field order

```text
magic: "CSID"
purpose: "identity"
canonicalVersion: u16
schemaVersion: string
chunkerId: string
profileId: string
profileSpecSha256: 32 bytes
chunkHashAlgorithm: string
contentLength: i64
wholeContentHashPresent: u8
wholeContentHash: 32 bytes if present
chunkCount: i64

for each chunk:
  index: i64
  offset: i64
  length: i32
  boundaryKind: u8
  hash: 32 bytes
```

---

## 15.6. Enum binary values

```text
ChunkBoundaryKind:
  ContentDefined = 1
  ForcedMaxSize  = 2
  EndOfStream    = 3
  FixedSize      = 4
```

---

## 15.7. Implementation notes

`BinaryCanonicalIdentityV1Writer` должен:

```text
1. Validate ManifestIdentity before hashing.
2. Use stackalloc for small temporary buffers.
3. Use BinaryPrimitives for all integer writes.
4. Never use BitConverter for canonical bytes.
5. Never depend on platform endianness.
6. Never allocate giant intermediate byte arrays.
7. Feed bytes into IncrementalHash directly.
```

---

# 16. Streaming IdentityHash rule

## 16.1. Почему header-first writer плох для v0.1

Для non-seekable stream до EOF неизвестны:

```text
ContentLength
ChunkCount
WholeContentHash
IdentityHash
```

Поэтому нельзя корректно сделать:

```text
WriteHeaderAsync(...)
WriteChunkAsync(...)
CompleteAsync(...)
```

если header требует final fields.

---

## 16.2. v0.1 policy

Не публиковать header-first `IChunkManifestWriter` в v0.1 public API.

В v0.1:

```text
JSON manifest writer работает с completed ChunkManifest.
```

API:

```csharp
public static class ChunkManifestJson
{
    public static Task WriteAsync(
        Stream destination,
        ChunkManifest manifest,
        CancellationToken cancellationToken = default);

    public static Task<ChunkManifest> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}
```

---

## 16.3. Future v0.2 writer

В v0.2 можно сделать footer-capable binary writer:

```csharp
public interface IStreamingManifestWriter : IAsyncDisposable
{
    ValueTask BeginAsync(
        ManifestStreamStart start,
        CancellationToken cancellationToken = default);

    ValueTask WriteChunkAsync(
        ChunkDescriptor chunk,
        CancellationToken cancellationToken = default);

    ValueTask CompleteAsync(
        ManifestStreamCompletion completion,
        CancellationToken cancellationToken = default);
}
```

---

# 17. WholeContentHash

## 17.1. Meaning

`WholeContentHash` означает:

```text
SHA-256 over the exact byte sequence consumed by CreateManifestAsync.
```

Это не:

```text
IdentityHash
Merkle root
hash of chunk hashes
manifest hash
```

---

## 17.2. Rules

```text
1. Computed only after EOF.
2. Does not require seekable stream.
3. Computed incrementally during same pass as chunking.
4. If operation is canceled, no manifest is returned.
5. Infinite streams are out of scope.
```

---

## 17.3. v0.1 modes

```csharp
public enum WholeContentHashMode
{
    None,
    Compute
}
```

Не добавлять `UsePrecomputed` в v0.1.

Причина: precomputed hash создаёт вопросы доверия и верификации.

---

# 18. JSON manifest v1

## 18.1. Назначение

JSON v1 — diagnostic/interchange format.

Файл:

```text
*.csm.json
```

Media type:

```text
application/vnd.chunkshift.manifest.v1+json
```

---

## 18.2. DTO layer

JSON v1 использует internal wire DTOs.

Публичная domain model не сериализуется напрямую.

DTO:

```text
ManifestV1Dto
IdentityV1Dto
EnvelopeV1Dto
ChunkV1Dto
```

Mapper:

```text
ManifestV1Mapper.ToDto(ChunkManifest manifest)
ManifestV1Mapper.FromDto(ManifestV1Dto dto)
```

Причина:

```text
Public API может развиваться без поломки JSON wire format.
```

---

## 18.3. Source-generated JSON

Использовать только source-generated `System.Text.Json`.

Не использовать reflection-based overloads в core.

Пример:

```csharp
[JsonSerializable(typeof(ManifestV1Dto))]
[JsonSerializable(typeof(IdentityV1Dto))]
[JsonSerializable(typeof(EnvelopeV1Dto))]
[JsonSerializable(typeof(ChunkV1Dto))]
internal partial class ChunkShiftJsonContext : JsonSerializerContext
{
}
```

---

## 18.4. Unknown fields policy v0.1

```text
1. Unknown identity fields are rejected.
2. Unknown envelope fields may be ignored.
3. Unknown metadata fields may be ignored.
4. Unknown major schema version is rejected.
5. Unsupported profile ID is rejected for normal operations.
6. Inspection-only mode may allow unsupported profile ID.
```

Critical extension system — future v0.2+.

---

# 19. Diff semantics

## 19.1. Что diff делает

v0.1 diff — только manifest-to-manifest.

Он не знает про global store.

Он не знает, какие chunks есть на сервере.

Он не считает upload bytes.

Он не является LCS diff.

Он не является byte-range edit diff.

---

## 19.2. Diff matching model

v0.1 использует:

```text
ContentAddressedPresence matching
```

Ключ:

```text
ChunkContentKey = (Hash256 Hash, int Length)
```

---

## 19.3. Matching rule

```text
A new chunk occurrence is Reused
if OldUniqueKeys contains its ChunkContentKey.

A new chunk occurrence is Added
if OldUniqueKeys does not contain its ChunkContentKey.

MatchedOldIndex, when emitted,
is the first old occurrence by index with the same ChunkContentKey.

The same old occurrence may be referenced by multiple new occurrences.
```

---

## 19.4. What this is not

```text
This is not LCS diff.
This is not byte-range edit diff.
This is not store availability diff.
This is not upload planning.
```

---

## 19.5. Diff formulas

```text
OldKeys = multiset of ChunkContentKey in old manifest
NewKeys = multiset of ChunkContentKey in new manifest

OldUniqueKeys = set(OldKeys)
NewUniqueKeys = set(NewKeys)
```

Logical metrics:

```text
LogicalReusedBytes =
  sum(length(new occurrence)) where key in OldUniqueKeys

LogicalAddedBytes =
  sum(length(new occurrence)) where key not in OldUniqueKeys

LogicalRemovedBytes =
  sum(length(old occurrence)) where key not in NewUniqueKeys
```

Unique metrics:

```text
UniqueReusedBytes =
  sum(length(key)) for key in OldUniqueKeys ∩ NewUniqueKeys

UniqueAddedBytesAgainstOldManifest =
  sum(length(key)) for key in NewUniqueKeys - OldUniqueKeys

UniqueRemovedBytesAgainstNewManifest =
  sum(length(key)) for key in OldUniqueKeys - NewUniqueKeys
```

---

# 20. Diff result model

## 20.1. ManifestDiffSummary

```csharp
public sealed record ManifestDiffSummary
{
    public required long OldLogicalBytes { get; init; }
    public required long NewLogicalBytes { get; init; }

    public required long LogicalReusedBytes { get; init; }
    public required long LogicalAddedBytes { get; init; }
    public required long LogicalRemovedBytes { get; init; }

    public required long UniqueReusedBytes { get; init; }

    public required long UniqueAddedBytesAgainstOldManifest { get; init; }

    public required long UniqueRemovedBytesAgainstNewManifest { get; init; }

    public required int UniqueReusedChunkCount { get; init; }
    public required int UniqueAddedChunkCount { get; init; }
    public required int UniqueRemovedChunkCount { get; init; }

    public required DiffCompatibility Compatibility { get; init; }

    public required IReadOnlyList<ManifestDiffWarning> Warnings { get; init; }

    public double LogicalReuseRatio =>
        NewLogicalBytes == 0 ? 1 : (double)LogicalReusedBytes / NewLogicalBytes;
}
```

---

## 20.2. ChunkDeltaEntry

```csharp
public sealed record ChunkDeltaEntry
{
    public required long NewIndex { get; init; }

    public required long NewOffset { get; init; }

    public required int Length { get; init; }

    public required Hash256 Hash { get; init; }

    public required ChunkDeltaStatus Status { get; init; }

    public long? MatchedOldIndex { get; init; }

    public long? MatchedOldOffset { get; init; }
}
```

---

## 20.3. InMemoryManifestDiffResult

```csharp
public sealed record InMemoryManifestDiffResult
{
    public required ManifestDiffSummary Summary { get; init; }

    public required IReadOnlyList<ChunkDeltaEntry> NewSequence { get; init; }
}
```

Не делать:

```csharp
public IAsyncEnumerable<ChunkDeltaEntry> NewSequence { get; init; }
```

в DTO result object.

Почему:

```text
IAsyncEnumerable может быть single-use.
Он может зависеть от disposed resources.
Он может выбросить exception после получения Summary.
Он может повторно вычисляться.
DTO не должен скрывать operation внутри property.
```

Streaming sequence — отдельный method return.

---

# 21. Diff compatibility and warnings

## 21.1. DiffCompatibility

```csharp
public enum DiffCompatibility
{
    Compatible,
    CrossProfileAllowed,
    IncompatibleSchema,
    IncompatibleChunkingProfile,
    IncompatibleHashAlgorithm,
    IncompatibleProfileSpec
}
```

---

## 21.2. ChunkDeltaStatus

```csharp
public enum ChunkDeltaStatus
{
    Reused,
    Added
}
```

---

## 21.3. ManifestDiffWarning

```csharp
public sealed record ManifestDiffWarning(
    ManifestDiffWarningCode Code,
    string Message);
```

```csharp
public enum ManifestDiffWarningCode
{
    CrossProfileComparison,
    CandidateProfileUsed,
    ExperimentalProfileUsed,
    MissingWholeContentHash
}
```

---

## 21.4. ManifestReadWarning

```csharp
public sealed record ManifestReadWarning(
    ManifestReadWarningCode Code,
    string Message);
```

```csharp
public enum ManifestReadWarningCode
{
    UnknownEnvelopeFieldIgnored,
    UnknownMetadataIgnored
}
```

`JsonManifestLarge` — CLI diagnostic, не core diff warning.

---

# 22. ManifestCompareOptions

```csharp
public sealed record ManifestCompareOptions
{
    public CrossProfileComparisonMode CrossProfileMode { get; init; }
        = CrossProfileComparisonMode.Reject;

    public bool RequireSameHashAlgorithm { get; init; } = true;
}
```

```csharp
public enum CrossProfileComparisonMode
{
    Reject,
    AllowWithWarning
}
```

Rules:

```text
1. Same ProfileId with different SpecSha256 is always incompatible.
2. Different hash algorithm is rejected by default.
3. Cross-profile comparison is rejected by default.
4. Cross-profile comparison may be allowed with warning.
```

---

# 23. Engine API

## 23.1. ChunkShiftEngine contract

`ChunkShiftEngine` immutable and thread-safe after construction.

Он может держать readonly dependencies:

```text
- default options;
- profile registry;
- hasher factory;
- clock for envelope creation;
- diagnostics hooks.
```

Per-operation mutable state scoped to method call.

---

## 23.2. Engine API

```csharp
public sealed class ChunkShiftEngine
{
    public Task<ChunkManifest> CreateManifestAsync(
        Stream source,
        ManifestCreationOptions? options = null,
        CancellationToken cancellationToken = default);

    public Task<ChunkManifest> CreateManifestAsync(
        PipeReader source,
        ManifestCreationOptions? options = null,
        CancellationToken cancellationToken = default);

    public ManifestDiffSummary CompareSummary(
        ManifestIdentity oldIdentity,
        ManifestIdentity newIdentity,
        ManifestCompareOptions? options = null);

    public InMemoryManifestDiffResult CompareInMemory(
        ManifestIdentity oldIdentity,
        ManifestIdentity newIdentity,
        ManifestCompareOptions? options = null);

    public IAsyncEnumerable<ChunkDeltaEntry> EnumerateDeltaSequenceAsync(
        ManifestIdentity oldIdentity,
        ManifestIdentity newIdentity,
        ManifestCompareOptions? options = null,
        CancellationToken cancellationToken = default);

    public Task<ManifestDiffAgainstSourceResult> CreateDiffAgainstSourceAsync(
        ManifestIdentity oldIdentity,
        Stream newSource,
        ManifestCreationOptions? creationOptions = null,
        ManifestCompareOptions? compareOptions = null,
        CancellationToken cancellationToken = default);

    public Task<VerificationResult> VerifyAsync(
        Stream source,
        ManifestIdentity identity,
        VerificationOptions? options = null,
        CancellationToken cancellationToken = default);
}
```

Не делать public static class `ChunkShift` внутри namespace `ChunkShift`.

Facade можно добавить позже:

```csharp
public static class ChunkShiftApi
{
}
```

Но в v0.1 он не нужен.

---

# 24. ManifestDiffAgainstSourceResult

```csharp
public sealed record ManifestDiffAgainstSourceResult
{
    public required ChunkManifest NewManifest { get; init; }

    public required ManifestDiffSummary Summary { get; init; }

    public IReadOnlyList<ChunkDeltaEntry>? NewSequence { get; init; }
}
```

Правило:

```text
CreateDiffAgainstSourceAsync must return NewManifest.
```

Почему: иначе пользователь вынужден читать newSource второй раз.

---

## 24.1. Default profile rule

```text
CreateDiffAgainstSourceAsync defaults to oldIdentity.Profile for newSource chunking,
unless creationOptions.ProfileId is explicitly specified.
```

Если explicit profile отличается от old profile — поведение регулируется `ManifestCompareOptions`.

---

# 25. ManifestCreationOptions

Для создания manifest пользователь передаёт `ProfileId`, не `ChunkingProfileRef`.

Engine resolves profile through registry and writes `ChunkingProfileRef` into manifest.

```csharp
public sealed record ManifestCreationOptions
{
    public ChunkingProfileId? ProfileId { get; init; }

    public WholeContentHashMode WholeContentHashMode { get; init; }
        = WholeContentHashMode.None;

    public bool AllowCandidateProfiles { get; init; }
        = true;

    public bool AllowExperimentalProfiles { get; init; }
        = false;

    public int? PipeReaderBufferSize { get; init; }

    public bool CompletePipeReader { get; init; }
        = false;
}
```

Default profile in v0.1:

```text
fastcdc.gear.candidate1.64k
```

`AllowCandidateProfiles = true` по умолчанию, потому что полезный default v0.1 — candidate.

Experimental profiles требуют explicit opt-in.

---

# 26. PipeReader ownership

```text
Stream overload:
  ChunkShift creates and owns internal PipeReader.
  ChunkShift completes internally created PipeReader.

PipeReader overload:
  Caller owns PipeReader by default.
  ChunkShift does not call CompleteAsync unless CompletePipeReader = true.

Cancellation:
  input stream/reader may be advanced.
  ChunkShift never rewinds streams.
```

---

# 27. Verification contract

## 27.1. VerifyAsync проверяет

```text
1. Resolves profile from ManifestIdentity.Profile.
2. Validates ManifestIdentity invariants.
3. Recomputes IdentityHash and compares it.
4. Re-chunks source using manifest profile.
5. Computes SHA-256 for each produced chunk.
6. Compares chunk count, index, offset, length, boundary kind and hash.
7. If WholeContentHash is present, computes and compares whole content hash.
8. Returns VerificationResult with first mismatch and summary.
```

---

## 27.2. VerificationResult

```csharp
public sealed record VerificationResult
{
    public required bool IsValid { get; init; }

    public required VerificationFailureCode FailureCode { get; init; }

    public long? ChunkIndex { get; init; }

    public long? Offset { get; init; }

    public Hash256? ExpectedHash { get; init; }

    public Hash256? ActualHash { get; init; }

    public string? Message { get; init; }
}
```

---

## 27.3. VerificationFailureCode

```csharp
public enum VerificationFailureCode
{
    None,
    UnsupportedProfile,
    ManifestValidationFailed,
    IdentityHashMismatch,
    ContentLengthMismatch,
    ChunkCountMismatch,
    ChunkOffsetMismatch,
    ChunkLengthMismatch,
    BoundaryKindMismatch,
    ChunkHashMismatch,
    WholeContentHashMismatch
}
```

---

# 28. Exceptions

## 28.1. Base exception

```csharp
public abstract class ChunkShiftException : Exception;
```

---

## 28.2. Manifest format

```csharp
public sealed class ChunkShiftManifestFormatException
    : ChunkShiftException;
```

---

## 28.3. Compatibility

```csharp
public sealed class ChunkShiftCompatibilityException
    : ChunkShiftException;
```

---

## 28.4. Profile

```csharp
public sealed class ChunkShiftProfileException
    : ChunkShiftException;
```

---

## 28.5. Verification

```csharp
public class ChunkShiftVerificationException
    : ChunkShiftException
{
    public long? ChunkIndex { get; init; }

    public long? Offset { get; init; }

    public Hash256? ExpectedHash { get; init; }

    public Hash256? ActualHash { get; init; }
}
```

```csharp
public sealed class ChunkShiftHashMismatchException
    : ChunkShiftVerificationException;
```

---

## 28.6. Cancellation

Не делать custom cancellation exception.

Использовать стандартный:

```text
OperationCanceledException
```

---

# 29. Cancellation policy

```text
Cancellation is cooperative.

CreateManifestAsync:
  throws OperationCanceledException;
  returns no partial manifest.

VerifyAsync:
  throws OperationCanceledException;
  returns no partial result.

CLI manifest:
  writes to temp output path;
  moves to final path only after success;
  removes temp output best-effort on cancellation.

Input streams:
  may be advanced;
  are never rewound by ChunkShift.
```

---

# 30. Storage policy

v0.1 не имеет production storage API.

v0.2+ вводит storage.

В v0.1 не использовать термины:

```text
BytesToUpload
MissingFromStore
StoreDeltaPlan
```

---

# 31. Compression future rule

Compression не входит в v0.1.

Future rule:

```text
Chunk identity hash is always computed over uncompressed original chunk bytes.

Compression is storage encoding.

OpenReadAsync must return decompressed original bytes.
```

---

# 32. Security model

ChunkShift v0.1 не обеспечивает confidentiality.

ChunkShift v0.1 не authenticates manifests.

ChunkShift v0.1 не делает cross-user dedup безопасным.

FastCDC/Gear rolling hash не cryptographic.

Gear fingerprint не является chunk identity.

`Hash256` chunk hashes дают integrity только если manifest identity trusted.

`IdentityHash` не signature.

Untrusted manifests должны authenticated external mechanism.

Future:

```text
ChunkShift.Signing
IManifestSigner
detached signatures
```

Не v0.1.

---

# 33. AOT and JSON policy

Core использует source-generated System.Text.Json.

Core serialization APIs must use:

```text
JsonTypeInfo<T>
source-generated context
```

Reflection-based `JsonSerializer` overloads не используются внутри ChunkShift core.

Использовать AOT-compatible enum converters.

Все public APIs в ChunkShift core должны быть trim-analysis clean.

---

# 34. CLI v0.1

## 34.1. Must-have commands

```bash
chunkshift manifest ./file.bin --out file.csm.json
chunkshift diff old.csm.json new.csm.json
chunkshift verify ./file.bin --manifest file.csm.json
chunkshift inspect file.csm.json
chunkshift profiles list
chunkshift profiles inspect fastcdc.gear.candidate1.64k
```

---

## 34.2. Dev/experimental commands

```bash
chunkshift dev chunk ./file.bin --profile fastcdc.gear.candidate1.64k
chunkshift dev benchmark ./dataset
```

Benchmark dataset format не входит в v0.1 stable CLI contract.

---

## 34.3. Output policy

```text
Data -> stdout
Progress/status/errors -> stderr
```

Если `--out` указан:

```text
write to temp file
flush
move atomically to final path
cleanup temp on cancellation/failure best-effort
```

---

# 35. Architecture: repository structure

## 35.1. Root structure

```text
ChunkShift/
  .github/
    workflows/
      ci.yml
      release.yml
      deterministic.yml

  eng/
    Directory.Build.props
    Directory.Packages.props
    global.json.template
    version.props
    build.ps1
    build.sh

    tools/
      GearTableGenerator/
        GearTableGenerator.csproj
        Program.cs
        GearTableGeneratorOptions.cs

  profiles/
    fastcdc-gear-candidate1-64k.profile.json
    fastcdc-gear-candidate1-64k.gear.bin
    fixed-v1-64k.profile.json

  src/
    ChunkShift/
      ChunkShift.csproj
      AssemblyInfo.cs

    ChunkShift.Cli/
      ChunkShift.Cli.csproj
      Program.cs

  tests/
    ChunkShift.Tests/
      ChunkShift.Tests.csproj

    ChunkShift.PropertyTests/
      ChunkShift.PropertyTests.csproj

    ChunkShift.CompatibilityTests/
      ChunkShift.CompatibilityTests.csproj

    ChunkShift.Cli.Tests/
      ChunkShift.Cli.Tests.csproj

    ChunkShift.AotSmoke/
      ChunkShift.AotSmoke.csproj

    ChunkShift.TrimSmoke/
      ChunkShift.TrimSmoke.csproj

  testdata/
    profiles/
    golden/
    manifests/
    corrupt/
    samples/

  benchmarks/
    ChunkShift.Benchmarks/
      ChunkShift.Benchmarks.csproj

  docs/
    README.md
    quickstart.md
    roadmap.md
    security.md
    compatibility-policy.md
    manifest-format.md
    binary-canonical-identity-v1.md
    diff-semantics.md
    profile-policy.md
    cli.md
    performance.md

    specs/
      profiles/
        fastcdc-gear-candidate1-64k.md
        fixed-v1-64k.md
```

---

# 36. src/ChunkShift folder architecture

```text
src/ChunkShift/
  Primitives/
    Hash256.cs
    HashAlgorithmId.cs
    ChunkerId.cs
    ChunkingProfileId.cs
    ManifestSchemaVersion.cs
    IdGrammar.cs

  Profiles/
    ChunkingProfile.cs
    ChunkingProfileRef.cs
    ChunkingProfileStability.cs
    ChunkingProfileRegistry.cs
    BuiltInProfiles.cs
    ProfileArtifactReader.cs
    ProfileChecksumValidator.cs

  Profiles/FastCdc/
    FastCdcProfileDefinition.cs
    GearTable.cs
    GearTableLoader.cs
    FastCdcCandidate1Profile.cs

  Profiles/FixedSize/
    FixedSizeProfileDefinition.cs
    FixedSizeV1Profile.cs

  Chunking/
    ChunkRegion.cs
    ChunkDescriptor.cs
    ChunkBoundaryKind.cs
    ChunkContentKey.cs
    IChunkBoundaryFinder.cs
    IChunker.cs

  Chunking/FastCdc/
    FastCdcBoundaryFinder.cs
    FastCdcChunker.cs
    FastCdcOptions.cs

  Chunking/FixedSize/
    FixedSizeBoundaryFinder.cs
    FixedSizeChunker.cs

  Hashing/
    IChunkHasher.cs
    Sha256ChunkHasher.cs
    WholeContentHasher.cs

  Manifest/
    ChunkManifest.cs
    ManifestIdentity.cs
    ManifestEnvelope.cs
    ManifestCreatedBy.cs
    ManifestIdentityValidator.cs
    ManifestValidationResult.cs
    ManifestReadOptions.cs
    ManifestWriteOptions.cs

  Manifest/BinaryCanonical/
    IdentityHashV1.cs
    BinaryCanonicalIdentityV1Writer.cs
    BinaryCanonicalConstants.cs
    BinaryCanonicalStringWriter.cs

  Manifest/Json/
    ChunkManifestJson.cs
    ChunkShiftJsonContext.cs
    ManifestV1Dto.cs
    IdentityV1Dto.cs
    EnvelopeV1Dto.cs
    ChunkV1Dto.cs
    ManifestV1Mapper.cs
    Hash256JsonConverter.cs
    IdJsonConverters.cs
    BoundaryKindJsonConverter.cs

  Diff/
    ManifestDiffSummary.cs
    ManifestDiffWarning.cs
    ManifestDiffWarningCode.cs
    ManifestReadWarning.cs
    ManifestReadWarningCode.cs
    DiffCompatibility.cs
    ChunkDeltaEntry.cs
    ChunkDeltaStatus.cs
    InMemoryManifestDiffResult.cs
    ManifestDiffAgainstSourceResult.cs
    ManifestCompareOptions.cs
    CrossProfileComparisonMode.cs
    ManifestDiffer.cs
    DeltaSequenceEnumerator.cs

  Verification/
    VerificationResult.cs
    VerificationFailureCode.cs
    VerificationOptions.cs
    ManifestVerifier.cs

  Engine/
    ChunkShiftEngine.cs
    ChunkShiftEngineOptions.cs
    ManifestCreationOptions.cs
    WholeContentHashMode.cs

  IO/
    PipeReaderFactory.cs
    PooledBuffer.cs
    StreamCopyHelpers.cs

  Exceptions/
    ChunkShiftException.cs
    ChunkShiftManifestFormatException.cs
    ChunkShiftCompatibilityException.cs
    ChunkShiftProfileException.cs
    ChunkShiftVerificationException.cs
    ChunkShiftHashMismatchException.cs

  Diagnostics/
    ChunkShiftEventSource.cs
    ChunkShiftMeter.cs
    ChunkShiftActivitySource.cs

  Internal/
    Guard.cs
    ThrowHelper.cs
    Hex.cs
    TimeProviderShim.cs
```

---

# 37. File-by-file responsibilities

## 37.1. Primitives

### `Hash256.cs`

Отвечает за:

```text
- raw 256-bit digest;
- parsing hex-lower;
- formatting hex-lower;
- fixed-time comparison;
- equality;
- GetHashCode;
- byte copy;
- default/zero rejection.
```

Не отвечает за:

```text
- SHA-256 computation;
- algorithm selection;
- JSON serialization directly.
```

---

### `HashAlgorithmId.cs`

Отвечает за:

```text
- validated algorithm ID;
- grammar enforcement;
- ordinal equality.
```

Known IDs отдельно:

```csharp
public static class HashAlgorithmIds
{
    public static readonly HashAlgorithmId Sha256 = new("sha256");
}
```

---

### `IdGrammar.cs`

Отвечает за:

```text
- ASCII lowercase validation;
- allowed chars;
- max length;
- shared validation for all ID types.
```

---

## 37.2. Profiles

### `ChunkingProfile.cs`

Full resolved profile definition:

```csharp
public sealed class ChunkingProfile
{
    public ChunkerId ChunkerId { get; }
    public ChunkingProfileId ProfileId { get; }
    public ChunkingProfileStability Stability { get; }
    public Hash256 SpecSha256 { get; }

    public int MinSize { get; }
    public int AverageSize { get; }
    public int MaxSize { get; }

    public IReadOnlyDictionary<string, string> Parameters { get; }
}
```

---

### `ChunkingProfileRef.cs`

Stored in manifest identity:

```csharp
public readonly record struct ChunkingProfileRef(
    ChunkerId ChunkerId,
    ChunkingProfileId ProfileId,
    Hash256 SpecSha256);
```

---

### `ChunkingProfileRegistry.cs`

Отвечает за:

```text
- resolving ProfileId to full ChunkingProfile;
- checking SpecSha256;
- exposing built-in profiles;
- rejecting unknown profiles in normal mode.
```

Основные методы:

```csharp
public sealed class ChunkingProfileRegistry
{
    public ChunkingProfile Resolve(ChunkingProfileId profileId);

    public bool TryResolve(
        ChunkingProfileId profileId,
        out ChunkingProfile profile);

    public IReadOnlyList<ChunkingProfile> ListProfiles();
}
```

---

### `ProfileChecksumValidator.cs`

Проверяет:

```text
- SpecSha256 from profile artifact;
- GearTableSha256 from .gear.bin;
- profile JSON consistency.
```

---

## 37.3. Chunking

### `IChunkBoundaryFinder.cs`

```csharp
public interface IChunkBoundaryFinder
{
    IAsyncEnumerable<ChunkRegion> FindRegionsAsync(
        PipeReader reader,
        ChunkingProfile profile,
        CancellationToken cancellationToken = default);
}
```

Ownership:

```text
Does not complete caller-supplied PipeReader.
```

---

### `FastCdcBoundaryFinder.cs`

Отвечает только за boundaries.

Не считает SHA-256.

Не создаёт manifest.

Не пишет JSON.

---

### `FixedSizeBoundaryFinder.cs`

Baseline implementation.

Используется для:

```text
- tests;
- baseline benchmarks;
- deterministic simple profile.
```

---

## 37.4. Hashing

### `Sha256ChunkHasher.cs`

Отвечает за:

```text
- SHA-256 chunk hash computation;
- returning Hash256.
```

### `WholeContentHasher.cs`

Отвечает за:

```text
- optional whole content hash;
- incremental hashing during source read.
```

---

## 37.5. Manifest

### `ManifestIdentity.cs`

Immutable-by-construction.

Создаётся только через factory.

Считает `IdentityHash`.

Валидирует invariants.

---

### `ManifestIdentityValidator.cs`

Отвечает за all manifest invariants.

Должен использоваться:

```text
- при создании manifest;
- при чтении JSON manifest;
- перед IdentityHash recomputation;
- в VerifyAsync.
```

---

### `ChunkManifest.cs`

Container:

```text
Identity + Envelope + Metadata
```

Не определяет semantic equality.

---

## 37.6. BinaryCanonical

### `IdentityHashV1.cs`

Main API:

```csharp
public static class IdentityHashV1
{
    public static Hash256 Compute(ManifestIdentity identity);

    public static Hash256 Compute(
        ManifestSchemaVersion schemaVersion,
        ChunkingProfileRef profile,
        HashAlgorithmId chunkHashAlgorithm,
        long contentLength,
        ImmutableArray<ChunkDescriptor> chunks,
        Hash256? wholeContentHash);
}
```

---

### `BinaryCanonicalIdentityV1Writer.cs`

Writes canonical bytes into hasher.

Не создаёт giant byte array.

Использует:

```text
IncrementalHash
BinaryPrimitives
stackalloc
```

---

## 37.7. JSON

### `ManifestV1Dto.cs`

Wire DTO.

Не domain model.

---

### `ManifestV1Mapper.cs`

Maps:

```text
domain -> DTO
DTO -> domain
```

При `DTO -> domain` вызывает manifest validator.

---

### `Hash256JsonConverter.cs`

Reads/writes hex-lower.

Rejects:

```text
wrong length
non-lowercase if strict mode
invalid chars
zero/default hash
```

---

## 37.8. Diff

### `ManifestDiffer.cs`

Core diff logic.

Основные методы:

```csharp
public ManifestDiffSummary CompareSummary(
    ManifestIdentity oldIdentity,
    ManifestIdentity newIdentity,
    ManifestCompareOptions options);

public InMemoryManifestDiffResult CompareInMemory(
    ManifestIdentity oldIdentity,
    ManifestIdentity newIdentity,
    ManifestCompareOptions options);

public IAsyncEnumerable<ChunkDeltaEntry> EnumerateDeltaSequenceAsync(
    ManifestIdentity oldIdentity,
    ManifestIdentity newIdentity,
    ManifestCompareOptions options,
    CancellationToken cancellationToken);
```

---

### `DeltaSequenceEnumerator.cs`

Generates ordered sequence.

Uses `ChunkContentKey`.

---

## 37.9. Verification

### `ManifestVerifier.cs`

Implements `VerifyAsync`.

Checks:

```text
profile
identity hash
chunk sequence
chunk offsets
lengths
hashes
boundary kinds
whole content hash if present
```

---

## 37.10. Engine

### `ChunkShiftEngine.cs`

High-level orchestrator.

Does:

```text
CreateManifestAsync
CompareSummary
CompareInMemory
EnumerateDeltaSequenceAsync
CreateDiffAgainstSourceAsync
VerifyAsync
```

Does not:

```text
store chunks
restore
upload
compress
encrypt
sign
```

---

# 38. CLI architecture

## 38.1. CLI folders

```text
src/ChunkShift.Cli/
  Program.cs

  Commands/
    ManifestCommand.cs
    DiffCommand.cs
    VerifyCommand.cs
    InspectCommand.cs
    ProfilesCommand.cs
    DevChunkCommand.cs
    DevBenchmarkCommand.cs

  Output/
    ConsoleOutput.cs
    JsonOutput.cs
    TableOutput.cs

  IO/
    AtomicFileWriter.cs
    StandardStreams.cs

  Diagnostics/
    CliExitCodes.cs
    CliErrorFormatter.cs
```

---

## 38.2. Exit codes

```text
0  success
1  general error
2  invalid arguments
3  manifest format error
4  compatibility error
5  verification failed
6  unsupported profile
7  operation canceled
```

---

## 38.3. Commands

### `chunkshift manifest`

```bash
chunkshift manifest ./file.bin --out file.csm.json
```

Options:

```text
--profile fastcdc.gear.candidate1.64k
--whole-hash
--json
--out <path>
```

Behavior:

```text
Creates manifest.
Writes to temp file first if --out is used.
Moves temp to final on success.
Progress to stderr.
Data to stdout if no --out.
```

---

### `chunkshift diff`

```bash
chunkshift diff old.csm.json new.csm.json
```

Options:

```text
--allow-cross-profile
--sequence
--json
```

Default:

```text
reject cross-profile comparison
```

---

### `chunkshift verify`

```bash
chunkshift verify ./file.bin --manifest file.csm.json
```

Exit code:

```text
0 if valid
5 if verification failed
```

---

### `chunkshift inspect`

```bash
chunkshift inspect file.csm.json
```

Shows:

```text
schema
profile id
spec hash
content length
chunk count
whole hash presence
identity hash
created by
created utc
warnings
```

---

### `chunkshift profiles list`

Lists built-in profiles.

---

### `chunkshift profiles inspect`

```bash
chunkshift profiles inspect fastcdc.gear.candidate1.64k
```

Shows:

```text
ProfileId
ChunkerId
Stability
SpecSha256
GearTableSha256
Min/avg/max
Masks
EOF behavior
```

---

### dev commands

```bash
chunkshift dev chunk ./file.bin
chunkshift dev benchmark ./dataset
```

Not stable CLI contract in v0.1.

---

# 39. Build configuration

## 39.1. Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>

    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>

    <IsAotCompatible>true</IsAotCompatible>
    <EnablePackageValidation>true</EnablePackageValidation>

    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>

    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <RepositoryType>git</RepositoryType>
  </PropertyGroup>
</Project>
```

Не добавлять:

```xml
<LangVersion>latest</LangVersion>
```

---

## 39.2. Directory.Packages.props

```xml
<Project>
  <ItemGroup>
    <PackageVersion Include="System.Collections.Immutable" Version="*" />
    <PackageVersion Include="System.CommandLine" Version="*" />
    <PackageVersion Include="BenchmarkDotNet" Version="*" />
    <PackageVersion Include="xunit" Version="*" />
    <PackageVersion Include="FluentAssertions" Version="*" />
  </ItemGroup>
</Project>
```

Версии зафиксировать конкретно при создании repo.

---

# 40. CI architecture

## 40.1. ci.yml

Matrix:

```text
ubuntu-latest
windows-latest
macos-latest
net8.0
net10.0
```

Steps:

```text
restore
build
test
profile checksum validation
gear table checksum validation
golden vector tests
JSON DTO tests
diff semantics tests
```

---

## 40.2. deterministic.yml

Purpose:

```text
Ensure deterministic output across OS/runtime.
```

Checks:

```text
same sample input
same profile
same ChunkRegions
same ChunkDescriptors
same IdentityHash
```

---

## 40.3. release.yml

v0.1:

```text
pack
Source Link
repository metadata
Trusted Publishing if GitHub Actions configured
```

Package validation baseline fully matters after first stable baseline release.

---

# 41. Test strategy

## 41.1. Must-have tests

```text
1. Hash256 equality.
2. Hash256 parsing.
3. Hash256 formatting.
4. Hash256 endian mapping.
5. ID grammar.
6. Profile JSON checksum.
7. Gear table checksum.
8. FastCDC golden vectors.
9. FixedSize golden vectors.
10. Binary Canonical Identity hash vectors.
11. ManifestIdentity validation.
12. JSON DTO read/write.
13. Unknown field policy.
14. Diff logical metrics.
15. Diff unique metrics.
16. Repeated chunk diff semantics.
17. Cross-profile rejection.
18. Cross-profile warning mode.
19. Verify success.
20. Verify failure.
21. Corrupt manifest tests.
22. Candidate ProfileId immutability tests.
23. AOT smoke.
24. Trim smoke.
```

---

## 41.2. Corrupt manifest tests

```text
negative ContentLength
non-contiguous offsets
duplicate indexes
skipped indexes
chunk length zero
chunk length > maxSize
invalid BoundaryKind
IdentityHash mismatch
SpecSha256 mismatch
unknown identity field
unsupported schema version
unsupported profile ID
default Hash256 in chunk
EndOfStream not final
FixedSize profile emits ContentDefined
FastCDC profile emits FixedSize
```

---

## 41.3. Fuzz-like edge tests v0.1

```text
empty input
1 byte
min-1
min
min+1
max-1
max
max+1
zero-filled
0xff-filled
random
repeated chunks
local insert
local delete
append-only
```

Full fuzzing tool selection can be v0.2.

---

# 42. Benchmark strategy

Use BenchmarkDotNet.

v0.1 benchmark CLI hidden/dev.

Metrics:

```text
throughput MB/s
allocated bytes per MB
chunk count
average chunk size
p50/p95/p99 chunk size
chunk-size variance
reuse ratio after insert/delete/edit
boundary stability after local edit
logical vs unique added bytes
identity hash time
manifest write time
manifest read time
```

Datasets later:

```text
random bytes
zeros
low entropy
logs
JSONL
SQLite DB
tar archive
compressed archive
large binary
repeated chunks
append-only file
local insert near beginning
```

---

# 43. Documentation structure

```text
docs/
  quickstart.md
  roadmap.md
  security.md
  compatibility-policy.md
  manifest-format.md
  binary-canonical-identity-v1.md
  diff-semantics.md
  profile-policy.md
  cli.md
  performance.md
  aot-trimming.md

  specs/
    profiles/
      fastcdc-gear-candidate1-64k.md
      fixed-v1-64k.md
```

---

## 43.1. README structure

```text
1. What is ChunkShift?
2. What it is not.
3. Quickstart.
4. Create manifest.
5. Diff manifests.
6. Verify file.
7. Profiles.
8. Candidate vs stable warning.
9. Security warning.
10. Roadmap.
```

---

# 44. Roadmap

## v0.1 — Candidate manifest/diff/verify SDK

```text
ChunkShift
ChunkShift.Cli

FastCDC/Gear candidate1 64k
FixedSize stable baseline
SHA-256
Hash256
ManifestIdentity / ManifestEnvelope
Binary Canonical Identity v1
JSON diagnostic manifest v1
IdentityHash
Manifest diff summary
Optional ordered delta sequence
Verify
Minimal CLI
Golden tests
AOT/trim smoke tests
```

---

## v0.2 — Binary manifest and hardening

```text
binary manifest candidate
footer IdentityHash
streaming manifest production path
full fuzzing setup
FileSystemChunkStore prototype
restore prototype
store missing prototype
OpenTelemetry basic metrics
```

---

## v0.3 — Storage and local index

```text
IChunkStore
IBatchChunkStore
IChunkStoreMaintenance
store verify
store prune
SQLite chunk index
ref counting
store compaction
directory manifest prototype
```

---

## v0.4 — Classic/advanced algorithms

```text
Rabin stable
UltraCDC preview
SuperCDC experimental
benchmark comparison
```

---

## v0.5+ — Research/high-throughput

```text
SeqCDC scalar prototype
VectorCDC SIMD experiments
strict scalar/vector equivalence tests
hardware intrinsics
```

---

## v1.0 — Stable archive-grade profile

```text
FastCDC.V1 profile frozen
actual Gear table published
canonical profile artifact published
SpecSha256 fixed
GearTableSha256 fixed
masks fixed
normalization fixed
EOF behavior fixed
golden vectors stable
ManifestIdentity v1 frozen
public API reviewed
Source Link enabled
Trusted Publishing enabled
AOT/trim tests pass
security docs complete
benchmark report published
CLI stable
```

---

# 45. Implementation order

## Sprint 0: contracts first

Day 1:

```text
repository setup
global.json
Directory.Build.props
Directory.Packages.props
CI skeleton
```

Day 2:

```text
Hash256
ID value objects
ID grammar tests
Hash256 endian tests
```

Day 3:

```text
GearTableGenerator
.gear.bin
profile.json
GearTableSha256
SpecSha256 validation
```

Day 4:

```text
BinaryCanonicalIdentityV1 writer
IdentityHashV1 vectors
```

Day 5:

```text
ManifestIdentity immutable factory
Manifest validation invariants
corrupt manifest tests
```

Day 6:

```text
FastCDC candidate boundary finder
FixedSize boundary finder
golden vectors
```

Day 7:

```text
CreateManifestAsync
CompareSummary
VerifyAsync skeleton
```

---

# 46. Blocking items before public v0.1

```text
1. Select and commit actual Gear table values.
2. Commit fastcdc-gear-candidate1-64k.profile.json.
3. Commit fastcdc-gear-candidate1-64k.gear.bin.
4. Define GearTableSha256 and SpecSha256.
5. Implement Hash256.
6. Define Binary Canonical Identity v1 writer.
7. Define IdentityHashV1 test vectors.
8. Define ChunkingProfileRef.
9. Implement immutable-by-construction ManifestIdentity.
10. Implement manifest validation invariants.
11. Define ChunkContentKey.
12. Define ManifestCompareOptions.
13. Define ManifestDiffSummary formulas.
14. Define VerificationResult.
15. Define exception hierarchy with actionable verification fields.
16. Define ManifestCreationOptions defaults.
17. Define JSON DTO layer.
18. Define JSON unknown-field policy.
19. Define CLI output/temp-file policy.
20. Add golden vector tests.
21. Add corrupt manifest tests.
22. Add cross-platform deterministic CI.
23. Add AOT/trim smoke tests.
```

---

# 47. Open questions before v1.0

```text
1. Which exact Gear table should become stable v1?
2. Should fastcdc.gear.v1.64k be promoted from candidate1 or candidateN?
3. Should binary manifest v0.2 reuse Binary Canonical Identity v1 as its core?
4. Should full fuzzing use SharpFuzz, dotnet-fuzz, or another tool?
5. Should external-memory diff be supported before v1.0?
6. Should BLAKE3 extension target v0.3 or later?
7. Should CLI benchmark become public stable in v0.2 or stay dev-only?
8. Should UltraCDC enter preview before or after Rabin stable profile?
9. Should FixedSize profiles include more sizes in v0.1?
10. Should profile registry allow external custom profiles in v0.1 or only built-ins?
```

---

# 48. Final engineering stance

ChunkShift v0.1 should not be sold as:

```text
production archive-grade dedup SDK
```

It should be sold as:

```text
deterministic manifest/diff/verify SDK for .NET,
with immutable candidate profile IDs,
Binary Canonical Identity,
SHA-256 Hash256 chunk identity,
strict validation invariants,
and manifest-to-manifest diff semantics.
```

Stable archive-grade FastCDC compatibility starts at v1.0, not v0.1.

---

# 49. Самое важное для реализации

Первым делом не писать CLI.

Первым делом зафиксировать:

```text
1. Hash256.
2. ID grammar.
3. profile.json.
4. gear.bin.
5. BinaryCanonicalIdentityV1.
6. ManifestIdentity immutable factory.
7. Manifest validation invariants.
8. FastCDC golden vectors.
```

Пока этих артефактов нет, ChunkShift ещё не имеет настоящего deterministic core.

После них можно писать engine, CLI и документацию.
