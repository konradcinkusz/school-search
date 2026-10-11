namespace SchoolSearch.Mcp.Agent;

/// <summary>Body of <c>POST /agent/chat</c>. Every field is nullable: the parser answers a missing field, never the binder.</summary>
public sealed class AgentChatRequest
{
    public List<AgentChatTurn>? Messages { get; set; }

    public AgentChatContext? Context { get; set; }
}

/// <summary>One turn of the conversation. Role is "user" or "assistant".</summary>
public sealed class AgentChatTurn
{
    public string? Role { get; set; }

    public string? Content { get; set; }
}

/// <summary>Optional context from the form the chat was opened from.</summary>
public sealed class AgentChatContext
{
    public string? Gmina { get; set; }
}

/// <summary>
/// The parsed request: the conversation as wrapped model messages and the gmina, or the first validation failure with the
/// uniform error code and a Polish message.
/// </summary>
public sealed record AgentRequestValidation(
    IReadOnlyList<AgentMessage>? Conversation,
    string? Gmina,
    string? ErrorCode,
    string? ErrorMessage);

/// <summary>
/// Validates the chat body (1 to 12 turns, 1 to 1000 characters each, starting and ending with the user, a 7-digit gmina
/// when given) and runs the pre-send guard for e-mail addresses, phone numbers and PESEL-like digit runs.
/// </summary>
public static class AgentRequestParser
{
    public const int MaxTurns = 12;

    public const int MaxTurnLength = 1000;

    public const string ValidationCode = "validation_failed";

    public const string PersonalDataCode = "personal_data_detected";

    public static AgentRequestValidation Parse(AgentChatRequest? request)
    {
        if (request?.Messages is not { } turns || turns.Count == 0 || turns.Count > MaxTurns)
        {
            return Reject(ValidationCode, "Podaj od 1 do 12 wiadomości.");
        }

        var conversation = new List<AgentMessage>(turns.Count);
        foreach (var turn in turns)
        {
            if (turn is null)
            {
                return Reject(ValidationCode, "Każda wiadomość musi mieć rolę i treść.");
            }

            AgentRole role;
            if (turn.Role == "user")
            {
                role = AgentRole.User;
            }
            else if (turn.Role == "assistant")
            {
                role = AgentRole.Assistant;
            }
            else
            {
                return Reject(ValidationCode, "Rola wiadomości musi mieć wartość user albo assistant.");
            }

            var text = Clean(turn.Content);
            if (text.Length == 0 || text.Length > MaxTurnLength)
            {
                return Reject(ValidationCode, "Każda wiadomość ma od 1 do 1000 znaków.");
            }

            if (AgentPrompts.ContainsPersonalData(text))
            {
                return Reject(PersonalDataCode, "Nie podawaj adresów e-mail, numerów telefonów ani numerów PESEL. Usuń je i spróbuj ponownie.");
            }

            var wrapped = role == AgentRole.User
                ? AgentPrompts.WrapUserText(text)
                : AgentPrompts.WrapAssistantText(text);
            conversation.Add(new AgentMessage(role, [new AgentTextPart(wrapped)]));
        }

        if (conversation[0].Role != AgentRole.User || conversation[^1].Role != AgentRole.User)
        {
            return Reject(ValidationCode, "Rozmowa musi zaczynać się i kończyć wiadomością użytkownika.");
        }

        string? gmina = null;
        if (request.Context?.Gmina is { } rawGmina)
        {
            var candidate = rawGmina.Trim();
            if (candidate.Length != 7 || !candidate.All(char.IsAsciiDigit))
            {
                return Reject(ValidationCode, "gmina musi być kodem TERYT z 7 cyfr.");
            }

            gmina = candidate;
        }

        return new AgentRequestValidation(conversation, gmina, null, null);
    }

    private static AgentRequestValidation Reject(string code, string message) =>
        new(null, null, code, message);

    /// <summary>Trims and drops control characters except line breaks and tabs.</summary>
    private static string Clean(string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        var kept = new string(value.Where(c => !char.IsControl(c) || c is '\n' or '\t').ToArray());
        return kept.Trim();
    }
}
