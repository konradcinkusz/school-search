using System;
using System.Text;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

public sealed class JwtSubjectTests
{
    [Fact]
    public void Reads_the_sub_claim_from_a_valid_token()
    {
        string? subject = JwtSubject.TryRead(FakeJwt.For("user-123"));

        Assert.Equal("user-123", subject);
    }

    [Fact]
    public void Round_trips_non_ascii_and_url_unsafe_subjects()
    {
        // The payload is base64url: '+', '/' and '=' must not break the decode, and JSON escapes must be read back.
        const string subject = "za\u017C\u00F3\u0142\u0107 ?>~/+=";

        string? read = JwtSubject.TryRead(FakeJwt.For(subject));

        Assert.Equal(subject, read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("only.two")]
    [InlineData("one.two.three.four")]
    [InlineData("header.!!!.signature")]
    [InlineData("header.a.signature")]
    public void Malformed_tokens_give_null(string token)
    {
        string? subject = JwtSubject.TryRead(token);

        Assert.Null(subject);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[\"sub\"]")]
    [InlineData("{}")]
    [InlineData("{\"sub\":123}")]
    [InlineData("{\"sub\":\"\"}")]
    [InlineData("{\"sub\":\"   \"}")]
    [InlineData("{\"sub\":null}")]
    [InlineData("{\"subject\":\"user-1\"}")]
    public void Payloads_without_a_non_empty_string_sub_give_null(string payloadJson)
    {
        string? subject = JwtSubject.TryRead(FakeJwt.WithPayload(payloadJson));

        Assert.Null(subject);
    }

    [Fact]
    public void Null_token_gives_null()
    {
        string? subject = JwtSubject.TryRead(null);

        Assert.Null(subject);
    }

    [Fact]
    public void Decoder_accepts_unpadded_base64url()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"sub\":\"a\"}");
        string encoded = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        byte[] decoded = JwtSubject.DecodeBase64Url(encoded);

        Assert.Equal(bytes, decoded);
    }
}
