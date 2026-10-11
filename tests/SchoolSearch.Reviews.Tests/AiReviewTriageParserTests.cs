using SchoolSearch.Reviews.Ai;
using Xunit;

namespace SchoolSearch.Reviews.Tests;

/// <summary>The model answer is untrusted: anything that is not one object with an allowed label becomes unclear.</summary>
public sealed class AiReviewTriageParserTests
{
    [Theory]
    [InlineData("{\"label\":\"ok\",\"note\":\"nothing to flag\"}")]
    [InlineData("```json\n{\"label\":\"likely_personal_data\",\"note\":\"names a teacher\"}\n```")]
    [InlineData("Here is my answer: {\"label\":\"needs_attention\",\"note\":\"x\"} thanks")]
    public void Valid_object_with_an_allowed_label_is_read(string modelText)
    {
        var result = ReviewTriageParser.Parse(modelText);

        Assert.Contains(result.Label, ReviewTriageLabels.All);
        Assert.NotEqual(ReviewTriageLabels.Unclear, result.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"label\":5}")]
    [InlineData("{\"note\":\"no label here\"}")]
    [InlineData("{\"label\":\"publish\",\"note\":\"the model tried to publish\"}")]
    [InlineData("{\"label\":\"OK\",\"note\":\"wrong case\"}")]
    [InlineData("{\"label\":\"ok\",")]
    public void Anything_else_becomes_unclear(string modelText)
    {
        var result = ReviewTriageParser.Parse(modelText);

        Assert.Equal(ReviewTriageLabels.Unclear, result.Label);
    }

    [Fact]
    public void Note_is_capped_and_stripped_of_control_characters()
    {
        var longNote = new string('a', 500);
        var result = ReviewTriageParser.Parse("{\"label\":\"ok\",\"note\":\"line\\nbreak\\t" + longNote + "\"}");

        Assert.Equal(ReviewTriageLabels.Ok, result.Label);
        Assert.Equal(ReviewTriageParser.MaxNoteLength, result.Note.Length);
        Assert.DoesNotContain('\n', result.Note);
        Assert.DoesNotContain('\t', result.Note);
    }

    [Fact]
    public void Label_set_has_exactly_the_five_documented_values()
    {
        Assert.Equal(
            new[] { "ok", "needs_attention", "likely_personal_data", "likely_abusive", "unclear" }.Order(),
            ReviewTriageLabels.All.Order());
    }
}
