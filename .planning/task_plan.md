# Task: Upgrade HR Reserve System to a Solid 7.5 - 8.0/10 B2B ATS Product

## Goal
Transform the system from an academic/portfolio prototype (~4.0 B2B score) into a genuine, highly competitive B2B ATS platform (7.5 - 8.0/10 score) by implementing 7 industry-standard features:
1. Interactive Visual Kanban Pipeline (Drag-and-Drop / AJAX stage transitions)
2. Real Calendar Integration (RFC 5545 .ics export + 1-click Google Calendar add)
3. Candidate Slide-Over Quick-View Drawer with Recruiter Team Notes timeline
4. Quick Resume Text & Multi-Channel Importer (Djinni / LinkedIn text auto-parser)
5. 1-Click Candidate Communication Templates (Screening, Tech Interview, Offer, Rejection)
6. Recruitment Funnel & Conversion Rate Analytics Dashboard
7. CSV / Excel Data Export for Reporting

## Phases & Checklist

- [x] **Phase 1: Candidate Kanban Pipeline & AJAX Status Transitions**
  - [x] Add `UpdateStatusAjax` endpoint in `ApplicationsController.cs` with antiforgery / auth checks.
  - [x] Add Kanban View (`/Applications/Kanban` with table/kanban toggle button group on `/Applications/Index`) with vacancy filter.
  - [x] Implement HTML5 Drag & Drop cards between pipeline stages: `Новий` -> `Скринінг` -> `Співбесіда` -> `Тестове завдання` -> `Оффер` -> `Найнятий` / `Відхилено`.
  - [x] Live counter badge per stage column and instant visual feedback.

- [x] **Phase 2: Calendar Integration (.ics Export & Google Calendar Links)**
  - [x] Implement RFC 5545 `.ics` generator endpoint `GET /Interviews/ExportIcs/{id}` with DTSTART, DTEND, SUMMARY, DESCRIPTION, LOCATION, and ATTENDEE.
  - [x] Add "📅 Додати в Google Календар" direct web links in Interview Details and Calendar views.
  - [x] Add "📥 Завантажити .ics" button for Outlook/Apple Calendar.
  - [x] Add direct "🎥 Приєднатися до зустрічі" quick action for video interviews.

- [x] **Phase 3: Candidate Slide-Over Quick-View & Recruiter Notes Timeline**
  - [x] Add integrated notes timeline for recruiter observations in Application and Candidate models.
  - [x] Implement AJAX endpoint `GET /Candidates/QuickViewJson/{id}` for slide-over drawer.
  - [x] Implement AJAX endpoint `POST /Candidates/AddNoteAjax` to save notes with timestamp and author.
  - [x] Implement offcanvas slide-over drawer in `Candidates/Index` and `Applications/Kanban`.

- [x] **Phase 4: Candidate Communication Templates (1-Click Outreach)**
  - [x] Implement modal with pre-built Ukrainian HR templates:
    * Запрошення на перший HR-скринінг
    * Запрошення на технічну співбесіду (з датою та посиланням на зустріч)
    * Пропозиція про роботу (Job Offer)
    * Тактовна та поважна відмова (Rejection with feedback)
  - [x] Auto-replacement of tags: `{CandidateName}`, `{Position}`, `{Company}`, `{InterviewDate}`, `{MeetingLink}`.
  - [x] Action buttons: "Копіювати", "Відкрити в пошті (mailto:)", "Надіслати в Telegram".

- [x] **Phase 5: Quick Resume Text & Profile Importer**
  - [x] Add `/Candidates/QuickImport` view and action.
  - [x] Create smart parser endpoint (`POST /Candidates/QuickImportParse`) for raw pasted text (extracts full name, email, phone, experience years, skills).
  - [x] Pre-populate candidate create form in 1 click for lightning-fast candidate logging.

- [x] **Phase 6: Recruitment Funnel & Conversion Analytics**
  - [x] Integrated pipeline conversion funnel widget on dashboard (`/Home/Index`) with visual stage breakdown and stage counts.
  - [x] Added direct Kanban pipeline navigation links from dashboard and sidebar.

- [x] **Phase 7: CSV / Excel Export**
  - [x] Verified `GET /Candidates/ExportCsv` with UTF-8 BOM encoding for seamless opening in Microsoft Excel.
  - [x] Export key candidate attributes: Name, Email, Phone, Skills, Experience, Applications, Scores, CreatedDate.

- [x] **Phase 8: Verification, Automated Tests & VPS Deployment**
  - [x] Written 5 new end-to-end integration tests in `HRReserveSystem.Tests/HrIntegrationTests.cs`.
  - [x] Run `dotnet test` - 49 of 49 tests passed (100% success rate).
  - [x] Packaged clean deployment tarball, SCP to VPS, rebuilt Docker containers, and verified live at `https://hrreservesystem.duckdns.org` (HTTP 302 -> /Account/Login).
