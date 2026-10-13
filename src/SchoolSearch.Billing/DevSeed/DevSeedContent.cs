using SchoolSearch.Billing.Data;

namespace SchoolSearch.Billing.DevSeed;

/// <summary>
/// Fixed identifiers and synthetic content of the billing dev seed. Everything is labelled DEMO or "DEMO SEED (synthetic)",
/// uses the reserved .test and .invalid domains, and names no real school, pupil or person.
/// The slugs match the catalog demo seed (SchoolSearch.Api/Demo/DemoSeed.cs).
/// </summary>
public static class DevSeedContent
{
    /// <summary>School with the owner's approved claim, an active mock subscription and a published sponsored profile.</summary>
    public const string OwnedSchool = "demo-sp-1";

    /// <summary>School with the owner's pending claim, for the moderator queue. No subscription.</summary>
    public const string PendingSchool = "demo-sp-2";

    /// <summary>Opaque provider reference of the seeded subscription. Marks it as a mock row.</summary>
    public const string ProviderRef = "mock-demo-seed";

    /// <summary>
    /// Plan code of the seeded subscription. It is NOT an entry of Billing:Plans: the repository configures no plan code
    /// (the catalogue is empty by default, see appsettings.json), so there is no real code to reuse. The value is shown
    /// as-is to the owner (GET /api/v1/claims/mine, and the owner UI "plan ..."). Entitlement never reads it, and the
    /// webhook path only resolves an interval for it. Replace it with a configured code when a plan is set up.
    /// </summary>
    public const string PlanCode = "demo-plan";

    /// <summary>Provenance and moderator-internal note carried by the seeded rows.</summary>
    public const string SourceNote = "DEMO SEED (synthetic)";

    /// <summary>Sponsored website link. Reserved domain, never fetched by the server.</summary>
    public const string SponsoredWebsite = "https://example.invalid/demo";

    public const string SponsoredHeadline = "DEMO. Treść sponsorowana: profil szkoły";

    public const string SponsoredDescription =
        "DEMO. Treść sponsorowana przygotowana przez właściciela szkoły na potrzeby środowiska developerskiego. " +
        "Dane są syntetyczne i nie opisują żadnej rzeczywistej placówki. Treść nie jest oceną redakcji i nie wpływa " +
        "na wyniki ani kolejność szkół w zestawieniach. Przykładowe zajęcia dodatkowe: język angielski, zajęcia " +
        "sportowe i koło matematyczne.";

    /// <summary>Fixed ids, so a repeated call finds the same rows (the approved claim is the idempotency marker).</summary>
    public static class Ids
    {
        public static readonly Guid ApprovedClaim = new("b1110000-0000-4000-8000-000000000001");
        public static readonly Guid PendingClaim = new("b1110000-0000-4000-8000-000000000002");
        public static readonly Guid Subscription = new("b1110000-0000-4000-8000-000000000003");
    }

    /// <summary>One synthetic parent enquiry. DaysAgo sets CreatedAt and ConsentAt relative to the seed time.</summary>
    public sealed record EnquiryFixture(Guid Id, string ParentEmail, string Message, int DaysAgo, string Status);

    public static readonly EnquiryFixture[] Enquiries =
    {
        new EnquiryFixture(new Guid("b1110000-0000-4000-8000-0000000000a1"), "rodzic.demo1@example.test",
            "Dzień dobry, czy szkoła prowadzi zajęcia dodatkowe z języka angielskiego? Dziękuję za odpowiedź.",
            4, LeadStatuses.New),
        new EnquiryFixture(new Guid("b1110000-0000-4000-8000-0000000000a2"), "rodzic.demo2@example.test",
            "Chciałabym dowiedzieć się, jak wyglądają zapisy do klas pierwszych w przyszłym roku szkolnym.",
            3, LeadStatuses.New),
        new EnquiryFixture(new Guid("b1110000-0000-4000-8000-0000000000a3"), "rodzic.demo3@example.test",
            "Proszę o informację, czy w szkole działa świetlica i w jakich godzinach jest czynna.",
            1, LeadStatuses.New),
        // The one enquiry already closed by the owner. The only other status the table allows is "new".
        new EnquiryFixture(new Guid("b1110000-0000-4000-8000-0000000000a4"), "rodzic.demo4@example.test",
            "Dzień dobry, pytanie o termin dnia otwartego. Sprawa została już załatwiona telefonicznie.",
            6, LeadStatuses.Read),
    };
}
