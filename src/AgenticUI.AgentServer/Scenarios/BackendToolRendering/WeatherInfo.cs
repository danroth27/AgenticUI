// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.AgentServer.Scenarios.BackendToolRendering;

/// <summary>Weather result returned by the backend <c>get_weather</c> tool.</summary>
public sealed class WeatherInfo
{
    [JsonPropertyName("temperature")]
    public int Temperature { get; init; }

    [JsonPropertyName("conditions")]
    public string Conditions { get; init; } = string.Empty;

    [JsonPropertyName("humidity")]
    public int Humidity { get; init; }

    [JsonPropertyName("wind_speed")]
    public int WindSpeed { get; init; }

    [JsonPropertyName("feelsLike")]
    public int FeelsLike { get; init; }
}
