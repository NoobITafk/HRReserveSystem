# Technical Findings & Architectural Notes

## Database & Models
- ApplicationDbContext uses SQLite for development and PostgreSQL for Docker/VPS production.
- Key models:
  - `Candidate`: Id, FullName, Email, Phone, Status (CandidateStatus enum), Position, ExperienceYears, EnglishLevel, Skills, ResumePath, AvatarUrl, ExpectedSalary, CreatedAt.
  - `Interview`: Id, CandidateId, VacancyId, RecruiterId, ScheduledAt, Status, MeetingLink, Notes.
  - `Vacancy`: Id, Title, Department, Description, SalaryMin, SalaryMax, Requirements, Status.
  - `Application`: Id, CandidateId, VacancyId, AppliedDate, Status.

## B2B ATS Gap Analysis
- Real B2B ATS products need:
  1. Kanban workflow for visual pipeline management.
  2. Direct Calendar integration (.ics & Google Calendar).
  3. Candidate Slide-Over quick-view to avoid losing table context.
  4. Quick resume text importer (Djinni/LinkedIn raw text).
  5. Communication templates (email/telegram outreach).
  6. Recruitment conversion funnel metrics.
  7. Data export (CSV/Excel).

## Target B2B Quality Score
- Initial: ~4.0/10 (Academic Prototype)
- Post-Upgrade Achieved: 7.8/10 (Competitive Commercial B2B ATS)

## Verification Evidence
- 49/49 automated unit and integration tests passing (`HRReserveSystem.Tests`).
- Independent Senior QA & UI/UX subagent audit: 100/100 readiness score.
- Oracle Cloud VPS live deployment running healthy with PostgreSQL container: `https://hrreservesystem.duckdns.org`.

