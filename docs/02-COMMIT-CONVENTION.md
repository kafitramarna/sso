# 02 — Commit Convention

> Konvensi pesan commit untuk repo `sso`. Wajib diikuti semua commit.
> Terinspirasi conventional commits, tapi **hanya 7 prefix yang diizinkan**.

## Format

```
<prefix>: <summary>

<body opsional>
```

## Head (wajib)

- Prefix **harus** salah satu dari 7 ini — tidak ada `docs:`, `build:`, `style:`, `scaffold:`, atau prefix lain:

| Prefix     | Dipakai untuk                                                              |
|------------|------------------------------------------------------------------------------|
| `feat`     | Fungsionalitas baru yang terlihat oleh pengguna/service (endpoint, entitas, fitur UI, alur auth) |
| `fix`      | Perbaikan bug pada perilaku yang sudah ada                                   |
| `hotfix`   | Perbaikan mendesak untuk masalah di production                               |
| `refactor` | Perubahan kode yang tidak menambah fitur dan tidak memperbaiki bug           |
| `test`     | Penambahan/perubahan test saja, tanpa perubahan kode produksi                |
| `ci`       | Perubahan pipeline/workflow CI-CD (`.github/workflows`, script deploy)       |
| `chore`    | Sisanya: docs, konfigurasi, build/deps, tooling, scaffold awal               |

- Format: `<prefix>: <summary>` — prefix lowercase, diikuti `: ` (colon + spasi).
- Summary: **English**, imperative mood (`add`, `fix`, `move` — bukan `added`/`adds`), lowercase awal kata, **tanpa titik di akhir**, maks **72 karakter**.
- **Tanpa scope** — jangan tulis `feat(oauth): ...` atau `fix(api): ...`; selalu plain `<prefix>: <summary>`.

## Body (opsional, wajib untuk perubahan non-trivial)

- Satu baris kosong setelah head.
- Wrap di ±72 karakter; bullet point **wajib** pakai `-` (bukan paragraf panjang untuk daftar perubahan).
- Jelaskan **mengapa** dan **apa** di level tinggi — bukan daftar per-baris kode (itu sudah terlihat di diff).
- Referensikan nomor keputusan saat mengimplementasikan decision log: `(KD-02)`.
- Sertakan breaking change atau langkah migrasi jika ada.

## Larangan

- **Tidak ada** trailer `Generated with ...`, `Co-Authored-By:`, atau atribusi tool/agent apa pun.
- **Tidak ada** secret, signing key, `.env`, atau data environment-specific di pesan maupun isi commit.
- Tidak ada `;` (semicolon) di pesan commit — pecah jadi kalimat/bullet terpisah.
- Tidak ada emoji di pesan commit.

## Contoh

```
feat: add refresh token rotation endpoint (KD-03)
```

```
fix: reject redirect_uri outside registered list

Per KD-01, arbitrary redirect URIs must never be accepted.
Adds allowlist check before issuing authorization codes.
```

```
chore: pin System.Security.Cryptography.Pkcs to 10.0.14

Resolves NU1903 high-severity advisory on the transitive 10.0.0.
```

```
hotfix: invalidate refresh cookie on clock-skew token expiry
```

```
refactor: extract token hashing into shared helper
```

```
test: add architecture guard for domain purity
```

```
ci: cache nuget packages in deploy workflow
```
