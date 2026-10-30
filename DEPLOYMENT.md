# HRReserveSystem — Інструкція з розгортання у продакшн (Production Deployment)

Даний документ регламентує процеси розгортання системи **HRReserveSystem** у хмарних середовищах та на виділених Linux/Docker серверах.

---

## 🌐 1. Архітектура продакшен-середовища

- **Operating System:** Linux (Ubuntu 22.04 LTS / Alpine Linux).
- **Runtime:** .NET 8 LTS (ASP.NET Core Kestrel).
- **Database:** PostgreSQL 16 LTS (підключення через Connection String з підтримкою connection pooling).
- **Reverse Proxy & SSL:** Nginx з автоматичним оновленням сертифікатів Let's Encrypt / Certbot.
- **Dynamic DNS:** DuckDNS (`https://hrreservesystem.duckdns.org`).

---

## 🐳 2. Розгортання через Docker & Docker Compose

Конфігурація `docker-compose.yml` містить сервіси веб-додатку та PostgreSQL 16 з ізольованою віртуальною мережею:

```yaml
version: '3.8'

services:
  web:
    build: .
    restart: always
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      - Database__Provider=PostgreSQL
      - ConnectionStrings__DefaultConnection=Host=db;Port=5432;Database=hrreserve;Username=hrreserve;Password=${DB_PASSWORD}
    depends_on:
      - db

  db:
    image: postgres:16-alpine
    restart: always
    environment:
      POSTGRES_DB: hrreserve
      POSTGRES_USER: hrreserve
      POSTGRES_PASSWORD: ${DB_PASSWORD}
    volumes:
      - postgres_data:/var/lib/postgresql/data

volumes:
  postgres_data:
```

### Команди запуску на сервері:

```bash
# 1. Створення файлу оточення з надійними секретами
cp .env.example .env

# 2. Підняття контейнерів у фоні
docker compose up -d --build

# 3. Перевірка логів запуску та міграцій
docker compose logs -f web
```

Під час першого старту контейнера автоматично спрацьовує `Database.Migrate()`, створюючи всі необхідні схеми та індекси, а також ініціалізується початковий пул демо-даних.

---

## 🔒 3. Налаштування Nginx Reverse Proxy та SSL

Приклад конфігурації Nginx для домену з HTTPS:

```nginx
server {
    listen 80;
    server_name hrreservesystem.duckdns.org;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name hrreservesystem.duckdns.org;

    ssl_certificate /etc/letsencrypt/live/hrreservesystem.duckdns.org/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/hrreservesystem.duckdns.org/privkey.pem;

    # OWASP Security Headers
    add_header X-Frame-Options "DENY" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;
    add_header X-XSS-Protection "1; mode=block" always;
    add_header Strict-Transport-Security "max-age=2592000; includeSubDomains" always;

    client_max_body_size 10M;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

---

## ☁️ 4. Розгортання у PaaS (Render / Railway / Azure)

Проєкт містить маніфест `render.yaml` та оптимізований `Dockerfile`, що дозволяє розгортати сервіс у хмарних платформах одним кліком:
- База даних: Managed PostgreSQL.
- Веб-сервіс: Web Service на базі Dockerfile.
- Health Check URL: `/health/ready` (перевіряє статус БД та доступність сервісів).
