# Documentation site (DocFX)

This folder is the **source** of the Nucs.JsonSettings documentation website. It is built with
[DocFX](https://dotnet.github.io/docfx/) and published to GitHub Pages at
<https://nucs.github.io/JsonSettings> by [`.github/workflows/docs.yml`](../../.github/workflows/docs.yml).

## Layout

| Path | What it is |
|------|------------|
| `docfx.json` | The DocFX build/metadata configuration. |
| `index.md` | The site homepage (authored as HTML, styled by the custom template). |
| `toc.yml` | Top navigation bar (Docs / API / Source). |
| `docs/` | Hand-written conceptual articles + their `toc.yml` sidebar. |
| `api/index.md`, `api/overwrites/` | API landing page and namespace summary overwrites (hand-written). |
| `api/*.yml` | **Generated** API metadata — git-ignored, produced from the assemblies on each build. |
| `filterConfig.yml` | API filter (hides vendored/compiler-generated types from the reference). |
| `templates/jsonsettings/` | Custom DocFX template: `public/main.css` (theme + homepage) and `public/main.js` (nav icons). |
| `images/` | Logo/favicon and any images used by the docs. |
| `scripts/generate-llms-txt.sh` | Generates `llms.txt`, `llms-full.txt` and `robots.txt` for AI crawlers. |

The built site is written to `../website` (i.e. `docs/website/`), which is git-ignored.

## Build and preview locally

Requires the .NET 8 SDK (DocFX compiles the `net8.0` target of the two shipped projects to read their
XML docs).

```sh
# one-time: install the DocFX global tool
dotnet tool install -g docfx

# from this folder:
docfx docfx.json --serve          # build, then serve at http://localhost:8080
# or build only:
docfx docfx.json

# regenerate the AI-friendly files (optional; CI does this automatically):
./scripts/generate-llms-txt.sh ../website
```

## Editing the docs

- **Conceptual pages** live in `docs/*.md`. Add a new page by creating the `.md` file and adding an
  entry to `docs/toc.yml`.
- **API reference** is generated from the source XML doc comments — improve it by improving the
  `///` comments in `src/`. To add prose to a namespace, edit/add a file under `api/overwrites/`.
- Keep code samples real: every sample here is drawn from the library's tests and examples.

## Deployment

`docs.yml` builds the site on every push and pull request (so a broken doc fails the PR), and deploys
to GitHub Pages only on pushes to `master`/`main` and manual runs.

> **One-time repository setting:** Settings → Pages → *Build and deployment* → **Source: GitHub
> Actions**. Without this the `deploy` job has nowhere to publish.
