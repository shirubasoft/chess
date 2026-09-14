using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Chess.Contracts;
using Chess.Server.Application;
using Chess.Server.Akka;
using Chess.Server.Orleans;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Chess.Server;

public sealed class GameServerOptions
{
    public string Backend { get; set; } = "Akka";
    [Range(50, 10000)] public int PollIntervalMilliseconds { get; set; } = 250;
    public string[] AllowedOrigins { get; set; } = [];
}

public static class GameServer
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 32 * 1024);
        builder.Services.AddOptions<GameServerOptions>().BindConfiguration("Chess")
            .ValidateDataAnnotations().Validate(o => o.Backend is "Akka" or "Orleans", "Chess:Backend must be Akka or Orleans.")
            .ValidateOnStart();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            var wire = GameJson.Options;
            options.SerializerOptions.PropertyNamingPolicy = wire.PropertyNamingPolicy;
            options.SerializerOptions.RespectNullableAnnotations = true;
            options.SerializerOptions.UnmappedMemberHandling = wire.UnmappedMemberHandling;
            foreach (var converter in wire.Converters) options.SerializerOptions.Converters.Add(converter);
        });
        builder.Services.AddSingleton(_ => NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("chess")
            ?? throw new InvalidOperationException("ConnectionStrings:chess is required. Start through Aspire or supply PostgreSQL configuration.")));
        builder.Services.AddSingleton<PostgresGameStore>();
        switch (builder.Configuration["Chess:Backend"] ?? "Akka")
        {
            case "Akka": builder.Services.AddAkkaGames(); break;
            case "Orleans": builder.AddOrleansGames(); break;
        }
        builder.Services.AddHostedService<InitializeStorage>();
        builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres");
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            var origins = builder.Configuration.GetSection("Chess:AllowedOrigins").Get<string[]>() ?? [];
            if (origins.Length > 0) policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try { await next(context); }
            catch (GameFault error) when (!context.Response.HasStarted)
            {
                context.Response.StatusCode = error.Status;
                await context.Response.WriteAsJsonAsync(error.Error, GameJson.Options, context.RequestAborted);
            }
            catch (Exception error) when (error is JsonException or BadHttpRequestException && !context.Response.HasStarted)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsJsonAsync(new GameError { Code = "invalid_request", Message = "The request does not match the API contract." }, GameJson.Options, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
            catch (Exception error) when (!context.Response.HasStarted)
            {
                app.Logger.LogError(error, "Game request failed with trace {TraceId}", context.TraceIdentifier);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsJsonAsync(new GameError
                {
                    Code = "server_error", Message = "The server could not complete the request. Retry with the same requestId."
                }, GameJson.Options, context.RequestAborted);
            }
        });
        app.UseCors();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapHealthChecks("/health");
        app.MapGet("/alive", () => Results.Ok(new { status = "alive" }));
        app.MapPost("/api/games", async (CreateGameRequest request, PostgresGameStore store, CancellationToken ct) =>
            Results.Json(await store.CreateAsync(request, ct), GameJson.Options, statusCode: 201));
        app.MapPost("/api/games/join", (JoinGameRequest request, PostgresGameStore store, CancellationToken ct) => store.JoinAsync(request, ct));
        app.MapPost("/api/matchmaking", (MatchmakingRequest request, PostgresGameStore store, CancellationToken ct) => store.MatchmakeAsync(request, ct));
        app.MapGet("/api/games/{id:guid}", (Guid id, HttpContext context, PostgresGameStore store) =>
            store.ReadAsync(new GameId(id), Code(context), context.RequestAborted));
        app.MapPost("/api/games/{id:guid}/commands", (Guid id, GameCommandRequest request, HttpContext context, IGameBackend backend) =>
            backend.CommandAsync(new GameId(id), Code(context), request, context.RequestAborted));
        app.MapGet("/api/games/{id:guid}/wait", GameStreams.WaitAsync);
        app.MapGet("/api/games/{id:guid}/events", GameStreams.SseAsync);
        app.Map("/api/games/{id:guid}/socket", GameStreams.WebSocketAsync);
        return app;
    }

    internal static string Code(HttpContext context) => context.Request.Headers["X-Game-Code"].ToString();
}

internal sealed class InitializeStorage(PostgresGameStore store) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => store.InitializeAsync(cancellationToken);
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class PostgresHealthCheck(NpgsqlDataSource source) : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var command = source.CreateCommand("SELECT 1");
        await command.ExecuteScalarAsync(cancellationToken);
        return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
    }
}
