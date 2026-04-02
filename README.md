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

> **Note:** `X-Encrypt-Password`, `X-Decrypt-Password`, and `X-Token` are also accepted for backward compatibility.

### Response Headers

| Header | Description |
|--------|---------|
| `X-Url-Delete` | URL to delete the uploaded file |
| `Expires` | Expiry date of the upload |
| `X-Remaining-Downloads` | Remaining download count |
| `X-Remaining-Days` | Remaining days until expiry |

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
| `TransferCs__ForceHttps` | `false` | Redirect HTTP to HTTPS |
| `TransferCs__RateLimitRequestsPerMinute` | `0` (disabled) | Rate limit per IP |
| `TransferCs__ClamAvHost` | *(empty)* | ClamAV host for virus scanning |
| `TransferCs__PerformClamAvPrescan` | `false` | Scan uploads before storing |
| `TransferCs__VirusTotalKey` | *(empty)* | VirusTotal API key |
| `TransferCs__CorsDomains` | *(empty)* | Comma-separated CORS origins |

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
