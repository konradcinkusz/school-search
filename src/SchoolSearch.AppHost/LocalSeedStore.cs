// The local secret store for the demo seed (Phase L). A gitignored JSON file at <repo>/.local/seed-secrets.json.
// The Postgres volume that authservice, reviews and billing use persists across runs, so the values stored in their
// databases (the test password hash, the subject hashes keyed by the peppers) must come back on the next run.
// Resolution per value: explicit configuration (user-secrets) first, then this file, then generated and written here.
// Reset the local data by deleting this file AND the Docker volume together; deleting only one breaks login.
using System.Text.Json;
using System.Text.Json.Serialization;

internal sealed class LocalSeedSecrets
{
    [JsonPropertyName("testPassword")]
    public string? TestPassword { get; set; }

    [JsonPropertyName("devSeedKey")]
    public string? DevSeedKey { get; set; }

    [JsonPropertyName("reviewsPepper")]
    public string? ReviewsPepper { get; set; }

    [JsonPropertyName("billingPepper")]
    public string? BillingPepper { get; set; }

    [JsonPropertyName("jwtPrivateKeyPem")]
    public string? JwtPrivateKeyPem { get; set; }

    // Keys the sealed ss_session cookie of the web BFF (SESSION_SECRET). Added after the first release of the store:
    // a file without this key still loads, and the value is generated on the next run that needs it.
    [JsonPropertyName("sessionSecret")]
    public string? SessionSecret { get; set; }

    // Shared by the web BFF and api, reviews, billing and mcp (BFF_SHARED_SECRET). Same compatibility rule as above.
    [JsonPropertyName("bffSharedSecret")]
    public string? BffSharedSecret { get; set; }
}

internal sealed class LocalSeedStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    private LocalSeedStore(string filePath, LocalSeedSecrets data, bool readable)
    {
        _filePath = filePath;
        Data = data;
        Readable = readable;
    }

    public LocalSeedSecrets Data { get; }

    // False when the file exists but cannot be parsed. Such a file is never overwritten, so its values are kept.
    public bool Readable { get; }

    // Set by the caller after it generates a value that must be kept.
    public bool Changed { get; set; }

    public static LocalSeedStore Open(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new LocalSeedStore(filePath, new LocalSeedSecrets(), readable: true);
        }

        try
        {
            var data = JsonSerializer.Deserialize<LocalSeedSecrets>(File.ReadAllText(filePath)) ?? new LocalSeedSecrets();
            return new LocalSeedStore(filePath, data, readable: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new LocalSeedStore(filePath, new LocalSeedSecrets(), readable: false);
        }
    }

    // Writes the file when something changed. Returns null on success or when there is nothing to write; otherwise the
    // reason, for the startup banner. The reason never contains a secret value.
    public string? TrySave()
    {
        if (!Readable)
        {
            return "the existing file cannot be read, so it was not overwritten";
        }

        if (!Changed)
        {
            return null;
        }

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_filePath, JsonSerializer.Serialize(Data, WriteOptions));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(_filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"write failed ({ex.GetType().Name})";
        }
    }
}
