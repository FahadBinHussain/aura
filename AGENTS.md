# aura - project notes

## layout

- `web/` = the Next.js app (this is also the Vercel project's Root Directory)
- `winui/`, `wpf/` = desktop app variants; `.github/workflows/build.yml` builds
  ONLY the WinUI app (dotnet publish + GitHub release `latest` tag) - it does
  not touch the web app.
- `pnpm` is the package manager (`web/pnpm-lock.yaml` is tracked - dependabot
  watches it).

## security: next.js advisory GHSA-2xp9-vwfh-vxw4 (2026-09-06)

- **unauthenticated RCE in the image optimization API when AVIF files are
  optimized** (libheif/sharp), critical 9.5. affected: next `>=10 <15.5.24`
  and `>=16.0.0 <16.3.3`; patched: 15.5.24 / 16.3.3.
- this repo was affected: lockfile pinned `next@16.2.6`. it is RELEVANT here
  because `web/next.config.ts` enables the optimizer (`images.remotePatterns`
  -> picsum.photos), so the `/_next/image` endpoint is live on production.
- fix: bump `"next"` in `web/package.json` to `^16.3.3` (lockfile resolves to
  16.3.8 as of 2026-09-06), commit `5861ed2`. when upgrading next, the build
  regenerates `web/next-env.d.ts` - commit it with the bump.
- dependabot alert clears on the next scan after the lockfile push.

## deploy (vercel)

- project: `aura`, domain `https://aura2027.vercel.app`. owner account: check
  `mainframe\vercel-usage-table.ps1` (repo -> profile mapping) - no hardcoded
  emails here, this repo is PUBLIC.
- **Root Directory is `web`** -> `vercel link` and `vercel deploy --prod` must
  run from the REPO ROOT, never from `web/` (deploying from inside `web/`
  uploads a tree with no `web/` dir and fails with: `The specified Root
  Directory "web" does not exist`).
- helper flow (from repo root):
  `mainframe\vercel-account.ps1 run <owner-email> link --yes --project aura --scope <team-slug>`
  then `... run <owner-email> deploy --prod` - watch for `✓ Ready` + the
  `Aliases https://aura2027.vercel.app` line, then curl the domain to verify.
- the link writes root `.vercel/` + `.env.local` (OIDC token) - both are
  gitignored (root `.gitignore` `.vercel` + `.env*`), never commit them.
