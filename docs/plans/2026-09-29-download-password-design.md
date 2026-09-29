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
can read metadata can already read the unencrypted file. Changing the password
invalidates all cookies automatically.

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

## Managing the password after upload

The uploader can set, change or remove the password after upload with the per-file admin
token (`Authorization: Bearer <admin-token>`, same non-enumerable 404 rules as the admin API):

- `PUT /api/admin/{token}/{filename}/password` with JSON `{"password": "..."}` sets or
  replaces the password (new salt). Any 1-1024 character string is accepted.
- `DELETE /api/admin/{token}/{filename}/password` removes it.
- Both return `204` with `Cache-Control: no-store`; `400` for an invalid password, `415`
  without a JSON body. The admin token is checked first, so callers without it only see `404`
  and cause no hashing work.
- Changing or removing the password invalidates existing unlock cookies, because the cookie
  HMAC key is derived from the stored hash, and resets the attempt counter for the file.

The upload-time `Download-Password`/`Download-Password-Base64` headers remain for the CLI and
the browser's optional pre-upload checkbox, so a file can be protected without an
unprotected window.

## Frontend

- The upload page has exactly one place to enter a password, set before uploading: the
  dropzone stays the first element; directly below it is one compact checkbox "Protect
  uploads with password". When checked, the password field appears right under it,
  prefilled with a suggestion; uploads made while checked send `Download-Password-Base64`.
  An empty or too long password blocks the dropzone. No helper paragraphs.
- The password field has show/hide, copy and "suggest" icons inside the input and a compact
  length slider (8-32, default 20) showing only the number; moving the slider or clicking
  suggest regenerates it. Suggested passwords use only `A-Za-z0-9` without the look-alikes
  `1lI0Oo2Zz5Ss6b8B9grnmvw` (39 characters); typed passwords are unrestricted.
- Every successful upload result shows its status: "Password protected" (lock) or
  "No password" (open lock). The badge links to the file's private admin page, which is the
  single place to set, change or remove a password after upload. There is no per-result
  editor and no "set password for all" on the upload page.
- Link and password are never copied together. Protected results get a separate "copy
  password" icon next to the badge while the page is open; the password is kept in memory
  only.
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
- Between a browser upload and applying a password the file is briefly unprotected; its
  link is random and not yet shared.
- Attempt counters are per process and reset on restart.
