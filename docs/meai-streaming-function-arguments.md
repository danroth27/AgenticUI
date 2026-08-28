# Proposal: provider-neutral streaming function arguments in MEAI

## Summary

Add an opt-in, stream-only MEAI content type for incremental function-call arguments. Provider
adapters emit it while arguments are generated, and `FunctionInvokingChatClient` continues to
produce the existing authoritative `FunctionCallContent` and `FunctionResultContent`.

This is a general tool-progress capability. Predictive state remains an application pattern built
on top of it.

## Motivation

Provider SDKs expose incremental function arguments, but MEAI consumers can access them only through
provider-specific `ChatResponseUpdate.RawRepresentation` values.

This forces libraries and applications to:

- Reference each provider SDK.
- Reconstruct call identity from provider indexes and partial IDs.
- Implement partial JSON and Unicode handling.
- Depend on provider-specific ordering behavior.
- Repeat the same extraction already implemented by other libraries.

AG-UI .NET 0.0.5 demonstrates the demand and provides existing prior art:

- `AGUIToolCallArgumentFragment`
- `AGUIStreamOptions.MapStreamingToolCallArguments(...)`

That hook correctly emits incremental `TOOL_CALL_ARGS`, but every caller must still supply a
provider-specific extractor. A MEAI abstraction would make the extractor unnecessary for compliant
providers while preserving it as a fallback for custom or older clients.

## Goals

- Expose streamed function arguments without provider SDK dependencies.
- Preserve FICC's existing function invocation, server-handled-call detection, and approval
  guarantees.
- Make completed `FunctionCallContent` authoritative.
- Keep streamed deltas out of reconstructed chat history.
- Support AG-UI, progress visualization, diagnostics, and other consumers without defining a
  predictive-state feature.

## Non-goals

- A predictive-state API.
- Incremental JSON parsing.
- Automatic conversion of arguments into application state.
- Invoking a function before its complete arguments are available.
- Disabling FICC buffering of completed calls.

## Proposed shape

The exact name is open, but the content should resemble:

```csharp
public sealed class FunctionCallArgumentsDeltaContent : AIContent
{
    public required string CallId { get; init; }
    public string? Name { get; init; }
    public required string ArgumentsDelta { get; init; }
    public bool IsFinal { get; init; }
}
```

### Contract

- `CallId` is present on every update. Provider adapters may cache the first ID associated with a
  provider-local index to satisfy this contract.
- `Name` is required on the first update and may be repeated.
- `ArgumentsDelta` is an arbitrary substring of the serialized JSON arguments. It may split a JSON
  token, escape sequence, Unicode escape, or surrogate pair.
- Consumers concatenate deltas in stream order and parse only the accumulated value.
- `IsFinal` indicates that no more argument deltas will be emitted for the call. A subsequent
  completed `FunctionCallContent` remains the authoritative parsed call.
- Deltas are informational and must never trigger invocation.

`string` is preferable to `BinaryData` because the contract is a text substring, not an
independently valid UTF-8 JSON payload.

## Emission and ordering

Provider adapters should guarantee:

1. All argument deltas for a call are emitted before its completed `FunctionCallContent`.
2. Deltas for each call retain provider order.
3. Parallel calls are correlated by `CallId`, not only by a provider-local index.
4. The completed call contains the same logical arguments obtained by concatenating the deltas.

The deterministic prototype showed that an adapter emitting a completed call and then later content
can interact badly with FICC buffering and produce invalid downstream protocol ordering. The
delta-before-completed-call guarantee should therefore be part of the adapter contract, not merely
an observation about the current OpenAI Chat Completions implementation.

## FunctionInvokingChatClient behavior

FICC should:

- Pass argument-delta updates through while they precede the completed call.
- Ignore delta content when discovering invocable `FunctionCallContent`.
- Exclude deltas from augmented request history and response reconstruction.
- Preserve existing buffering once a completed call is observed.
- Continue rewriting completed calls for approval and marking server-handled calls as
  informational.

FICC should not independently invoke, parse, or mutate the deltas.

If MEAI wants to support providers that cannot guarantee deltas precede the completed call, that
requires a separate design for selectively passing stream-only content through while other updates
are buffered. It should not be required for the initial API.

## History and aggregation

The content is stream-only:

- `ChatResponseUpdate.ToChatResponse()` must not add it to assistant messages.
- FICC must not send it back to the provider on later iterations.
- MAF may forward it in `AgentResponseUpdate` while streaming, but must not persist it as
  conversation content.
- The completed `FunctionCallContent` is the only representation stored in history.

This rule is required; otherwise every fragment can be replayed to the provider on the next FICC
iteration.

## Compatibility

Adding a new `AIContent` subtype changes observable update contents for consumers. Initial exposure
should therefore be opt-in and experimental, for example through a `ChatOptions` capability or
request flag.

Providers that do not support streamed arguments continue to emit only completed
`FunctionCallContent`.

Consumers must tolerate:

- No deltas.
- A stream ending before `IsFinal`.
- A completed call without prior deltas.
- Interleaved deltas from multiple calls.

## AG-UI integration

When the typed MEAI content is available, AG-UI can map it directly:

```text
FunctionCallArgumentsDeltaContent
    -> TOOL_CALL_START / TOOL_CALL_ARGS
    -> completed FunctionCallContent
    -> TOOL_CALL_END
```

`MapStreamingToolCallArguments(...)` remains useful for provider-specific raw representations, but
becomes an escape hatch instead of required application plumbing.

AG-UI also needs to coordinate streamed calls with approval rewriting. If a call was opened from
deltas and FICC later surfaces it as an approval request, AG-UI must not emit a duplicate
`TOOL_CALL_START`.

## Provider work

Each provider adapter owns translating its native updates:

- OpenAI Chat Completions
- OpenAI Responses
- Azure AI Inference
- Other providers with streamed function/tool arguments

The prototype confirmed that a Chat-Completions-specific raw extractor does not work with the
OpenAI Responses adapter. The MEAI abstraction should normalize both when their underlying SDKs
provide equivalent progress.

## Acceptance criteria

- A consumer receives argument deltas before the completed call.
- `CallId` is stable across every delta for a call.
- Sequential calls may reuse provider indexes without corrupting accumulation.
- Parallel calls can be correlated independently.
- Escapes and fragmented Unicode round-trip after concatenation.
- Mixed assistant text and tool calls retain valid ordering.
- Approval-required calls do not produce duplicate downstream tool lifecycles.
- FICC emits one completed call and one result per executed function.
- Deltas do not appear in reconstructed or persisted conversation history.
- Cancellation and incomplete streams do not leave calls permanently active.
- Providers without delta support remain source- and behavior-compatible.

## Questions for MEAI review

- What should the opt-in capability be called and where should it live?
- Is `IsFinal` useful, or is the completed `FunctionCallContent` sufficient as the normal terminal
  signal?
- Should `Name` be required on every delta for symmetry with `CallId`?
- Should stream-only content have a common marker or base contract so aggregation and persistence
  can exclude it generically?
- Which provider adapters can support the contract initially?
