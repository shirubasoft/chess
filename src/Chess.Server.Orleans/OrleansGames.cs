using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.Json;
using Chess.Contracts;
using Chess.Server.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;

namespace Chess.Server.Orleans;

public sealed class OrleansGameOptions
{
    [Required] public string ClusterId { get; set; } = "chess";
    [Required] public string ServiceId { get; set; } = "chess";
    [Required] public string AdvertisedAddress { get; set; } = "127.0.0.1";
    [Range(1, 65535)] public int SiloPort { get; set; }
    [Range(1, 300)] public int RequestTimeoutSeconds { get; set; } = 30;
    [Range(1, 86400)] public int IdleSeconds { get; set; } = 300;
}

public static class OrleansGames
{
    public static IHostApplicationBuilder AddOrleansGames(this IHostApplicationBuilder builder)
    {
        builder.Services.AddOptions<OrleansGameOptions>().BindConfiguration("Chess:Orleans")
            .ValidateDataAnnotations().Validate(o => IPAddress.TryParse(o.AdvertisedAddress, out _),
                "Chess:Orleans:AdvertisedAddress must be an IP address.").ValidateOnStart();
        builder.Services.AddHostedService<InitializeOrleansStorage>();
        builder.UseOrleans(silo =>
        {
            silo.UseAdoNetClustering(options =>
            {
                options.Invariant = "Npgsql";
                options.ConnectionString = builder.Configuration.GetConnectionString("chess")
                    ?? throw new InvalidOperationException("ConnectionStrings:chess is required.");
            });
            silo.Services.AddOptions<ClusterOptions>().Configure<IOptions<OrleansGameOptions>>((options, game) =>
            {
                options.ClusterId = game.Value.ClusterId;
                options.ServiceId = game.Value.ServiceId;
            });
            silo.Services.AddOptions<EndpointOptions>().Configure<IOptions<OrleansGameOptions>>((options, game) =>
            {
                options.AdvertisedIPAddress = IPAddress.Parse(game.Value.AdvertisedAddress);
                options.SiloPort = game.Value.SiloPort;
                options.GatewayPort = 0;
            });
            silo.Services.AddOptions<SiloMessagingOptions>().Configure<IOptions<OrleansGameOptions>>((options, game) =>
                options.ResponseTimeout = TimeSpan.FromSeconds(game.Value.RequestTimeoutSeconds));
            silo.Services.AddOptions<GrainCollectionOptions>().Configure<IOptions<OrleansGameOptions>>((options, game) =>
                options.ClassSpecificCollectionAge[typeof(GameGrain).FullName!] = TimeSpan.FromSeconds(game.Value.IdleSeconds));
        });
        builder.Services.AddSingleton<IGameBackend, OrleansGameBackend>();
        builder.Services.AddHealthChecks().AddCheck<OrleansReady>("orleans-silo");
        return builder;
    }
}

[Alias("chess.game.v1")]
public interface IGameGrain : IGrainWithGuidKey
{
    [Alias("command")]
    Task<GameReply> CommandAsync(string code, string commandJson);
}

[GenerateSerializer, Alias("chess.game-reply.v1")]
public sealed record GameReply
{
    [Id(0)] public required int Status { get; init; }
    [Id(1)] public required string Payload { get; init; }
}

public sealed class GameGrain(PostgresGameStore store, ILogger<GameGrain> logger) : Grain, IGameGrain
{
    public async Task<GameReply> CommandAsync(string code, string commandJson)
    {
        var gameId = new GameId(this.GetPrimaryKey());
        try
        {
            var command = JsonSerializer.Deserialize<GameCommandRequest>(commandJson, GameJson.Options)!;
            var snapshot = await store.CommandAsync(gameId, code, command);
            return new GameReply { Status = 200, Payload = JsonSerializer.Serialize(snapshot, GameJson.Options) };
        }
        catch (GameFault error)
        {
            return new GameReply { Status = error.Status, Payload = JsonSerializer.Serialize(error.Error, GameJson.Options) };
        }
        catch (Exception error)
        {
            logger.LogError(error, "Game command failed for {GameId}", gameId.Value);
            return new GameReply
            {
                Status = 503, Payload = JsonSerializer.Serialize(new GameError
                {
                    Code = "backend_unavailable", Message = "Retry the command with the same requestId."
                }, GameJson.Options)
            };
        }
    }
}

internal sealed class OrleansGameBackend(IGrainFactory grains) : IGameBackend
{
    public async Task<GameSnapshot> CommandAsync(GameId gameId, string code, GameCommandRequest command, CancellationToken cancellationToken)
    {
        GameReply reply;
        try
        {
            reply = await grains.GetGrain<IGameGrain>(gameId.Value)
                .CommandAsync(code, JsonSerializer.Serialize(command, GameJson.Options)).WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new GameFault(503, "backend_timeout", "The command may have committed. Retry it with the same requestId.");
        }
        catch (SiloUnavailableException)
        {
            throw new GameFault(503, "backend_unavailable", "Retry the command with the same requestId.");
        }
        if (reply.Status == 200) return JsonSerializer.Deserialize<GameSnapshot>(reply.Payload, GameJson.Options)!;
        var error = JsonSerializer.Deserialize<GameError>(reply.Payload, GameJson.Options)!;
        throw new GameFault(reply.Status, error.Code, error.Message, error.CurrentRevision);
    }
}

internal sealed class OrleansReady(ISiloStatusOracle status) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(status.CurrentStatus == SiloStatus.Active
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The Orleans silo has not joined the cluster."));
}
