using ExplainableFraud.Web.Components;
using ExplainableFraud.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var apiBase = builder.Configuration["ApiBaseUrl"]?.Trim();
if (string.IsNullOrEmpty(apiBase))
    throw new InvalidOperationException("Configure ApiBaseUrl in appsettings (e.g. https://localhost:7088/).");

builder.Services.AddScoped(_ => new HttpClient
{
    BaseAddress = new Uri(apiBase.EndsWith('/') ? apiBase : apiBase + "/")
});
builder.Services.AddScoped<IFraudScoringApi, FraudScoringApi>();
builder.Services.AddScoped<ITrainingApi, TrainingApi>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
