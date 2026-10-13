namespace SchoolSearch.Reviews.DevSeed;

/// <summary>
/// Fixed identifiers and tokens of the demo rows. A second seed run finds these rows and creates nothing.
/// The acknowledgement tokens are fixed so a developer can open the demo notices. They are demo values, produced only
/// by this seed, and the seed endpoint is never enabled outside a local or development environment.
/// </summary>
public static class DevSeedIds
{
    /// <summary>Marker: when this review exists, the seed has already run and nothing is written.</summary>
    public static readonly Guid ReviewParentSp1 = new Guid("7e5d0001-0000-4000-8000-00000000a001");

    public static readonly Guid ReviewParentSp2 = new Guid("7e5d0001-0000-4000-8000-00000000a002");

    public static readonly Guid ReviewParentSp3Pending = new Guid("7e5d0001-0000-4000-8000-00000000a003");

    public static readonly Guid ReviewParent2Sp1 = new Guid("7e5d0001-0000-4000-8000-00000000a004");

    public static readonly Guid ReviewParent2Sp3 = new Guid("7e5d0001-0000-4000-8000-00000000a005");

    /// <summary>Published, then removed by the moderation decision of <see cref="NoticeRemoved"/>.</summary>
    public static readonly Guid ReviewParent2Sp2Removed = new Guid("7e5d0001-0000-4000-8000-00000000a006");

    /// <summary>Open notice against <see cref="ReviewParentSp2"/>.</summary>
    public static readonly Guid NoticeOpen = new Guid("7e5d0003-0000-4000-8000-00000000b001");

    /// <summary>Decided (removal) notice against <see cref="ReviewParent2Sp2Removed"/>, appealed by a complaint.</summary>
    public static readonly Guid NoticeRemoved = new Guid("7e5d0003-0000-4000-8000-00000000b002");

    /// <summary>Decided (keep) notice about content that is not a review.</summary>
    public static readonly Guid NoticeKept = new Guid("7e5d0003-0000-4000-8000-00000000b003");

    /// <summary>Complaint (DSA art. 20) against the decision on <see cref="NoticeRemoved"/>.</summary>
    public static readonly Guid Complaint = new Guid("7e5d0004-0000-4000-8000-00000000c001");

    public const string NoticeOpenToken = "DemoAckNoticeOpen00001";

    public const string NoticeRemovedToken = "DemoAckNoticeDone00002";

    public const string NoticeKeptToken = "DemoAckNoticeKeep00003";

    public const string ComplaintToken = "DemoAckComplaint000004";

    /// <summary>Every seeded review id. Used to count and to recognise demo rows.</summary>
    public static readonly IReadOnlyList<Guid> ReviewIds = new Guid[]
    {
        ReviewParentSp1,
        ReviewParentSp2,
        ReviewParentSp3Pending,
        ReviewParent2Sp1,
        ReviewParent2Sp3,
        ReviewParent2Sp2Removed,
    };

    /// <summary>Every seeded notice id.</summary>
    public static readonly IReadOnlyList<Guid> NoticeIds = new Guid[]
    {
        NoticeOpen,
        NoticeRemoved,
        NoticeKept,
    };
}
