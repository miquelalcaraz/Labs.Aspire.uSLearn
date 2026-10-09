# uSLearn

Microservices reference app on .NET 10 + .NET Aspire, based on [dotnet/eShop](https://github.com/dotnet/eShop). See `README.md` for architecture and the stage roadmap.

## Rules

- All code — including comments, log messages, exception messages, labels and strings — is written in **English**, even when the conversation with the user is in Spanish. Documentation in `docs/` is in Spanish.
- Stage documentation: use the `stage-docs` skill, and never create or modify docs without the user's explicit confirmation that the stage is complete.
- Blazor UI work: use the `blazor-radzen` skill.

## Commands

- Run everything: `dotnet run --project src/uSLearn.AppHost` (requires Docker)
- Build: `dotnet build uSLearn.slnx`
- Clean bin/obj: `cleanup.cmd` (or `sln-tools/cleanup.ps1 -WhatIf` to preview)
- Pack building blocks: `sln-tools/publish.ps1`
