# Chess web

<!-- impeccable:product-schema 1 -->

## Platform

web

## Stack

The requested all-C# clients share Chess.Client, Chess.Contracts, and the chess domain. This client uses Blazor WebAssembly so a persistent connection is optional. An ASP.NET host proxies the API and supplies an Aspire service boundary.

## Users and purpose

Players create a private game, share an opposing side code, join by code, or find a random opponent. Their opponents can use any of the other clients. A player can close the browser and resume using their saved side code.

## Capabilities and constraints

The server is authoritative. The browser uses the core domain to render positions and preview legal moves. Each side shows its client name. The board supports mouse and touch dragging, click moves and promotion, SAN and UCI entry, game history, draws, and resignation. Polling is the default; SSE and WebSockets are optional update transports. Game codes grant access to one side and should be shared only with the intended player.

## Open decisions

No user brand, typeface, or palette was specified. The board-first responsive layout and quiet club-scorebook visual treatment are implementation choices for this client.
