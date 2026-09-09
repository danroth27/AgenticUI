// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.AI;

namespace AgenticUI.Web.Components.Pages.Scenarios;

[ToolBlock("get_weather")]
public partial class WeatherToolBlock : FunctionInvocationContentBlock
{
    [ToolParameter(Name = "location")]
    public string? Location { get; set; }

    [ToolResult]
    public WeatherInfo? Weather { get; set; }
}

public sealed class WeatherInfo
{
    [JsonPropertyName("temperature")]
    public int Temperature { get; set; }

    [JsonPropertyName("conditions")]
    public string Conditions { get; set; } = "sunny";

    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }

    [JsonPropertyName("wind_speed")]
    public int WindSpeed { get; set; }

    [JsonPropertyName("feelsLike")]
    public int FeelsLike { get; set; }
}
