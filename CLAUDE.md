# uSLearn

Microservices reference app on .NET 10 + .NET Aspire, based on [dotnet/eShop](https://github.com/dotnet/eShop). See `README.md` for architecture and the stage roadmap.

## Rules

- Everything in the repository is written in **English** — code (including comments, log messages, exception messages, labels and strings), `README.md`, the documentation in `docs/` and file names — even when the conversation with the user is in Spanish.
- Stage documentation: use the `stage-docs` skill, and never create or modify docs without the user's explicit confirmation that the stage is complete.
- Blazor UI work: use the `blazor-radzen` skill.

## Commands

- Run everything: `dotnet run --project src/uSLearn.AppHost` (requires Docker)
- Build: `dotnet build uSLearn.slnx`
- Clean bin/obj: `cleanup.cmd` (`cleanup.cmd -n` to preview); it runs `git clean -Xd -e "!*.user" -- src`, so it removes only git-ignored files under `src`
- Add an EF Core migration: `src/uSLearn.Accounts.API/buildschema.bat <Name>` or `src/uSLearn.Identity.API/buildschema.bat <Name>`
