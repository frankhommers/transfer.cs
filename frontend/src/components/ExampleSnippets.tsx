import {useState} from 'react'
import {Icon} from '@mdi/react'
import {mdiBookOpenVariant, mdiChevronDown} from '@mdi/js'
import {CodeBlock} from '@/components/CodeBlock'

interface Example {
  title: string
  code: string
}

export function ExampleSnippets({baseUrl}: { baseUrl: string }) {
  const [open, setOpen] = useState(false)

  const examples: Example[] = [
    {
      title: 'Upload using curl',
      code: `curl --upload-file ./hello.txt ${baseUrl}/hello.txt`,
    },
    {
      title: 'Upload with expiry and download limit',
      code: `curl -H "Max-Downloads: 1" -H "Expires: 5d" --upload-file ./hello.txt ${baseUrl}/hello.txt`,
    },
    {
      title: 'Upload with custom token',
      code: `curl --upload-file ./hello.txt -H "X-Token: my-slug" ${baseUrl}/hello.txt`,
    },
    {
      title: 'Upload using wget',
      code: `wget --method PUT --body-file=./file.txt ${baseUrl}/file.txt -O - -nv`,
    },
    {
      title: 'Upload using PowerShell',
      code: `Invoke-WebRequest -Method PUT -InFile .\\file.txt ${baseUrl}/file.txt`,
    },
    {
      title: 'Encrypt with GPG before upload',
      code: `# Upload\ncat ./secret.txt | gpg -ac -o- | curl -X PUT --upload-file "-" ${baseUrl}/secret.txt\n\n# Download and decrypt\ncurl ${baseUrl}/<token>/secret.txt | gpg -o- > ./secret.txt`,
    },
    {
      title: 'Encrypt with OpenSSL',
      code: `# Upload\ncat ./secret.txt | openssl aes-256-cbc -pbkdf2 -e | curl -X PUT --upload-file "-" ${baseUrl}/secret.txt\n\n# Download and decrypt\ncurl ${baseUrl}/<token>/secret.txt | openssl aes-256-cbc -pbkdf2 -d > ./secret.txt`,
    },
    {
      title: 'Server-side encryption',
      code: `# Upload with encryption\ncurl --upload-file ./secret.txt -H "X-Encrypt-Password: mypass" ${baseUrl}/secret.txt\n\n# Download and decrypt\ncurl -H "X-Decrypt-Password: mypass" ${baseUrl}/<token>/secret.txt -o ./secret.txt`,
    },
    {
      title: 'Upload multiple files',
      code: `curl -X POST -F "file=@a.txt" -F "file=@b.txt" ${baseUrl}/`,
    },
    {
      title: 'Archive and upload a directory',
      code: `tar czf - *.txt | curl --upload-file - ${baseUrl}/files.tar.gz\n\n# Download and extract\ncurl ${baseUrl}/<token>/files.tar.gz | tar xzf -`,
    },
    {
      title: 'Backup database, encrypt and transfer',
      code: `mysqldump --all-databases | gzip | gpg -ac -o- | curl -X PUT --upload-file "-" ${baseUrl}/db-backup.sql.gz`,
    },
    {
      title: 'Scan for malware',
      code: `# ClamAV scan\ncurl -X PUT --upload-file ./file.txt ${baseUrl}/file.txt/scan\n\n# VirusTotal scan\ncurl -X PUT --upload-file ./file.txt ${baseUrl}/file.txt/virustotal`,
    },
    {
      title: 'Shell function for .bashrc / .zshrc',
      code: `transfer() {\n  if [ $# -eq 0 ]; then\n    echo "Usage: transfer <file>" >&2\n    return 1\n  fi\n  file="$1"\n  basename=$(basename "$file")\n  if [ -d "$file" ]; then\n    basename="$basename.tar.gz"\n    tar czf - -C "$file" . | curl --progress-bar --upload-file "-" "${baseUrl}/$basename" | tee /dev/null\n  else\n    curl --progress-bar --upload-file "$file" "${baseUrl}/$basename" | tee /dev/null\n  fi\n}`,
    },
  ]

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        className="flex items-center gap-2 w-full text-left group"
      >
        <Icon path={mdiBookOpenVariant} size={0.75} className="text-muted-foreground shrink-0"/>
        <span className="text-sm font-medium text-muted-foreground group-hover:text-foreground transition-colors">More Examples</span>
        <Icon
          path={mdiChevronDown}
          size={0.625}
          className={[
            'text-muted-foreground transition-transform ml-auto',
            open ? 'rotate-180' : '',
          ].join(' ')}
        />
      </button>

      {open && (
        <div className="mt-6 space-y-6">
          {examples.map((ex, i) => (
            <div key={i} className="space-y-2">
              <h3 className="text-sm font-medium text-muted-foreground">{ex.title}</h3>
              <CodeBlock code={ex.code}/>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
