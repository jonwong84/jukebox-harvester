namespace Harvester.Service;

public sealed class MusicBrainzClient : IDisposable
{
    private const string BaseUrl = "https://musicbrainz.org/ws/2/";
    private const string UserAgent = "JukeboxHarvester/0.1 (https://github.com/jonwong84/jukebox-harvester)";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly SemaphoreSlim _throttle = new(1, 1);
    private DateTimeOffset _nextAllowedRequestAt = DateTimeOffset.MinValue;

    public MusicBrainzClient()
        : this(new HttpClient { BaseAddress = new Uri(BaseUrl) }, ownsHttpClient: true)
    {
    }

    public MusicBrainzClient(HttpClient httpClient)
        : this(httpClient, ownsHttpClient: false)
    {
    }

    private MusicBrainzClient(HttpClient httpClient, bool ownsHttpClient)
    {
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri(BaseUrl);
        }

        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
    }

    public async Task<string> SearchReleaseAsync(string artist, string title, CancellationToken cancellationToken = default)
    {
        await ThrottleAsync(cancellationToken).ConfigureAwait(false);

        var query = $"artist:\"{artist}\" AND release:\"{title}\"";
        var relativeUrl = $"release/?query={Uri.EscapeDataString(query)}&fmt=json";

        using var response = await _httpClient.GetAsync(relativeUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ThrottleAsync(CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _nextAllowedRequestAt - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            // MusicBrainz allows at most 1 request per second for anonymous clients.
            _nextAllowedRequestAt = DateTimeOffset.UtcNow.AddSeconds(1);
        }
        finally
        {
            _throttle.Release();
        }
    }

    public void Dispose()
    {
        _throttle.Dispose();
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
