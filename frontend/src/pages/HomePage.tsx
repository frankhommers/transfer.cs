import { UploadDropzone } from '@/components/UploadDropzone'
import { CodeBlock } from '@/components/CodeBlock'
import { Separator } from '@/components/ui/separator'

export function HomePage() {
  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-3xl mx-auto px-4 py-16">
        <div className="text-center mb-12">
          <h1 className="text-4xl font-bold tracking-tight mb-2">transfer.sh</h1>
          <p className="text-lg text-muted-foreground">
            Easy and fast file sharing from the command line
          </p>
        </div>

        <UploadDropzone />

        <Separator className="my-12" />

        <div className="space-y-6">
          <h2 className="text-2xl font-semibold tracking-tight">
            Command Line Usage
          </h2>

          <div className="space-y-4">
            <div>
              <p className="text-sm font-medium mb-2 text-muted-foreground">Upload</p>
              <CodeBlock code="curl --upload-file ./hello.txt https://transfer.sh/hello.txt" />
            </div>

            <div>
              <p className="text-sm font-medium mb-2 text-muted-foreground">Encrypt & Upload</p>
              <CodeBlock code="cat /tmp/hello.txt | gpg -ac -o- | curl -X PUT --upload-file &quot;-&quot; https://transfer.sh/hello.txt" />
            </div>

            <div>
              <p className="text-sm font-medium mb-2 text-muted-foreground">Download & Decrypt</p>
              <CodeBlock code="curl https://transfer.sh/xxxx/hello.txt | gpg -o- > /tmp/hello.txt" />
            </div>

            <div>
              <p className="text-sm font-medium mb-2 text-muted-foreground">Set max downloads</p>
              <CodeBlock code='curl --upload-file ./hello.txt -H "Max-Downloads: 1" https://transfer.sh/hello.txt' />
            </div>

            <div>
              <p className="text-sm font-medium mb-2 text-muted-foreground">Set max days</p>
              <CodeBlock code='curl --upload-file ./hello.txt -H "Max-Days: 5" https://transfer.sh/hello.txt' />
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
