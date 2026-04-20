// NOTE: This URL is hardcoded for development – we'll fix this with Aspire!
// The API must be running on https://localhost:7100 before starting the web app.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient<WeatherApiClient>(client =>
    client.BaseAddress = new("https://localhost:7100"));

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<WeatherWeb.Components.App>()
    .AddInteractiveServerRenderMode();

app.Run();
