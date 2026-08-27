// Copyright (c) Microsoft. All rights reserved.

namespace AgenticUI.Web.Models;

/// <summary>Structured content and presentation data for a generated haiku card.</summary>
public sealed class HaikuData
{
    /// <summary>Gets or sets the Japanese lines.</summary>
    public List<string> Japanese { get; set; } = [];

    /// <summary>Gets or sets the English translation lines.</summary>
    public List<string> English { get; set; } = [];

    /// <summary>Gets or sets the card background gradient.</summary>
    public string Gradient { get; set; } = string.Empty;
}
