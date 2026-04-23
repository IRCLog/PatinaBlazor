namespace PatinaBlazor.Data;

public record BotCommand(
    string Action,
    string User,
    string Message,
    DateTime Timestamp,
    string Source,
    string MessageId,
    string? Channel = null,
    string? Target = null
);
