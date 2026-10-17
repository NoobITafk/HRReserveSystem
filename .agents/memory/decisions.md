# Architectural Decision Records (ADRs)

## ADR-001: Forensic Enterprise Audit Trail (2026-10-17)
- **Context:** ATS systems process sensitive PII (resumes, salaries, contact information, evaluations). Compliance standards (SOC2, GDPR) require immutable recording of data access.
- **Decision:** Implemented `AuditLogs` entity, `IAuditService` with automatic HttpContext enrichment (user, role, IP), and dedicated `AuditLogsController` restricted to `Admin` role.
- **Consequences:** Every resume download, CSV export, and candidate lifecycle modification is traceable.

## ADR-002: Transactional Durable Outbox for Outreach Emails (2026-10-17)
- **Context:** In-memory queue (`Channel<T>`) loses unsent candidate communications if the container crashes or reboots.
- **Decision:** Messages are written to `EmailOutboxMessages` in the same database transaction as `CandidateCommunication` (`SaveChangesAsync`), then drained and delivered by `EmailQueueHostedService` with retry tracking and dead-letter protection.
- **Consequences:** Guaranteed delivery; zero message loss across restarts.

## ADR-003: Decomposition of CandidatesController (2026-10-17)
- **Context:** `CandidatesController` had grown to ~900 lines with raw filesystem manipulation and CSV string concatenation.
- **Decision:** Extracted file operations into `ICandidateFileStorageService` and CSV generation into `ICandidateExportService` (with UTF-8 BOM).
- **Consequences:** Adheres to Single Responsibility; allows future substitution of local storage with AWS S3 / Cloudflare R2 without touching controller logic.

## ADR-004: Dual-Dialect Schema Evolution (2026-10-17)
- **Context:** The system runs SQLite locally for lightweight dev/tests and PostgreSQL on production Docker VPS.
- **Decision:** Unified runtime schema updates inside `SeedData.EnsureDatabaseSchemaUpdatedAsync` handling both SQLite (`INTEGER PRIMARY KEY AUTOINCREMENT`) and PostgreSQL (`serial PRIMARY KEY`, `ALTER TABLE ... ADD COLUMN IF NOT EXISTS`).
- **Consequences:** Zero manual migration conflicts across dev and production.
