# Decision Log — SSO (VenusVerse Identity)

**Tanggal:** 6 November 2026
**Status:** Disetujui — mengikat seluruh dokumen di `docs/` dan kode di `src/`
**Sumber:** KD-01..08 dipindahkan dari `01-PRD.md` §4; keputusan owner 6 Nov 2026 (Q1–Q5 di §16 PRD) dijadikan KD-09..12.

Dokumen lain WAJIB selaras dengan decision log ini. Jika ada dokumen/kode yang bertentangan, decision log ini yang menang.

---

## KD-01 — Protokol: OIDC/OAuth 2.1, authorization code + PKCE saja

- Grant v1: `authorization_code` (PKCE **wajib**, method `S256`) dan `refresh_token`.
- `client_credentials` (service-to-service) dan `end_session` penuh: **Fase 2**.
- Implicit/password grant tidak akan pernah diimplementasikan — legacy dan tidak aman.
- Alasan: standar → library OIDC apa pun bisa dipakai (openid-client di agent-office, `AddOpenIdConnect` di .NET) tanpa SDK khusus.

## KD-02 — Signing: RS256 + JWKS, bukan HS256

- Access token & ID token = JWT **RS256**, `iss=https://sso.<domain>`, `aud=<client_id>`, TTL 15 menit.
- `GET /.well-known/jwks.json` menerbitkan public key; client memvalidasi lokal — tanpa shared secret, tanpa DB call. (HS256 plotsure tidak dipakai di sini: symmetric key tidak boleh dibagikan lintas service.)
- Kunci: **RSA-2048 khusus SSO** (diputuskan owner 6 Nov 2026), file di-mount ke container (`Signing__KeyPath`), `kid` = thumbprint. `oracle/private.key` TIDAK dipakai — isolasi kebocoran.
- Rotasi v1 manual lewat runbook (overlap `kid` lama di JWKS); tabel `signing_keys` + rotasi otomatis = Fase 2.

## KD-03 — Sesi SSO sendiri: token SPA + refresh cookie (pola plotsure)

- `Sso.Web` memakai pola Plotsure.Web: access token di memori + refresh cookie `sso_rt` (`HttpOnly;Secure;SameSite=Strict;Path=/v1/auth`), disimpan di Redis, rotasi tiap pakai, reuse-detection → cabut satu family.
- Lapisan ini = *login ke SSO*; berbeda dengan *login lewat SSO* (authorization code flow KD-01). Keduanya ada dan tidak boleh dicampur.

## KD-04 — Login & consent UI dihosting SSO di domain sendiri

- `sso.<domain>`: SPA (`/`), API (`/v1/*`), OIDC (`/oauth/*`, `/.well-known/*`), admin (`/admin`).
- `/oauth/authorize` tanpa sesi → redirect halaman login SPA → kembali lanjut flow.
- Consent v1 implisit untuk client first-party (`skip_consent` di registry client, e.g. agent-office).

## KD-05 — Database: postgres sendiri di stack `sso-net`

- `sso-postgres` (`postgres:16-alpine`) + `sso-redis` (`redis:7-alpine`, AOF on) — bagian dari compose stack SSO.
- TIDAK memakai `postgres_db` bersama (`oracle/database`) — kredensial auth adalah data paling sensitif; isolasi blast radius + backup per-app.

## KD-06 — Admin digabung ke `Sso.Web`, bukan SPA terpisah

- Area `/admin` di `Sso.Web` (guard claim `sso_admin`); endpoint API di `/admin/*` (JWT `sso_admin` atau `X-Admin-Key`, pola dua lapis seperti plotsure).
- Alasan: konsol admin SSO kecil (user, client, audit) — tidak layak menambah service seperti `Plotsure.Admin`.

## KD-07 — Tanpa Worker/RabbitMQ di v1

- Email (verifikasi, reset, invite) dikirim sinkron via adapter `INotifier` (SMTP; dev → mailpit; tanpa SMTP → ConsoleNotifier).
- Gagal kirim = error ke user, bukan antrian. Volume email auth rendah; broker menambah infra + service tanpa kebutuhan saga.

## KD-08 — agent-office = confidential client, exchange di server Node

- `server/index.js` (bukan browser) menukar authorization code → token dengan `client_secret` di env server.
- Session app di cookie `sso_session` (HttpOnly) menggantikan isi `access-gate.js`; gate tetap melindungi HTTP + WS upgrade.
- Transisi: `AUTH_MODE=studio_token|sso`, `STUDIO_ACCESS_TOKEN` bertahan satu rilis sebagai escape hatch, lalu dihapus.

## KD-09 — Registrasi produksi: invite-only

- `AllowSelfRegistration=false` default produksi → `/v1/auth/register` 403; UI menyembunyikan form register.
- Satu-satunya jalur akun non-admin: invite admin (link set-password, TTL 72 jam, FR-A04).
- Diputuskan owner 6 Nov 2026 (PRD §16-Q1).

## KD-10 — Email produksi: Resend via relay SMTP

- `Email__Provider=smtp` → `smtp.resend.com:587` + `RESEND_API_KEY` sebagai password SMTP.
- Adapter `SmtpNotifier` (pola plotsure, MailKit) langsung dipakai; adapter API Resend native boleh ditambah nanti, bukan blocker.
- Dev tetap mailpit; tanpa konfigurasi → ConsoleNotifier (email ke log).
- Diputuskan owner 6 Nov 2026 (PRD §16-Q4).

## KD-11 — Bootstrap admin pertama: seed via env

- `SEED_ADMIN_EMAIL` + `SEED_ADMIN_PASSWORD` di `.env` → akun `sso_admin` dibuat sekali saat DB kosong (idempotent, saat startup/migrasi).
- Diputuskan owner 6 Nov 2026 (PRD §16-Q5).

## KD-12 — Port & penamaan stack

- Dev host ports: postgres `5434` · redis `6381` · mailpit `1027/8027` (melanjutkan pola +1 dari plotsure: 5433/6380/1026/8026) · API `5140` · Web `5175`.
- Container: `sso-api`, `sso-web`, `sso-postgres`, `sso-redis`; network `sso-net` + `app-network` (external, NPM); alias `sso-*` di data plane agar tidak tabrakan di `app-network`.
- Env koneksi: `SSO_DB`, `SSO_REDIS` (pola `PLOTSURE_*`).

## KD-13 — Cookie `sso_sess` (family id) untuk deteksi sesi di /oauth/authorize

- `sso_sess` = session family id, `HttpOnly;Secure;SameSite=Lax;Path=/`, TTL mengikuti refresh TTL; dibuat/dihapus bersama `sso_rt` di login/refresh/verify/accept-invite/logout/reset.
- Alasan: `/oauth/authorize` dipanggil via navigasi GET lintas-origin — `sso_rt` (`Strict` + `Path=/v1/auth`, KD-03) tidak terkirim. `Lax` lolos navigasi top-level GET, `Strict` tetap tidak akan dikirim dari origin lain.
- `sso_sess` hanya membuktikan "sesi hidup" (lookup `sso:fam:{id}`); ia **tidak pernah** men-rotate atau menggantikan `sso_rt`. GUID family 128-bit = unguessable, sepadan dengan session id klasik.
- Ditambahkan saat implementasi Fase 2 (OIDC provider); melengkapi KD-03 tanpa mengubah kontrak `sso_rt`.

## KD-14 — App launcher: per-user client assignment + gerbang `require_assignment`

- `/dashboard` di Sso.Web = launcher aplikasi VenusVerse. Daftar kartu berasal dari `GET /v1/me/apps`: hanya client dengan `launch_url` terisi yang muncul — client OIDC-only tidak pernah menjadi kartu.
- Tabel `client_assignments` (`user_id` × `client_id` unik, `granted_by`, `created_at`) menyimpan grant per-user. Admin mengganti set via `PUT /v1/admin/users/{id}/apps` (replace idempotent); grant/revoke diaudit append-only (`app.granted`/`app.revoked`).
- `sso_admin` melihat dan bisa meluncur SEMUA client aktif tanpa baris assignment — konsisten antara `/v1/me/apps` dan gerbang authorize.
- `clients.require_assignment=true` → `/oauth/authorize` menolak `access_denied` (redirect OAuth standar, bukan 400) untuk user tanpa assignment; `sso_admin` lolos. Default `false` = semua user boleh, perilaku lama.

## Masih terbuka (menunggu owner)

- **OQ-1 Domain** — `sso.<domain>` dan `office.<domain>` pakai root domain apa? (menentukan `DOMAIN`, CORS, `redirect_uri`, cookie scope, `iss` claim)
- **OQ-2 Plotsure→SSO** — saat plotsure migrasi, `org_id`/roles tetap di plotsure dan SSO hanya identitas? Diasumsikan **ya**.

---

*Keputusan baru dicatat di sini dengan ID berikutnya (KD-13, dst). Jangan edit keputusan yang sudah disetujui — tambahkan keputusan pengganti.*
