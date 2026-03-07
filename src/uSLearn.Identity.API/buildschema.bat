rem https://docs.microsoft.com/es-es/ef/core/cli/dotnet

dotnet ef migrations add Initial -c IdentityContext -o  Infrastructure/Migrations 
