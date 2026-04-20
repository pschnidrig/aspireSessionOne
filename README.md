# Lab 1 – Adding Aspire to an Existing App

> **Session 1 | ~30 minutes**  
> Starting point: A plain Weather API + Blazor frontend — **no Aspire yet**.  
> Goal: Orchestrate the app with Aspire, explore the dashboard, and add Redis caching.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10)
- Container runtime running: [Docker Desktop](https://www.docker.com/products/docker-desktop/) or [Podman Desktop](https://podman-desktop.io/)
- Aspire CLI 13.x — [aspire.dev/get-started/install-cli](https://aspire.dev/get-started/install-cli/)
- IDE: VS 2022 (17.10+), VS Code + C# Dev Kit + Aspire extension, or Rider (2024.1+)

---

## Starter Solution Structure

```
aspire-lab1-starter/
├── WeatherApi/          ← ASP.NET Core Minimal API — GET /weatherforecast
├── WeatherWeb/          ← Blazor Web App — calls WeatherApi via a HARDCODED URL
└── aspire-lab1-starter.slnx
```

Clone the repo:
```bash
git clone https://github.com/pschnidrig/aspireSessionOne.git
cd aspire-lab1-starter
```

Look at `WeatherWeb/Program.cs` — notice the problem:
```csharp
// ⚠️ Hardcoded URL — breaks when ports change or when deploying
builder.Services.AddHttpClient<WeatherApiClient>(client =>
    client.BaseAddress = new("https://localhost:7100"));
```

Run both manually to feel the pain:
```bash
# Terminal 1
cd WeatherApi && dotnet run
# Terminal 2
cd WeatherWeb && dotnet run
```
Two terminals, manual port management, no observability. **Aspire solves this.**

---

## Step 1 – Add Aspire to the Solution

From the solution root, run:
```bash
aspire init
```

This single command does all the heavy lifting:
- Creates the **AppHost** project (`WeatherApp.AppHost/`) and adds it to the solution
- Creates the **ServiceDefaults** project (`WeatherApp.ServiceDefaults/`) and adds it to the solution
- Adds a `ServiceDefaults` reference to **both** `WeatherApi` and `WeatherWeb`
- Injects `builder.AddServiceDefaults()` and `app.MapDefaultEndpoints()` into each `Program.cs`

> The generated entry file is **`AppHost.cs`** — this is where you define the architecture.

---

## Step 2 – Orchestrate Services in AppHost.cs

Open `WeatherApp.AppHost/AppHost.cs` and replace the content with:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddProject<Projects.WeatherApi>("weatherapi")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.WeatherWeb>("weatherweb")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(api)
    .WaitFor(api);

builder.Build().Run();
```

What each call does:
- `AddProject` — registers the project as an Aspire resource
- `WithHttpHealthCheck("/health")` — polls the health endpoint to determine readiness
- `WithReference(api)` — injects the API URL and sets up service discovery
- `WaitFor(api)` — web app will not start until the API's health check passes
- `WithExternalHttpEndpoints()` — exposes endpoints externally (required for `aspire deploy`)

---

## Step 3 – Replace the Hardcoded URL

In `WeatherWeb/Program.cs`, swap the hardcoded URL for Aspire service discovery:

```csharp
// Remove:
builder.Services.AddHttpClient<WeatherApiClient>(client =>
    client.BaseAddress = new("https://localhost:7100"));

// Add (name must match AppHost.cs registration):
builder.Services.AddHttpClient<WeatherApiClient>(client =>
    client.BaseAddress = new("https+http://weatherapi"));
```

The `https+http://` scheme tells Aspire to try HTTPS first, fall back to HTTP.

---

## Step 4 – Run with Aspire

From the solution root (or the AppHost folder):
```bash
aspire run
```

You will see output like:
```
🔍  Finding apphosts...
    AppHost: WeatherApp.AppHost/WeatherApp.AppHost.csproj
  Dashboard: https://localhost:17068/login?t=ea559845d54cea66b837dc0ff33c3bd3
      Press CTRL+C to stop.
```

**Click the dashboard URL** and enter the login token. Then explore:
- 📋 **Resources** — both services running, state, URLs, start time
- 🪵 **Console** — structured logs per service (click a service to filter)
- 🔍 **Traces** — open the web app, load the weather page, then check the trace here

✅ One command started everything.

---

## Step 5 – Add Redis Caching

### 5a. Add Redis to AppHost.cs

```csharp
var cache = builder.AddRedis("cache");

var api = builder.AddProject<Projects.WeatherApi>("weatherapi")
    .WithReference(cache)
    .WithHttpHealthCheck("/health");
```

### 5b. Add the Redis integration package to WeatherApi
```bash
cd WeatherApi
dotnet add package Aspire.StackExchange.Redis.OutputCaching
```

### 5c. Register in WeatherApi/Program.cs

After `builder.AddServiceDefaults();`:
```csharp
builder.AddRedisOutputCache("cache");
```

After `var app = builder.Build();`:
```csharp
app.UseOutputCache();
```

### 5d. Apply caching to the endpoint
```csharp
app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast(
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        )).ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.CacheOutput(p => p.Expire(TimeSpan.FromSeconds(10)));
```

---

## Step 6 – Observe Caching in the Dashboard

1. Restart: `aspire run`
2. Open the web app and refresh the weather page several times
3. Dashboard → **Traces** — look for spans tagged with `cache`
4. Dashboard → **Resources** — you now see a `cache` container (Redis)

---

## ✅ You Are Done!

You have:
- Added an Aspire `AppHost` and `ServiceDefaults` to an existing solution with `aspire init`
- Orchestrated two services with a single `aspire run`
- Eliminated hardcoded URLs with service discovery
- Added Redis caching with zero manual connection string config

---

## ⭐ Bonus Tasks

### Bonus 1 – Verify Health Endpoints
```bash
# Find the API port from the dashboard Resources tab
curl https://localhost:<api-port>/health
curl https://localhost:<api-port>/alive
```

### Bonus 2 – Persistent Redis
```csharp
var cache = builder.AddRedis("cache").WithDataVolume();
```
Restart — cached data survives restarts now.

### Bonus 3 – Deploy with Docker Compose

```bash
# 1. Add the Docker Compose deployment package (interactive)
aspire add docker
# Select Aspire.Hosting.Docker from the list

# 2. In AppHost.cs, add before builder.Build().Run():
#    builder.AddDockerComposeEnvironment("env");

# 3. Deploy — builds images, generates docker-compose.yaml, starts services
aspire deploy
```

After deploying, inspect `WeatherApp.AppHost/aspire-output/`:
- `docker-compose.yaml` — full compose config, generated by Aspire (no Dockerfile needed)
- `.env.Production` — image names and port assignments

### Bonus 4 – Explore Metrics

Dashboard → **Metrics** → look at request duration histograms and memory usage per service.

---

## Resources

- [Aspire – Add Aspire to an existing app](https://aspire.dev/get-started/add-aspire-existing-app/)
- [Aspire quickstart – Build your first app](https://aspire.dev/get-started/first-app/?aspire-lang=csharp)
- [Aspire quickstart – Deploy your first app](https://aspire.dev/get-started/deploy-first-app/?aspire-lang=csharp)
- [Aspire – Service Defaults](https://aspire.dev/get-started/csharp-service-defaults/)
- [Aspire – App Host](https://aspire.dev/get-started/app-host/)
- [Aspire on GitHub](https://github.com/microsoft/aspire)
