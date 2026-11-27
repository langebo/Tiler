namespace Tiler.X11.Tests;

/// <summary>
/// Opens the display under test, retrying a transient failure: a display that is up still answers
/// XOpenDisplay with NULL for roughly one attempt in a hundred (server up, client sends a setup
/// request without the auth cookie), so a single attempt cannot decide that the display is gone.
/// A display that really is missing fails every attempt and surfaces as the same BackendException.
/// </summary>
internal static class TestDisplay
{
    private const int ATTEMPTS = 5;
    private const int RETRY_DELAY_MS = 20;

    public static X11Wire Open(string? displayName = null)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return X11Wire.Open(displayName);
            }
            catch (BackendException) when (attempt < ATTEMPTS)
            {
                Thread.Sleep(RETRY_DELAY_MS);
            }
        }
    }
}
