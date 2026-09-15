# transfer.cs

Easy and fast file sharing from the command line. Inspired by [transfer.sh](https://github.com/dutchcoders/transfer.sh).

## Features

- Upload and download files via curl
- Custom URL tokens (`-H "Token: my-slug"`)
- Server-side encryption (`-H "Encrypt-Password: secret"`)
- Client-side GPG encryption (pipe-based)
- Expiry and download limits
- Drop multiple files as one ZIP with one download and administration link
- Multi-file API uploads via multipart POST
- Archive upload/download via tar (with optional gzip)
- Bundle download as zip/tar/tar.gz
- Interactive command builder in the web UI
- AI agent skill file at `/SKILL.md`
- ClamAV and VirusTotal scanning
- Rate limiting, IP filtering, basic auth
- Private per-upload admin links with optional download IP history
- Strict multi-site hosting with isolated branding, limits, and storage

## Quick Start

```bash
docker run -d \
  --name transfer-cs \
  -p 8080:8080 \
  -v transfer-data:/data \
  -e TransferCs__PurgeDays=14 \
  -e TransferCs__PurgeIntervalHours=24 \
  -e TransferCs__MaxUploadSizeKb=1048576 \
  ghcr.io/frankhommers/transfer.cs:main
```

## Usage

### Single file

```bash
# Upload
curl --upload-file ./hello.txt https://transfer.example.com/hello.txt

# Upload with custom token
curl --upload-file ./hello.txt -H "Token: my-slug" https://transfer.example.com/hello.txt

# Upload with expiry and download limit
curl --upload-file ./hello.txt -H "File-Lifetime: 7d" -H "Max-Downloads: 5" https://transfer.example.com/hello.txt

# Download
curl https://transfer.example.com/<token>/hello.txt -o ./hello.txt

# Delete (URL from the Link header or JSON deleteUrl)
curl -X DELETE https://transfer.example.com/<token>/hello.txt/<deletion-token>
```

PUT uploads accept `/{filename}`, `/put/{filename}`, or `/upload/{filename}`. GET and
HEAD support canonical `/{token}/{filename}` plus `/get/{token}/{filename}`,
`/download/{token}/{filename}`, and `/inline/{token}/{filename}`. GET uses attachment
disposition for the canonical, `get`, and `download` routes, while `inline` uses inline
disposition. HEAD always reports attachment disposition, including on the `inline` route.

### Multiple files as one ZIP

Dropping multiple files in the browser uploads them together and creates one `files.zip`
on the server. A single dropped file is uploaded directly. The browser shows upload
progress followed by "Creating ZIP...", and returns one download link and one private
administration link for the archive. Retry resends the whole selection if it fails.
Previous upload results remain visible.

```bash
# Create one ZIP from multiple files
curl -F "file=@a.txt" -F "file=@b.txt" https://transfer.example.com/archive

# Get the ZIP's metadata and private management links
curl -H "Accept: application/json" -H "File-Lifetime: 7d" -H "Max-Downloads: 5" \
  -F "file=@a.txt" -F "file=@b.txt" https://transfer.example.com/archive
```

`POST /archive` returns `201 Created`, `Location`, and management `Link` headers for the
single ZIP. JSON responses have one entry in `files`; its checksum, expiry, custom `Token`,
and download limit apply to the archive as a whole. The original files are stored only
inside the ZIP. Downloading the ZIP counts as one download.

ZIPs are built using temporary disk storage. Entries use flat filenames; duplicate names
are suffixed (`photo (2).jpg`, etc.), comparing names case-insensitively. Path components
are stripped and characters invalid in Windows filenames are replaced. Empty files are
preserved. Both the combined original size and the resulting ZIP must fit
`MaxUploadSizeKb`; the HTTP request-body limit also applies, including multipart overhead.
Configured ClamAV prescan checks the completed ZIP before storage.

### Multiple independent files through the API

`POST /` remains available for separate file uploads:

```bash
curl -H "Accept: application/json" -F "file=@a.txt" -F "file=@b.txt" https://transfer.example.com/

# Bundle already-uploaded files on download
curl "https://transfer.example.com/bundle.zip?files=token1/a.txt,token2/b.txt" -o bundle.zip
```

This endpoint returns per-file metadata and management links in JSON, or one download URL
per line in plain text. `File-Lifetime` and `Max-Downloads` apply to each file. `Token`
requires one file per request. Empty files reject this request before storage. If storage
fails, completed uploads from that request are cleaned up.

Both multipart endpoints reject `Encrypt-Password` and `Content-Digest`, which are PUT-only.
For an encrypted or client-verified archive, create it locally and upload it with PUT.

### Archive (tar)

```bash
# Upload directory as tar.gz
tar czf - *.txt | curl --upload-file - https://transfer.example.com/files.tar.gz

# Download and extract
curl https://transfer.example.com/<token>/files.tar.gz | tar xzf -

# Without compression
tar cf - *.txt | curl --upload-file - https://transfer.example.com/files.tar
curl https://transfer.example.com/<token>/files.tar | tar xf -
```

### Standalone scanning

```bash
# Scan with ClamAV without creating a transfer
curl --upload-file ./hello.txt https://transfer.example.com/hello.txt/scan

# Submit to VirusTotal without creating a transfer
curl --upload-file ./hello.txt https://transfer.example.com/hello.txt/virustotal
```

`PUT /{filename}/scan` uses ClamAV and `PUT /{filename}/virustotal` uses VirusTotal.
Neither creates a persistent transfer or download URL. Request data is staged temporarily;
the VirusTotal endpoint also sends it to VirusTotal, a third-party service.

### Encryption

```bash
# Server-side encryption
curl --upload-file ./secret.txt -H "Encrypt-Password: mypass" https://transfer.example.com/secret.txt
curl -H "Decrypt-Password: mypass" https://transfer.example.com/<token>/secret.txt -o ./secret.txt

# Client-side GPG encryption
cat ./secret.txt | gpg -ac -o- | curl -X PUT --upload-file "-" https://transfer.example.com/secret.txt
curl https://transfer.example.com/<token>/secret.txt | gpg -o- > ./secret.txt

# Both combined
cat ./secret.txt | gpg -ac -o- | curl -X PUT --upload-file "-" -H "Encrypt-Password: mypass" https://transfer.example.com/secret.txt
```

### Request Headers

| Header | Scope | Description | Example |
|--------|-------|-------------|---------|
| `File-Lifetime` | PUT, multipart POST | Positive duration or future expiry date | `7d`, `12h30m`, `2027-04-15T00:00:00Z` |
| `Max-Downloads` | PUT, multipart POST | Download limit | `1`, `5`, `100` |
| `Token` | PUT, single-file POST `/`, POST `/archive` | Custom URL slug (min 4 chars, `a-z0-9-`) | `my-slug` |
| `Encrypt-Password` | PUT | Server-side encryption password | any string |
| `Content-Digest` | PUT | Validate the uploaded bytes before storage | `sha-256=:<base64>:` |
| `Decrypt-Password` | GET | Decrypt an encrypted download | any string |
| `Authorization` | Admin API | Per-file capability token | `Bearer <admin-token>` |
| `Accept` | PUT, multipart POST | Request structured upload metadata | `application/json` |

### Response Headers

| Header | Response scope | Description |
|--------|----------------|-------------|
| `Location` | PUT, single-file POST `/`, POST `/archive` | URL of the created file; uploads return `201 Created` |
| `Link` | PUT, single-file POST `/`, POST `/archive` | Private administration and deletion links with URI relation types |
| `Repr-Digest` | Unencrypted GET/HEAD; decrypted GET | SHA-256 of the entire selected file representation, including on range responses |
| `Sunset` | GET, HEAD for an expiring file | Expected unavailability time as an HTTP date |
| `X-Remaining-Downloads` | GET, HEAD | Remaining download count |
| `Cache-Control: no-store` | Upload responses, file GET/HEAD, admin API | Prevent caching of capabilities and download-limited responses |
| `Vary: Accept` | Upload responses | Response format depends on `Accept` |

`Expires` is reserved for HTTP cache freshness. It is not used for file retention.
`Sunset` is a hint about the file's lifetime, not a guarantee of availability: deletion,
download limits or physical cleanup can make the file unavailable earlier.

Multi-file `POST /` responses return `201 Created` with per-file metadata in JSON (or
newline-separated URLs in plain text). They have no single-file `Location`, `Link`, or
digest headers. Upload responses do not put a file's hash in `Content-Digest` or
`Repr-Digest`: their body describes the upload result, not the file contents.

### Upload metadata

PUT and multipart POST use the same JSON envelope when requested with `Accept: application/json`:

```json
{
  "files": [
    {
      "filename": "hello.txt",
      "url": "https://transfer.example.com/<token>/hello.txt",
      "deleteUrl": "https://transfer.example.com/<token>/hello.txt/<deletion-token>",
      "adminUrl": "https://transfer.example.com/admin/<token>/hello.txt#<admin-token>",
      "sha256": "<64 lowercase hexadecimal characters>",
      "expires": "2027-04-15T00:00:00Z"
    }
  ]
}
```

`expires` is `null` when no expiry is configured. `sha256` describes the bytes received
from the uploader, including the plaintext before server-side encryption. Keep the full
response private; share only the public `url`. Plain-text responses remain convenient
for shell pipelines: one download URL per line, without metadata.

### Checksums

The server accepts one SHA-256 value in `Content-Digest`, using the byte-sequence format
from [RFC 9530](https://www.rfc-editor.org/rfc/rfc9530.html). The algorithm key is `sha-256`
and the hash is base64 encoded between colons. Multiple digests, other algorithms, and
parameters are not supported by this upload API and return `400`.

```bash
# Validate the upload before storage and receive the resulting metadata
curl --upload-file ./hello.txt \
  -H "Accept: application/json" \
  -H "Content-Digest: sha-256=:$(openssl dgst -sha256 -binary ./hello.txt | openssl base64 -A):" \
  https://transfer.example.com/hello.txt

# Inspect the full file's digest, size and expiry without counting as a download
curl -sI https://transfer.example.com/<token>/hello.txt

# Compute a base64 SHA-256 locally for comparison with Repr-Digest
openssl dgst -sha256 -binary ./hello.txt | openssl base64 -A
```

Malformed or mismatching upload digests return `400`; no upload is stored. For GET and
HEAD, `Repr-Digest` describes the entire file, even when a range request returns only
part of it. A `Content-Digest` of that partial body would be a different value.

Encrypted files omit `Repr-Digest` on HEAD and ciphertext downloads. A decrypted GET
returns the plaintext digest. This prevents a public link alone from revealing the
plaintext checksum. The private upload JSON and authenticated admin API retain it.

The installable CLI handles digest encoding and validation:

```bash
transfer ./big.iso --verify
```

It sends `Content-Digest` and reports success only after `201 Created` confirms that the
server accepted the validated upload. Reinstall the CLI when migrating an existing server.

### File administration

Single-file upload responses (including ZIP creation) include this [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288.html)
link relation, also available as `adminUrl` in JSON:

```http
Link: <https://transfer.example.com/admin/sample-token/hello.txt#example-admin-capability>; rel="https://github.com/frankhommers/transfer.cs#file-administration"
```

The relation identifies the private administration page for the uploaded file. The
capability follows `#`, so it is not sent in the HTTP request target. The UI saves it in
`sessionStorage`, removes it from the address bar, and sends it to the admin API as
`Authorization: Bearer <admin-token>`. The admin token is independent of the deletion token.

```bash
curl -H "Authorization: Bearer <admin-token>" https://transfer.example.com/api/admin/<token>/hello.txt
curl -X DELETE -H "Authorization: Bearer <admin-token>" https://transfer.example.com/api/admin/<token>/hello.txt
```

Download IP history is disabled by default. Enable it with
`TransferCs__DownloadLogEnabled=true`; `TransferCs__DownloadLogMaxEntries` bounds the
retained entries while the total counter continues increasing. Full client IP addresses
are stored, so enable this only when your privacy policy and local law permit it.

### File deletion

The deletion relation identifies a capability URL that accepts DELETE, also available
as `deleteUrl` in JSON. It does not use GET for deletion:

```http
Link: <https://transfer.example.com/sample-token/hello.txt/example-deletion-capability>; rel="https://github.com/frankhommers/transfer.cs#file-deletion"
```

Keep both management links private. A single upload can return multiple `Link` fields;
HTTP clients may combine them into one comma-separated field. The URI relation names
are application-defined extensions using the standard Link syntax.

### Header migration

This is a breaking API change. Old response headers are no longer emitted. Uploads
using `Expires`, `Max-Days`, `Expected-Checksum`, `X-Expected-Checksum`, `X-Token`, or
`X-Encrypt-Password` return `400` rather than silently losing their options. Replace
these with `File-Lifetime`, `Content-Digest`, `Token`, and `Encrypt-Password` as appropriate.
`X-Decrypt-Password` is rejected on GET; use `Decrypt-Password`. Admin requests require
`Authorization: Bearer`; `Admin-Token` is no longer accepted. Replace `X-Url-Admin` and
`X-Url-Delete` parsing with `Link` or JSON metadata, and `Checksum`/`X-Checksum` with
`Repr-Digest` on downloads or `sha256` in private metadata. Calculate remaining time
from `Sunset` instead of `X-Remaining-Days`.

### AI Agent Integration

Every instance serves a dynamic `/SKILL.md` with instance-specific usage instructions,
base URL, available headers, and limits. Point your AI agent at it:

```bash
curl https://transfer.example.com/SKILL.md
```

## Configuration

Settings use the `TransferCs__` environment-variable prefix. The current storage
implementation is the local filesystem.

| Variable | Default | Description |
|----------|---------|-------------|
| `TransferCs__Title` | `transfer.cs` | Instance title shown in the UI |
| `TransferCs__BaseUrl` | *(request URL)* | Absolute public base URL used in generated links; when empty, derive it from the request |
| `TransferCs__BasePath` | `./data` app; `/data` container | Local payload and metadata directory |
| `TransferCs__TempPath` | System temp; `/tmp` container | Temporary directory for uploads, multipart bodies, ZIPs, encryption/decryption, bundles, and scans |
| `TransferCs__PurgeDays` | `0` (disabled) | Default upload expiry in days and physical file-age threshold |
| `TransferCs__PurgeIntervalHours` | `0` (disabled) | Global interval for physical purge runs |
| `TransferCs__MaxUploadSizeKb` | `0` (unlimited) | Upload size limit in KB |
| `TransferCs__MinFreeDiskSpaceMb` | `0` (disabled) | Minimum available disk space in MiB on each filesystem used for payloads and temporary files |
| `TransferCs__RandomTokenLength` | `10` | Generated token length; must be from 6 through 128 |
| `TransferCs__DownloadLogEnabled` | `false` | Retain client IP and UTC time for accepted downloads |
| `TransferCs__DownloadLogMaxEntries` | `50` | Recent download entries retained per file; effective minimum is one when logging is enabled |
| `TransferCs__ForceHttps` | `false` | Redirect HTTP requests to HTTPS with status 308, except `/health`, its subpaths, and `.onion` hosts |
| `TransferCs__RateLimitRequestsPerMinute` | `0` (disabled) | Global fixed-window request limit per client IP |
| `TransferCs__ClamAvHost` | *(empty)* | ClamAV `host` or `host:port`; the default port is `3310` |
| `TransferCs__PerformClamAvPrescan` | `false` | Prescan PUT uploads when `ClamAvHost` is also configured |
| `TransferCs__VirusTotalKey` | *(empty)* | API key for the standalone VirusTotal scan endpoint |
| `TransferCs__HttpAuthUser` | *(empty)* | Optional basic-auth username for upload and legacy deletion methods |
| `TransferCs__HttpAuthPass` | *(empty)* | Password paired with `HttpAuthUser` |
| `TransferCs__HttpAuthHtpasswd` | *(empty)* | Optional path to an htpasswd-style credentials file |
| `TransferCs__HttpAuthIpWhitelist` | *(empty)* | Client IPs/CIDRs that bypass optional basic auth |
| `TransferCs__IpWhitelist` | *(empty)* | When set, reject every client outside these IPs/CIDRs |
| `TransferCs__IpBlacklist` | *(empty)* | Reject clients in these IPs/CIDRs |
| `TransferCs__CorsDomains` | *(empty)* | Comma-separated allowed CORS origins |
| `TransferCs__ProxyPath` | *(empty)* | Public path prefix added to generated links when `BaseUrl` is empty |
| `TransferCs__ProxyPort` | *(request port)* | Public port used in generated links when `BaseUrl` is empty |
| `TransferCs__TrustedProxies` | *(empty)* | Proxy IPs/CIDRs allowed to supply `X-Forwarded-For`, `-Host`, and `-Proto` |
| `TransferCs__InitialSiteId` | *(empty)* | Exact configured site ID used as the legacy-data migration target; required with `Sites` |
| `TransferCs__Sites` | *(empty)* | Site-ID-keyed definitions; see below |

`PurgeDays` is used as an upload's logical expiry when no `File-Lifetime` header is supplied. Logical expiry blocks downloads.
Separately, a positive `PurgeIntervalHours` runs physical cleanup immediately at startup
and then every N hours; zero disables physical deletion. Physical cleanup removes payloads
whose filesystem `CreationTimeUtc` is older than that site's `PurgeDays`. It does not use
an upload's `MaxDate` value, so logical and physical expiry can occur at different
times.

For PUT, `MaxUploadSizeKb` limits the request payload/file. For multipart POST, it is
checked for each file, while Kestrel's request-body limit still applies to the complete
multipart request. In multi-site mode Kestrel uses the largest effective finite site
limit; if any site is unlimited, its global request-body limit is unlimited. The selected
site's per-file limit is still enforced by the application. ClamAV prescan applies only
to PUT uploads and generated ZIPs when `PerformClamAvPrescan=true` and `ClamAvHost` is set;
independent files uploaded through `POST /` are not prescanned.

### Disk space reserve

Set `TransferCs__MinFreeDiskSpaceMb=5120` to keep a 5 GiB reserve. Zero disables the
guard; negative values are rejected at startup. The setting is global across all sites.
The server checks the actual filesystem behind each destination, including container
volumes and `TempPath`, before writing more data. If storage and temporary files use
different filesystems, each must have the configured reserve available.

Checks continue while writing, including requests without `Content-Length`, ZIP creation,
encryption, scans, and generated download bundles. Concurrent writes within one server
process share the same guard. Temporary copies count too: a transfer can be refused even
if its final file alone would fit. On refusal the server returns `507 Insufficient Storage`
with a readable message. Failed uploads and their temporary files are cleaned up; an
independent multipart batch is rolled back as a whole. Uploads work again once space is
available. No existing uploads are automatically deleted by this guard.

Ordinary downloads and deletion remain available. Updates to existing metadata, such as
download counters, may use the reserve. Downloads requiring temporary files can return
507. This is an application guard, not an operating-system quota or a preallocated file:
filesystem overhead, other processes, and other server replicas can still consume free
space. A small additional margin covers allocation overhead; use filesystem quotas when
a hard boundary is required. Health checks continue to report process availability.

### Access controls

Basic auth, when configured, protects PUT, both multipart POST endpoints, and the legacy DELETE route.
Basic auth does not protect GET or HEAD, so downloads remain public. The admin API
bypasses basic auth and instead requires the per-file `Authorization: Bearer` token. A configured
username/password or a matching entry in `HttpAuthHtpasswd` is accepted; the file reader
supports only plaintext passwords and `{SHA}` SHA-1/base64 entries.

`HttpAuthIpWhitelist`, `IpWhitelist`, and `IpBlacklist` accept comma-separated individual
IPv4/IPv6 addresses and CIDR ranges. The first only bypasses basic auth; the application
whitelist and blacklist apply to all routes. `TrustedProxies` accepts the same formats
and controls which direct proxy sources may set the forwarded client IP, host, and scheme
used by IP controls, rate limiting, download logs, HTTPS handling, and site selection.
Use `*` only when the application cannot be reached directly, because it allows any
source to supply forwarded values.

### Multi-site configuration

When `Sites` is configured, every request except exact `/health` must match a configured
host. Unknown hosts receive `421 Misdirected Request`. Host matching happens after
trusted forwarded-header processing, so configure `TrustedProxies` when TLS terminates
at a reverse proxy.

```json
{
  "TransferCs": {
    "BasePath": "/data",
    "PurgeIntervalHours": 24,
    "InitialSiteId": "public",
    "Sites": {
      "public": {
        "Hosts": ["transfer.example.com"],
        "Title": "Public transfers",
        "BaseUrl": "https://transfer.example.com",
        "DataDirectory": "public",
        "PurgeDays": 14,
        "MaxUploadSizeKb": 1048576,
        "RandomTokenLength": 12
      },
      "internal": {
        "Hosts": ["send.internal.example.com"],
        "Title": "Internal transfers",
        "DataDirectory": "internal",
        "PurgeDays": 3,
        "MaxUploadSizeKb": 10485760
      }
    }
  }
}
```

The same site can be configured with nested environment variables in Compose. Each host
uses a numeric array index; setting `TransferCs__Sites` by itself does not create a site:

```yaml
environment:
  TransferCs__InitialSiteId: public
  TransferCs__Sites__public__Hosts__0: transfer.example.com
  TransferCs__Sites__public__DataDirectory: public
  TransferCs__Sites__public__BaseUrl: https://transfer.example.com
```

Each site requires at least one `Hosts` entry. `DataDirectory` is site-specific and
defaults to the site ID. `Title`, `BaseUrl`, `PurgeDays`, `MaxUploadSizeKb`, and
`RandomTokenLength` may override global defaults. Other settings, including
`PurgeIntervalHours`, authentication, IP controls, scanning, and temporary storage,
remain global.

Host matching is an exact, case-insensitive match after surrounding whitespace and a
trailing dot are ignored; wildcards are not supported. Site IDs are case-sensitive,
1-63 characters, use only lowercase letters, numbers, and hyphens, and must start and end
with a letter or number. `InitialSiteId` is required whenever `Sites` is non-empty and
must exactly identify one configured site.

Every `DataDirectory` must be a case-insensitively unique single directory name beneath
`BasePath`; it cannot be `.` or `..`, resolve outside `BasePath`, or be an existing
symbolic link. This keeps storage at `BasePath/DataDirectory`, so equal tokens on
different sites remain isolated.

Normal startup never migrates legacy data. Running the normal multi-site application
first can initialize the target `DataDirectory`; a non-empty target intentionally blocks
the migration. Use this ordered runbook before the first multi-site startup:

1. Update the Compose file and environment to the new image tag and multi-site
   configuration. Define `Sites`, `InitialSiteId`, and each site's `DataDirectory`, keep
   the existing volume mounted at `/data`, and do not start the normal application yet.
   Use nested environment keys as shown above or mount the JSON at
   `/app/appsettings.Production.json` for both `compose run` and the normal service.
2. Pull the configured service image without starting it:

   ```bash
   docker compose pull transfer-cs
   ```

3. Stop the service so no writes occur during backup or migration:

   ```bash
   docker compose stop transfer-cs
   ```

4. Create and verify a timestamped backup in `./backups`. The archive is streamed from a
   one-off container to a host-owned temporary file, then published under its final name
   only after creation and integrity checking succeed:

   ```bash
   (
     set -eu
     umask 077
     mkdir -p ./backups
     BACKUP_FILE="transfer-data-pre-multisite-$(date -u +%Y%m%dT%H%M%SZ).tar.gz"
     PARTIAL="./backups/$BACKUP_FILE.partial"
     FINAL="./backups/$BACKUP_FILE"
     test ! -e "$FINAL"
     rm -f "$PARTIAL"
     trap 'rm -f "$PARTIAL"' EXIT HUP INT TERM
     docker compose run --rm --no-deps \
       -T \
       --entrypoint /bin/sh \
       transfer-cs \
       -c 'exec tar -C /data -czf - .' > "$PARTIAL"
     tar -tzf "$PARTIAL" >/dev/null
     chmod 600 "$PARTIAL"
     mv "$PARTIAL" "$FINAL"
     trap - EXIT HUP INT TERM
     printf 'Verified backup: %s\n' "$FINAL"
   )
   ```

5. Inspect the root of `/data` without invoking the application entrypoint:

   ```bash
   docker compose run --rm --no-deps --entrypoint /bin/sh transfer-cs -c 'ls -la /data'
   ```

   The migration moves every root-level directory whose name is not exactly a configured
   site `DataDirectory` into the `InitialSiteId` site's `DataDirectory`. It does not
   validate token naming, so directories such as `@eaDir`, `tmp`, and any unrelated
   directory would also be moved. Root-level files are not moved. After confirming the
   backup, manually relocate or remove non-token directories that must not be migrated;
   the command does not delete them automatically.
6. Run the explicit migration. It reports the number of directories moved and exits
   without starting the HTTP server:

   ```bash
   docker compose run --rm --no-deps transfer-cs migrate-legacy-data
   ```

7. Compare the reported count with the directories identified in step 5, then inspect
   the configured target `DataDirectory`:

   ```bash
   INITIAL_SITE_DATA_DIRECTORY='replace-with-configured-data-directory'
   docker compose run --rm --no-deps \
     --entrypoint /bin/sh \
     -e "INITIAL_SITE_DATA_DIRECTORY=$INITIAL_SITE_DATA_DIRECTORY" \
     transfer-cs \
     -c 'ls -la "/data/$INITIAL_SITE_DATA_DIRECTORY"'
   ```

8. Start the service only after the target contents and migration count are correct:

   ```bash
   docker compose up -d transfer-cs
   ```

The migration fails closed when multi-site configuration is missing, names conflict by
case, a configured site directory is a symbolic link, non-directory, or non-empty,
any symbolic link exists anywhere in a legacy source, or a destination would collide.
Resolve the condition and inspect the stopped volume before retrying.

#### Recovery after a failed migration

If migration partially moves directories or verification fails, keep `transfer-cs`
stopped. Do not blindly rerun migration against a partially populated target. Either
reconcile every source and target directory manually, or restore the verified backup and
restart the runbook from the root inspection.

The following restore **replaces all active `/data` contents**, including hidden entries.
Preserve the failed state separately first if it is needed for diagnosis. Set
`BACKUP_FILE` to the exact verified archive from step 4. Leave `RESTORE_CONFIRM` empty
until replacement is intended, then change it to `REPLACE_ACTIVE_DATA`:

```bash
(
  set -eu
  BACKUP_FILE='replace-with-verified-backup-filename'
  RESTORE_CONFIRM=''
  test "$RESTORE_CONFIRM" = 'REPLACE_ACTIVE_DATA'
  case "$BACKUP_FILE" in
    ''|*/*) printf 'BACKUP_FILE must be a filename in ./backups\n' >&2; exit 1 ;;
  esac
  BACKUP_DIR="$PWD/backups"
  docker compose stop transfer-cs
  tar -tzf "$BACKUP_DIR/$BACKUP_FILE" >/dev/null
  docker compose run --rm --no-deps \
    -T \
    --user 0:0 \
    --entrypoint /bin/sh \
    -e "RESTORE_CONFIRM=$RESTORE_CONFIRM" \
    transfer-cs \
    -c 'set -eu
test "$RESTORE_CONFIRM" = "REPLACE_ACTIVE_DATA"
find /data -mindepth 1 -maxdepth 1 -exec rm -rf {} +
tar -C /data -xzf -' < "$BACKUP_DIR/$BACKUP_FILE"
)
```

The restore streams the host-owned archive into a root one-off container that inherits
the service's `/data` volume. The bounded `find` removes every immediate child, including
hidden entries, without removing the mount point itself. If restore fails, leave the
service stopped and repeat the guarded restore after resolving the error. After a
successful restore, inspect `/data` again with the command in step 5 and reclassify its
root directories before retrying migration. Do not start the application until migration
and verification succeed. Restoring an archive can assign new filesystem creation times
on filesystems with native birth-time support, which restarts the physical `PurgeDays`
age. Reconcile retention after restore by checking metadata expiry and original archive
timestamps before returning the service to normal operation.

Historical exception: `.multisite-migration-v1` is an obsolete marker written only by
the short-lived automatic-migration version. If that version already migrated the volume,
verify the site directories and start normally instead of running this migration. Current
code neither reads nor writes the marker; remove it only after verification if desired.

## Deploy with Traefik

Traefik is a common reverse proxy for Docker deployments. Ordinary file downloads are
streamed; PUT uploads, decrypted downloads, and generated bundles use transfer.cs
temporary storage. Size `TempPath` for those operations. Reverse-proxy buffering adds a
second full-body buffer and should remain disabled for large transfers.

### docker-compose.yml

```yaml
services:
  transfer-cs:
    image: ghcr.io/frankhommers/transfer.cs:main
    restart: unless-stopped
    volumes:
      - transfer-data:/data
    environment:
      TransferCs__PurgeDays: 14
      TransferCs__PurgeIntervalHours: 24
      TransferCs__MaxUploadSizeKb: 10485760  # 10 GB
      TransferCs__BaseUrl: https://transfer.example.com
      TransferCs__TrustedProxies: 172.16.0.0/12
    labels:
      traefik.enable: "true"
      traefik.http.routers.transfer.rule: Host(`transfer.example.com`)
      traefik.http.routers.transfer.entrypoints: websecure
      traefik.http.routers.transfer.tls.certresolver: letsencrypt
      traefik.docker.network: proxy
      traefik.http.services.transfer.loadbalancer.server.port: "8080"
      traefik.http.services.transfer.loadbalancer.responseForwarding.flushInterval: "100ms"
    networks:
      - proxy
    logging:
      driver: "json-file"
      options:
        max-size: "10m"
        max-file: "3"

volumes:
  transfer-data:

networks:
  proxy:
    external: true
```

The external `proxy` network must also be attached to Traefik. Replace its name and the
`traefik.docker.network` label together if your proxy network uses another name.

### Traefik static configuration

For large file transfers, increase the entrypoint timeouts in `traefik.yml`:

```yaml
entryPoints:
  websecure:
    address: ":443"
    transport:
      respondingTimeouts:
        readTimeout: 3600s  # 1 hour to receive a request body
        writeTimeout: 0s    # unlimited response write time
        idleTimeout: 180s
```

Traefik's request read timeout defaults to 60 seconds. Increase it enough for the largest
expected upload; the defaults for write and idle timeouts are `0s` and `180s`.

### Important: Custom headers

Preserve `Authorization`, `Content-Digest`, `File-Lifetime`, `Token`, `Encrypt-Password`,
`Decrypt-Password`, and `Max-Downloads` on requests, and `Location`, `Link`, `Repr-Digest`,
and `Sunset` on responses. Traefik passes them through by default. Check any configured
`customRequestHeaders` or `customResponseHeaders` overrides. When CORS is enabled,
transfer.cs exposes the response headers needed by browser clients.

`TrustedProxies` is required for correct client-IP filtering, auth bypass lists, rate
limiting, and download logging behind a reverse proxy. Prefer the actual Docker network
CIDR. `*` trusts every source and is only safe when the app cannot be reached except
through Traefik; otherwise clients can forge `X-Forwarded-For`.

### Traefik with file provider (non-Docker)

If you use Traefik's file provider instead of Docker labels:

```yaml
http:
  routers:
    transfer:
      rule: Host(`transfer.example.com`)
      entryPoints:
        - websecure
      tls:
        certResolver: letsencrypt
      service: transfer

  services:
    transfer:
      loadBalancer:
        servers:
          - url: http://transfer-cs:8080
```

### Nginx (alternative)

If you use Nginx instead of Traefik:

```nginx
server {
    listen 443 ssl;
    server_name transfer.example.com;

    client_max_body_size 10G;
    proxy_http_version 1.1;
    proxy_request_buffering off;
    proxy_buffering off;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;

    location / {
        proxy_pass http://transfer-cs:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
    }
}
```

Add the Nginx address or network CIDR to `TransferCs__TrustedProxies`; otherwise forwarded
client addresses are intentionally ignored.

## Build

```bash
docker build -t transfer-cs .
```

## Development

Open the project in JetBrains Rider and use the **Full Stack** run configuration, run
`./start-dev.sh`, or start each process manually:

```bash
# Backend (with hot-reload)
cd backend/src/TransferCs.Api && dotnet watch run

# Frontend (with HMR)
cd frontend && bun install --frozen-lockfile && bun run dev
```

The frontend dev server runs on `:3002` and proxies API requests to the backend on `:5002`.

## Credits

Inspired by [transfer.sh](https://transfer.sh) by [DutchCoders](https://github.com/dutchcoders/transfer.sh). transfer.cs is a from-scratch reimplementation in C# / ASP.NET with a React frontend.
