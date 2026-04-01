# {{Title}}

File sharing service at {{BaseUrl}}

## Upload

```bash
curl --upload-file ./file.txt {{BaseUrl}}/file.txt
```

## Upload with options

```bash
curl -H "Max-Downloads: 1" -H "Expires: 5d" --upload-file ./file.txt {{BaseUrl}}/file.txt
```

## Upload with custom token

```bash
curl --upload-file ./file.txt -H "X-Token: my-slug" {{BaseUrl}}/file.txt
```

The response URL will be `{{BaseUrl}}/my-slug/file.txt`.

## Upload multiple files

```bash
curl -X POST -F "file=@a.txt" -F "file=@b.txt" {{BaseUrl}}/
```

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
| `Expires` | Expiry duration or date | `-H "Expires: 7d"` |
| `Max-Downloads` | Download limit | `-H "Max-Downloads: 1"` |
| `X-Encrypt-Password` | Server-side encrypt with password | `-H "X-Encrypt-Password: secret"` |
| `X-Token` | Custom URL slug (min 4 chars, a-z0-9 and hyphens) | `-H "X-Token: my-slug"` |

## Download

```bash
curl {{BaseUrl}}/<token>/file.txt -o ./file.txt
```

To decrypt a server-side encrypted file:

```bash
curl -H "X-Decrypt-Password: secret" {{BaseUrl}}/<token>/file.txt -o ./file.txt
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
mysqldump --all-databases | gzip | gpg -ac -o- | curl -X PUT --upload-file "-" {{BaseUrl}}/db-backup.sql.gz
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

The upload response includes an `X-Url-Delete` header with the delete URL:

```bash
curl -X DELETE {{BaseUrl}}/<token>/file.txt/<deletion-token>
```

## Response Headers

| Header | Description |
|--------|-------------|
| `X-Url-Delete` | URL to delete the uploaded file |
| `Expires` | Expiry date of the upload |
| `X-Remaining-Downloads` | Remaining download count |
| `X-Remaining-Days` | Remaining days until expiry |

## Instance Limits

- **Max upload size:** {{MaxUploadSize}}
- **Auto-purge:** {{PurgeDays}}

## Source

[GitHub](https://github.com/frankhommers/transfer.cs)
