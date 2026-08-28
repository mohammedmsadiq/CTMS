using CTMS.Api.Endpoints;
using CTMS.Api.Infrastructure;
using CTMS.Application;
using CTMS.Infrastructure;
using CTMS.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApplicationExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<CtmsDbContext>(name: "database", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// TODO: auth — add authentication/authorization here. Expected: JWT bearer.
// Register the scheme(s) above with builder.Services.AddAuthentication(...).AddJwtBearer(...)
// and builder.Services.AddAuthorization(), then call app.UseAuthentication() and
// app.UseAuthorization() at this point in the pipeline, and protect the /api/* endpoints.

app.MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.MapProjectEndpoints();

app.Run();
