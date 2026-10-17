# Project Architecture & Operational Runbook: HR Reserve System

## 1. System Overview
- **Domain:** Internal Corporate ATS & Candidate Reserve Management System.
- **Framework:** ASP.NET Core 8.0 MVC + Entity Framework Core 8.0.
- **Frontend:** Vanilla JS (ES6+), Bootstrap 5.3 + custom Scandinavian/Swiss design tokens, SignalR for real-time Kanban sync.
- **Database:**
  - Local / Development / Tests: SQLite (`hrreserve.db` in WAL mode).
  - Production (VPS): PostgreSQL 16 on Docker (`postgres:16-alpine`), connection string via `DATABASE_URL`.
- **Infrastructure & Deployment:**
  - Oracle Cloud VPS: `152.70.19.219`, user `ubuntu`, key `c:\Users\maks2\OneDrive\Desktop\ssh\oracle_a1.txt`.
  - Docker Compose: `hrreservesystem-app-1` (port 8080) + `hrreservesystem-postgres-1` (port 5432).
  - Reverse Proxy: Nginx on host with Let's Encrypt SSL.
  - Live Domain: `https://hrreservesystem.duckdns.org`
  - Health checks: `/health/live`, `/health/ready`.

## 2. Security & RBAC Invariants
- **Roles:** `Admin`, `Recruiter`, `Interviewer`.
- **Seed Logins:**
  - Admin: `admin` / `admin123`
  - Recruiter: `recruiter` / `recruiter123`
  - Interviewer: `interviewer` / `interviewer123`
- **Audit Logging:** Every sensitive operation (Resume download, CSV export, Candidate edit/delete/restore, Outreach email, Kanban status change) logs to `AuditLogs` table via `IAuditService`.
- **Durable Outbox:** Candidate emails are stored atomically in `EmailOutboxMessages` with retry count, drain on startup, and dead-letter handling.
- **Protected File Storage:** Resumes are isolated in `App_Data/Storage/Resumes`, direct URL `/uploads/resumes/` blocked via middleware.

## 3. Test Suite
- Project: `HRReserveSystem.Tests` (xUnit, WebApplicationFactory).
- Current Baseline: 62 integration and unit tests passing with zero warnings and zero failures.
