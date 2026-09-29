# Per-upload download password design

## Goal

An uploader can optionally protect an upload with a password. Anyone holding the link must
supply that password before receiving the file, its checksum, or its size. This is an access
gate, not encryption: files stay unencrypted at rest. The existing `Encrypt-Password` feature
is unchanged and may be combined with it.

## Setting a password

The `Download-Password` request header is accepted on every upload route: `PUT /{filename}`,
`PUT /put/{filename}`, `PUT /upload/{filename}`, `POST /` and `POST /archive`. Every file
stored by the request receives the same password. The value is used verbatim (no trimming);
an empty or whitespace-only value or one longer than 1024 characters returns 400. The upload
JSON response reports `passwordProtected: true` per file.

`FileMetadata` gains `PasswordHash`, formatted `pbkdf2-sha256$<iterations>$<salt>$<hash>`
(base64, 16-byte random salt, 32-byte hash, 600 000 iterations). Verification uses
`CryptographicOperations.FixedTimeEquals`. Empty means unprotected, so existing metadata
remains valid.

## Enforcement

A request is authorized for a protected file when either:

- it carries a `Download-Password` header matching the hash (CLI path), or
- it carries a valid unlock cookie for that file (browser path).

Protected routes: `GET`/`HEAD` on `/{token}/{filename}` and `/{download|get|inline}/{token}/{filename}`,
the bundle endpoints (each protected file in the bundle must be authorized), and
`GET /api/preview/{token}/{filename}`.

Unauthorized behaviour:

| Request | Response |
|---|---|
| Browser (`Accept` contains `text/html`) on `/{token}/{filename}` | SPA index (site title injected); the preview page shows the unlock form |
| Browser on `/{action}/{token}/{filename}` | `303` to `/{token}/{filename}` |
| Anything else without credentials | `401`, plain text explaining the `Download-Password` header |
| Wrong `Download-Password` header | `401`, counts as a failed attempt |
| Attempt limit reached | `429` with `Retry-After`, checked before hashing |
| Bundle containing an unauthorized protected file | `401` for the whole bundle |

Unauthorized responses never include `Repr-Digest`, `Content-Length` of the file, `Sunset`,
or `X-Remaining-Downloads`, and never increment the download counter. The preview API returns
only `filename`, `url`, `downloadUrl`, `token`, `hostname`, `qrCode` and
`passwordProtected: true` while locked; once authorized it returns the full preview plus
`passwordProtected: true`.

Admin API/page (admin bearer token) and deletion via deletion token are not gated. Admin
metadata reports `passwordProtected`; the admin page shows a lock badge.

## Browser unlock cookie

`POST /api/unlock/{token}/{filename}` with JSON `{"password": "..."}`:

- `204` and `Set-Cookie` when correct; `401` when wrong; `429` with `Retry-After` when
  limited; `404` when the file does not exist; `204` without a cookie for unprotected files.
- Responses use `Cache-Control: no-store`.
- `BasicAuthMiddleware` must not require upload credentials for this route.

Cookie:

- Name: `tcs_unlock_<first 16 hex chars of SHA-256("<token>/<filename>")>`.
- Value: `<expiry unix seconds>.<base64url HMAC-SHA256>`, where the HMAC key is the raw
  stored password hash bytes and the message is `unlock\n<token>\n<filename>\n<expiry>`.
- Attributes: `HttpOnly`, `SameSite=Lax`, `Path=/`, `Secure` when the request is HTTPS,
  `Expires` = expiry.

Validating a cookie is an HMAC check, so range requests and resumed downloads stay cheap.
No separate signing key is stored: forging a cookie requires the stored hash, and whoever
can read metadata can already read the unencrypted file. Changing the password (not
supported yet) would invalidate all cookies automatically.

## Attempt limiting

An in-memory limiter keyed by site, token and filename counts failed password attempts in a
sliding window. When the count reaches the maximum, verification is refused with `429`
before any PBKDF2 work. Success clears the counter. Anyone holding the link can temporarily
lock out other recipients; that trade-off is accepted.

## Configuration

Global in `TransferCs`, overridable per site in `Sites:<id>`:

| Setting | Default | Meaning |
|---|---|---|
| `DownloadPasswordMaxAttempts` | `50` | Failed attempts per file per window; `0` disables limiting |
| `DownloadPasswordAttemptWindowMinutes` | `15` | Sliding window length, at least 1 |
| `DownloadPasswordUnlockHours` | `12` | Unlock cookie lifetime, at least 1 |

## Frontend

- Upload: an optional "Protect with password" toggle below the dropzone reveals a password
  field with a show/hide control. The password is sent as `Download-Password` for single and
  multi-file uploads. Results show a lock badge and a "Copy link + password" action.
- Preview page: when `passwordProtected` and locked, show a lock, password field and unlock
  button. Unlock calls the unlock API, then refetches the preview and shows the normal preview
  (inline media included) and download button. Show clear messages for wrong passwords and
  for `429` (using `Retry-After`).
- Command composer and example snippets gain the `Download-Password` option.

## CLI and documentation

- `transfer` script: `-P, --password <password>` sends `Download-Password` (`-p` stays
  `--encrypt`).
- `SKILL.md` template and README document upload and download usage
  (`curl -H "Download-Password: secret" …`), resume with `curl -C -`, the settings above,
  the unlock/limit behaviour, and add `Download-Password` to the reverse-proxy header list.

## Known limitations

- Every GET, including a range request, counts as a download against `Max-Downloads`
  (existing behaviour, out of scope).
- The password cannot be changed or removed after upload.
- Attempt counters are per process and reset on restart.
