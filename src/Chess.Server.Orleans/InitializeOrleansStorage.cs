using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Chess.Server.Orleans;

internal sealed class InitializeOrleansStorage(NpgsqlDataSource source) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var connection = await source.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync("""
            SELECT pg_advisory_xact_lock(714203003);
            CREATE TABLE IF NOT EXISTS chess_orleans_schema (version integer PRIMARY KEY);
            """);
        await using var query = new NpgsqlCommand("SELECT version FROM chess_orleans_schema", connection, transaction);
        var version = await query.ExecuteScalarAsync(cancellationToken);
        if (version is null)
        {
            foreach (var name in new[] { "PostgreSQL-Main.sql", "PostgreSQL-Clustering.sql" })
            {
                await using var stream = typeof(InitializeOrleansStorage).Assembly
                    .GetManifestResourceStream($"Chess.Server.Orleans.Storage.{name}")
                    ?? throw new InvalidOperationException($"Missing Orleans schema resource {name}.");
                using var reader = new StreamReader(stream);
                await ExecuteAsync(await reader.ReadToEndAsync(cancellationToken));
            }
            await ExecuteAsync("INSERT INTO chess_orleans_schema (version) VALUES (1)");
        }
        else if (version is not 1) throw new InvalidOperationException("Unsupported chess Orleans membership schema version.");
        await transaction.CommitAsync(cancellationToken);

        async Task ExecuteAsync(string sql)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
