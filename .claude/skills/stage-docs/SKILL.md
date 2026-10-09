---
name: stage-docs
description: Write or update the per-stage documentation in docs/ (Stage.XX-N-Name.md). Use when the user asks to document a stage or step, update stage docs, or the README roadmap, or after a stage is reported as finished.
---

# Stage documentation

Each stage of the project is documented in `docs/` so the repository reads as a step-by-step guide.

## Before writing anything

Do **not** create or modify any documentation `.md` file until the user has explicitly confirmed the step/stage is complete. If they haven't, ask first ("¿Doy por cerrado el Stage.XX-N y genero la documentación?") and wait for a yes.

## File naming

- Single-file stage: `docs/Stage.XX-name.md` (e.g. `Stage.02-domain events.md`)
- Stage split into steps: `docs/Stage.XX-N-Name.md` (e.g. `Stage.04-2-Validations.md`)
- A revision of an existing step: `docs/Stage.XX-Nrev-Name.md`

Look at the existing files in `docs/` and follow their language (Spanish) and tone.

## Style

Short, precise and explanatory. Every stage doc includes:

1. **Objetivo** — what the stage achieves and why, in a few lines.
2. **Decisiones arquitectónicas** — choices made and trade-offs; mention when a pattern comes from [dotnet/eShop](https://github.com/dotnet/eShop) and where we diverge.
3. **Diagramas** — Mermaid (flowchart / sequence) for flows between components or services.
4. **Código relevante** — only the meaningful snippets, with the file path; don't paste whole files.
5. **Flujo paso a paso** — numbered walkthrough of the runtime flow.
6. **Verificación práctica** — how to check it works (run the AppHost, `.http` requests, what to look for in the Aspire dashboard / logs / DB).
7. **Próximos pasos** — what the next stage builds on.

## After writing

Update `README.md`:
- the stage status in the **Roadmap por etapas** table;
- the **Documentación** table with a link to the new file (URL-encode spaces as `%20`).
