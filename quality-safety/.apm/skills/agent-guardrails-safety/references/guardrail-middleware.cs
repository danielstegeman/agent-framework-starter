// references/guardrail-middleware.cs
//
// Small glue examples for current Microsoft Agent Framework middleware. Three layers:
//   1. Input guardrail    - redact/check user and external-content messages before inference.
//   2. Output guardrail   - inspect assistant messages before returning them.
//   3. Tool-call guardrail - approve/audit/policy-check functions before invocation.
//
// Compose IChatClient middleware with chatClient.AsBuilder().Use(...).Build().
// Compose agent run + function-calling middleware with agent.AsBuilder().Use(...).Build().

using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

public static class GuardrailMiddleware
{
    public static IChatClient WithChatGuardrails(
        this IChatClient chatClient,
        IPiiDetector pii,
        IContentSafety contentSafety) =>
        chatClient
            .AsBuilder()
            .Use(
                getResponseFunc: (messages, options, inner, cancellationToken) =>
                    InputGuardrailAsync(messages, options, inner, pii, contentSafety, cancellationToken),
                getStreamingResponseFunc: null)
            .Use(
                getResponseFunc: (messages, options, inner, cancellationToken) =>
                    OutputGuardrailAsync(messages, options, inner, contentSafety, cancellationToken),
                getStreamingResponseFunc: null)
            .Build();

    public static AIAgent WithAgentGuardrails(
        this AIAgent agent,
        IToolPolicy policy,
        IAuditSink audit) =>
        agent
            .AsBuilder()
            .Use(
                runFunc: AuditAgentRunAsync,
                runStreamingFunc: null)
            .Use((innerAgent, context, next, cancellationToken) =>
                FunctionPolicyAsync(innerAgent, context, next, policy, audit, cancellationToken))
            .Build();

    public static AIFunction RequireHumanApproval(Delegate tool) =>
        new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tool));

    private static async Task<ChatResponse> InputGuardrailAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient inner,
        IPiiDetector pii,
        IContentSafety contentSafety,
        CancellationToken cancellationToken)
    {
        var guarded = new List<ChatMessage>();
        foreach (var message in messages)
        {
            if (message.Role == ChatRole.User)
            {
                await contentSafety.ThrowIfPromptInjectionAsync(message.Text ?? string.Empty, cancellationToken);
                guarded.Add(message with { Text = pii.Redact(message.Text ?? string.Empty) });
            }
            else
            {
                guarded.Add(message);
            }
        }

        return await inner.GetResponseAsync(guarded, options, cancellationToken);
    }

    private static async Task<ChatResponse> OutputGuardrailAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        IChatClient inner,
        IContentSafety contentSafety,
        CancellationToken cancellationToken)
    {
        var response = await inner.GetResponseAsync(messages, options, cancellationToken);
        foreach (var message in response.Messages.Where(m => m.Role == ChatRole.Assistant))
        {
            await contentSafety.ThrowIfUnsafeOutputAsync(message.Text ?? string.Empty, cancellationToken);
        }

        return response;
    }

    private static async Task<AgentResponse> AuditAgentRunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session,
        AgentRunOptions? options,
        AIAgent innerAgent,
        CancellationToken cancellationToken)
    {
        using var activity = GuardrailActivity.Source.StartActivity("agent.run");
        activity?.SetTag("agent.guardrails", true);
        return await innerAgent.RunAsync(messages, session, options, cancellationToken);
    }

    private static async ValueTask<object?> FunctionPolicyAsync(
        AIAgent agent,
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
        IToolPolicy policy,
        IAuditSink audit,
        CancellationToken cancellationToken)
    {
        var toolName = context.Function.Name;
        var argsHash = policy.HashArguments(context);

        if (policy.IsBlocked(toolName, argsHash))
        {
            await audit.RecordAsync(toolName, argsHash, success: false, cancellationToken);
            throw new GuardrailException($"Tool call blocked by policy: {toolName}");
        }

        using var activity = GuardrailActivity.Source.StartActivity("tool.call");
        activity?.SetTag("tool.name", toolName);

        try
        {
            var result = await next(context, cancellationToken);
            activity?.SetTag("tool.success", true);
            await audit.RecordAsync(toolName, argsHash, success: true, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetTag("tool.success", false);
            activity?.SetTag("tool.error_type", ex.GetType().Name);
            await audit.RecordAsync(toolName, argsHash, success: false, cancellationToken);
            throw;
        }
    }
}

public static class GuardrailActivity
{
    public static readonly ActivitySource Source = new("Agent.Guardrails");
}

public interface IPiiDetector { string Redact(string input); }

public interface IContentSafety
{
    Task ThrowIfPromptInjectionAsync(string text, CancellationToken cancellationToken);
    Task ThrowIfUnsafeOutputAsync(string text, CancellationToken cancellationToken);
}

public interface IToolPolicy
{
    string HashArguments(FunctionInvocationContext context);
    bool IsBlocked(string toolName, string argsHash);
}

public interface IAuditSink
{
    Task RecordAsync(string toolName, string argsHash, bool success, CancellationToken cancellationToken);
}

public sealed class GuardrailException(string message) : Exception(message);
