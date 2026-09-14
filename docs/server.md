# Game server

Start PostgreSQL and the server with Aspire 13.5.3:

```sh
aspire start --apphost Chess.AppHost/Chess.AppHost.csproj
aspire wait server --apphost Chess.AppHost/Chess.AppHost.csproj
aspire describe --apphost Chess.AppHost/Chess.AppHost.csproj
```

Use the server URL reported by Aspire. The AppHost gives PostgreSQL a persistent
volume and injects its connection string into the server. `Chess:Backend` selects
the command executor. The AppHost passes its `backend` parameter to this setting.
For a separately hosted server, set `ConnectionStrings__chess` and `Chess__Backend`.

Select `Akka` or `Orleans` with `--Parameters:backend=Orleans` after the Aspire
command's `--` separator. Akka is the default. Akka.Hosting manages the actor system, and cluster sharding routes each
game UUID to one actor. `ReceiveAsync` suspends that actor's mailbox while its
PostgreSQL transaction runs. Idle entities passivate and reactivate on demand.
Durable game data remains in the common PostgreSQL format, so changing executors
does not require a game migration.

`Chess:Akka` binds the remoting hostname/port, seed nodes, shard count, request
timeout, and idle timeout. An empty seed list forms a local single-node cluster
on an automatically assigned port. Configure reachable seed addresses and stable
ports for multiple nodes. Every node in the cluster must use the same shard count.
The readiness endpoint requires the Akka member to reach `Up`. Remoting uses
versioned game message serializers. An HTTP timeout can occur after a commit;
retry the same request ID to recover the committed response.

Orleans uses one UUID-keyed, non-reentrant grain per game. Grain turns call the
same transactional store and idle activations are collected. Its generated
grain protocol carries versioned request and reply envelopes. ADO.NET clustering
uses PostgreSQL membership tables, initialized before the silo starts from the
Orleans 10.3.1 scripts shipped in `Chess.Server.Orleans/Storage`. Membership setup
runs once under a transaction and advisory lock, separately from game data.

`Chess:Orleans` binds `ClusterId`, `ServiceId`, `AdvertisedAddress`, `SiloPort`,
`RequestTimeoutSeconds`, and `IdleSeconds`. Aspire allocates the silo TCP port.
The default advertised address is loopback; use a reachable IP and port for each
node in a multi-host cluster. Nodes share the same cluster ID, service ID, and
database. HTTP hosts call grains through their in-process clients, so an external
Orleans gateway is disabled. Readiness requires an active silo. Choose the same
backend on all servers in a deployment. To switch under Aspire, stop `server`
with `aspire resource server stop` before stopping the AppHost, then restart
with the other backend. This lets the silo leave PostgreSQL membership before
the orchestrator exits. Game codes, history, and retry receipts remain valid.
The AppHost's `orleans-cluster-id` parameter defaults to `chess`; tests use their
own cluster ID so failed earlier test runs cannot delay cluster membership.

The server hosts untimed standard chess. Match decisions use the core's
[documented adjudication scope](position-outcomes.md). `Chess.Notation` parses
incoming SAN/UCI moves and optional FEN setups.

## Joining and resuming

`POST /api/games` accepts `requestId`, `clientName`, and optional `initialFen`.
It creates White's seat and returns `GameAccess`, including White's `code` and
an `opponentCode` to share with Black. Each code grants control of one side.
Keep your own code private. A code works after disconnecting or switching clients.

`POST /api/games/join` accepts `code` and `clientName`. It returns the matching
game and side, updates that seat's client name, and includes the current snapshot.
All clients see both seat names. Joining an occupied seat with its code resumes
that seat; two devices using the same code share control of the same side.

`POST /api/matchmaking` accepts `requestId` and `clientName`. It joins a waiting
game or creates a waiting seat. It returns only the caller's code. Refresh or
subscribe to learn when an opponent joins. Pairing is atomic across server
processes and does not depend on either player staying connected.

## Commands and snapshots

Supply your code in the `X-Game-Code` header for game endpoints.

| Endpoint | Behavior |
| --- | --- |
| `GET /api/games/{id}` | Current snapshot, including FEN, legal UCI moves, move history, seats, result and revision |
| `POST /api/games/{id}/commands` | Apply a command for the authenticated side |
| `GET /api/games/{id}/wait?afterRevision=4&timeoutSeconds=30` | Wait for a newer snapshot; return 204 on timeout |
| `GET /api/games/{id}/events?afterRevision=4` | SSE snapshots after the supplied revision |
| `GET /api/games/{id}/socket` | WebSocket snapshot subscription |

Command example:

```json
{
  "requestId": "6cc35b5a-03c1-42e8-a2ad-55cec4aefef2",
  "expectedRevision": 1,
  "action": "move",
  "move": "e4",
  "notation": "san"
}
```

Actions are `move`, `resign`, `offerDraw`, `acceptDraw`, `declineDraw`,
`claimThreefold`, and `claimFiftyMoves`. Draw claims may include an intended
`move` and its `notation`. The server derives the side from the code.

Each logical command has one nonempty UUID request ID. Retry the exact request
with that ID when a response is lost. A committed retry returns its original
snapshot even if the game has advanced. Reusing the ID for a different command
returns 409. A fresh command with an old expected revision also returns 409;
refresh the snapshot before deciding what to send next.

Errors contain `code`, `message`, and optional `currentRevision`. Invalid
credentials return 401, malformed input returns 400, concurrency conflicts
return 409, and rejected chess actions return 422. HTTP requests are limited to
32 KiB. Client names accept at most 64 printable characters.

## Event transports

SSE uses `event: snapshot`, `id: <revision>`, and one JSON `data` line. The
`Last-Event-ID` header overrides `afterRevision` when reconnecting. Committed
snapshots are replayed in revision order. Comment heartbeats keep idle streams
active.

For WebSockets, send this JSON message within ten seconds of upgrading:

```json
{"code":"your-side-code","afterRevision":4}
```

The server then sends the same snapshots as standalone JSON text messages.
Authentication stays out of URLs. Submit commands through the HTTP command
endpoint while watching either transport. Closing the subscription leaves the
game intact. Polling and waiting work without a persistent connection.

`Chess:PollIntervalMilliseconds` controls how often subscriptions read committed
updates. PostgreSQL owns the history, so reconnects and subscriptions served by
another process see the same updates. `Chess:AllowedOrigins` permits explicitly
listed cross-origin HTTP clients; the hosted web client uses a same-origin proxy.

## Storage and validation

PostgreSQL stores versioned game documents, core match events, snapshots, and
request receipts. A row lock serializes game mutations. The document, snapshot,
and command receipt commit in one transaction. Matchmaking uses a database lock
to pair seats across processes. Core event revision and public game revision
have different meanings: the public revision also advances when seat names change.

Game codes use 128 random bits. Seat lookups store SHA-256 hashes. Private
creation and matchmaking receipts retain the returned access data for request
retries, so database access and backups must protect those credentials too.

Run the real PostgreSQL/HTTP/SSE/WebSocket suite with:

```sh
bash scripts/test-server.sh Akka
bash scripts/test-server.sh Orleans
bash scripts/test-backend-switch.sh
```

The script starts an isolated Aspire application and stops it after the tests.
The switch check plays the same game across Akka, Orleans, Akka, and Orleans again. It
verifies both side codes, snapshots, further moves, and retries of commands
committed by the previous backend.
To test an already running application, set `CHESS_TEST_SERVER` and run
`dotnet test --project tests/Chess.Server.Tests/Chess.Server.Tests.csproj`.
The shared C# `Chess.Client` project implements the API and reuses the core for
position interpretation.
