namespace SchoolSearch.Api.Scoring;

/// <summary>
/// The national reference for one subject in one exam year. The pupil standard deviation is sourced
/// from data and is an OPEN item (docs/data/SOURCES.md); it must be strictly positive.
/// </summary>
/// <param name="Subject">The exam subject.</param>
/// <param name="NationalMeanPercent">The national mean result in percent.</param>
/// <param name="PupilStdDevPoints">The pupil-level standard deviation in percentage points.</param>
public sealed record SubjectBaseline(Subject Subject, double NationalMeanPercent, double PupilStdDevPoints)
{
    public SubjectBaseline
    {
        if (!double.IsFinite(PupilStdDevPoints) || PupilStdDevPoints <= 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PupilStdDevPoints),
                PupilStdDevPoints,
                "The pupil standard deviation must be a finite number greater than zero.");
        }
    }
}
