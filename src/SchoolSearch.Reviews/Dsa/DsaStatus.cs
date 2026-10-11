namespace SchoolSearch.Reviews.Dsa;

/// <summary>Stored status strings (Review, ReviewReply, Notice, Complaint). Values match the data model spec.</summary>
internal static class DsaStatus
{
    public const string Pending = "pending";
    public const string Published = "published";
    public const string Rejected = "rejected";
    public const string Removed = "removed";

    public const string Received = "received";
    public const string Decided = "decided";

    public const string ComplaintReceived = "received";
    public const string Upheld = "upheld";
    public const string Reversed = "reversed";
}
