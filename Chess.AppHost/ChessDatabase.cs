using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

internal static class ChessDatabase
{
    public static IResourceBuilder<IResourceWithConnectionString> AddChessDatabase(this IDistributedApplicationBuilder builder)
    {
        if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("chess")))
            return builder.AddConnectionString("chess");

        var postgres = builder.AddPostgres("postgres");
        if (!builder.Configuration.GetValue<bool>("Chess:Ephemeral"))
            postgres.WithDataVolume().WithLifetime(ContainerLifetime.Persistent);
        return postgres.AddDatabase("chess");
    }
}
