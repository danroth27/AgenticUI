// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;
using AgenticUI.AgentServer.Scenarios.AgenticGenerativeUi;
using AgenticUI.AgentServer.Scenarios.BackendToolRendering;
using AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;
using AgenticUI.AgentServer.Scenarios.SharedState;

namespace AgenticUI.AgentServer;

/// <summary>Source-generated JSON context for the tool argument/result types used by the agents.</summary>
[JsonSerializable(typeof(WeatherInfo))]
[JsonSerializable(typeof(Recipe))]
[JsonSerializable(typeof(Ingredient))]
[JsonSerializable(typeof(RecipeResponse))]
[JsonSerializable(typeof(Plan))]
[JsonSerializable(typeof(Step))]
[JsonSerializable(typeof(StepStatus))]
[JsonSerializable(typeof(StepStatus?))]
[JsonSerializable(typeof(JsonPatchOperation))]
[JsonSerializable(typeof(List<JsonPatchOperation>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(DocumentState))]
public sealed partial class AgentServerSerializerContext : JsonSerializerContext;
