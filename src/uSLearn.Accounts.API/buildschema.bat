rem https://docs.microsoft.com/es-es/ef/core/cli/dotnet

rem rmdirXX /S /Q "Data/Migrations"
rem dotnet ef migrations add Initial -c QltSystemDBContext -o  Persistence/Migrations --startup-project ../Account.Api/ 

rem dotnet ef migrations add --startup-project uSLearn.Account.ApiService --context AccountContext Initial


rem dotnet ef migrations add Initial -c AccountContext -o  Infrastructure/Migrations
dotnet ef migrations add Add_Account_EventLog -c AccountContext -o  Infrastructure/Migrations
