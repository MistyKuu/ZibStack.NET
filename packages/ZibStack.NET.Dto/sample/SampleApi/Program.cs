using ZibStack.NET.Dto;
using ZibStack.NET.Utils;
using ZibStack.NET.Dto.Sample;
using ZibStack.NET.Dto.Sample.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new PatchFieldJsonConverterFactory()));

// Register in-memory stores for generated CRUD endpoints
builder.Services.AddSingleton<ICrudStore<Player, int>>(_ =>
    new InMemoryCrudStore<Player, int>(
        new List<Player>
        {
            new Player
            {
                Id = 1, Name = "Alice", Level = 10, Email = "alice@example.com",
                Address = new Address { Street = "123 Main St", City = "Springfield", ZipCode = "62701" },
                Password = "hashed_secret", IsAdmin = false, CreatedAt = DateTime.UtcNow
            }
        },
        p => p.Id, (p, id) => p.Id = id, nextId: 2));

builder.Services.AddSingleton<ICrudStore<Team, int>>(_ =>
    new InMemoryCrudStore<Team, int>(
        new List<Team>
        {
            new Team { Id = 1, Name = "Alpha", Description = "First team", MaxMembers = 5, CreatedAt = DateTime.UtcNow }
        },
        t => t.Id, (t, id) => t.Id = id, nextId: 2));

var app = builder.Build();

// Generated minimal API endpoints
app.MapPlayerEndpoints();
app.MapTeamEndpoints();

// Legacy hand-written controllers still work
app.MapControllers();
app.Run();
