using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace SchoolSearch.Seeder;

/// <summary>Builds every request the seeder sends. Pure: no I/O, so the shapes are unit tested.</summary>
public static class SeedRequests
{
    public const string DevSeedKeyHeader = "X-Dev-Seed-Key";
    private const string JsonMediaType = "application/json";

    public static HttpRequestMessage Get(Uri url, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    public static HttpRequestMessage Post(Uri url, JsonNode body, string? bearerToken = null)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(body);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, JsonMediaType),
        };
        if (bearerToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    /// <summary>POST to a dev seed endpoint. The key travels only in the header, never in the URL or the body.</summary>
    public static HttpRequestMessage DevSeed(Uri url, string devSeedKey, IReadOnlyDictionary<string, string> subjects)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentException.ThrowIfNullOrEmpty(devSeedKey);
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(DevSeedBody(subjects).ToJsonString(), Encoding.UTF8, JsonMediaType),
        };
        request.Headers.Add(DevSeedKeyHeader, devSeedKey);
        return request;
    }

    /// <summary>Body of the authservice register call. The consent versions must match the service's ConsentVersions.</summary>
    public static JsonObject Register(string email, string password, string termsVersion, string privacyVersion) => new()
    {
        ["email"] = email,
        ["password"] = password,
        ["acceptedTermsVersion"] = termsVersion,
        ["acceptedPrivacyVersion"] = privacyVersion,
    };

    public static JsonObject Login(string email, string password) => new()
    {
        ["email"] = email,
        ["password"] = password,
    };

    public static JsonObject AssignRole(string role) => new()
    {
        ["role"] = role,
    };

    /// <summary>Body of the reviews and billing dev seed calls: <c>{"subjects": {"parent": "&lt;sub&gt;", ...}}</c>.</summary>
    public static JsonObject DevSeedBody(IReadOnlyDictionary<string, string> subjects)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        var map = new JsonObject();
        foreach (KeyValuePair<string, string> pair in subjects)
        {
            map[pair.Key] = pair.Value;
        }

        return new JsonObject
        {
            ["subjects"] = map,
        };
    }
}
