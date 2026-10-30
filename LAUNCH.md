# HRReserveSystem — Керівництво швидкого запуску (Quick Start)

Даний документ містить інструкції для швидкого локального розгортання та тестування системи.

---

## ⚡ Швидкий старт через Docker Compose (Рекомендовано)

Найпростіший спосіб підняти систему разом із базою даних PostgreSQL 16:

```bash
# 1. Запуск контейнерів додатку та СУБД у фоновому режимі
docker compose up -d

# 2. Перевірка статусу контейнерів
docker compose ps
```

Після успішного запуску веб-інтерфейс доступний за адресою:
👉 **[http://localhost:5000](http://localhost:5000)** (або `http://localhost:8080` залежно від налаштувань проксі).

---

## 💻 Локальний запуск через .NET 8 CLI

### Передумови
- Встановлений [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- SQLite (налаштований за замовчуванням для локальної розробки) або локальний екземпляр PostgreSQL.

### Кроки запуску

```bash
# 1. Відновлення NuGet-залежностей
dotnet restore

# 2. Збірка рішення
dotnet build

# 3. Запуск веб-додатку
dotnet run --configuration Release
```

Після запуску консоль виведе:
```text
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

---

## 🧪 Запуск автоматизованих тестів

Рішення покрито інтеграційними тестами на базі `xUnit` та `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory`:

```bash
dotnet test --verbosity normal
```

Усі 80 інтеграційних тестів перевіряють автентифікацію, рольовий доступ (RBAC), валідацію CV/резюме, воронку кандидатів, Google Calendar інтеграцію, Outbox патерн та Rate Limiting.

---

## 🔑 Тестові облікові записи (Seed Data)

Для тестування функціоналу за ролями в базі даних автоматично створюються наступні користувачі:

| Роль | Логін | Пароль | Сфера відповідальності |
| :--- | :--- | :--- | :--- |
| **Admin** | `admin` | `admin123` | Повний доступ до конфігурації, персоналу, аналітики та аудиту |
| **Recruiter** | `recruiter` | `recruiter123` | Управління кандидатами, вакансіями, воронкою відбору та співбесідами |
| **Interviewer** | `interviewer` | `interviewer123` | Перегляд графіку інтерв'ю, внесення зворотного зв'язку та оцінка Soft Skills |
