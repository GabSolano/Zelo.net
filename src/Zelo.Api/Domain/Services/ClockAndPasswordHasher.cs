using System.Security.Cryptography;
using System.Text;

namespace Zelo.Api.Domain.Services;

public interface IClock
{
    DateTime UtcNow { get; }
    void SetSimulatedNow(DateTime? simulatedUtcNow);
}

public class SystemOrSimulatedClock : IClock
{
    private DateTime? _simulatedUtcNow;

    public DateTime UtcNow => _simulatedUtcNow ?? DateTime.UtcNow;

    public void SetSimulatedNow(DateTime? simulatedUtcNow)
    {
        _simulatedUtcNow = simulatedUtcNow?.ToUniversalTime();
    }
}

public static class PasswordHasher
{
    public static string Hash(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToBase64String(bytes);
    }

    public static bool Verify(string password, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(Hash(password)),
            Encoding.UTF8.GetBytes(storedHash));
}
