# README Audit Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Make `README.md` accurately describe the current API, configuration, migration, purge, proxy deployment, and development workflow.

**Architecture:** Preserve the existing README organization and generic examples. Correct only behavior verified against current source and group related details into concise tables, notes, and operational sequences.

**Tech Stack:** Markdown, ASP.NET Core 10, Docker Compose, Traefik, Nginx, React/Vite, Bun

---

### Task 1: Correct API and Admin Documentation

**Files:**
- Modify: `README.md:34-168`

**Step 1: Document route variants and scan endpoints**

Add the supported PUT aliases, GET/HEAD disposition aliases, and standalone ClamAV/VirusTotal routes without duplicating the primary usage examples.

**Step 2: Correct request-header scope**

State which headers apply to PUT, multipart POST, downloads, and admin API requests. Explicitly document legacy `Max-Days`, decryption headers, and `Admin-Token`.

**Step 3: Correct response-header and checksum scope**

Document that PUT and single-file multipart responses expose checksums/admin links, while multi-file multipart returns only URLs.

**Step 4: Tighten admin security wording**

Describe fragment-to-`sessionStorage` handling and capability authentication without calling the route itself non-discoverable.

**Step 5: Review the section against source**

Check:

```bash
rg 'Map(Put|Post|Get|Head|Delete)|Headers\[' backend/src/TransferCs.Api/Endpoints
```

Expected: every documented route and header has a matching endpoint implementation.

### Task 2: Correct Configuration and Purge Documentation

**Files:**
- Modify: `README.md:22-31`
- Modify: `README.md:179-239`

**Step 1: Enable cleanup in examples**

Add `TransferCs__PurgeIntervalHours=24` to Quick Start and add global `PurgeIntervalHours` to the multi-site example.

**Step 2: Expand the configuration table**

Add the currently omitted operative settings for temp storage, HTTP auth, IP controls, proxy URL generation, and multi-site selection. Clarify application versus Docker defaults.

**Step 3: Describe purge semantics precisely**

State that `PurgeDays` supplies default logical expiry and the physical age threshold, while positive `PurgeIntervalHours` runs cleanup at startup and periodically. Note that physical purge uses payload creation age rather than per-upload expiry.

**Step 4: Clarify related edge behavior**

Document minimum retained download-log entry, PUT-only ClamAV prescan, HTTPS exemptions, normalized host matching, and multi-site upload-limit enforcement.

**Step 5: Compare every setting with code**

Run:

```bash
rg 'public .*\{ get; set; \}' backend/src/TransferCs.Api/Configuration
```

Expected: all common operative settings are represented or explicitly described as advanced .NET configuration.

### Task 3: Replace Migration Notes with a Safe Runbook

**Files:**
- Modify: `README.md:241-262`

**Step 1: Document prerequisites**

Require the new multi-site environment configuration and image before invoking the command, while keeping normal application startup stopped.

**Step 2: Add the ordered migration sequence**

Document `pull`, `stop`, backup, root inspection, explicit migration, result verification, and `up -d` in that order.

**Step 3: State the migration scope exactly**

Warn that every unconfigured root directory is moved and that directories such as `@eaDir`, `tmp`, and unrelated data must first be moved or removed.

**Step 4: Preserve failure and historical-marker guidance**

Keep fail-closed conditions and move `.multisite-migration-v1` into a clearly labeled historical exception.

**Step 5: Verify against implementation**

Run:

```bash
rg 'migrate-legacy-data|Directory.Move|rootEntries|siteDirectories' backend/src/TransferCs.Api entrypoint.sh
```

Expected: documented command invocation and directory selection match the implementation.

### Task 4: Correct Reverse-Proxy and Development Examples

**Files:**
- Modify: `README.md:264-398`

**Step 1: Correct streaming language**

State that downloads stream directly while PUT uploads are staged to temporary storage, and that proxy buffering remains unnecessary.

**Step 2: Make Traefik examples internally consistent**

Add purge interval to Compose, explain the required shared Docker network, remove file-provider buffering, and correct timeout defaults/example values.

**Step 3: Correct Nginx forwarding and buffering**

Use `X-Forwarded-For`, disable response buffering, use HTTP/1.1, and require the Nginx proxy address/CIDR in `TrustedProxies`.

**Step 4: Use Bun for frontend development**

Replace `npm run dev` with `bun run dev` and mention `./start-dev.sh` as the combined workflow.

**Step 5: Verify snippets against repository files**

Run:

```bash
rg 'bun run|ASPNETCORE_URLS|TransferCs__BasePath|ENTRYPOINT' Dockerfile entrypoint.sh start-dev.sh frontend/package.json
```

Expected: documented build and development commands match repository tooling.

### Task 5: Final Documentation Verification

**Files:**
- Verify: `README.md`

**Step 1: Inspect the complete diff**

Run:

```bash
git diff -- README.md
```

Expected: only intentional documentation updates; no production-specific values or secrets.

**Step 2: Check Markdown whitespace**

Run:

```bash
git diff --check
```

Expected: no whitespace errors.

**Step 3: Search for known stale statements**

Run:

```bash
rg 'npm run dev|Every new upload|buffering:|default is 60s' README.md
```

Expected: no obsolete claims remain; any `buffering` occurrence explicitly says to disable it.

**Step 4: Commit the audited README**

```bash
git add README.md docs/plans/2026-08-19-readme-audit.md
git commit -m "docs: align README with current behavior"
```
