import {useState} from 'react'
import {Icon} from '@mdi/react'
import {
  mdiClockOutline, mdiDownload, mdiLock, mdiShieldLock,
  mdiTagText, mdiFile, mdiFileMultiple, mdiArchive, mdiPlus, mdiClose, mdiFolderZip, mdiProgressHelper, mdiFolder, mdiConsoleLine,
} from '@mdi/js'
import {CodeBlock} from '@/components/CodeBlock'
import {Input} from '@/components/ui/input'

type Mode = 'single' | 'multiple' | 'archive' | 'cli'

interface HeaderOption {
  key: string
  label: string
  icon: string
  header: string
  placeholder: string
  type: 'text' | 'number'
}

const headerOptions: HeaderOption[] = [
  {key: 'expires', label: 'Expires', icon: mdiClockOutline, header: 'Expires', placeholder: '7d', type: 'text'},
  {key: 'maxDownloads', label: 'Max downloads', icon: mdiDownload, header: 'Max-Downloads', placeholder: '1', type: 'number'},
  {key: 'serverEncrypt', label: 'Server encrypt', icon: mdiLock, header: 'Encrypt-Password', placeholder: 'password', type: 'text'},
  {key: 'customToken', label: 'Custom token', icon: mdiTagText, header: 'Token', placeholder: 'my-slug', type: 'text'},
]

const modes: { key: Mode; label: string; icon: string }[] = [
  {key: 'single', label: 'Single file', icon: mdiFile},
  {key: 'multiple', label: 'Multiple files', icon: mdiFileMultiple},
  {key: 'archive', label: 'Archive (tar)', icon: mdiArchive},
  {key: 'cli', label: 'transfer CLI', icon: mdiConsoleLine},
]

function buildHeaderFlags(headerOpts: HeaderOption[], active: Record<string, boolean>, values: Record<string, string>): string {
  return headerOpts
    .filter((o) => active[o.key] && (values[o.key] || o.placeholder))
    .map((o) => `-H "${o.header}: ${values[o.key] || o.placeholder}"`)
    .join(' ')
}

export function CommandComposer({baseUrl}: { baseUrl: string }) {
  const [mode, setMode] = useState<Mode>('single')

  // Single file state
  const [filename, setFilename] = useState('hello.txt')

  // Multiple files state
  const [files, setFiles] = useState(['hello.txt', 'world.txt'])

  // Archive state
  const [archiveSource, setArchiveSource] = useState<'glob' | 'directory'>('glob')
  const [globPattern, setGlobPattern] = useState('*.txt')
  const [dirPath, setDirPath] = useState('./my-directory')
  const [archiveName, setArchiveName] = useState('files')
  const [gzip, setGzip] = useState(true)

  // Shared option state
  const [active, setActive] = useState<Record<string, boolean>>({})
  const [values, setValues] = useState<Record<string, string>>({})
  const [clientGpg, setClientGpg] = useState(false)
  const [showProgress, setShowProgress] = useState(false)

  const toggle = (key: string) => {
    setActive((prev) => ({...prev, [key]: !prev[key]}))
  }

  const setValue = (key: string, value: string) => {
    setValues((prev) => ({...prev, [key]: value}))
  }

  const addFile = () => setFiles((prev) => [...prev, ''])
  const removeFile = (i: number) => setFiles((prev) => prev.filter((_, idx) => idx !== i))
  const updateFile = (i: number, v: string) => setFiles((prev) => prev.map((f, idx) => idx === i ? v : f))

  const headerFlags = buildHeaderFlags(headerOptions, active, values)
  const tokenSlug = active['customToken'] && values['customToken'] ? values['customToken'] : '<token>'
  const serverDecryptHeader = active['serverEncrypt']
    ? ` -H "Decrypt-Password: ${values['serverEncrypt'] || 'password'}"`
    : ''

  // --- Command generation per mode ---
  let uploadCmd: string
  let downloadCmd: string

  if (mode === 'single') {
    const file = filename || 'hello.txt'
    if (clientGpg) {
      const curlParts = [
        'curl -X PUT --upload-file "-"',
        headerFlags,
        `${baseUrl}/${file}`,
      ].filter(Boolean).join(' ')
      uploadCmd = `cat ./${file} | gpg -ac -o- | ${curlParts}`
    } else {
      uploadCmd = [
        'curl --upload-file',
        `./${file}`,
        headerFlags,
        `${baseUrl}/${file}`,
      ].filter(Boolean).join(' ')
    }
    if (clientGpg) {
      downloadCmd = `curl${serverDecryptHeader} ${baseUrl}/${tokenSlug}/${file} | gpg -o- > ./${file}`
    } else {
      downloadCmd = `curl${serverDecryptHeader} ${baseUrl}/${tokenSlug}/${file} -o ./${file}`
    }
  } else if (mode === 'multiple') {
    const fileList = files.filter(Boolean)
    if (fileList.length === 0) {
      uploadCmd = '# Add at least one file'
      downloadCmd = '# Add at least one file'
    } else {
      const formFields = fileList.map((f) => `-F "file=@${f}"`).join(' ')
      uploadCmd = [
        'curl -X POST',
        formFields,
        headerFlags,
        `${baseUrl}/`,
      ].filter(Boolean).join(' ')
      const bundleFiles = fileList.map((f) => `${tokenSlug}/${f}`).join(',')
      downloadCmd = `curl "${baseUrl}/bundle.zip?files=${bundleFiles}" -o bundle.zip`
    }
  } else if (mode === 'archive') {
    const name = archiveName || 'files'
    const ext = gzip ? 'tar.gz' : 'tar'
    const tarFlag = gzip ? 'czf' : 'cf'
    const untarFlag = gzip ? 'xzf' : 'xf'
    const tarFile = `${name}.${ext}`
    const tarSource = archiveSource === 'directory'
      ? `-C ${dirPath || './my-directory'} .`
      : (globPattern || '*.txt')

    if (clientGpg) {
      uploadCmd = `tar ${tarFlag} - ${tarSource} | gpg -ac -o- | curl -X PUT --upload-file "-" ${headerFlags ? headerFlags + ' ' : ''}${baseUrl}/${tarFile}`
    } else {
      uploadCmd = `tar ${tarFlag} - ${tarSource} | curl --upload-file - ${headerFlags ? headerFlags + ' ' : ''}${baseUrl}/${tarFile}`
    }
    if (clientGpg) {
      downloadCmd = `curl${serverDecryptHeader} ${baseUrl}/${tokenSlug}/${tarFile} | gpg -o- | tar ${untarFlag} -`
    } else {
      downloadCmd = `curl${serverDecryptHeader} ${baseUrl}/${tokenSlug}/${tarFile} | tar ${untarFlag} -`
    }
  } else {
    // cli mode
    const file = filename || 'hello.txt'
    const cliFlags: string[] = []
    if (active['expires'] && (values['expires'] || 'default')) cliFlags.push(`-e ${values['expires'] || '7d'}`)
    if (active['maxDownloads'] && (values['maxDownloads'] || 'default')) cliFlags.push(`-d ${values['maxDownloads'] || '1'}`)
    if (active['customToken'] && values['customToken']) cliFlags.push(`-t ${values['customToken']}`)
    if (active['serverEncrypt'] && (values['serverEncrypt'] || 'default')) cliFlags.push(`-p ${values['serverEncrypt'] || 'password'}`)
    const flags = cliFlags.length > 0 ? ' ' + cliFlags.join(' ') : ''
    uploadCmd = `transfer ${file}${flags}`
    downloadCmd = `curl ${baseUrl}/${tokenSlug}/${file} -o ./${file}`
  }

  // Inject --progress-bar into curl commands
  if (showProgress) {
    uploadCmd = uploadCmd.replace(/curl /g, 'curl --progress-bar ')
    downloadCmd = downloadCmd.replace(/curl /g, 'curl --progress-bar ')
  }

  return (
    <div className="space-y-6">
      {/* Mode selector */}
      <div className="flex gap-2">
        {modes.map((m) => (
          <button
            key={m.key}
            type="button"
            onClick={() => {
              setMode(m.key)
              if (m.key === 'multiple' || m.key === 'cli') {
                setClientGpg(false)
              }
              if (m.key === 'multiple') {
                setActive((prev) => ({...prev, customToken: false}))
              }
            }}
            className={[
              'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
              mode === m.key
                ? 'bg-primary text-primary-foreground border-primary'
                : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
            ].join(' ')}
          >
            <Icon path={m.icon} size={0.625}/>
            {m.label}
          </button>
        ))}
      </div>

      {/* Install hint for CLI mode */}
      {mode === 'cli' && (
        <div>
          <label className="text-sm font-medium text-muted-foreground mb-2 block">Install</label>
          <CodeBlock code={`curl -fsSL ${baseUrl}/install.sh | bash`}/>
        </div>
      )}

      {/* Mode-specific inputs */}
      {(mode === 'single' || mode === 'cli') && (
        <div>
          <label className="text-sm font-medium text-muted-foreground mb-2 block">
            {mode === 'cli' ? 'File or directory' : 'Filename'}
          </label>
          <Input
            value={filename}
            onChange={(e) => setFilename(e.target.value)}
            placeholder={mode === 'cli' ? 'hello.txt or ./my-directory/' : 'hello.txt'}
            className="font-mono"
          />
        </div>
      )}

      {mode === 'multiple' && (
        <div className="space-y-2">
          <label className="text-sm font-medium text-muted-foreground block">Files</label>
          {files.map((f, i) => (
            <div key={i} className="flex items-center gap-2">
              <Input
                value={f}
                onChange={(e) => updateFile(i, e.target.value)}
                placeholder="filename.txt"
                className="font-mono"
              />
              {files.length > 1 && (
                <button
                  type="button"
                  onClick={() => removeFile(i)}
                  className="text-muted-foreground hover:text-foreground transition-colors shrink-0"
                >
                  <Icon path={mdiClose} size={0.625}/>
                </button>
              )}
            </div>
          ))}
          <button
            type="button"
            onClick={addFile}
            className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            <Icon path={mdiPlus} size={0.625}/>
            Add file
          </button>
        </div>
      )}

      {mode === 'archive' && (
        <div className="space-y-3">
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => setArchiveSource('glob')}
              className={[
                'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
                archiveSource === 'glob'
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
              ].join(' ')}
            >
              <Icon path={mdiFile} size={0.625}/>
              Glob pattern
            </button>
            <button
              type="button"
              onClick={() => setArchiveSource('directory')}
              className={[
                'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
                archiveSource === 'directory'
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
              ].join(' ')}
            >
              <Icon path={mdiFolder} size={0.625}/>
              Directory
            </button>
          </div>
          {archiveSource === 'glob' ? (
            <div>
              <label className="text-sm font-medium text-muted-foreground mb-2 block">Glob pattern</label>
              <Input
                value={globPattern}
                onChange={(e) => setGlobPattern(e.target.value)}
                placeholder="*.txt"
                className="font-mono"
              />
            </div>
          ) : (
            <div>
              <label className="text-sm font-medium text-muted-foreground mb-2 block">Directory path</label>
              <Input
                value={dirPath}
                onChange={(e) => setDirPath(e.target.value)}
                placeholder="./my-directory"
                className="font-mono"
              />
            </div>
          )}
          <div>
            <label className="text-sm font-medium text-muted-foreground mb-2 block">Archive name</label>
            <Input
              value={archiveName}
              onChange={(e) => setArchiveName(e.target.value)}
              placeholder="files"
              className="font-mono"
            />
          </div>
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={() => setGzip((prev) => !prev)}
              className={[
                'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
                gzip
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
              ].join(' ')}
            >
              <Icon path={mdiFolderZip} size={0.625}/>
              Gzip compression
            </button>
          </div>
        </div>
      )}

      {/* Option toggles */}
      <div className="space-y-3">
        <label className="text-sm font-medium text-muted-foreground block">Options</label>
        <div className="flex flex-wrap gap-2">
          {headerOptions.map((o) => {
            const disabled = mode === 'multiple' && o.key === 'customToken'
            return (
              <button
                key={o.key}
                type="button"
                disabled={disabled}
                onClick={() => toggle(o.key)}
                title={disabled ? 'Custom token is not compatible with multipart upload' : undefined}
                className={[
                  'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
                  disabled
                    ? 'bg-muted text-muted-foreground/40 border-border cursor-not-allowed'
                    : active[o.key]
                      ? 'bg-primary text-primary-foreground border-primary'
                      : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
                ].join(' ')}
              >
                <Icon path={o.icon} size={0.625}/>
                {o.label}
              </button>
            )
          })}
          {mode !== 'cli' && (
            <button
              type="button"
              disabled={mode === 'multiple'}
              onClick={() => setClientGpg((prev) => !prev)}
              title={mode === 'multiple' ? 'GPG encryption is not compatible with multipart upload' : undefined}
              className={[
                'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
                mode === 'multiple'
                  ? 'bg-muted text-muted-foreground/40 border-border cursor-not-allowed'
                  : clientGpg
                    ? 'bg-primary text-primary-foreground border-primary'
                    : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
              ].join(' ')}
            >
              <Icon path={mdiShieldLock} size={0.625}/>
              Client encrypt (GPG)
            </button>
          )}
          {mode !== 'cli' && (
            <button
              type="button"
              onClick={() => setShowProgress((prev) => !prev)}
              className={[
                'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md text-sm font-medium transition-colors border',
                showProgress
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'bg-muted text-muted-foreground border-border hover:border-primary/30',
              ].join(' ')}
            >
              <Icon path={mdiProgressHelper} size={0.625}/>
              Show progress
            </button>
          )}
        </div>

        {headerOptions.filter((o) => active[o.key]).map((o) => (
          <div key={o.key} className="flex items-center gap-3">
            <span className="text-sm font-medium text-muted-foreground w-32 shrink-0">{o.label}</span>
            <Input
              type={o.type}
              value={values[o.key] || ''}
              onChange={(e) => setValue(o.key, e.target.value)}
              placeholder={o.placeholder}
              className="font-mono"
            />
          </div>
        ))}
      </div>

      {/* Output */}
      <div className="space-y-3">
        <label className="text-sm font-medium text-muted-foreground block">Upload</label>
        <CodeBlock code={uploadCmd}/>
        <label className="text-sm font-medium text-muted-foreground block">Download</label>
        <CodeBlock code={downloadCmd}/>
      </div>
    </div>
  )
}
