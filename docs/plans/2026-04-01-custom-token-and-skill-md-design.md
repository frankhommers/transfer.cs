# Custom Token & SKILL.md Design

## Feature A: Custom Token bij Upload

**Doel:** Gebruiker kan zelf een token/slug opgeven via een header i.p.v. random generatie.

### Backend
- Header: `X-Token`
- Validatie: min 4 chars, alleen `a-z`, `0-9`, `-`
- 409 Conflict als token al bestaat in storage
- Toegepast in `HandlePut` en `HandlePost`
- Alles verder (metadata, deletion token, response headers) werkt identiek

### Frontend (CommandComposer)
- Nieuwe toggle "Custom token" met text input
- Voegt `-H "X-Token: slug"` toe aan upload-commando
- Download-commando gebruikt ingevulde token i.p.v. `<token>`

---

## Feature B: Dynamische SKILL.md Endpoint

**Doel:** `GET /skill.md` retourneert instance-specifieke markdown die een AI-agent kan gebruiken.

### Backend
- Endpoint: `GET /skill.md`
- Content-Type: `text/markdown`
- Dynamisch gegenereerd met:
  - Base URL (via `UrlHelper.ResolveUrl`)
  - Alle beschikbare headers (Expires, Max-Downloads, X-Encrypt-Password, X-Decrypt-Password, X-Token)
  - Instance-limieten uit config (PurgeDays, MaxUploadSizeKb)
  - Curl voorbeelden voor upload, download, delete
