using System;
using System.Threading;
using System.Threading.Tasks;
using SchoolSearch.Seeder;

// Exit codes: 0 seeded, 1 seeding failed, 2 configuration missing or invalid. See SeederApp.
return await SeederApp.RunAsync(
    name => Environment.GetEnvironmentVariable(name),
    Console.Out,
    Console.Error,
    handler: null,
    cancellationToken: CancellationToken.None);
