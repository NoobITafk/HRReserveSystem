# HRReserveSystem — Enterprise Talent Acquisition & HR Reserve Management Platform

[![.NET 8](https://img.shields.io/badge/.NET-8.0%20LTS-purple.svg)](https://dotnet.microsoft.com/)
[![CI](https://github.com/NoobITafk/HRReserveSystem/actions/workflows/ci.yml/badge.svg)](https://github.com/NoobITafk/HRReserveSystem/actions/workflows/ci.yml)
[![C# 12](https://img.shields.io/badge/C%23-12-blue.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![Database](https://img.shields.io/badge/Database-PostgreSQL%2016%20%7C%20SQLite-0064a5.svg)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Ready-2496ED.svg)](https://www.docker.com/)
[![Tests](https://img.shields.io/badge/Tests-80%20Passed-brightgreen.svg)](https://xunit.net/)
[![Live Demo](https://img.shields.io/badge/Live%20Demo-hrreservesystem.duckdns.org-success.svg)](https://hrreservesystem.duckdns.org)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

Сучасна, масштабована платформа промислового рівня для автоматизації повного циклу рекрутингу та управління кадровим резервом (**ATS — Applicant Tracking System**), побудована на стеку **ASP.NET Core 8 (.NET 8 LTS)** та **Entity Framework Core 8**.

---

## 🌐 Живий продакшн (Live Demo)

Система розгорнута на виділеному сервері під керуванням Nginx Reverse Proxy з автоматичним SSL (Let's Encrypt):

🔗 **[https://hrreservesystem.duckdns.org](https://hrreservesystem.duckdns.org)**

### Демо-доступи:

| Роль | Логін | Пароль | Сфера відповідальності |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin` | `admin123` | Повний доступ до вакансій, кандидатів, аналітики, аудиту та персоналу |
| **Recruiter** | `recruiter` | `recruiter123` | Управління кандидатами, вакансіями, воронкою відбору та співбесідами |
| **Interviewer** | `interviewer` | `interviewer123` | Календар інтерв'ю, внесення зворотного зв'язку та оцінка Soft Skills |

---

## 🎬 Демонстрація інтерфейсу (Silent Video Showcase)

![HRReserveSystem Live Preview](Docs/hrreserve_preview.gif)

> 📹 **Повний відеоролик високої чіткості (Full HD 1080p, без звуку):**  
> [Завантажити або переглянути Docs/hrreserve_demo_silent.mp4](Docs/hrreserve_demo_silent.mp4)

---

## 🌟 Ключові можливості платформи

- **📋 Інтерактивна Канбан-дошка (Candidate Pipeline):**
  - Візуалізація етапів рекрутингу (`New` $\rightarrow$ `Screening` $\rightarrow$ `Interview` $\rightarrow$ `TestTask` $\rightarrow$ `Offer` $\rightarrow$ `Hired` / `Rejected`).
  - Миттєве перетягування карток через Drag-and-Drop з AJAX-синхронізацією стану (`POST /MoveStage`).
- **👤 Профіль кандидата та резюме:**
  - Повний життєвий цикл кандидата, детальний стек навичок, історія подач та комунікацій.
  - Безпечне завантаження резюме (`.pdf`, `.doc`, `.docx` до 5 MB) зі збереженням під унікальним `GUID` та захищеним завантаженням.
  - CSV експорт бази талантів з фільтрацією за досвідом та ключовими навичками.
- **📅 Планування співбесід та календарна синхронізація:**
  - Фіксація дат, типу зустрічі (технічна, ейчар, фінальна), призначеного рекрутера та результату.
  - Автоматична генерація посилань на Google Calendar та експорт подій у форматі `.ics` (iCalendar RFC 5545).
- **⭐ Багаторівнева оцінка Soft Skills:**
  - 5-факторний вектор оцінки: *Комунікація*, *Командна робота*, *Відповідальність*, *Стресостійкість*, *Лідерство* (шкала 1–10).
  - Автоматичний розрахунок зведеного індексу кандидата та візуалізація матриці компетенцій.
- **💬 Фідбеки інтерв'юерів та скоринг:**
  - Структуровані відгуки після кожного етапу з фіксацією рекомендацій (`Hire`, `Strong Hire`, `Hold`, `Reject`).
- **📨 Transactional Outbox & Email Notifications:**
  - Гарантована доставка сповіщень через патерн Transactional Outbox (`EmailOutbox` з fallback у разі недоступності SMTP).
- **🔒 Безпека промислового рівня (OWASP Compliance):**
  - Автентифікація через ASP.NET Core Identity з безпечними HttpOnly/SameSite cookies та PBKDF2 хешуванням.
  - Захист від CSRF (Anti-Forgery Tokens), Rate Limiting для публічних форм прийому заявок (`/Careers/Apply`).
  - Незмінний аудит-лог дій користувачів (`AuditLogs`) з прив'язкою IP-адреси, часу та типу дії.

---

## 🏛️ Архітектура системи

```mermaid
flowchart TD
    Client["Веб-браузер / Зовнішній API-клієнт"] --> Proxy["Nginx Reverse Proxy & SSL"]
    Proxy --> Kestrel["Kestrel Web Server (.NET 8 LTS)"]
    
    subgraph Middlewares["Middleware Pipeline"]
        Kestrel --> RateLimit["IP Rate Limiter"]
        RateLimit --> SecurityHeaders["OWASP Security Headers"]
        SecurityHeaders --> Auth["Identity Auth & RBAC"]
    end

    subgraph MVC["Контролери та Razor Views"]
        Auth --> WebControllers["MVC Controllers (Candidates, Vacancies, Interviews)"]
        Auth --> ApiControllers["REST API (/api/*)"]
    end

    subgraph Services["Сервісний шар (Business Logic)"]
        WebControllers --> DashSvc["IDashboardService (Аналітика)"]
        WebControllers --> CareerSvc["ICareersService (Публічні заявки)"]
        WebControllers --> CalSvc["IGoogleCalendarSyncService (iCal / Google Cal)"]
        WebControllers --> OutboxSvc["IEmailNotificationService (Transactional Outbox)"]
        WebControllers --> AuditSvc["IAuditLogService (Незмінний аудит дій)"]
    end

    subgraph Storage["Шар збереження даних"]
        DashSvc --> EFCore["Entity Framework Core 8 (DbContext)"]
        CareerSvc --> EFCore
        AuditSvc --> EFCore
        EFCore --> Postgres[("PostgreSQL 16 (Production) / SQLite (Dev/Test)")]
        CareerSvc --> Files["Secure Resumes Storage (/uploads/resumes)"]
    end
```

---

## 🔄 Життєвий цикл кандидата (Recruitment Funnel)

```mermaid
flowchart LR
    Apply["Подача резюме / Додавання"] --> Screening["Первинний скринінг"]
    Screening --> Interview["Співбесіда & Soft Skills"]
    Interview --> TestTask["Тестове завдання"]
    TestTask --> Offer["Оффер кандидату"]
    Offer --> Hired["Прийнятий в штат 🎉"]
    
    Screening -.-> Rejected["Відхилено"]
    Interview -.-> Rejected
    TestTask -.-> Rejected
```

---

## 🗄️ Схема бази даних (ER Diagram)

Детальний опис сутностей та зв'язків доступний у [Docs/ARCHITECTURE.md](Docs/ARCHITECTURE.md).

```mermaid
erDiagram
    CANDIDATE ||--o{ APPLICATION : submits
    VACANCY ||--o{ APPLICATION : contains
    APPLICATION ||--o{ INTERVIEW : schedules
    INTERVIEW ||--o{ INTERVIEW_FEEDBACK : evaluates
    CANDIDATE ||--o{ SOFT_SKILL_ASSESSMENT : assesses
    RECRUITER ||--o{ INTERVIEW : conducts
    RECRUITER ||--o{ INTERVIEW_FEEDBACK : provides

    CANDIDATE {
        int Id PK
        string FullName
        string Email
        string Phone
        string Skills
        string ResumeFilePath
        int ExperienceYears
        datetime CreatedAt
    }

    VACANCY {
        int Id PK
        string Title
        string Description
        decimal SalaryMin
        decimal SalaryMax
        string Status
    }

    APPLICATION {
        int Id PK
        int CandidateId FK
        int VacancyId FK
        string Status
        datetime AppliedAt
    }

    INTERVIEW {
        int Id PK
        int ApplicationId FK
        int RecruiterId FK
        datetime InterviewDate
        string InterviewType
        string Result
    }

    SOFT_SKILL_ASSESSMENT {
        int Id PK
        int CandidateId FK
        int Communication
        int Teamwork
        int Responsibility
        int StressResistance
        int Leadership
    }
```

---

## 🧪 Автоматизоване тестування (CI Quality Gate)

Рішення має повне покриття інтеграційними тестами на базі `xUnit` та `WebApplicationFactory`.

```bash
dotnet test --verbosity normal
```

```text
Passed!  - Failed: 0, Passed: 80, Skipped: 0, Total: 80, Duration: 25 s - HRReserveSystem.Tests.dll (net8.0)
```

Перевірені сценарії:
- Автентифікація, cookie lifecycle та розмежування прав за ролями (`Admin`, `Recruiter`, `Interviewer`).
- Повний цикл подачі заявки кандидатом через публічний портал `/Careers` з IP Rate Limiter.
- Безпека завантаження резюме (відхилення `.exe` та файлів > 5 MB, GUID-санітизація).
- Усунення колізій та обробка конкурентних запитів.
- Валідація валідності DTO у REST API (відсутність витоку парольних хешів).
- Робота Transactional Outbox та генерація `.ics` календарних файлів.

---

## 🚀 Швидкий запуск

### Варіант 1: Docker Compose

```bash
docker compose up -d
```
Сервіс доступний за адресою: `http://localhost:5000`.

### Варіант 2: .NET 8 CLI

```bash
dotnet restore
dotnet build
dotnet run --configuration Release
```

Детальні інструкції дивіться у [LAUNCH.md](LAUNCH.md) та [DEPLOYMENT.md](DEPLOYMENT.md).

---

## 📄 Ліцензія

Проєкт поширюється під ліцензією [MIT](LICENSE).
