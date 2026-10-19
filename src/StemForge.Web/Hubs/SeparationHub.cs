using Microsoft.AspNetCore.SignalR;

namespace StemForge.Web.Hubs;

public sealed class SeparationHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}
