using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Akka.Actor;
using Akka.Cluster;
using Akka.Cluster.Hosting;
using Akka.Cluster.Hosting.SBR;
using Akka.Cluster.Sharding;
using Akka.Event;
using Akka.Hosting;
using Akka.Remote.Hosting;
using Akka.Serialization;
using Chess.Contracts;
using Chess.Server.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Chess.Server.Akka;

public sealed class AkkaGameOptions
{
    [Required] public string Hostname { get; set; } = "127.0.0.1";
    [Range(0, 65535)] public int Port { get; set; }
    public string[] SeedNodes { get; set; } = [];
    [Range(1, 4096)] public int ShardCount { get; set; } = 128;
    [Range(1, 300)] public int RequestTimeoutSeconds { get; set; } = 30;
    [Range(1, 86400)] public int IdleSeconds { get; set; } = 300;
}

public static class AkkaGames
{
    public static IServiceCollection AddAkkaGames(this IServiceCollection services)
    {
        services.AddOptions<AkkaGameOptions>().BindConfiguration("Chess:Akka").ValidateDataAnnotations().ValidateOnStart();
        services.AddAkka("Chess", (builder, provider) =>
        {
            var options = provider.GetRequiredService<IOptions<AkkaGameOptions>>().Value;
            var store = provider.GetRequiredService<PostgresGameStore>();
            builder.WithRemoting(options.Hostname, options.Port)
                .WithClustering(new ClusterOptions
                {
                    SeedNodes = options.SeedNodes,
                    SplitBrainResolver = SplitBrainResolverOption.Default
                })
                .WithCustomSerializer("chess-game-v1", [typeof(GameCall), typeof(GameReply)], system => new GameMessageSerializer(system))
                .WithShardRegion<GameRegion>("games",
                    entityId => Props.Create(() => new GameActor(new GameId(Guid.Parse(entityId)), store, options.IdleSeconds)),
                    HashCodeMessageExtractor.Create(options.ShardCount, message => message is GameCall call ? call.GameId.ToString("N") : null, message => message),
                    new ShardOptions { StateStoreMode = StateStoreMode.DData, RememberEntities = false })
                .WithActors((system, _) =>
                {
                    if (options.SeedNodes.Length == 0)
                    {
                        var cluster = Cluster.Get(system);
                        cluster.Join(cluster.SelfAddress);
                    }
                });
        });
        services.AddSingleton<IGameBackend, AkkaGameBackend>();
        services.AddHealthChecks().AddCheck<AkkaReady>("akka-cluster");
        return services;
    }
}

internal sealed class GameRegion;

internal sealed record GameCall
{
    public required Guid GameId { get; init; }
    public required string Code { get; init; }
    public required GameCommandRequest Command { get; init; }
}

internal sealed record GameReply
{
    public required int Status { get; init; }
    public required string Payload { get; init; }
}

internal sealed class GameActor : ReceiveActor
{
    public GameActor(GameId gameId, PostgresGameStore store, int idleSeconds)
    {
        Context.SetReceiveTimeout(TimeSpan.FromSeconds(idleSeconds));
        ReceiveAsync<GameCall>(async call =>
        {
            var replyTo = Sender;
            try
            {
                if (call.GameId != gameId.Value) throw GameFault.Invalid("The command belongs to another game.");
                var snapshot = await store.CommandAsync(gameId, call.Code, call.Command);
                replyTo.Tell(new GameReply { Status = 200, Payload = JsonSerializer.Serialize(snapshot, GameJson.Options) });
            }
            catch (GameFault error)
            {
                replyTo.Tell(new GameReply { Status = error.Status, Payload = JsonSerializer.Serialize(error.Error, GameJson.Options) });
            }
            catch (Exception error)
            {
                Context.GetLogger().Error(error, "Game command failed for {0}", gameId.Value);
                replyTo.Tell(new GameReply
                {
                    Status = 503, Payload = JsonSerializer.Serialize(new GameError
                    {
                        Code = "backend_unavailable", Message = "Retry the command with the same requestId."
                    }, GameJson.Options)
                });
            }
        });
        Receive<ReceiveTimeout>(_ => Context.Parent.Tell(new Passivate(PoisonPill.Instance)));
    }
}

internal sealed class AkkaGameBackend(IRequiredActor<GameRegion> region, IOptions<AkkaGameOptions> options) : IGameBackend
{
    public async Task<GameSnapshot> CommandAsync(GameId gameId, string code, GameCommandRequest command, CancellationToken cancellationToken)
    {
        GameReply reply;
        try
        {
            reply = await region.ActorRef.Ask<GameReply>(new GameCall { GameId = gameId.Value, Code = code, Command = command },
                TimeSpan.FromSeconds(options.Value.RequestTimeoutSeconds), cancellationToken);
        }
        catch (AskTimeoutException)
        {
            throw new GameFault(503, "backend_timeout", "The command may have committed. Retry it with the same requestId.");
        }
        if (reply.Status == 200) return JsonSerializer.Deserialize<GameSnapshot>(reply.Payload, GameJson.Options)!;
        var error = JsonSerializer.Deserialize<GameError>(reply.Payload, GameJson.Options)!;
        throw new GameFault(reply.Status, error.Code, error.Message, error.CurrentRevision);
    }
}

internal sealed class GameMessageSerializer(ExtendedActorSystem system) : SerializerWithStringManifest(system)
{
    public override int Identifier => 714203;
    public override string Manifest(object message) => message switch
    {
        GameCall => "game-call-v1", GameReply => "game-reply-v1", _ => throw new NotSupportedException()
    };
    public override byte[] ToBinary(object message) => JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), GameJson.Options);
    public override object FromBinary(byte[] bytes, string manifest) => manifest switch
    {
        "game-call-v1" => JsonSerializer.Deserialize<GameCall>(bytes, GameJson.Options)!,
        "game-reply-v1" => JsonSerializer.Deserialize<GameReply>(bytes, GameJson.Options)!,
        _ => throw new NotSupportedException($"Unsupported Akka game message {manifest}.")
    };
}

internal sealed class AkkaReady(ActorSystem system) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(Cluster.Get(system).SelfMember.Status == MemberStatus.Up
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("The Akka node has not joined the cluster."));
}
