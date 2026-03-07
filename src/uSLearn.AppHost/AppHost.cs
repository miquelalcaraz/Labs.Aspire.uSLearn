

var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var rabbitMq = builder.AddRabbitMQ("eventbus")
    .WithLifetime(ContainerLifetime.Persistent);

var sqlPassword = builder.AddParameter("sqlPassword", value: "***REMOVED***", secret: true);
var sqlserver = builder.AddSqlServer("sqlserver", sqlPassword, 1444)
    .WithImageTag("latest")
    .WithLifetime(ContainerLifetime.Persistent);

//Databases
var accountDb = sqlserver.AddDatabase("accountDb");
var identityDb = sqlserver.AddDatabase("identitydb");

var identity = builder.AddProject<Projects.uSLearn_Identity_API>("identity")
   .WithHttpHealthCheck("/health")
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithReference(rabbitMq).WaitFor(rabbitMq);

var apiService = builder.AddProject<Projects.uSLearn_Accounts_API>("apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(accountDb)
    .WaitFor(accountDb)
    .WithReference(rabbitMq).WaitFor(rabbitMq)
    .WaitFor(identity);

builder.AddProject<Projects.uSLearn_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(apiService)
    .WaitFor(apiService)
    .WaitFor(identity);

builder.Build().Run();
