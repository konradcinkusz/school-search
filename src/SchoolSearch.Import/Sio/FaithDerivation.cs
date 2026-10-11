using System;

namespace SchoolSearch.Import;

/// <summary>
/// Derives the faith-affiliation flag. The only input is the governing body type code.
/// </summary>
/// <remarks>
/// The flag is never derived from a school name, a patron name or any text. This class has no overload that
/// accepts text, so that cannot happen by accident. A school named "Szkoła ... imienia Jana Pawła II" with
/// governing body type 130 gets false.
/// </remarks>
public static class FaithDerivation
{
    /// <summary>Governing body type "kościelna osoba prawna" (brief). The code is unverified, see GoverningBodyDictionary.</summary>
    public const int ChurchLegalPersonCode = 140;

    /// <summary>True only for governing body type 140. Null (no type) gives false.</summary>
    public static bool IsDerivedFromGoverningBody(int? governingBodyTypeCode) =>
        governingBodyTypeCode == ChurchLegalPersonCode;
}
