# Cove Filesizes

Adds a standard database icon and file sizes to Cove's performer, studio, and video cards. Requires **Cove 1.5.1 or newer**.

- Performer: a line below age (or below the name when age is absent). The portrait becomes 18 pixels shorter to keep the overall card height unchanged.
- Studio: joins the existing bottom row of scene, performer, image, and substudio counts.
- Video: joins the bottom row of performer/tag counts and the organized indicator.

Sizes use decimal KB, MB, GB, and TB, with at most one decimal place: 1234 GB becomes **1.2 TB**. Hover for the exact byte count.

## Install

In **Settings → Extensions → Install from URL**, paste:

```text
https://github.com/jiwenjimiran/cove_filesizes/releases/download/v1.0.0/io.github.jiwenjimiran.filesizes-1.0.0.zip
```

Alternatively download the ZIP from [the latest release](https://github.com/jiwenjimiran/cove_filesizes/releases/latest) and use **Install from ZIP**. Enable the extension and refresh Cove once.

## What is counted

Performer and studio totals sum every recorded file attached to directly attributed videos, images, gallery archives, audios, and texts. Multiple versions of a video are all included. Shared video sources attributed through clips are counted once per performer/studio, even when both the parent and clips have that attribution. Individual video cards show all source files; clips show their parent's files.

Studio totals cover that studio's own attribution, excluding substudios. Images belonging to a gallery contribute when the images themselves carry the attribution. Generated thumbnails, cover blobs, captions, and unrelated files are excluded. Totals reflect Cove's recorded byte sizes and refresh every 30 seconds while the page is active.

The extension makes read-only batched requests (up to 100 IDs per request), has no settings, and removes its added markup when disabled. It uses the same DOM observation approach as [Performer Highlights](https://github.com/jiwenjimiran/cove_performer_highlights), without React internals. Video sizes require `files.read` and `videos.read`; full performer/studio totals additionally require the corresponding entity read permission and read access to images, galleries, audios, and texts. If access is denied, the label is hidden rather than showing an incomplete total.

## Development

Requires .NET 10, Node.js 24, PowerShell, and a checkout of [Cove](https://github.com/yourcove/cove) beside this repository named `cove`. The validated host source commit is `e4f691eee80c57e3a01689661e7f409f559b5249`. Host assemblies are build dependencies and are excluded from the install ZIP.

```powershell
npm ci
npm test
npx playwright install chromium
node tests/layout.mjs
dotnet run --project tests/Backend/Backend.csproj -c Release -p:CoveCiVersion=true
./scripts/package.ps1
```

Packaging also accepts `-CoveSourceRoot` for an alternative checkout path. Browser checks use a fixture matching Cove's card layout; they verify age/no-age placement, unchanged performer heights at three widths, responsive resizing, footer placement, and cleanup. Backend checks execute against SQLite fixtures and verify PostgreSQL translation against the full Cove model.
