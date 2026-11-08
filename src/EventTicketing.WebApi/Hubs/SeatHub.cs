using Microsoft.AspNetCore.SignalR;

namespace EventTicketing.WebApi.Hubs;

public class SeatHub : Hub
{
    public async Task JoinSeatMapGroup(string eventId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, eventId);
    }

    public async Task NotifySeatUpdated(string eventId, string seatLabel, string status)
    {
        await Clients.Group(eventId).SendAsync("SeatUpdated", new { seatLabel, status });
    }
}
