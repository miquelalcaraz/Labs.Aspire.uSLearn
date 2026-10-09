---
name: blazor-radzen
description: Guidance for creating or modifying Blazor Razor pages and components in src/uSLearn.Web using the Radzen Blazor component library. Use whenever editing .razor files or building UI in the web frontend.
---

# Blazor + Radzen components

The web frontend (`src/uSLearn.Web`) uses the [Radzen Blazor](https://blazor.radzen.com/) component library.

## Before changing a page or component

Check the Radzen documentation for the components involved **before** writing markup, so usage follows Radzen's recommendations (parameters, events, data binding, theming):

- Use a Radzen MCP / docs tool if one is configured in the session.
- Otherwise fetch the relevant page from https://blazor.radzen.com/docs/ or the component demo on https://blazor.radzen.com/.

Prefer a Radzen component over hand-written HTML/Bootstrap when one exists (`RadzenDataGrid`, `RadzenTemplateForm`, `RadzenTextBox`, `RadzenButton`, `RadzenDialog`, `RadzenNotification`…).

## Conventions

- All code, comments, labels and UI strings in **English** (see `CLAUDE.md`).
- Call the backend through typed HTTP clients that use Aspire service discovery (`https+http://apiservice`), not hard-coded URLs.
- Keep components small; move non-trivial logic into code-behind (`.razor.cs`) or services.
