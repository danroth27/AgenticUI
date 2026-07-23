# Draft comment for dotnet/aspnetcore#67673 (Blazor AI components)

> Context: we copied this branch's `src/Components/AI` into a standalone sample
> ([danroth27/AgenticUI](https://github.com/danroth27/AgenticUI)) that exercises the AG-UI scenarios
> end-to-end against Microsoft Agent Framework. Two things came up that are worth folding into the
> components. Paths below are relative to `src/Components/AI/src`.

---

Thanks for these components — wiring them to an AG-UI/MAF backend was genuinely low-ceremony
(`UIAgent` over an `IChatClient` "just worked" for chat, backend tools, reasoning, and rendering
server-driven state). Two rough edges surfaced while building the samples.

## 1. Frontend tools (`UIActionBlock`) stall the run — they're treated like human approvals

`UIActionBlock` (a client-side "frontend tool" the model calls) and `FunctionApprovalBlock` (a
human approval) both implement `IInteractiveBlock`, so `Engine/AgentContext.cs` treats them
identically: it collects them, sets `Status = AwaitingInput`, and awaits an **external**
`GetResultAsync()` before resuming the run.

For an approval that's correct — a human clicks Approve/Reject. But a frontend tool has **no human
step**: the model declared it precisely so the browser runs it automatically. As shipped, the run
stalls forever unless the app adds glue that finds each `UIActionBlock` and calls `InvokeAsync()`.
Every page that uses frontend tools needs that boilerplate (we wrote a `UIActionRunner` component
just to un-stall the run).

**Suggested fix (what we did in our copy, verified end-to-end):**

- `Engine/AgentContext.cs` — when draining interactive blocks, auto-invoke `UIActionBlock`s and only
  enter `AwaitingInput` for blocks that genuinely need a person:

  ```csharp
  foreach (var interactive in interactiveBlocks)
  {
      resultTasks.Add(interactive.GetResultAsync(cancellationToken));

      // Frontend tools are client-side actions the model expects the browser to run
      // automatically — there is no human step. Invoke now so the run resumes without glue.
      if (interactive is UIActionBlock action)
      {
          _ = action.InvokeAsync(cancellationToken);
      }
  }

  var needsHumanInput = interactiveBlocks.Any(b => b is not UIActionBlock);
  if (needsHumanInput)
  {
      Status = ConversationStatus.AwaitingInput;
      NotifyStatusChanged();
  }
  ```

- `Blocks/UIActionBlock.cs` — make `InvokeAsync` idempotent (guard against double-invocation) and
  route failures to the awaiting run via `_tcs.TrySetException(ex)` instead of an unobserved task.

With that, the frontend-tools scenario needs zero app glue, and human approval still correctly
stalls at `AwaitingInput` until approved. (Both verified against a live MAF AG-UI server.) If you'd
prefer this be opt-in, a `UIAgentOptions.AutoInvokeUIActions` flag defaulting to `true` would also
work — happy to send a PR in whatever shape you like.

## 2. State mapping is inbound-only — no symmetric hook to send client state upstream

`UIAgentOptions.StateMapper` gives a clean **inbound** hook (server → client: map a
`STATE_SNAPSHOT`/`STATE_DELTA` into `UIAgent<TState>.State`). There's no **outbound** counterpart, so
a bidirectional "shared state" scenario (client edits the state and the agent should see it on the
next turn) has no first-class path. Today the app has to reach around the component and set the
transport's request state manually (for AG-UI, `ChatOptions.RawRepresentationFactory` returning a
`RunAgentInput { State = ... }`; the `AGUIChatClient` does forward `State`).

The components are deliberately transport-agnostic, so the actual wire-up belongs to the transport
bridge — but the **component** could expose the current state to that bridge symmetrically, e.g. an
outbound state provider on `UIAgentOptions` (or having `UIAgent<TState>` surface `State.Value` to a
request-building hook). Right now the asymmetry (inbound hook, no outbound hook) pushes that concern
entirely into app code.

This one we did **not** patch in our copy (it would couple the library to a transport); flagging it
as an API-shape gap for consideration.

---

Both are minor; #1 in particular removes per-page boilerplate for a core scenario. Glad to turn
either into a PR against this branch.
