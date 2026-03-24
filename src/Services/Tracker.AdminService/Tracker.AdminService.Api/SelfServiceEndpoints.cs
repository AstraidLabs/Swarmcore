using System.Security.Claims;
using Audit.Application;
using Audit.Domain;
using Identity.SelfService.Application;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using BeeTracker.Contracts.Identity;

namespace Tracker.AdminService.Api;

public static class SelfServiceEndpoints
{
    public static RouteGroupBuilder MapSelfServiceEndpoints(this IEndpointRouteBuilder app)
    {
        var selfService = app.MapGroup("/api/self-service/admin");

        selfService.MapPost("/register", HandleRegisterAsync);
        selfService.MapPost("/activate", HandleActivateAsync);
        selfService.MapPost("/reactivate/request", HandleReactivateRequestAsync);
        selfService.MapPost("/reactivate/confirm", HandleReactivateConfirmAsync);
        selfService.MapPost("/password/forgot", HandleForgotPasswordAsync);
        selfService.MapPost("/password/reset", HandleResetPasswordAsync);
        selfService.MapPost("/password/change", HandleChangePasswordAsync)
            .RequireAuthorization();
        selfService.MapGet("/me", HandleGetProfileAsync)
            .RequireAuthorization();

        return selfService;
    }

    private static async Task<IResult> HandleRegisterAsync(
        HttpContext httpContext,
        [FromBody] RegisterAdminRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "UserName, Email, and Password are required.", null));

        var command = new RegisterAdminCommand(
            request.UserName, request.Email, request.Password, request.ConfirmPassword,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        var result = await sender.Send(command, ct);

        if (!result.Success)
            return Results.BadRequest(new SelfServiceErrorResponse(result.ErrorCode ?? "REGISTRATION_FAILED", "Registration failed.", result.ErrorMessages));

        return Results.Ok(new RegisterAdminResponse(result.UserId ?? string.Empty, true));
    }

    private static async Task<IResult> HandleActivateAsync(
        HttpContext httpContext,
        [FromBody] ActivateAdminRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "Token is required.", null));

        var command = new ActivateAdminCommand(
            request.Token, GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        var result = await sender.Send(command, ct);

        if (!result.Success)
            return Results.BadRequest(new SelfServiceErrorResponse(result.ErrorCode ?? "ACTIVATION_FAILED", "Activation failed.", result.ErrorMessages));

        return Results.Ok(new ActivateAdminResponse(true, "Account activated successfully."));
    }

    private static async Task<IResult> HandleReactivateRequestAsync(
        HttpContext httpContext,
        [FromBody] ReactivateAdminRequestDto request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "Email is required.", null));

        var command = new RequestReactivationCommand(
            request.Email, GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        await sender.Send(command, ct);

        // Always return 200 to avoid leaking account existence
        return Results.Ok(new { message = "If an account exists with that email, a reactivation link has been sent." });
    }

    private static async Task<IResult> HandleReactivateConfirmAsync(
        HttpContext httpContext,
        [FromBody] ReactivateAdminConfirmRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "Token is required.", null));

        var command = new ConfirmReactivationCommand(
            request.Token, GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        var result = await sender.Send(command, ct);

        if (!result.Success)
            return Results.BadRequest(new SelfServiceErrorResponse(result.ErrorCode ?? "REACTIVATION_FAILED", "Reactivation failed.", result.ErrorMessages));

        return Results.Ok(new AccountStateTransitionResponse(true, AdminAccountState.Active, "Account reactivated successfully."));
    }

    private static async Task<IResult> HandleForgotPasswordAsync(
        HttpContext httpContext,
        [FromBody] ForgotPasswordRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "Email is required.", null));

        var command = new ForgotPasswordCommand(
            request.Email, GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        await sender.Send(command, ct);

        // Always return 200 to avoid leaking account existence
        return Results.Ok(new { message = "If an account exists with that email, a password reset link has been sent." });
    }

    private static async Task<IResult> HandleResetPasswordAsync(
        HttpContext httpContext,
        [FromBody] ResetPasswordRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Token) || string.IsNullOrWhiteSpace(request.NewPassword))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "Token and NewPassword are required.", null));

        var command = new ResetPasswordCommand(
            request.Token, request.NewPassword, request.ConfirmNewPassword,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        var result = await sender.Send(command, ct);

        if (!result.Success)
            return Results.BadRequest(new SelfServiceErrorResponse(result.ErrorCode ?? "RESET_FAILED", "Password reset failed.", result.ErrorMessages));

        return Results.Ok(new { message = "Password reset successfully." });
    }

    private static async Task<IResult> HandleChangePasswordAsync(
        HttpContext httpContext,
        [FromBody] ChangePasswordRequest request,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        if (string.IsNullOrWhiteSpace(request.CurrentPassword) || string.IsNullOrWhiteSpace(request.NewPassword))
            return Results.BadRequest(new SelfServiceErrorResponse("VALIDATION_ERROR", "CurrentPassword and NewPassword are required.", null));

        var command = new ChangePasswordCommand(
            userId, request.CurrentPassword, request.NewPassword, request.ConfirmNewPassword,
            GetIpAddress(httpContext), GetUserAgent(httpContext), GetCorrelationId(httpContext));

        var result = await sender.Send(command, ct);

        if (!result.Success)
            return Results.BadRequest(new SelfServiceErrorResponse(result.ErrorCode ?? "CHANGE_FAILED", "Password change failed.", result.ErrorMessages));

        return Results.Ok(new { message = "Password changed successfully." });
    }

    private static async Task<IResult> HandleGetProfileAsync(
        HttpContext httpContext,
        [FromServices] ISender sender,
        CancellationToken ct)
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Results.Unauthorized();

        var profile = await sender.Send(new GetAdminProfileQuery(userId), ct);

        if (profile is null)
            return Results.NotFound();

        return Results.Ok(new AdminProfileResponse(
            profile.UserId, profile.UserName, profile.Email,
            Enum.TryParse<AdminAccountState>(profile.AccountState, true, out var state) ? state : AdminAccountState.PendingActivation,
            profile.Roles, profile.CreatedAtUtc, profile.LastLoginAtUtc));
    }

    private static string? GetIpAddress(HttpContext httpContext)
        => httpContext.Connection.RemoteIpAddress?.ToString();

    private static string? GetUserAgent(HttpContext httpContext)
        => httpContext.Request.Headers.UserAgent.FirstOrDefault();

    private static string GetCorrelationId(HttpContext httpContext)
        => httpContext.TraceIdentifier;
}
