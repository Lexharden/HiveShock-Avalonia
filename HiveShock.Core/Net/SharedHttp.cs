namespace HiveShock.Net;

/// <summary>Un único <see cref="HttpClient"/> para las llamadas cortas (Twitch, consultas). Evita crear uno por petición.</summary>
public static class SharedHttp
{
    public static HttpClient Client { get; } = new() { Timeout = TimeSpan.FromSeconds(20) };
}
