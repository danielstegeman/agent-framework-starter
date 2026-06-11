// references/guardrail-middleware.cs
//
// Guardrail middleware patterns for agents. Three layers:
//
//   1. Input guardrail   - inspect/redact the user prompt before it hits the LLM.
//   2. Output guardrail  - inspect/redact the assistant message before returning.
//   3. Tool-call guardrail - inspect tool arguments before invocation; block on policy.
//
// Implemented as IChatClient delegating middleware. Compose them with
// ChatClientBuilder.UsePipeline(...) before passing to ChatClientAgent.

using Microsoft.Extensions.AI;

public sealed class InputRedactionMiddleware : DelegatingChatClient
{
    private readonly IPiiDetector _pii;

    public InputRedactionMiddleware(IChatClient inner, IPiiDetector pii) : base(inner)
        => _pii = pii;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var redacted = messages.Select(m => m with { Text = _pii.Redact(m.Text ?? string.Empty) }).ToList();
        return await base.GetResponseAsync(redacted, options, cancellationToken);
    }
}

public sealed class PromptInjectionGuardMiddleware : DelegatingChatClient
{
    public PromptInjectionGuardMiddleware(IChatClient inner) : base(inner) { }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var m in messages.Where(m => m.Role == ChatRole.User))
        {
            // Real implementation: call Azure AI Content Safety prompt-shield, or a
            // local classifier. Throw or rewrite when injection is detected.
            if (LooksLikeJailbreak(m.Text)) throw new GuardrailException("Prompt injection detected.");
        }
        return await base.GetResponseAsync(messages, options, cancellationToken);
    }

    private static bool LooksLikeJailbreak(string? t) =>
        t is not null && t.Contains("ignore previous", StringComparison.OrdinalIgnoreCase);
}

public sealed class ToolCallAuditMiddleware
{
    // Hook into AIFunction by wrapping it at registration time. The Agent Framework
    // exposes AIFunctionFactory.Create(...) — wrap the resulting AIFunction in a
    // class that overrides InvokeAsync to record an Activity span with
    // arguments (in dev), result hash, success/failure.
}

public interface IPiiDetector { string Redact(string input); }
public sealed class GuardrailException(string message) : Exception(message);
