namespace SiteChecker.Scripting;

/// <summary>
/// How a script's run ended: Succeeded with content, or a Known Failure the script recognised.
/// Return a <see cref="string"/> for Succeeded; it converts implicitly.
/// </summary>
public sealed class ScriptOutcome
{
    private ScriptOutcome(string? content, string? knownFailureMessage, IReadOnlyList<RequestedAction> requestedActions)
    {
        Content = content;
        KnownFailureMessage = knownFailureMessage;
        RequestedActions = requestedActions;
    }

    /// <summary>
    /// Whether the run ended in a Known Failure rather than with content.
    /// </summary>
    public bool IsKnownFailure => KnownFailureMessage is not null;

    /// <summary>
    /// The Site's content, when the run Succeeded. Compared exactly with the previous content to
    /// decide whether the Site was Updated, so it should be stable while the page is unchanged.
    /// </summary>
    public string? Content { get; }

    /// <summary>
    /// What the script recognised, when the run is a Known Failure.
    /// </summary>
    public string? KnownFailureMessage { get; }

    /// <summary>
    /// What a Known Failure asks the Site Check Runner to do after recording it. Empty when the
    /// run Succeeded.
    /// </summary>
    public IReadOnlyList<RequestedAction> RequestedActions { get; }

    /// <summary>
    /// A run that Succeeded with the given content.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <c>null</c>.</exception>
    public static ScriptOutcome Success(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return new(content, null, []);
    }

    /// <summary>
    /// A Known Failure: a state the script recognises, such as access denied or a blank page,
    /// optionally asking the runner to change the VPN Location or retry.
    /// </summary>
    /// <param name="message">What the script found.</param>
    /// <param name="requestedActions">What the runner should do after recording the failure.</param>
    public static ScriptOutcome KnownFailure(string message, params RequestedAction[] requestedActions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(requestedActions);
        return new(null, message, requestedActions.Distinct().ToArray());
    }

    /// <summary>
    /// A run that Succeeded with the given content. A <c>null</c> string throws, so it's an
    /// Unexpected Failure rather than empty content.
    /// </summary>
    public static implicit operator ScriptOutcome(string content) => Success(content);
}

/// <summary>
/// Something a Known Failure asks the Site Check Runner to do after recording it.
/// </summary>
public enum RequestedAction
{
    /// <summary>
    /// Stop using the VPN Location this run used, and move off it before the next VPN-routed
    /// check. Ignored for a Site that doesn't use the VPN.
    /// </summary>
    ChangeVpnLocation = 1,

    /// <summary>
    /// Queue another Site Check for the Site now instead of waiting for its Schedule. Honored once
    /// per Failing Run.
    /// </summary>
    Retry = 2,
}
