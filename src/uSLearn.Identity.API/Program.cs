
using uSLearn.Identity.Api;
using uSLearn.Identity.Api.Extensions;
using uSLearn.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();
builder.AddApplicationServices();

// Add services to the container.
builder.Services.AddProblemDetails();


var withApiVersioning = builder.Services.AddApiVersioning();

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

// Register API endpoints (available in all environments)
var identity = app.NewVersionedApi("identity");
identity.MapIdentityApiV1();

// OpenAPI/Swagger UI only in Development
if (app.Environment.IsDevelopment())
{
    app.UseDefaultOpenApi();
}

app.MapDefaultEndpoints();

app.Run();