# SSO — Agent Rules

SSO adalah identity provider OIDC untuk ekosistem VenusVerse (klien pertama: agent-office). Status repo: **greenfield/Fase 0** — skeleton `src/`; sumber kebenaran adalah dokumen di `docs/`.

## Sumber kebenaran (wajib)

- `docs/00-DECISION-LOG.md` (KD-01..KD-12) **kanonik** — jika dokumen/kode bertentangan, decision log menang.
- Keputusan kunci: OIDC code+PKCE saja (KD-01) · RS256+JWKS, RSA khusus SSO (KD-02) · refresh cookie `sso_rt` di Redis (KD-03) · UI di `sso.<domain>` (KD-04) · postgres+redis sendiri, BUKAN shared `postgres_db` (KD-05) · admin di Sso.Web (KD-06) · tanpa Worker/RabbitMQ v1 (KD-07) · agent-office confidential client (KD-08) · invite-only (KD-09) · Resend via SMTP (KD-10) · admin seed env (KD-11) · port dev 5434/6381/1027/8027, API :5140, Web :5175 (KD-12).

## Stack

.NET 10 minimal API · EF Core 10 + Npgsql (tanpa PostGIS) · Redis 7 · MailKit/SMTP (Resend di prod, mailpit di dev) · `Sso.Web` = React + TS + Vite (+ shadcn/ui nanti).

## Aturan dependency

`Api → Application → Domain`; `Infrastructure → Application`. Domain tanpa referensi apa pun. Dijaga NetArchTest (`tests/Sso.Api.Tests/ArchitectureTests.cs`).

## Prinsip yang berdampak pada kode

- **Dua lapisan auth, jangan dicampur** — sesi SSO sendiri (`/v1/auth`, cookie `sso_rt`) ≠ OIDC provider (`/oauth/*`, KD-03).
- **PKCE wajib** untuk semua client; implicit/password grant tidak ada.
- **Secret tidak pernah disimpan polos** — client secret & one-time token di-hash; password via `PasswordHasher<T>` ASP.NET Identity.
- **Audit append-only** untuk semua event auth (login, authorize, token, admin action).
- **Idempotent bootstrap** — seed admin/client hanya jalan saat DB kosong.

## Commands

- Build: `dotnet build Sso.sln`
- Test: `dotnet test` — unit + arch test setiap commit; integration (Testcontainers) di PR
- Infra dev: `docker compose -f deploy/docker-compose.dev.yml up -d` — postgres **5434**, redis **6381**, mailpit SMTP **1027** (UI **8027**)
- Migrasi: `dotnet ef database update -p src/Sso.Infrastructure -s src/Sso.Api`
- API: `dotnet run` dari `src/Sso.Api/` → http://localhost:5140 — dev creds di `appsettings.Development.json` (mirror compose, local-only)
- Web: `yarn install` / `yarn dev` / `yarn build` di `src/Sso.Web/` → http://localhost:5175 — vite proxy meneruskan `/v1`, `/oauth`, `/.well-known`, `/admin`, `/health` ke :5140 (override via `VITE_API_BASE`)

## Commit

- Ikuti `docs/02-COMMIT-CONVENTION.md` — **hanya 7 prefix**: `chore`, `feat`, `fix`, `hotfix`, `refactor`, `test`, `ci`. Tidak ada `docs:`/`scaffold:`/prefix lain (docs → `chore`).
- Head: `<prefix>: <summary>` — English, imperative, lowercase, tanpa titik akhir, maks 72 karakter. Scope opsional `feat(oauth): ...`.
- Body: jelaskan why/what tingkat tinggi; referensikan nomor keputusan `(KD-02)`.
- **Never** add `Generated with ...`, `Co-Authored-By:`, or any tool/agent attribution trailer to commit messages.
- **Never** commit signing keys, `.env`, atau secret apa pun — lihat `.gitignore`.
