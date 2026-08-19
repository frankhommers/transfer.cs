# README Audit Design

## Goal

Bring the README in line with the current application, container image, multi-site migration flow, purge scheduling, and Bun-based development workflow.

## Approach

Keep the existing README structure and examples. Audit each documented command, header, configuration option, deployment instruction, and operational behavior against the current source. Change only verifiably stale or incomplete documentation.

## Required Updates

- Make purge examples functional by configuring both retention and interval.
- Explain that purge runs at startup and then after each configured interval.
- Expand the multi-site migration section into a safe upgrade sequence: pull, stop, back up, inspect the volume root, migrate, verify, and start.
- Warn that non-token root directories such as Synology metadata and temporary directories must not be migrated as tokens.
- Keep the obsolete migration marker as a clearly separated historical exception.
- Remove the contradiction between the Traefik no-buffering guidance and file-provider example.
- Use Bun for frontend development commands.
- Correct any additional discrepancies found during the full audit.

## Constraints

- Keep examples generic; do not include production domains, paths, credentials, or tokens.
- Do not change application behavior.
- Prefer a minimal diff over restructuring the entire document.

## Verification

- Compare documented settings with configuration classes and endpoint code.
- Compare migration instructions with `MigrationCommand`, `SiteDataMigration`, and `entrypoint.sh`.
- Compare package commands with repository manifests and project instructions.
- Validate shell/YAML snippets where practical and run `git diff --check`.
