using System.Runtime.InteropServices;

namespace PasteHatch.Platform;

internal static class ClipboardAccess
{
    private const int RetryAttempts = 10;
    private const int RetryDelayMilliseconds = 40;

    // Windows clipboard ownership is shared across processes and can be transiently unavailable.
    public static T Retry<T>(Func<T> operation)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return operation();
            }
            catch (ExternalException) when (attempt < RetryAttempts - 1)
            {
                Thread.Sleep(RetryDelayMilliseconds);
            }
        }
    }

    public static void Retry(Action operation) => Retry(() =>
    {
        operation();
        return true;
    });
}
