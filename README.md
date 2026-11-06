# SSO — VenusVerse Identity

Identity provider OIDC ringan (.NET 10) untuk ekosistem VenusVerse. Klien pertama: **agent-office**; service lain menyusul.

- **PRD:** [`docs/01-PRD.md`](docs/01-PRD.md) · **Keputusan:** [`docs/00-DECISION-LOG.md`](docs/00-DECISION-LOG.md) (kanonik)
- **Stack:** .NET 10 minimal API · EF Core + PostgreSQL · Redis · React + Vite (`Sso.Web`) · Docker

## Menjalankan (dev)

```bash
docker compose -f deploy/docker-compose.dev.yml up -d   # postgres :5434 · redis :6381 · mailpit :8027

dotnet ef database update -p src/Sso.Infrastructure -s src/Sso.Api
dotnet run --project src/Sso.Api                         # http://localhost:5140

cd src/Sso.Web && yarn install && yarn dev               # http://localhost:5175
```

## Produksi

`docker-compose.yml` di root: `sso-web` + `sso-api` di `app-network` (Nginx Proxy Manager route `sso.<domain>`), data plane `sso-net` — tidak ada port publik. Deploy via `deploy-app sso` di VPS (lihat `.github/workflows/deploy.yml` dan `oracle/ci-cd/README.md`).

## Struktur

```
src/Sso.Api            minimal API — /v1/*, /oauth/*, /.well-known/*, /admin, /health
src/Sso.Application    use-case auth & OIDC (abstractions: stores, notifier, token svc)
src/Sso.Domain         entities, enums, rules (password policy, lockout)
src/Sso.Infrastructure EF Core + Npgsql, Redis stores, SMTP, JWT RS256, audit
src/Sso.Web            React SPA: login, consent, account, sessions, admin
tests/                 Sso.{Api,Application,Domain}.Tests (xunit + NetArchTest)
deploy/                docker-compose.dev.yml + .env.example
docs/                  PRD, decision log, arsitektur, data model, API spec
```
