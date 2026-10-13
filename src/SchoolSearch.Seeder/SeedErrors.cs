using System;

namespace SchoolSearch.Seeder;

/// <summary>Environment configuration is missing or invalid. The seeder exits with code 2.</summary>
public sealed class SeederConfigurationException : Exception
{
    public SeederConfigurationException(string message)
        : base(message)
    {
    }
}

/// <summary>A seeding step failed. The seeder exits with code 1. Messages never contain tokens, passwords or the dev seed key.</summary>
public sealed class SeedFailureException : Exception
{
    public SeedFailureException(string message)
        : base(message)
    {
    }
}
