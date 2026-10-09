---
name: stage-docs
description: Write or update the per-stage documentation in docs/ (Stage.XX-N-Name.md). Use when the user asks to document a stage or step, update stage docs, or the README roadmap, or after a stage is reported as finished.
---

# Stage documentation

Each stage of the project is documented in `docs/` so the repository reads as a step-by-step guide.

## Before writing anything

Do **not** create or modify any documentation `.md` file until the user has explicitly confirmed the step/stage is complete. If they haven't, ask first (in the user's language) whether the stage can be closed and documented, and wait for a yes.

## File naming

File names are in English, in Title-Case words separated by hyphens (no spaces):

- Single-file stage: `docs/Stage.XX-Name.md` (e.g. `Stage.02-Domain-Events.md`)
- Stage split into steps: `docs/Stage.XX-N-Name.md` (e.g. `Stage.04-2-Validations.md`)

Fix an existing step by updating its document rather than adding a separate revision file.

## Language

Documentation is written in **English**, even when the conversation with the user is in Spanish. Follow the tone of the existing files in `docs/`.

## Style

Short, precise and explanatory. Every stage doc includes:

1. **Goal** — what the stage achieves and why, in a few lines.
2. **Architectural decisions** — choices made and trade-offs; mention when a pattern comes from [dotnet/eShop](https://github.com/dotnet/eShop) and where we diverge.
3. **Diagrams** — Mermaid (flowchart / sequence) for flows between components or services.
4. **Relevant code** — only the meaningful snippets, with the file path; don't paste whole files.
5. **Step-by-step flow** — numbered walkthrough of the runtime flow.
6. **Practical verification** — how to check it works (run the AppHost, `.http` requests, what to look for in the Aspire dashboard / logs / DB). Verify every request, URL and expected result against the running app before writing it down.
7. **Next steps** — what the next stage builds on.

## After writing

Update `README.md`:
- the stage status in the **Roadmap** table;
- the **Documentation** table with a link to the new file.
