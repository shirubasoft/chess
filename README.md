# Chess

A C# chess core, durable game server, and crossplay clients for Android, web,
Windows, Linux, and the terminal. The server supports untimed standard chess
through either Akka cluster sharding or Orleans grains. Both backends use the
same PostgreSQL game history and the same core rules.

## Run the server and web client

Install the .NET SDK selected by `global.json`, the .NET 10 runtime used by the
AppHost, Aspire CLI 13.5.3, and a working Docker or Podman runtime. From this
directory:

```sh
aspire start --apphost Chess.AppHost/Chess.AppHost.csproj -- --Parameters:backend=Akka
aspire wait web --apphost Chess.AppHost/Chess.AppHost.csproj
aspire describe --apphost Chess.AppHost/Chess.AppHost.csproj
```

Open the reported `web` URL. Other clients connect to the reported `server` URL.
Use `--Parameters:backend=Orleans` to choose Orleans. See the
[server guide](docs/server.md) for backend settings, PostgreSQL configuration,
the HTTP API, SSE, WebSockets, and switching backends.

Create a private game and share its opponent code. Keep your own side's code to
resume from any client. Random matchmaking pairs players without exchanging
codes. Both players see the opponent's client name, and a game remains available
when either player disconnects.

## Clients

| Client | Technology | Build and usage |
| --- | --- | --- |
| Android | .NET for Android native views | [Android guide](clients/Chess.Android/README.md) |
| Web | Blazor WebAssembly and ASP.NET Core proxy | [Web guide](clients/Chess.Web/README.md) |
| Windows | WPF native controls | [Windows guide](clients/Chess.Windows/README.md) |
| Linux | GTK4 through Gir.Core | [Linux guide](clients/Chess.Linux/README.md) |
| `dotnet chess` | Terminal.Gui v2 and JSON commands | [CLI guide](clients/Chess.Cli/README.md) |

The CLI can address a saved game or an explicit match, submit SAN/UCI moves,
and wait for the next opponent move. The native solution is
`clients/Chess.Native.slnx`; Android requires its SDK workload and Windows UI
execution requires Windows.

## Validate

```sh
dotnet test --configuration Release
bash scripts/test-server.sh Akka
bash scripts/test-server.sh Orleans
bash scripts/test-backend-switch.sh
bash scripts/test-frontends.sh Akka
bash scripts/test-frontends.sh Orleans
```

The frontend runner starts a disposable Aspire deployment and requires every
test to execute without skips. It runs actual CLI processes and Chromium UI,
plus each native client's shared session code. All ten frontend pairings run in
both color assignments. Separate Android, WPF, and GTK UI jobs exercise the real
platform controls against both backends. The [crossplay test guide](tests/Chess.Crossplay.Tests/README.md)
defines those boundaries, and the [native CI guide](scripts/native/README.md)
describes target-platform execution.

The core's [external validation guide](docs/validation.md) covers independent
rule and notation checks. See [adjudication scope](docs/position-outcomes.md),
[match history](docs/matches.md), and [notation](docs/notation.md) for the core's
contracts and supported outcomes.
