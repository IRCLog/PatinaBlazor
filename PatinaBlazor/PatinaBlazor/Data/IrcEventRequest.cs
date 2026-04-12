namespace PatinaBlazor.Data;

public record IrcEventRequest(
    string Action,
    string Network,
    DateTime? Timestamp = null,
    string? Message = null,
    string? Target = null,
    string? Channel = null,
    string? Sender = null,
    string? User = null
);
