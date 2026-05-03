using ExplainableFraud.Infrastructure;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Explainable Fraud API",
        Version = "v1",
        Description = "Fraud scoring and explainability endpoints for the thesis demo."
    });
});

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p =>
    {
        p.WithOrigins(
                "https://localhost:7078",
                "http://localhost:5246")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Explainable Fraud API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.UseCors();
app.UseAuthorization();
app.MapControllers();

app.Run();
