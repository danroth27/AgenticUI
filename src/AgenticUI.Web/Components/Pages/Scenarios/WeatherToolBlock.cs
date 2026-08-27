// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.AI;

namespace AgenticUI.Web.Components.Pages.Scenarios;

/// <summary>Strongly typed client representation of the <c>get_weather</c> tool call.</summary>
[ToolBlock("get_weather")]
public partial class WeatherToolBlock : FunctionInvocationContentBlock
{
    /// <summary>Gets or sets the requested location.</summary>
    [ToolParameter(Name = "location")]
    public string? Location { get; set; }

    /// <summary>Gets or sets the weather returned by the server tool.</summary>
    [ToolResult]
    public WeatherInfo? Weather { get; set; }
}

/// <summary>Weather data rendered by <see cref="WeatherToolBlock"/>.</summary>
public sealed class WeatherInfo
{
    /// <summary>Gets or sets the temperature in degrees Celsius.</summary>
    [JsonPropertyName("temperature")]
    public int Temperature { get; set; }

    /// <summary>Gets or sets the current conditions.</summary>
    [JsonPropertyName("conditions")]
    public string Conditions { get; set; } = string.Empty;

    /// <summary>Gets or sets the relative humidity percentage.</summary>
    [JsonPropertyName("humidity")]
    public int Humidity { get; set; }

    /// <summary>Gets or sets the wind speed in kilometers per hour.</summary>
    [JsonPropertyName("wind_speed")]
    public int WindSpeed { get; set; }

    /// <summary>Gets or sets the apparent temperature in degrees Celsius.</summary>
    [JsonPropertyName("feelsLike")]
    public int FeelsLike { get; set; }
}
