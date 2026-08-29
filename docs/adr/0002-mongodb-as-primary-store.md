# 2. MongoDB as the primary datastore

Date: 2026-08-29

## Status

Accepted

Changes the persistence technology chosen in the initial scaffold (PostgreSQL +
EF Core, commit `95ce272`).

## Context

The first backend scaffold used **PostgreSQL** via **EF Core** (`Npgsql`):

- One `IEntityTypeConfiguration<T>` per aggregate, foreign keys between
  `Project` / `Locale` / `TranslationKey` / `TranslationString`, cascade deletes,
  and a single `InitialCreate` migration as the baseline.
- Optimistic concurrency on `TranslationString` via PostgreSQL's `xmin` system
  column, mapped read-only.
- Tests run against SQLite in-memory with `EnsureCreated()`.

As the delivery model came into focus, the data access patterns turned out to be
a poor fit for a relational store:

- **Reads are document-shaped.** The primary read is "give me every approved
  string for this project + locale" - i.e. reassemble a nested
  `project -> locale -> { keyName: value }` document. On SQL this is a multi-way
  join reconstructed on every request.
- **Published bundles are denormalised blobs.** A `TranslationBundle` is an
  immutable snapshot of a whole locale's approved strings, served read-heavy to
  client SDKs and cached. It is naturally one document, not a set of rows.
- **The write model is a small aggregate with app-enforced invariants.** All the
  interesting rules (slug uniqueness, review-state transitions, "edit an approved
  string sends it back to review") already live in the domain types, not in
  database constraints. The relational schema was duplicating a subset of those
  rules, not adding safety.
- **Schema churn.** The model is still moving; EF migrations were friction on
  every shape change for little benefit given no production data yet.
- Operationally, the target hosting (Azure Container Apps) pairs more cleanly
  with a managed document store + Redis than with managed PostgreSQL for this
  workload.

## Decision

Use **MongoDB** (`MongoDB.Driver` 3.11.1) as the primary datastore for CTMS.

- Collections (names are constants on `CtmsMongoContext`): `projects`,
  `locales`, `translationKeys`, `translationStrings`, `translationBundles`,
  `auditEntries`.
- **Unique indexes replace relational constraints:** `{ slug }`,
  `{ projectId, code }`, `{ projectId, keyName }`,
  `{ translationKeyId, localeId }`, and `{ projectId, localeCode, version }` for
  bundles. Created on startup by the `MongoIndexInitializer` hosted service.
- **Referential integrity and cascades move fully into the application layer:**
  services verify parent existence before writes; repository delete operations
  explicitly remove dependent documents (a key/locale delete removes its
  `translationStrings`).
- **Optimistic concurrency uses an explicit `Version` field** on
  `TranslationString`, widened from `uint` to **`long`** with an `internal`
  setter (the infrastructure assembly advances it). The string repository's
  `UpdateAsync` filters on `{ _id, version: expected }` and `$set`s the next
  version; a matched-count of 0 raises `ConcurrencyException(long currentVersion)`.
  The public API contract (`expectedVersion` in, `version` out, `409` +
  `currentVersion`) is otherwise unchanged.
- **No unit-of-work transaction.** `NoOpUnitOfWork` implements `IUnitOfWork` as a
  no-op: each repository call is a single-document atomic write, durable on
  return. Services still call `SaveChangesAsync` so use cases read as a unit of
  work and a future multi-document transaction has one seam.
- **No migration tool.** EF migrations and the pinned `dotnet-ef` tool
  (`.config/dotnet-tools.json`) are removed. `createIndexes` is idempotent;
  unavoidable data reshapes are handled by one-off backfill commands.
- **Duplicate-key handling:** `MongoWriteExceptions.IsDuplicateKey` recognises
  E11000 and repositories translate it into `ConflictException` /
  `SlugAlreadyInUseException`.
- **BSON mapping** (`MongoMappingRegistration`): camelCase elements, ignore
  unknown fields, enums as strings, GUIDs as the standard UUID subtype
  (including `_id`), and `TranslationBundle.Entries` as an array of `{k,v}`
  documents.
- Timestamp stamping (`CreatedAt` / `UpdatedAt`) moves from
  `DbContext.SaveChanges` to `EntityStamps` extension methods called by the
  repositories before each write.
- Tests run against a real MongoDB started in-process by **`EphemeralMongo`**
  (a shared `MongoFixture`), instead of SQLite in-memory.
- The domain layer is largely unaffected - entities stay POCOs with guarded
  constructors and private setters; the review state machine gains a `Published`
  state (`Approved -> Published` via a new `publish` action, `Published ->
  NeedsReview` via `reopen`). `CTMS.Infrastructure` and the test fixtures carry
  the rest of the change.

## Consequences

### Positive

- The main read path is a single-document fetch; published bundles are stored
  and served as-is, and cache well in Redis by `(project, locale, version)`.
- No migration ceremony while the model is still moving; index changes are code
  and are idempotent.
- One place for invariants (the domain), rather than rules split between domain
  code and database DDL.
- Hosting/operational fit with the chosen Azure Container Apps + managed
  document DB + Redis stack.

### Negative / risks

- **The database no longer enforces referential integrity.** A bug in a service
  can orphan documents or miss a cascade. Mitigations: keep write paths funnelled
  through the services, cover parent-existence and cascade behaviour with tests,
  and add periodic consistency checks if needed.
- **Cross-document changes are not transactional by default.** Publishing a
  bundle + writing an audit entry spans collections; we rely on operation
  ordering and idempotency (or a MongoDB multi-document transaction on a replica
  set) rather than a single commit.
- **Concurrency is now the application's job.** Every `TranslationString` update
  path must go through the version-checked update helper; forgetting it silently
  loses the protection `xmin` gave for free. Enforced by routing all writes
  through the repository method and testing the conflict case.
- Uniqueness violations surface as driver errors (duplicate-key, code 11000)
  that the repositories must translate into `ConflictException` /
  `SlugAlreadyInUseException`, where EF previously raised typed exceptions.
- Team tooling changes: running the app locally needs a MongoDB (Docker
  Compose). Tests avoid that via `EphemeralMongo`, but it downloads a `mongod`
  binary on first use and is slower than SQLite-in-memory was.
- Ad-hoc querying/reporting loses SQL; consumers use the aggregation pipeline or
  a downstream analytics copy.
