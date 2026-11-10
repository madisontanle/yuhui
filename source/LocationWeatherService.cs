using System.Net.Http.Json;
using System.Net.Http;
using System.Text.Json.Serialization;

namespace Yuhui;

public sealed class LocationWeatherService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public async Task<OutdoorContext> GetOutdoorContextAsync(CancellationToken cancellationToken)
    {
        Client.DefaultRequestHeaders.UserAgent.ParseAdd("YuhuiDesktop/1.0");
        var response = await Client.GetFromJsonAsync<I5pResponse>("https://www.i5p.com/api/myip", cancellationToken)
            ?? throw new InvalidOperationException("无法读取位置");
        var location = response.Data;
        if (response.Code != 0 || location?.Latitude is null || location.Longitude is null) throw new InvalidOperationException("位置数据不完整");

        var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude:0.####}&longitude={location.Longitude:0.####}&current=weather_code,cloud_cover&daily=sunset&timezone=auto&forecast_days=1";
        var weather = await Client.GetFromJsonAsync<WeatherResponse>(weatherUrl, cancellationToken)
            ?? throw new InvalidOperationException("无法读取天气");
        var sunset = weather.Daily?.Sunset?.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(sunset) || !DateTime.TryParse(sunset, out var sunsetTime)) throw new InvalidOperationException("天气服务未返回日落时间");

        return new OutdoorContext(
            string.Concat(location.Province, location.City) is { Length: > 0 } city ? city : "当前位置",
            location.Latitude.Value,
            location.Longitude.Value,
            sunsetTime,
            WeatherLabel(weather.Current?.WeatherCode, weather.Current?.CloudCover));
    }

    private static string WeatherLabel(int? code, int? clouds)
    {
        if (code is 95 or 96 or 99) return "雷雨，天色会提前沉下来";
        if (code is 51 or 53 or 55 or 61 or 63 or 65 or 80 or 81 or 82) return "有雨，留意窗外的光";
        if ((clouds ?? 0) >= 75) return "云层很厚，余晖会更柔和";
        if ((clouds ?? 0) >= 35) return "有云，适合等一场余晖";
        return "天空清朗，日落会很清晰";
    }

    private sealed class I5pResponse
    {
        [JsonPropertyName("code")] public int Code { get; init; }
        [JsonPropertyName("data")] public I5pLocation? Data { get; init; }
    }

    private sealed class I5pLocation
    {
        [JsonPropertyName("city")] public string? City { get; init; }
        [JsonPropertyName("province")] public string? Province { get; init; }
        [JsonPropertyName("lat")] public double? Latitude { get; init; }
        [JsonPropertyName("lng")] public double? Longitude { get; init; }
    }

    private sealed class WeatherResponse
    {
        [JsonPropertyName("current")] public CurrentWeather? Current { get; init; }
        [JsonPropertyName("daily")] public DailyWeather? Daily { get; init; }
    }

    private sealed class CurrentWeather
    {
        [JsonPropertyName("weather_code")] public int? WeatherCode { get; init; }
        [JsonPropertyName("cloud_cover")] public int? CloudCover { get; init; }
    }

    private sealed class DailyWeather
    {
        [JsonPropertyName("sunset")] public string[]? Sunset { get; init; }
    }
}

public sealed record OutdoorContext(string City, double Latitude, double Longitude, DateTime Sunset, string WeatherNote);
