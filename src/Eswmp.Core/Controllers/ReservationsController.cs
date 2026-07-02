using Eswmp.Core.Data;
using Eswmp.Core.Models;
using Eswmp.Shared.Auth;
using Eswmp.Shared.Events;
using Eswmp.Shared.Middleware;
using MassTransit;
using Microsoft.AspNetCore.Mvc;

namespace Eswmp.Core.Controllers;

public record CreateReservationRequest(
    Guid ResourceId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    int HoldDurationMinutes,
    string ExternalReferenceType,
    string ExternalReferenceId);

[ApiController]
[Route("api/v1/reservations")]
public class ReservationsController(
    CoreDbContext db,
    ITenantContext tenantContext,
    IPublishEndpoint publishEndpoint) : ControllerBase
{
    [HttpPost]
    [RequirePermission(EswmpPermissions.ReservationCreate)]
    public async Task<IActionResult> Create(CreateReservationRequest request)
    {
        var reservation = new Reservation
        {
            TenantId = tenantContext.RequiredTenantId,
            ResourceId = request.ResourceId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Status = ReservationStatus.Held,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(request.HoldDurationMinutes <= 0 ? 15 : request.HoldDurationMinutes),
            ExternalReferenceType = request.ExternalReferenceType,
            ExternalReferenceId = request.ExternalReferenceId,
        };

        db.Reservations.Add(reservation);
        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new SlotReservedEvent(
            reservation.Id, reservation.TenantId, reservation.ResourceId,
            reservation.StartTime, reservation.EndTime,
            reservation.ExternalReferenceType, reservation.ExternalReferenceId,
            Guid.NewGuid()));

        return CreatedAtAction(nameof(GetById), new { id = reservation.Id }, reservation);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(EswmpPermissions.ReservationRead)]
    public async Task<IActionResult> GetById(Guid id)
    {
        var reservation = await db.Reservations.FindAsync(id);
        return reservation is null ? NotFound() : Ok(reservation);
    }

    [HttpPost("{id:guid}/confirm")]
    [RequirePermission(EswmpPermissions.ReservationConfirm)]
    public async Task<IActionResult> Confirm(Guid id)
    {
        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null)
            return NotFound();

        if (reservation.Status != ReservationStatus.Held)
            return Conflict(new { error = $"Reservation is {reservation.Status}, cannot confirm." });

        reservation.Status = ReservationStatus.Confirmed;

        var appointment = new Appointment
        {
            TenantId = reservation.TenantId,
            ReservationId = reservation.Id,
            ResourceId = reservation.ResourceId,
            StartTime = reservation.StartTime,
            EndTime = reservation.EndTime,
        };
        db.Appointments.Add(appointment);

        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new ReservationConfirmedEvent(
            reservation.Id, reservation.TenantId, reservation.ResourceId, appointment.Id,
            reservation.ExternalReferenceType, reservation.ExternalReferenceId,
            Guid.NewGuid()));

        return Ok(reservation);
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(EswmpPermissions.ReservationCancel)]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var reservation = await db.Reservations.FindAsync(id);
        if (reservation is null)
            return NotFound();

        reservation.Status = ReservationStatus.Cancelled;
        await db.SaveChangesAsync();

        await publishEndpoint.Publish(new ReservationCancelledEvent(
            reservation.Id, reservation.TenantId, "Cancelled by caller", Guid.NewGuid()));

        return Ok(reservation);
    }
}
