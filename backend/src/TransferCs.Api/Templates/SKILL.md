---
name: "{{SkillName}}"
description: {{SkillDescription}}
---

# {{Title}}

File sharing service at {{BaseUrl}}

## Upload

```bash
curl --upload-file ./file.txt {{BaseUrl}}/file.txt
```

## Upload with options

```bash
curl -H "Max-Downloads: 1" -H "File-Lifetime: 5d" --upload-file ./file.txt {{BaseUrl}}/file.txt
```

## Upload with custom token

```bash
curl --upload-file ./file.txt -H "Token: my-slug" {{BaseUrl}}/file.txt
```

The response URL will be `{{BaseUrl}}/my-slug/file.txt`.

## Upload multiple files as one ZIP

Only files sent together in the same request are combined into one ZIP. Uploading one
file first and another later creates two separate uploads, each with its own download
link. Later uploads are never added to an existing ZIP.

```bash
curl -H "Accept: application/json" -F "file=@a.txt" -F "file=@b.txt" {{BaseUrl}}/archive
```

The server creates one `files.zip`, with one download link and one private administration
link. The response is `201 Created` with `Location` and `Link`. JSON has one entry in
`files`, with `filename`, `url`, `deleteUrl`, `adminUrl`, `sha256` (hex), and `expires`
(UTC date or null). `File-Lifetime`, `Max-Downloads`, and `Token` apply to the whole archive.
Without `Accept: application/json`, the body is the ZIP's download URL.

Files are flattened into the ZIP; duplicate names receive a numeric suffix and empty
files are preserved. Both the total original size and ZIP size must fit the upload limit.
The browser uses this endpoint when multiple files are selected or dropped at once.
Each new selection or drop starts a separate upload. A failed ZIP upload is retried
as a whole. The configured ClamAV prescan checks the completed ZIP.

For independent files instead, POST multipart to `{{BaseUrl}}/`. That endpoint returns
per-file metadata in JSON or one URL per line. A custom `Token` then requires one file,
and empty files reject the request. Both multipart endpoints reject `Encrypt-Password`
and `Content-Digest`; create an archive locally and PUT it for encryption or validation.

## Upload archive

```bash
tar czf - *.txt | curl --upload-file - {{BaseUrl}}/files.tar.gz
```

## Upload using wget

```bash
wget --method PUT --body-file=./file.txt {{BaseUrl}}/file.txt -O - -nv
```

## Upload using PowerShell

```powershell
Invoke-WebRequest -Method PUT -InFile .\file.txt {{BaseUrl}}/file.txt
```

## Upload using HTTPie

```bash
http {{BaseUrl}}/ < ./file.txt
```

## Request Headers

| Header | Description | Example |
|--------|-------------|---------|
| `File-Lifetime` | Expiry duration or date | `-H "File-Lifetime: 7d"` |
| `Max-Downloads` | Download limit | `-H "Max-Downloads: 1"` |
| `Encrypt-Password` | Server-side encrypt with password | `-H "Encrypt-Password: secret"` |
| `Token` | Custom URL slug (min 4 chars, a-z0-9 and hyphens) | `-H "Token: my-slug"` |
| `Content-Digest` | PUT only: validate one SHA-256 digest | `-H "Content-Digest: sha-256=:<base64>:"` |
| `Accept` | Request upload metadata as JSON | `-H "Accept: application/json"` |
| `Authorization` | Authenticate the private admin API | `-H "Authorization: Bearer <admin-token>"` |

Old upload headers (`Expires`, `Max-Days`, `Expected-Checksum` and the old `X-` aliases)
are rejected. Use the headers above. `File-Lifetime` requires a positive duration or a
future date. `Expires` is a cache header, not a file retention option.

## Verify an upload

Send a base64 SHA-256 digest of the uploaded bytes. The server checks it before storage:

```bash
curl --upload-file ./file.txt \
  -H "Accept: application/json" \
  -H "Content-Digest: sha-256=:$(openssl dgst -sha256 -binary ./file.txt | openssl base64 -A):" \
  {{BaseUrl}}/file.txt
```

Malformed, unsupported, or mismatching digests return 400; nothing is stored. Only one
SHA-256 digest, with no parameters, is supported. Successful uploads return 201 Created.
For downloads, `Repr-Digest` contains the whole file's digest, including for range responses:

```bash
curl -sD- -o ./file.txt {{BaseUrl}}/<token>/file.txt
openssl dgst -sha256 -binary ./file.txt | openssl base64 -A
```

Upload responses describe the upload result, so they have no file `Content-Digest` or
`Repr-Digest` header. Read `sha256` from their JSON metadata instead; it is lowercase hex
and covers the bytes received before any server-side encryption.

## Inspect without downloading

`HEAD` returns size, expiry and checksum without transferring the body, and
**without counting as a download** - a `Max-Downloads: 1` link survives it:

```bash
curl -sI {{BaseUrl}}/<token>/file.txt
```

For a server-side encrypted upload the `Repr-Digest` header is omitted here, because
the digest is over the plaintext and `HEAD` cannot decrypt.

## Download

```bash
curl {{BaseUrl}}/<token>/file.txt -o ./file.txt
```

To decrypt a server-side encrypted file:

```bash
curl -H "Decrypt-Password: secret" {{BaseUrl}}/<token>/file.txt -o ./file.txt
```

## Download archive and extract

```bash
curl {{BaseUrl}}/<token>/files.tar.gz | tar xzf -
```

## Bundle download

```bash
curl "{{BaseUrl}}/bundle.zip?files=<token1>/a.txt,<token2>/b.txt" -o bundle.zip
curl "{{BaseUrl}}/bundle.tar.gz?files=<token1>/a.txt,<token2>/b.txt" -o bundle.tar.gz
```

## Client-side GPG encryption

```bash
# Upload
cat ./secret.txt | gpg -ac -o- | curl -X PUT --upload-file "-" {{BaseUrl}}/secret.txt

# Download
curl {{BaseUrl}}/<token>/secret.txt | gpg -o- > ./secret.txt
```

## Client-side OpenSSL encryption

```bash
# Upload
cat ./secret.txt | openssl aes-256-cbc -pbkdf2 -e | curl -X PUT --upload-file "-" {{BaseUrl}}/secret.txt

# Download
curl {{BaseUrl}}/<token>/secret.txt | openssl aes-256-cbc -pbkdf2 -d > ./secret.txt
```

## Backup database, encrypt and transfer

```bash
pg_dump -Fc mydb | gpg -ac -o- | curl -X PUT --upload-file "-" {{BaseUrl}}/db-backup.dump
```

## Scan for malware

```bash
# ClamAV scan
curl -X PUT --upload-file ./file.txt {{BaseUrl}}/file.txt/scan

# VirusTotal scan
curl -X PUT --upload-file ./file.txt {{BaseUrl}}/file.txt/virustotal
```

## Shell function

Add to `.bashrc` or `.zshrc`:

```bash
transfer() {
  if [ $# -eq 0 ]; then
    echo "Usage: transfer <file>" >&2
    return 1
  fi
  file="$1"
  basename=$(basename "$file")
  if [ ! -e "$file" ]; then
    echo "$file: No such file or directory" >&2
    return 1
  fi
  if [ -d "$file" ]; then
    basename="$basename.tar.gz"
    tar czf - -C "$file" . | curl --progress-bar --upload-file "-" "{{BaseUrl}}/$basename" | tee /dev/null
  else
    curl --progress-bar --upload-file "$file" "{{BaseUrl}}/$basename" | tee /dev/null
  fi
}
```

Usage:

```bash
$ transfer hello.txt
{{BaseUrl}}/<token>/hello.txt

$ transfer ./my-directory/
{{BaseUrl}}/<token>/my-directory.tar.gz
```

## Delete

Single-file upload responses include `Link` fields with these application-defined
URI relations (using standard HTTP Link syntax):

- `https://github.com/frankhommers/transfer.cs#file-administration`: private admin page,
  with the admin capability after `#`.
- `https://github.com/frankhommers/transfer.cs#file-deletion`: URL accepting DELETE,
  with a separate deletion capability.

Both are also returned as `adminUrl` and `deleteUrl` in the upload JSON, including for
multi-file uploads. Keep these private; share only each file's `url`.

Use the deletion URL:

```bash
curl -X DELETE {{BaseUrl}}/<token>/file.txt/<deletion-token>
```

## Private administration

Extract the fragment from `adminUrl` and send it as a bearer token:

```bash
curl -H "Authorization: Bearer <admin-token>" {{BaseUrl}}/api/admin/<token>/file.txt
curl -X DELETE -H "Authorization: Bearer <admin-token>" {{BaseUrl}}/api/admin/<token>/file.txt
```

The old `Admin-Token` header is no longer accepted.

## Response Headers

| Header | Description |
|--------|-------------|
| `Location` | Created file URL on single-file uploads (`201 Created`) |
| `Link` | Private admin and deletion URLs on single-file uploads |
| `Sunset` | Expected file expiry on GET/HEAD, as an HTTP date |
| `Repr-Digest` | `sha-256=:<base64>:` of the whole selected representation on downloads/HEAD |
| `X-Remaining-Downloads` | Remaining download count |
| `Cache-Control: no-store` | Upload, file and admin responses must not be cached |

Independent multi-file uploads to `/` have no single-file `Location`, `Link`, or digest
headers; request JSON for per-file metadata. ZIP creation at `/archive` returns one file
and its `Location` and management `Link` headers. `Sunset` is a hint: download limits, deletion or physical
cleanup can make a file unavailable earlier. HEAD and ciphertext GET omit plaintext
digests for encrypted files. Decrypted GET includes `Repr-Digest`.

## Instance Limits

- **Max upload size:** {{MaxUploadSize}}
- **Auto-purge:** {{PurgeDays}}
- **Minimum free disk space:** {{MinFreeDiskSpace}}

The server can return **507 Insufficient Storage** when an upload or temporary-file
operation would use its disk space reserve. This can also happen during an upload,
including a ZIP upload. Incomplete uploads are removed; retry the complete request
after space becomes available. Do not retry continuously or delete other files to make room.

## Source

[GitHub](https://github.com/frankhommers/transfer.cs)
