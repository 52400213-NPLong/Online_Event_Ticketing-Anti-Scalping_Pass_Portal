using System;
using System.Collections.Generic;
using System.Linq;
using EventTicketing.Infrastructure.Data;
using EventTicketing.WebApi.Controllers;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace EventTicketing.Tests.UnitTests;

public class EventSeatMapTests
{
    [Fact]
    public void SeedSampleData_PopulatesEventsSeatsAndSampleTicket()
    {
        using var db = new ApplicationDbContext();
        db.SeedSampleData();

        Assert.Equal(4, db.Events.Count());
        Assert.Equal(128, db.Seats.Count()); // 4 sự kiện x 32 ghế
        Assert.Equal(1, db.Tickets.Count());
    }

    [Fact]
    public void GetSeats_ValidEventId_Returns32SeatsForSeatSelection()
    {
        using var db = new ApplicationDbContext();
        db.SeedSampleData();

        var controller = new EventsController(db);
        var eventId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var response = controller.GetSeats(eventId);
        var okResult = Assert.IsType<OkObjectResult>(response);
        var seats = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);

        Assert.Equal(32, seats.Count());
    }

    [Fact]
    public void SearchEvents_KeywordJazz_ReturnsMatchingEvent()
    {
        using var db = new ApplicationDbContext();
        db.SeedSampleData();

        var controller = new EventsController(db);
        var response = controller.SearchEvents("Jazz", null);
        var okResult = Assert.IsType<OkObjectResult>(response);
        var events = Assert.IsAssignableFrom<IEnumerable<object>>(okResult.Value);

        Assert.Single(events);
    }
}
