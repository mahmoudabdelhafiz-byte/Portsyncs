namespace PortSyncs.Api.Infrastructure.Sms;

/// <summary>
/// Sends one-time login codes (T20). Development uses the console stub;
/// a real provider (Twilio, a local provider in the pilot country, ...) plugs in here
/// and is chosen with Sms__Provider.
/// </summary>
public interface ISmsSender
{
    Task SendAsync(string phone, string message, CancellationToken ct = default);
}

/// <summary>Writes the message to the log instead of sending it. For development only.</summary>
public sealed class ConsoleSmsSender(ILogger<ConsoleSmsSender> logger) : ISmsSender
{
    public Task SendAsync(string phone, string message, CancellationToken ct = default)
    {
        logger.LogWarning("[SMS stub] To {Phone}: {Message}", phone, message);
        return Task.CompletedTask;
    }
}
