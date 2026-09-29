namespace CloudAlarmOverlay.Infrastructure.Google;

// Desktop OAuth identifies the distributed application; user tokens are never bundled.
internal sealed class GoogleBuiltInClient
{
    private readonly Lazy<GoogleClient?> client;
    public GoogleBuiltInClient() : this(() =>
    {
        using var stream = typeof(GoogleBuiltInClient).Assembly.GetManifestResourceStream("GoogleDesktopOAuth.json");
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return GoogleApi.ParseClient(reader.ReadToEnd());
    }) { }
    internal GoogleBuiltInClient(Func<GoogleClient?> factory) => client = new(factory);
    public GoogleClient? Client => client.Value;
}
