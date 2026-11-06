namespace Sso.Application.Abstractions;

/// <summary>Time source — faked in tests for TTL/lockout assertions.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
