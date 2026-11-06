# PRD — SSO (VenusVerse Identity)

**Versi:** 0.2 · **Tanggal:** 6 November 2026 · **Status:** Draft — 4 keputusan awal sudah dijawab owner (§16); menunggu 2 sisanya
**Repo:** `oracle/repos/sso` (greenfield) · **Klien pertama:** `agent-office` (Claw3D) · **Referensi struktur:** `plotsure`

> Dokumen ini kanonik untuk scope SSO v1. Jika nanti ada `docs/00-DECISION-LOG.md`, decision log yang menang atas dokumen ini (pola yang sama seperti plotsure).

---

## 0. Skills yang dipakai untuk dokumen ini

| Skill | Dipakai untuk |
|---|---|
| `codebase-inspection` | Inventaris struktur plotsure / agent-office / infra workspace (`repos/*`, `oracle/{database,npm,9router,ci-cd}`) |
| `codebase-design` | Bahasa desain di dokumen ini — modul, interface, seam, adapter, depth (dipakai di §8 dan pemilihan seam OIDC) |

Skill yang direncanakan dipakai saat eksekusi (bukan sekarang): `test-driven-development`, `requesting-code-review`, `verification-before-completion`, `executing-plans`, `git-commit`, `simplify-code`.

---

## 1. Latar belakang & masalah

Ekosistem VenusVerse di VPS Oracle (ARM64, Nginx Proxy Manager di `app-network`) menampung beberapa aplikasi: `plotsure` (.NET 10), `agent-office` (Next.js 16 / Claw3D), `9router`, `windsurfapi`. Saat ini tidak ada identitas terpusat:

- **agent-office** diproteksi satu shared secret `STUDIO_ACCESS_TOKEN` → cookie `studio_access` (`server/access-gate.js`). Satu token untuk semua orang: tidak ada identitas per-user, tidak ada revoke per orang, token bocor = ganti untuk semua.
- **plotsure** punya auth sendiri (HS256 JWT + refresh token di Redis, `Auth:SigningKey`) — bagus untuk satu service, tidak bisa dipakai silang tanpa berbagi symmetric key.
- Service baru ke depan akan mengulang masalah yang sama: bikin login sendiri atau berbagi secret.

**Solusi:** satu service SSO ber-.NET 10 yang menjadi identity provider untuk agent-office dulu, lalu service lain — menerbitkan token yang bisa divalidasi setiap service tanpa berbagi secret (RS256 + JWKS).

## 2. Tujuan, non-goals, ukuran sukses

### Tujuan v1

1. User login **satu akun** di `sso.<domain>` → masuk agent-office tanpa `STUDIO_ACCESS_TOKEN`.
2. Semua endpoint OIDC standar cukup untuk client confidential (agent-office) maupun public SPA (klien masa depan): authorization code + **PKCE wajib**.
3. SSO self-contained dalam satu `docker-compose.yml` — `docker compose up -d` di VPS langsung jalan, pola sama seperti plotsure.
4. Fondasi untuk service berikutnya: registrasi client via admin, JWKS untuk validasi lintas service, audit login.

### Non-goals v1 (jangan dibangun)

- Bukan IdP komersial: tidak ada dynamic client registration publik, tidak ada consent screen granular, tidak ada SAML/social login (Google/GitHub → Fase 2+).
- Tidak migrasi auth plotsure — plotsure tetap auth sendiri dulu ("yang plotsure jangan masuk sso dulu").
- Tidak ada MFA/TOTP, tidak ada org/tenant multi-level (tenant milik aplikasi, bukan SSO), tidak ada RBAC kompleks — SSO hanya tahu *user global* + *client*.
- Tidak ada RabbitMQ/Worker — email dikirim sinkron lewat adapter SMTP; outbox hanya jika nanti perlu.

### Ukuran sukses

- Login agent-office end-to-end via SSO di domain dev VPS (< 5 detik redirect loop).
- `docker compose up -d` fresh → semua healthcheck hijau tanpa langkah manual selain `.env`.
- Service ketiga bisa integrasi hanya dengan: registrasi client + validasi JWT via `/.well-known` + JWKS, tanpa menyentuh DB SSO.

## 3. Analisis kondisi existing

### 3.1 agent-office — titik integrasi

- Custom Node server `server/index.js`: HTTP + WebSocket upgrade dilewatkan `createAccessGate` (`server/access-gate.js`).
- Gate sekarang: cookie `studio_access` dibandingkan constant-time ke `STUDIO_ACCESS_TOKEN`, rate limit 10/menit/IP, melindungi HTTP **dan** WS upgrade (`/api/gateway/ws`).
- `STUDIO_ACCESS_TOKEN` kosong → gate disabled (dev lokal). Bind host publik tanpa token ditolak `network-policy.js`.
- **Seam integrasi yang tepat:** ganti isi `access-gate.js` — verifikasi session SSO (cookie `sso_session` milik agent-office hasil code exchange) alih-alih shared token. Upstream gateway (`CLAW3D_GATEWAY_TOKEN`) tidak berubah — itu kredensial mesin, bukan identitas user.

### 3.2 plotsure — pola yang ditiru (bukan diubah)

| Pola plotsure | Dipakai di SSO? |
|---|---|
| Layering `Api → Application → Domain`, `Infrastructure → Application`, NetArchTest | Ya, identik |
| Minimal API + `ApiEnvelope` + `Endpoints/*Endpoints.cs` | Ya |
| EF Core 10 + Npgsql, migrasi di Infrastructure | Ya (tanpa PostGIS) |
| Redis refresh token store (`RedisRefreshTokenStore`), cookie refresh `HttpOnly;Secure;SameSite=Strict` | Ya, untuk sesi SSO sendiri |
| `PasswordHasher<T>` ASP.NET Identity (PBKDF2) | Ya |
| Rate limit policy `"auth"` | Ya |
| Compose: `app-network` (NPM) + `<app>-net` internal, alias `ps-*` agar tidak tabrakan | Ya — `sso-net`, alias `sso-*` |
| Tidak ada port publik; NPM terminate TLS; probe lokal bind `127.0.0.1` | Ya |
| CI/CD `deploy-app` + rsync via GitHub Actions (`ci-cd/README.md`) | Ya, `APP_NAME: sso` |
| HS256 `Auth:SigningKey` untuk menandatangani token | **Tidak** — SSO butuh kunci asimetris (§4 KD-02) |
| `/admin/*` + `Plotsure.Admin` SPA terpisah | Parsial — admin SSO digabung di `Sso.Web` (§4 KD-06) |

### 3.3 Infra workspace

- `oracle/database/docker-compose.yml`: postgres+redis bersama di `app-network` untuk aplikasi lama — **SSO tidak pakai**; ikuti pola plotsure: data plane sendiri di dalam stack (blast radius kecil, backup per-app).
- `oracle/npm`: Nginx Proxy Manager di `app-network`, satu-satunya jalan masuk 80/443.
- `oracle/ci-cd`: `deploy-app <nama>` di VPS melakukan `docker compose build && up -d` di `/opt/apps/<nama>`; workflow `.github/workflows/deploy.yml` tinggal disalin.
- `oracle/private.key` + `public.pub`: keypair PEM yang sudah ada di host — opsi sumber kunci signing (KD-02), bukan kewajiban.

## 4. Keputusan kunci (KD)

> **Kanonik: `00-DECISION-LOG.md`.** Ringkasan di bawah hanya indeks — teks mengikat ada di decision log. ID memakai prefix KD agar tidak tertukar dengan decision log plotsure.

| ID | Keputusan |
|---|---|
| KD-01 | OIDC/OAuth 2.1 — hanya `authorization_code` + PKCE (S256) dan `refresh_token` di v1 |
| KD-02 | JWT RS256 + JWKS; RSA-2048 khusus SSO di-mount file; `kid` = thumbprint |
| KD-03 | Sesi SSO sendiri: access token SPA + refresh cookie `sso_rt` di Redis (rotasi + reuse detection) |
| KD-04 | Login/consent UI dihosting di `sso.<domain>`; consent implisit untuk client first-party |
| KD-05 | Postgres+Redis sendiri di `sso-net` — tidak pakai `postgres_db` bersama |
| KD-06 | Admin digabung di `Sso.Web` area `/admin`, bukan SPA terpisah |
| KD-07 | Tanpa Worker/RabbitMQ v1 — email sinkron via `INotifier` (SMTP/Console) |
| KD-08 | agent-office = confidential client; code exchange di `server/index.js`; `AUTH_MODE` transisi |
| KD-09 | Produksi invite-only (`AllowSelfRegistration=false`) |
| KD-10 | Email prod = Resend via `smtp.resend.com`; dev = mailpit; fallback = ConsoleNotifier |
| KD-11 | Admin pertama via seed env `SEED_ADMIN_*` saat DB kosong |
| KD-12 | Port dev 5434/6381/1027/8027; API :5140; Web :5175; container/alias `sso-*` |

## 5. Persona & user stories

| Persona | Cerita |
|---|---|
| Pemilik VPS (Anda) | Deploy `docker compose up -d`; daftarkan client `agent-office`; buat akun pertama lewat invite/bootstrap; lihat siapa login kapan (audit). |
| User agent-office | Buka `office.<domain>` → dialihkan ke `sso.<domain>` login → kembali sudah login; logout membunuh sesi. |
| Service masa depan (9router/windsurfapi/app baru) | Registrasi client via admin SSO → validasi JWT via JWKS → tidak menulis auth sendiri. |
| Admin SSO | Reset password user, disable user, putar kunci, cabut semua sesi user. |

## 6. Functional requirements

### FR-A — Identitas & kredensial

- **FR-A01** Register email+password (kebijakan: min 12 char, cek kebocoran offline k-anonymity boleh Fase 2), verifikasi email wajib sebelum `email_verified=true`. **Produksi: invite-only** — `AllowSelfRegistration=false` default; endpoint `/v1/auth/register` mengembalikan 403 dan UI menyembunyikan form register (diputuskan §16-Q1).
- **FR-A02** Login email+password → sesi SSO (KD-03). Lockout progresif: 5 gagal → 5 mnt; 10 → 30 mnt. Semua percobaan diaudit.
- **FR-A03** Reset password via link email single-use (TTL 1 jam, hash token di DB).
- **FR-A04** Invite: admin membuat undangan → link set-password (TTL 72 jam). **Ini satu-satunya cara akun non-admin dibuat di produksi.**
- **FR-A05** Ganti password & display name di `/account`; mengganti password mencabut semua refresh family lain.

### FR-B — OIDC provider

- **FR-B01** `GET /.well-known/openid-configuration` — discovery lengkap untuk endpoint di bawah.
- **FR-B02** `GET /oauth/authorize` — hanya `response_type=code`, PKCE S256 wajib, `state`/`nonce` diteruskan, validasi `client_id` + `redirect_uri` exact-match dari registry.
- **FR-B03** `POST /oauth/token` — exchange code (single-use, TTL 90 detik, Redis) → `access_token` (RS256, 15 mnt) + `id_token` + `refresh_token` (opaque, rotasi). Refresh via grant `refresh_token`.
- **FR-B04** `GET /oauth/userinfo` — claims user dari access token valid.
- **FR-B05** `GET /.well-known/jwks.json` — publik; cacheable.
- **FR-B06** `POST /oauth/revoke` — cabut refresh token (client-authenticated).
- **FR-B07** Claims v1: `sub`(user id, stabil), `email`, `email_verified`, `name`, `roles` (`user`/`sso_admin`), `aud`, `iss`, `exp`, `iat`, `jti`.

### FR-C — Registry client

- **FR-C01** Admin CRUD client: `client_id`, `name`, `type` (`confidential`/`public`), `secret` (disimpan hash; hanya confidential), daftar `redirect_uris`, `allowed_scopes`, `skip_consent`, `active`.
- **FR-C02** Secret confidential ditampilkan **sekali** saat dibuat/dirotasi; di-DB hanya hash.
- **FR-C03** Client `agent-office` (confidential) di-seed dari env saat bootstrap pertama (`Seed__AgentOffice__*`).

### FR-D — Sesi & audit

- **FR-D01** `GET /v1/sessions` — daftar sesi aktif user (device/UA, IP, created, last seen); `DELETE /v1/sessions/{id}` revoke satu; `POST /v1/sessions/revoke-all` revoke semua kecuali current.
- **FR-D02** Audit append-only: login sukses/gagal, register, reset, authorize, token issue/refresh/revoke, admin actions — simpan `user_id`, `client_id`, `ip`, `ua`, `metadata jsonb`, `created_at`.
- **FR-D03** Admin: list user (search, paginate), disable/enable, paksa revoke semua sesi, kirim ulang verifikasi, list client, audit viewer.

### FR-E — Operasional

- **FR-E01** `/health/live` (proses) + `/health/ready` (postgres+redis) untuk healthcheck compose & NPM.
- **FR-E02** Migrasi EF Core jalan otomatis saat boot (idempotent, pola plotsure) — atau job sekali jalan; pilih satu, dokumentasikan.
- **FR-E03** Config murni env; `.env.example` lengkap; tidak ada secret ter-commit.
- **FR-E04** Rate limit policy `auth` di semua endpoint sensitif; security headers (CSP, `X-Content-Type-Options`, `Referrer-Policy`, HSTS lewat NPM).

## 7. Non-functional requirements

| Kategori | Target |
|---|---|
| Keamanan | TLS di NPM; PKCE wajib; refresh rotation + reuse detection; password PBKDF2 (`PasswordHasher<T>`); secret client di-hash; audit append-only; lockout + rate limit; cookie `Secure;HttpOnly;SameSite` |
| Performa | authorize → token < 300 ms p95 intra-VPS; JWKS cacheable ≥ 5 mnt |
| Ketersediaan | satu replica cukup (solo-ops); sesi tetap hidup saat API restart (refresh di Redis AOF) |
| Privasi | SSO tidak menyimpan data aplikasi; `sub` per-user stabil, tidak mengandung email |
| Maintainability | satu repo, `dotnet build/test`, NetArchTest menjaga arah dependency |

## 8. Arsitektur & struktur repo (mirror plotsure)

```
sso/
├── AGENTS.md  README.md  Sso.sln  docker-compose.yml  .dockerignore  .gitignore
├── .github/workflows/deploy.yml          # APP_NAME: sso (pola ci-cd/)
├── deploy/
│   ├── docker-compose.dev.yml            # postgres 5434 · redis 6381 · mailpit 1027/8027
│   └── .env.example
├── docs/  (00-DECISION-LOG · 01-PRD · 02-ARSITEKTEK · 03-DATA-MODEL
│           04-API-SPEC · 05-OIDC-FLOWS · 08-RUNBOOK · 09-HONEST-LIMITS · 10-DEPLOYMENT)
├── src/
│   ├── Sso.Api/            minimal API · Dockerfile · auth handlers · Endpoints/
│   ├── Sso.Application/    Auth/, Oidc/, Clients/, Admin/, Abstractions/ (INotifier,
│   │                       ITokenService, IRefreshTokenStore, IAuthorizationCodeStore, IClock)
│   ├── Sso.Domain/         Entities, Enums, Rules (password policy, lockout), ValueObjects
│   ├── Sso.Infrastructure/ Persistence/ (DbContext, Migrations), Auth/ (Jwt RS256 service,
│   │                       Redis stores), Notifications/ (Smtp + Console), Audit/
│   └── Sso.Web/            React + TS + Vite + shadcn/ui · Dockerfile + nginx.conf
│                           pages: Login, Register, Consent, Account, Sessions,
│                           Forgot/Reset, AcceptInvite, admin/{Users,Clients,Audit}
└── tests/
    ├── Sso.Api.Tests/        endpoint, security-hardening, OIDC flow tests (WebApplicationFactory)
    ├── Sso.Application.Tests/
    └── Sso.Domain.Tests/
```

Aturan dependency (NetArchTest, sama seperti plotsure): `Api → Application → Domain`; `Infrastructure → Application`; `Web` standalone. Domain tanpa referensi.

**Catatan desain (vocabulary `codebase-design`):** seam utama SSO adalah *interface OIDC* (`/oauth/*` + JWKS) — deep module: client hanya perlu tahu discovery URL, seluruh kompleksitas (kunci, rotasi refresh, lockout, audit) tersembunyi. Seam internal: `IRefreshTokenStore`/`IAuthorizationCodeStore` (adapter Redis) dan `INotifier` (adapter SMTP/Console) — masing-masing punya ≥1 adapter nyata, jadi seam-nya layak ada.

## 9. Model data (PostgreSQL, ringkas)

| Tabel | Kolom kunci |
|---|---|
| `users` | `id uuid pk`, `email citext unique`, `display_name`, `password_hash`, `email_verified bool`, `status` (`active`/`disabled`), `is_sso_admin bool`, `failed_login_count int`, `locked_until timestamptz`, `last_login_at`, `created_at`, `updated_at` |
| `clients` | `id uuid pk`, `client_id text unique`, `name`, `type`, `secret_hash`, `redirect_uris text[]`, `allowed_scopes text[]`, `skip_consent bool`, `active bool`, `created_at` |
| `one_time_tokens` | `id uuid pk`, `user_id fk`, `kind` (`verify_email`/`reset_password`/`invite`), `token_hash unique`, `expires_at`, `used_at null` |
| `audit_events` | `id bigserial pk`, `user_id null`, `client_id null`, `event text`, `ip inet`, `user_agent text`, `metadata jsonb`, `created_at` — append-only, index `(user_id, created_at desc)` |

**Di Redis (bukan postgres):** authorization codes (`oidc:code:{code}` TTL 90 dtk), refresh token families (`sso:rt:{family}`, rotasi + reuse detection), rate-limit counters, JWKS cache.

## 10. Kontrak API (ringkas)

```
OIDC     GET  /.well-known/openid-configuration
         GET  /.well-known/jwks.json
         GET  /oauth/authorize?response_type=code&client_id&redirect_uri&scope
                 &state&nonce&code_challenge&code_challenge_method=S256
         POST /oauth/token            grant_type=authorization_code|refresh_token
         GET  /oauth/userinfo         (Bearer access token)
         POST /oauth/revoke
Sesi SSO POST /v1/auth/{register,login,refresh,logout,accept-invite}
              /v1/auth/{verify-email,forgot-password,reset-password}
         GET  /v1/me · PATCH /v1/me · /v1/sessions (GET, DELETE, revoke-all)
Admin    GET/PATCH /admin/users[/{id}] · POST /admin/users/{id}/{disable,enable,revoke-sessions,resend-verification}
         CRUD /admin/clients[/{id}] · POST /admin/clients/{id}/rotate-secret
         GET  /admin/audit?user&client&event&from&to
Ops      GET  /health/{live,ready}
```

Envelop respons & kode error mengikuti `ApiEnvelope` plotsure (`{data}` / `{error:{code,message}}`).

## 11. Docker & deployment

### Dev (`deploy/docker-compose.dev.yml`, `name: sso-dev`)

| Service | Image/build | Port host |
|---|---|---|
| postgres | `postgres:16-alpine` | `5434:5432` (geser +2 dari default — plotsure sudah di 5433) |
| redis | `redis:7-alpine` (AOF) | `6381:6379` |
| mailpit | `axllent/mailpit` | SMTP `1027:1025` · UI `8027:8025` |

API `dotnet run` → `http://localhost:5140`; Web `yarn dev` → `http://localhost:5175` (vite proxy `/v1`,`/oauth`,`/.well-known`,`/admin`,`/health` → :5140).

### Prod (`docker-compose.yml`, `name: sso`) — pola plotsure §10

- `web` (nginx SPA) + `api` di **`app-network` (external)** + `sso-net`; `postgres`/`redis` hanya di `sso-net` alias `sso-postgres`/`sso-redis`; `api` bind `127.0.0.1:5140:8080` untuk probe lokal — tidak ada port publik.
- NPM: `sso.<domain>` → `web` untuk `/`, → `api:8080` untuk `/v1`, `/oauth`, `/.well-known`, `/admin`, `/health` + websockets tidak dibutuhkan.
- Env wajib (`.env` mode 600): `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `SIGNING_KEY`/`SIGNING_KEY_PATH`, `DOMAIN`, `SEED_ADMIN_EMAIL`+`SEED_ADMIN_PASSWORD` (bootstrap admin pertama — diputuskan §16-Q5; hanya dipakai sekali saat DB kosong), `EMAIL_*` (Resend — lihat catatan di bawah), `SEED_AGENTOFFICE_CLIENT_SECRET`.
- **Email prod = Resend** (diputuskan §16-Q4): `Email__Provider=smtp` dengan relay `smtp.resend.com:587` + `RESEND_API_KEY` sebagai password SMTP — adapter `SmtpNotifier` pola plotsure langsung bisa dipakai; adapter API Resend native boleh ditambah nanti, bukan blocker.
- Healthcheck identik pola plotsure (`curl /health/live`); `depends_on: service_healthy`.
- CI/CD: salin `.github/workflows/deploy.yml` plotsure → `APP_NAME: sso`; job test = `dotnet test --filter Category!=Integration` + `yarn build` `Sso.Web`; deploy = rsync → `deploy-app sso` di `/opt/apps/sso`.
- Dockerfile `Sso.Api`: multi-stage `mcr.microsoft.com/dotnet/sdk:10.0` → `aspnet:10.0` (arm64 native — build di VPS), pola sama `src/Plotsure.Api/Dockerfile`.

## 12. Rencana integrasi agent-office (fase berikutnya — konteks PRD ini)

1. Admin SSO daftarkan client `agent-office` (confidential, `redirect_uri=https://office.<domain>/auth/callback`, `skip_consent=true`).
2. agent-office: tambah `AUTH_MODE`, `SSO_ISSUER`, `SSO_CLIENT_ID`, `SSO_CLIENT_SECRET`, `SSO_REDIRECT_URI` di env.
3. `access-gate.js` → gate baru: tanpa `sso_session` valid → 302 ke `/oauth/authorize` (PKCE); `/auth/callback` exchange code di server; cookie `sso_session` HttpOnly; refresh diam-diam via `refresh_token` grant; WS upgrade tetap memakai gate yang sama.
4. Masa transisi: `AUTH_MODE=studio_token` menjaga perilaku lama; setelah stabil → hapus jalur token & doc.
5. Identitas user (`sub`,`email`,`name`) diteruskan ke UI/agent-context; `CLAW3D_GATEWAY_TOKEN` tetap kredensial mesin.

## 13. Strategi testing

- **Unit:** password policy, lockout rules, PKCE verify, token claims, refresh rotation/reuse — `Sso.Domain.Tests` + `Sso.Application.Tests`.
- **API:** `WebApplicationFactory` + Testcontainers postgres — register→verify→login→authorize→token→userinfo→refresh→revoke happy path; negative (redirect_uri mismatch, code replay, PKCE salah, refresh reuse → family revoked); security hardening (rate limit, lockout, headers); admin authz.
- **Arch:** NetArchTest dependency rules.
- **Manual/CI smoke:** `docker compose up -d` fresh → health hijau → satu flow login agent-office.
- Integration (Testcontainers) jalan di PR; unit+arch tiap commit — sama seperti plotsure.

## 14. Roadmap fase

| Fase | Isi | Kriteria selesai |
|---|---|---|
| 0 — Docs | PRD ini disetujui; `00-DECISION-LOG` (KD-01..08 dipindah); skeleton repo | Struktur + docs commit pertama |
| 1 — Core identity | Domain+Infra+`/v1/auth`+`/v1/me`; EF migrasi; Redis refresh; compose dev+prod; CI | `docker compose up` hijau; login SPA lokal |
| 2 — OIDC provider | authorize/token/userinfo/jwks/discovery; client registry+seed agent-office; audit | Flow code+PKCE lolos test |
| 3 — agent-office | Implementasi §12 di repo agent-office (banyak modif — sesuai rencana) | Login E2E di `sso.<dev-domain>` |
| 4 — Hardening | Email beneran (SMTP prod), rate-limit tuning, runbook, rotasi kunci terdokumentasi | Checklist §7 hijau |
| Fase 2+ (out of scope v1) | client_credentials grant, end_session, MFA/TOTP, social login, key rotation otomatis (tabel `signing_keys`), k-anonymity password check, consent screen penuh, RLS | — |

## 15. Risiko & honest limits

- **Bukan pengganti Keycloak/Duende** — subset OIDC sengaja sempit; kalau nanti butuh SAML/social/federasi, evaluasi ulang vs memperluas.
- Rotasi kunci v1 manual → salah prosedur = semua token mati; mitigasi: runbook + overlap `kid` lama di JWKS saat rotasi.
- Satu replica + Redis AOF: restart Redis tanpa AOF fsync terbaru bisa kehilangan ≤1 dtk refresh token (user login ulang — acceptable).
- Email deliverability adalah risiko operasional nyata (VPS IP reputation) — dev pakai mailpit; prod perlu SMTP relay (Resend/SES/Brevo) sebelum invite dipakai luas.
- Session fixation lewat `state`/`nonce` wajib diverifikasi di sisi agent-office — masuk checklist integrasi §12.
- `sub` stabil per-user; jangan pernah expose `user_id` lain atau enumerasi email lewat pesan error (respons generik).

## 16. Pertanyaan terbuka

Terjawab (6 Nov 2026):

1. ~~Self-registration~~ → **invite-only** di produksi (`AllowSelfRegistration=false`; FR-A04 jadi satu-satunya jalur akun non-admin).
2. ~~Kunci signing~~ → **RSA-2048 khusus SSO**, file di-mount ke container; `oracle/private.key` tidak dipakai.
3. ~~SMTP prod~~ → **Resend** lewat relay SMTP (`smtp.resend.com:587` + `RESEND_API_KEY`); mailpit tetap untuk dev.
4. ~~Bootstrap admin~~ → **seed via env** `SEED_ADMIN_EMAIL`+`SEED_ADMIN_PASSWORD`, dijalankan sekali saat DB masih kosong.

Masih terbuka:

5. **Domain** — SSO di `sso.<domain>` — domain root apa? Dan agent-office nanti di subdomain apa? (menentukan `DOMAIN`, CORS, redirect_uri client, cookie scope)
6. **Plotsure nanti** — saat plotsure migrasi ke SSO, `org_id`/roles tetap dikelola plotsure dan SSO hanya memberi identitas? Diasumsikan **ya** — konfirmasi bila berbeda.
