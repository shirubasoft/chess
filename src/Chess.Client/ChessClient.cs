using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Chess.Contracts;
using Chess.Notation;

namespace Chess.Client;

public sealed class ChessClient(HttpClient http, string clientName)
{
    public string ClientName { get; } = clientName;

    public Task<GameAccess> CreateAsync(string? initialFen = null, Guid? requestId = null, CancellationToken cancellationToken = default) =>
        SendAsync<GameAccess>(HttpMethod.Post, "api/games", new CreateGameRequest
        {
            RequestId = requestId ?? Guid.NewGuid(), ClientName = ClientName, InitialFen = initialFen
        }, null, cancellationToken);

    public Task<GameAccess> JoinAsync(string code, CancellationToken cancellationToken = default) =>
        SendAsync<GameAccess>(HttpMethod.Post, "api/games/join", new JoinGameRequest
        {
            Code = code, ClientName = ClientName
        }, null, cancellationToken);

    public Task<GameAccess> MatchmakeAsync(Guid? requestId = null, CancellationToken cancellationToken = default) =>
        SendAsync<GameAccess>(HttpMethod.Post, "api/matchmaking", new MatchmakingRequest
        {
            RequestId = requestId ?? Guid.NewGuid(), ClientName = ClientName
        }, null, cancellationToken);

    public Task<GameSnapshot> GetAsync(GameAccess access, CancellationToken cancellationToken = default) =>
        SendAsync<GameSnapshot>(HttpMethod.Get, $"api/games/{access.GameId}", null, access.Code, cancellationToken);

    public Task<GameSnapshot> CommandAsync(GameAccess access, GameCommandRequest command, CancellationToken cancellationToken = default) =>
        SendAsync<GameSnapshot>(HttpMethod.Post, $"api/games/{access.GameId}/commands", command, access.Code, cancellationToken);

    public Task<GameSnapshot> MoveAsync(GameAccess access, GameSnapshot snapshot, string move, MoveNotation notation = MoveNotation.San,
        Guid? requestId = null, CancellationToken cancellationToken = default) =>
        CommandAsync(access, new GameCommandRequest
        {
            RequestId = requestId ?? Guid.NewGuid(), ExpectedRevision = snapshot.Revision,
            Action = GameAction.Move, Move = move, Notation = notation
        }, cancellationToken);

    public async Task<GameSnapshot?> WaitAsync(GameAccess access, long afterRevision, int timeoutSeconds = 30, CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get,
            $"api/games/{access.GameId}/wait?afterRevision={afterRevision}&timeoutSeconds={timeoutSeconds}", null, access.Code);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        return await ReadAsync<GameSnapshot>(response, cancellationToken);
    }

    public async IAsyncEnumerable<GameSnapshot> WatchSseAsync(GameAccess access, long afterRevision = -1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var request = Request(HttpMethod.Get, $"api/games/{access.GameId}/events?afterRevision={afterRevision}", null, access.Code);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
            if (line.StartsWith("data: ", StringComparison.Ordinal))
                yield return JsonSerializer.Deserialize<GameSnapshot>(line.AsSpan(6), GameJson.Options)
                    ?? throw new JsonException("The server returned an empty snapshot.");
    }

    public async IAsyncEnumerable<GameSnapshot> WatchWebSocketAsync(GameAccess access, long afterRevision = -1,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var endpoint = new UriBuilder(new Uri(http.BaseAddress ?? throw new InvalidOperationException("Set the server address."),
            $"api/games/{access.GameId}/socket"));
        endpoint.Scheme = endpoint.Scheme == "https" ? "wss" : "ws";
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(endpoint.Uri, cancellationToken);
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new WatchRequest { Code = access.Code, AfterRevision = afterRevision }, GameJson.Options),
            WebSocketMessageType.Text, true, cancellationToken);
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open)
        {
            using var message = new MemoryStream();
            ValueWebSocketReceiveResult received;
            do
            {
                received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close) yield break;
                if (message.Length + received.Count > 4 * 1024 * 1024) throw new JsonException("The server snapshot exceeded the size limit.");
                message.Write(buffer, 0, received.Count);
            } while (!received.EndOfMessage);
            message.Position = 0;
            yield return await JsonSerializer.DeserializeAsync<GameSnapshot>(message, GameJson.Options, cancellationToken)
                ?? throw new JsonException("The server returned an empty snapshot.");
        }
    }

    public static Position PositionOf(GameSnapshot snapshot) => Fen.Parse(snapshot.Fen).OrThrow();

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? code, CancellationToken cancellationToken)
    {
        using var request = Request(method, path, body, code);
        using var response = await http.SendAsync(request, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, object? body, string? code)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: GameJson.Options);
        if (code is not null) request.Headers.Add("X-Game-Code", code);
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(GameJson.Options, cancellationToken)
            ?? throw new JsonException("The server returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var error = await response.Content.ReadFromJsonAsync<GameError>(GameJson.Options, cancellationToken);
        throw new ChessServerException(response.StatusCode, error ?? new GameError
        {
            Code = "server_error", Message = $"Server returned {(int)response.StatusCode}."
        });
    }
}

public sealed class ChessServerException(HttpStatusCode statusCode, GameError error) : Exception(error.Message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public GameError Error { get; } = error;
}
