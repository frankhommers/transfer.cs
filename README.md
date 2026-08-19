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

### Multiple files

```bash
# Upload via multipart POST
curl -X POST -F "file=@a.txt" -F "file=@b.txt" https://transfer.example.com/

# Bundle download
curl "https://transfer.example.com/bundle.zip?files=token1/a.txt,token2/b.txt" -o bundle.zip
```

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

| Header | Description | Example |
|--------|-------------|---------|
| `Expires` | Expiry duration or date | `7d`, `12h30m`, `2026-04-15T00:00:00Z` |
| `Max-Downloads` | Download limit | `1`, `5`, `100` |
| `Encrypt-Password` | Server-side encryption password | any string |
| `Token` | Custom URL slug (min 4 chars, `a-z0-9-`) | `my-slug` |
| `Expected-Checksum` | Reject the upload unless it hashes to this SHA-256 | `sha256:9f86d081...` |

> **Note:** `X-Encrypt-Password`, `X-Decrypt-Password`, `X-Token`, and `X-Expected-Checksum` are also accepted for backward compatibility.

### Response Headers

| Header | Description |
|--------|---------|
| `X-Url-Delete` | URL to delete the uploaded file |
| `X-Url-Admin` | Private admin URL; its capability token is kept in the URL fragment |
| `Expires` | Expiry date of the upload |
| `Checksum` | `sha256:<hex>` of the file as received (also sent as `X-Checksum`) |
| `X-Remaining-Downloads` | Remaining download count |
| `X-Remaining-Days` | Remaining days until expiry |

### Checksums

Every upload response carries a `Checksum` header with the SHA-256 of the bytes the
server received. For server-side encrypted uploads this is the digest of the
*plaintext*, so it matches what you compute locally — the stored ciphertext is not
byte-reproducible.

```bash
# Show the checksum of an upload
curl -sD- --upload-file ./hello.txt https://transfer.example.com/hello.txt | grep -i '^checksum:'

# Have the server reject a corrupted or truncated upload instead of storing it
curl --upload-file ./hello.txt \
  -H "Expected-Checksum: sha256:$(shasum -a 256 ./hello.txt | cut -d' ' -f1)" \
  https://transfer.example.com/hello.txt
```

A mismatch returns `400` and nothing is stored. `Expected-Checksum` accepts a bare
64-character hex digest too, so you can paste `sha256sum` output directly. It is
supported on `PUT` uploads; multipart `POST` only reports checksums.

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

Every new upload returns an `X-Url-Admin` header. Keep this URL private: it opens a
non-discoverable page with file metadata, the checksum, download counters, optional IP
history, and permanent deletion. The admin capability is stored after `#` and is never
sent in a URL, query string, referrer, or proxy access log. It is separate from the
legacy deletion capability in `X-Url-Delete`.

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

All settings are configured via environment variables with the `TransferCs__` prefix:

| Variable | Default | Description |
|----------|---------|-------------|
| `TransferCs__Title` | `transfer.cs` | Instance title shown in UI |
| `TransferCs__BaseUrl` | *(auto-detect)* | Override base URL (e.g. `https://transfer.example.com`) |
| `TransferCs__BasePath` | `./data` | Storage directory |
| `TransferCs__PurgeDays` | `0` (disabled) | Auto-delete files after N days |
| `TransferCs__PurgeIntervalHours` | `0` (disabled) | How often to run purge |
| `TransferCs__MaxUploadSizeKb` | `0` (unlimited) | Max upload size in KB |
| `TransferCs__RandomTokenLength` | `10` | Length of generated tokens |
| `TransferCs__DownloadLogEnabled` | `false` | Store client IP and UTC time for accepted GET downloads |
| `TransferCs__DownloadLogMaxEntries` | `50` | Maximum recent IP log entries retained per file |
| `TransferCs__ForceHttps` | `false` | Redirect HTTP to HTTPS |
| `TransferCs__RateLimitRequestsPerMinute` | `0` (disabled) | Rate limit per IP |
| `TransferCs__TrustedProxies` | *(empty)* | Proxy IPs/CIDRs trusted for `X-Forwarded-*`; use `*` only when direct access is impossible |
| `TransferCs__ClamAvHost` | *(empty)* | ClamAV host for virus scanning |
| `TransferCs__PerformClamAvPrescan` | `false` | Scan uploads before storing |
| `TransferCs__VirusTotalKey` | *(empty)* | VirusTotal API key |
| `TransferCs__CorsDomains` | *(empty)* | Comma-separated CORS origins |

### Multi-site configuration

When `Sites` is configured, every request except exact `/health` must match a configured
host. Unknown hosts receive `421 Misdirected Request`. Host matching happens after
trusted forwarded-header processing, so configure `TrustedProxies` when TLS terminates
at a reverse proxy.

```json
{
  "TransferCs": {
    "BasePath": "/data",
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

All site fields except `Hosts` are overrides. `DataDirectory` defaults to the site ID.
Storage is rooted at `BasePath/DataDirectory`, and equal tokens on different hosts remain
fully isolated. Site IDs use lowercase letters, numbers, and hyphens; hosts are exact
matches without wildcards.

The first multi-site startup moves existing root-level token directories into the
`InitialSiteId` data directory and writes `.multisite-migration-v1`. Startup refuses
collisions or ambiguous non-empty configured site directories instead of merging data.
Back up `BasePath` before enabling multi-site. After migration, removing or renaming a
site never causes its old directory to be migrated again.

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
