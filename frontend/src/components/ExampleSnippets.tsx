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
      title: 'Upload',
      code: `curl --upload-file ./hello.txt ${baseUrl}/hello.txt`,
    },
    {
      title: 'Upload with expiry and download limit',
      code: `curl -H "Max-Downloads: 1" -H "Expires: 5d" --upload-file ./hello.txt ${baseUrl}/hello.txt`,
    },
    {
      title: 'Upload with custom token',
      code: `curl --upload-file ./hello.txt -H "Token: my-slug" ${baseUrl}/hello.txt`,
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
      title: 'Encrypt with GPG',
      code: `cat ./secret.txt | gpg -ac -o- | curl -X PUT --upload-file "-" ${baseUrl}/secret.txt`,
    },
    {
      title: 'Decrypt GPG download',
      code: `curl ${baseUrl}/<token>/secret.txt | gpg -o- > ./secret.txt`,
    },
    {
      title: 'Encrypt with OpenSSL',
      code: `cat ./secret.txt | openssl aes-256-cbc -pbkdf2 -e | curl -X PUT --upload-file "-" ${baseUrl}/secret.txt`,
    },
    {
      title: 'Decrypt OpenSSL download',
      code: `curl ${baseUrl}/<token>/secret.txt | openssl aes-256-cbc -pbkdf2 -d > ./secret.txt`,
    },
    {
      title: 'Server-side encrypt upload',
      code: `curl --upload-file ./secret.txt -H "Encrypt-Password: mypass" ${baseUrl}/secret.txt`,
    },
    {
      title: 'Server-side decrypt download',
      code: `curl -H "Decrypt-Password: mypass" ${baseUrl}/<token>/secret.txt -o ./secret.txt`,
    },
    {
      title: 'Upload multiple files',
      code: `curl -X POST -F "file=@a.txt" -F "file=@b.txt" ${baseUrl}/`,
    },
    {
      title: 'Archive files and upload',
      code: `tar czf - *.txt | curl --upload-file - ${baseUrl}/files.tar.gz`,
    },
    {
      title: 'Archive directory (preserve paths)',
      code: `tar czf - -C ./my-directory . | curl --upload-file - ${baseUrl}/my-directory.tar.gz`,
    },
    {
      title: 'Download and extract archive',
      code: `curl ${baseUrl}/<token>/files.tar.gz | tar xzf -`,
    },
    {
      title: 'Backup database, encrypt and transfer',
      code: `pg_dump -Fc mydb | gpg -ac -o- | curl -X PUT --upload-file "-" ${baseUrl}/db-backup.dump`,
    },
    {
      title: 'ClamAV scan',
      code: `curl -X PUT --upload-file ./file.txt ${baseUrl}/file.txt/scan`,
    },
    {
      title: 'VirusTotal scan',
      code: `curl -X PUT --upload-file ./file.txt ${baseUrl}/file.txt/virustotal`,
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
