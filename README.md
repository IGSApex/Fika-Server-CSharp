# IGS Gaming Community — Fika Server

Community fork of [Project Fika's C# server module](https://github.com/project-fika/Fika-Server-CSharp). It provides multiplayer server-side behavior for the IGS SPT environment, including custom squad management.

## Squad system

The server is authoritative for **pre-raid squads**. Clients do not assign their own squad IDs or directly edit membership.

`FikaServer/Services/SquadService.cs` maintains squads, membership and pending invitations. A squad has a generated identifier, leader and member set. The service supports creating squads, inviting other players, accepting or declining invitations, leaving squads, leader-only management, and roster lookup. The client UI currently presents a **five-player** squad capacity. Squad state is transient/in-memory; do not assume it survives a server restart.

Main integration points:

- `FikaServer/Services/SquadService.cs` — authoritative state and rules.
- `FikaServer/Routers/Static/SquadStaticRouter.cs` — squad HTTP actions.
- `FikaServer/Models/Fika/Routes/Squad/` — request/response contracts.
- `FikaServer/Models/Fika/WebSocket/Notifications/SquadNotification.cs` — notification contracts.
- [Fika client plugin](https://github.com/IGSApex/Fika-Plugin) — UI, invitations and notification handling.

The server also includes `FikaServer/Services/ClientModHashesService.cs`, which is separate from the SPT server's launcher-facing client-mod manifest and file downloads.

## Architecture and flow

1. A player connects using the compatible Fika client plugin.
2. A player creates a squad or invites another online player.
3. The server checks the sender, target, membership and invitation state.
4. Accepted invitations update server-owned membership.
5. Notifications and roster responses update the client UI.

Do not treat client-side squad labels as proof of membership; server validation must remain authoritative.

## Build and deployment

This fork depends on the matching SPT/Fika server environment and its referenced assemblies. Check the target framework in the project files and install that .NET SDK; the upstream README listed .NET 9, while other IGS components use .NET 10.

```powershell
git clone https://github.com/IGSApex/Fika-Server-CSharp.git
cd Fika-Server-CSharp
dotnet restore
dotnet build -c Release
```

Deploy the compiled server module in the location required by your SPT/Fika server installation, and restart the server. Test squad creation, invitations, disconnects, leader behavior and full squads with multiple clients before production use.

## Related projects

[IGS Fika client](https://github.com/IGSApex/Fika-Plugin) · [IGS SPT server](https://github.com/IGSApex/server-csharp) · [IGS launcher](https://github.com/IGSApex/launcher)

Project Fika and its contributors retain credit for the upstream code. See the repository license and upstream documentation for terms.
