# Chess web

The Blazor WebAssembly client runs C# in the browser and shares the chess domain through `Chess.Client`. Its host serves the browser assets and proxies `/api` requests to the game server with YARP, including SSE and WebSockets. Game access is saved in browser local storage, so closing the tab does not abandon the game.

If a create or matchmaking response is lost, retrying that action after a reload recovers the same game and side. The previous saved game stays available until the new access has been saved.

Run through the repository's Aspire AppHost. For a standalone host against an already running server:

```sh
Chess__ServerUrl=http://localhost:5080 dotnet run --project clients/Chess.Web/Host --urls http://localhost:5090
```

Open `http://localhost:5090`. Create a private game and send the opponent's join code to another player, or find a random opponent. A side code can also resume the same side from a different client. The interface shows each side's current client name.

Select a piece and a highlighted destination, or enter SAN or UCI in the move field. Pawn promotion opens a piece chooser. Game access and update settings are below the move history. Polling works without a persistent connection. SSE and WebSocket modes reconnect and refresh the current snapshot after transport failures.

The Aspire resource name for the referenced API is `server`. The host accepts `services:server:https:0`, `services:server:http:0`, or an explicit `Chess:ServerUrl`. Browser requests use the web host's origin. The host fails startup if its server URL is absent or invalid.

```sh
dotnet build clients/Chess.Web/Host
dotnet test --project tests/Chess.Web.Tests
dotnet publish clients/Chess.Web/Host -c Release -o artifacts/web
```

Run the Chromium integration tests against a running web host and server:

```sh
dotnet build tests/Chess.Web.Tests
pwsh tests/Chess.Web.Tests/bin/Debug/net11.0/playwright.ps1 install chromium
CHESS_WEB_URL=http://localhost:5090 CHESS_SERVER_URL=http://localhost:5080 dotnet test --project tests/Chess.Web.Tests
```

The `Browser` category exercises real browser moves, a reference opponent using the shared client, saved access after reload, recovery from committed requests with lost responses, underpromotion, draw agreement, resignation, and each update transport. Set these addresses in each backend's CI job; absent addresses explicitly skip browser tests. Desktop and phone screenshots go to `artifacts/chess-web` or `CHESS_SCREENSHOT_DIRECTORY` when specified. The crossplay suite combines real browser and CLI execution with the native apps' shared `GameSession`. Native process and control checks run separately on each target platform.

Trimming is disabled because the shared domain's union serializer uses reflection. The self-hosted Newsreader font is under the SIL Open Font License in `wwwroot/fonts/OFL.txt`.

Browser automation uses `data-testid` attributes. The board exposes `data-fen`, `data-game-id`, and `data-revision`; its buttons are `square-e2`, `square-e4`, and so on. The lobby exposes `create-game`, `join-code`, `join-game`, and `find-opponent`. The move controls are `move-input` and `play-move`. Side access is available through `opponent-code` and the private `your-code` field inside game details. Promotion controls are `promote-q`, `promote-r`, `promote-b`, and `promote-n`.

The host uses ASP.NET Core's [WebAssembly hosting](https://learn.microsoft.com/en-us/aspnet/core/blazor/host-and-deploy/webassembly) and YARP's [in-memory configuration provider](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/yarp/config-providers). It forwards traffic to the authoritative game server; chess commands use the same shared client as the other platforms.
