namespace IdCard.Domain.Enums;

public enum PrintJobStatus
{
    Queued = 0,
    Sent = 1,
    Printing = 2,
    Done = 3,
    Failed = 4,
    Cancelled = 5
}
