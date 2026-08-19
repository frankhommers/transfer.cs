# transfer.cs

Easy and fast file sharing from the command line. Inspired by [transfer.sh](https://github.com/dutchcoders/transfer.sh).

## Features

- Upload and download files via curl
- Custom URL tokens (`-H "Token: my-slug"`)
- Server-side encryption (`-H "Encrypt-Password: secret"`)
- Client-side GPG encryption (pipe-based)
- Expiry and download limits
- Multi-file upload via multipart POST
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
curl --upload-file ./hello.txt -H "Expires: 7d" -H "Max-Downloads: 5" https://transfer.example.com/hello.txt

# Download
curl https://transfer.example.com/<token>/hello.txt -o ./hello.txt

# Delete (URL from X-Url-Delete response header)
curl -X DELETE https://transfer.example.com/<token>/hello.txt/<deletion-token>
```

PUT uploads accept `/{filename}`, `/put/{filename}`, or `/upload/{filename}`. GET and
HEAD support canonical `/{token}/{filename}` plus `/get/{token}/{filename}`,
`/download/{token}/{filename}`, and `/inline/{token}/{filename}`. GET uses attachment
disposition for the canonical, `get`, and `download` routes, while `inline` uses inline
disposition. HEAD always reports attachment disposition, including on the `inline` route.

### Multiple files

```bash
# Upload via multipart POST
curl -X POST -F "file=@a.txt" -F "file=@b.txt" https://transfer.example.com/

# Bundle download
curl "https://transfer.example.com/bundle.zip?files=token1/a.txt,token2/b.txt" -o bundle.zip
```

Multipart POST supports the expiry, download-limit, and token headers below. A custom
token is rejected when the request contains multiple files. Encryption and expected
checksum validation are not supported for multipart uploads.

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
| `Expires` | PUT, multipart POST | Expiry duration or date | `7d`, `12h30m`, `2026-04-15T00:00:00Z` |
| `Max-Days` | PUT, multipart POST | Legacy expiry in days | `7` |
| `Max-Downloads` | PUT, multipart POST | Download limit | `1`, `5`, `100` |
| `Token` / `X-Token` | PUT, multipart POST | Custom URL slug (min 4 chars, `a-z0-9-`) | `my-slug` |
| `Encrypt-Password` / `X-Encrypt-Password` | PUT | Server-side encryption password | any string |
| `Expected-Checksum` / `X-Expected-Checksum` | PUT | Reject unless the body matches this SHA-256 | `sha256:9f86d081...` |
| `Decrypt-Password` / `X-Decrypt-Password` | GET | Decrypt an encrypted download | any string |
| `Admin-Token` | Admin API | Authorize access to a file's admin API | capability token |

### Response Headers

| Header | Response scope | Description |
|--------|----------------|-------------|
| `X-Url-Delete` | PUT | URL to delete the uploaded file |
| `X-Url-Admin` | PUT, single-file multipart POST | Private admin URL with its capability token in the fragment |
| `Expires` | PUT when expiry is set; HEAD for an expiring file | Expiry date |
| `Checksum` / `X-Checksum` | PUT; single-file multipart POST; unencrypted GET/HEAD; decrypted GET | `sha256:<hex>` of the file as received |
| `X-Remaining-Downloads` | GET, HEAD | Remaining download count |
| `X-Remaining-Days` | GET, HEAD | Remaining days until expiry |

A single-file multipart response has no deletion URL. A multi-file multipart response
contains only newline-separated download URLs, with no per-file admin, deletion, or
checksum headers.

### Checksums

Successful PUT and single-file multipart POST responses carry a `Checksum` header with the
SHA-256 of the bytes the server received. For server-side encrypted PUT uploads this is
the digest of the *plaintext*, so it matches what you compute locally; the stored
ciphertext is not byte-reproducible.

```bash
# Show the checksum of an upload
curl -sD- --upload-file ./hello.txt https://transfer.example.com/hello.txt | grep -i '^checksum:'

# Have the server reject a corrupted or truncated upload instead of storing it
curl --upload-file ./hello.txt \
  -H "Expected-Checksum: sha256:$(shasum -a 256 ./hello.txt | cut -d' ' -f1)" \
  https://transfer.example.com/hello.txt
```

A mismatch returns `400` and nothing is stored. `Expected-Checksum` accepts a bare
64-character hexadecimal digest, or that digest prefixed by case-insensitive `sha256` or
`sha-256` and a `:` or `=` separator. Do not include the filename from a full `sha256sum`
output line. It is supported only on PUT uploads. Single-file multipart POST reports a
checksum; multi-file multipart POST does not expose per-file checksum headers.

The download response carries the same `Checksum` header, except for encrypted files —
there the plaintext digest is only sent when you supply `Decrypt-Password`, so a link
alone can never be used to confirm the contents.

To read the checksum, size and expiry *without* downloading, use `HEAD`. It does not
count as a download, so a `Max-Downloads: 1` link stays intact:

```bash
curl -sI https://transfer.example.com/<token>/hello.txt
```

For encrypted uploads the `Checksum` header is omitted on `HEAD`, since it cannot decrypt.

The installable CLI has a `--verify` flag that does the hashing for you:

```bash
transfer ./big.iso --verify
```

### Private file administration

Successful PUT and single-file multipart POST responses return an `X-Url-Admin` header. Keep
this URL private: the admin route is capability-protected and provides file metadata,
the checksum, download counters, optional IP history, and permanent deletion. The
capability follows `#`, so it is not sent in the HTTP request target or referrer. The UI
stores it in `sessionStorage`, removes it from the address bar, and sends it to the API
in the `Admin-Token` header. It is separate from the legacy deletion capability in
`X-Url-Delete`.

Download IP history is disabled by default. Enable it with
`TransferCs__DownloadLogEnabled=true`; `DownloadLogMaxEntries` bounds the retained
entries while the total counter continues increasing. Full client IP addresses are
stored, so enable this only when your privacy policy and local law permit it.

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
| `TransferCs__TempPath` | System temp; `/tmp` container | Directory used to stage PUT and standalone scan request bodies |
| `TransferCs__PurgeDays` | `0` (disabled) | Default upload expiry in days and physical file-age threshold |
| `TransferCs__PurgeIntervalHours` | `0` (disabled) | Global interval for physical purge runs |
| `TransferCs__MaxUploadSizeKb` | `0` (unlimited) | Upload size limit in KB |
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

`PurgeDays` is used as an upload's logical expiry when neither a valid `Expires`
header nor a positive `Max-Days` header supplies one. Logical expiry blocks downloads.
Separately, a positive `PurgeIntervalHours` runs physical cleanup immediately at startup
and then every N hours; zero disables physical deletion. Physical cleanup removes payloads
whose filesystem `CreationTimeUtc` is older than that site's `PurgeDays`. It does not use
an upload's `MaxDate`/`Expires` value, so logical and physical expiry can occur at different
times.

For PUT, `MaxUploadSizeKb` limits the request payload/file. For multipart POST, it is
checked for each file, while Kestrel's request-body limit still applies to the complete
multipart request. In multi-site mode Kestrel uses the largest effective finite site
limit; if any site is unlimited, its global request-body limit is unlimited. The selected
site's per-file limit is still enforced by the application. ClamAV prescan applies only
to PUT uploads and only when `PerformClamAvPrescan=true` and `ClamAvHost` is set;
multipart uploads are not prescanned.

Basic auth, when configured, protects PUT, multipart POST, and the legacy DELETE route.
Basic auth does not protect GET or HEAD, so downloads remain public. The admin API
bypasses basic auth and instead requires the per-file `Admin-Token`. A configured
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

Normal startup never moves data. Before the first multi-site startup, run the explicit
one-time migration command. It moves existing root-level token directories into the
`InitialSiteId` data directory, reports the number moved, and exits without starting the
HTTP server. It writes no marker.

```bash
docker compose stop transfer-cs

# Back up the volume before this step.
docker compose run --rm transfer-cs migrate-legacy-data

docker compose up -d transfer-cs
```

The command refuses collisions, symlinks, ambiguous non-empty configured site
directories, missing multi-site configuration, and a second execution after data has
already moved. Do not start the multi-site server before running the command: a new
upload would make the target site directory non-empty and intentionally block migration.

If `.multisite-migration-v1` exists because the short-lived automatic migration version
already migrated this volume, skip the command and start normally. The file is no longer
read or written and may be removed after verifying the site directories.

## Deploy with Traefik

Traefik is a common reverse proxy for Docker deployments. transfer.cs streams uploads and downloads directly — **do not** use the Traefik `buffering` middleware, as it will buffer the entire request body and timeout on large files.

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
      TransferCs__MaxUploadSizeKb: 10485760  # 10 GB
      TransferCs__BaseUrl: https://transfer.example.com
      TransferCs__TrustedProxies: 172.16.0.0/12
    labels:
      traefik.enable: "true"
      traefik.http.routers.transfer.rule: Host(`transfer.example.com`)
      traefik.http.routers.transfer.entrypoints: websecure
      traefik.http.routers.transfer.tls.certresolver: letsencrypt
      traefik.http.services.transfer.loadbalancer.server.port: "8080"
      traefik.http.services.transfer.loadbalancer.responseForwarding.flushInterval: "100ms"
    logging:
      driver: "json-file"
      options:
        max-size: "10m"
        max-file: "3"

volumes:
  transfer-data:
```

### Traefik static configuration

For large file transfers, increase the entrypoint timeouts in `traefik.yml`:

```yaml
entryPoints:
  websecure:
    address: ":443"
    transport:
      respondingTimeouts:
        readTimeout: 3600s   # 1 hour for large uploads
        writeTimeout: 3600s  # 1 hour for large downloads
        idleTimeout: 120s
```

Without these timeouts, Traefik will kill connections during large transfers (default is 60s).

### Important: Custom headers

transfer.cs uses custom request/response headers (`Token`, `Encrypt-Password`, `Expires`, `Max-Downloads`, etc.). Traefik passes these through by default. However, if you use a `headers` middleware with `customRequestHeaders` or `customResponseHeaders`, make sure you don't strip these headers. The `X-Url-Delete` response header is needed for clients to delete uploaded files.

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
      middlewares:
        - transfer-body
      service: transfer

  middlewares:
    transfer-body:
      buffering:
        maxRequestBodyBytes: 10737418240
        maxResponseBodyBytes: 10737418240
        memRequestBodyBytes: 10485760
        memResponseBodyBytes: 10485760

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
    proxy_request_buffering off;
    proxy_read_timeout 3600s;
    proxy_send_timeout 3600s;

    location / {
        proxy_pass http://transfer-cs:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

## Build

```bash
docker build -t transfer-cs .
```

## Development

Open the project in JetBrains Rider and use the **Full Stack** run configuration, or start manually:

```bash
# Backend (with hot-reload)
cd backend/src/TransferCs.Api && dotnet watch run

# Frontend (with HMR)
cd frontend && npm run dev
```

The frontend dev server runs on `:3002` and proxies API requests to the backend on `:5002`.

## Credits

Inspired by [transfer.sh](https://transfer.sh) by [DutchCoders](https://github.com/dutchcoders/transfer.sh). transfer.cs is a from-scratch reimplementation in C# / ASP.NET with a React frontend.
