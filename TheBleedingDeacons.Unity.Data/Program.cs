//using Microsoft.EntityFrameworkCore;
//using Microsoft.Extensions.Configuration;
//using Microsoft.Extensions.DependencyInjection;
//using Microsoft.Extensions.Hosting;
//using TheBleedingDeacons.Unity.Client;
//using TheBleedingDeacons.Unity.Data.Data;
//using TheBleedingDeacons.Unity.Data.Repositories;
//using TheBleedingDeacons.Unity.Data.Repositories.Interfaces;
//using TheBleedingDeacons.Unity.Data.Services;

//var builder = Host.CreateApplicationBuilder(args);

//// ── Configuration ────────────────────────────────────────────────

//builder.Configuration
//    .SetBasePath(Directory.GetCurrentDirectory())
//    .AddJsonFile("appsettings.json", optional: false);

//// ── Services ─────────────────────────────────────────────────────

//builder.Services.AddDbContext<UnityDbContext>(options =>
//    options.UseSqlite(builder.Configuration.GetConnectionString("UnityDb")));

//builder.Services.AddSingleton(sp =>
//{
//    var config = sp.GetRequiredService<IConfiguration>();
//    var baseUrl = config["Unity:BaseUrl"] ?? throw new InvalidOperationException("Unity:BaseUrl is not configured");
//    var apiKey = config["Unity:ApiKey"] ?? throw new InvalidOperationException("Unity:ApiKey is not configured");
//    return new UnityRestSharp(baseUrl, apiKey);
//});

//builder.Services.AddScoped<UnitySyncService>();

//builder.Services.AddScoped<IGroupRepository, GroupRepository>();
//builder.Services.AddScoped<IMeetingRepository, MeetingRepository>();
//builder.Services.AddScoped<IMemberRepository, MemberRepository>();
//builder.Services.AddScoped<IPositionRepository, PositionRepository>();
//builder.Services.AddScoped<IIntergroupMeetingRepository, IntergroupMeetingRepository>();

//var host = builder.Build();

//// ── Run ──────────────────────────────────────────────────────────

//using var scope = host.Services.CreateScope();
//var db = scope.ServiceProvider.GetRequiredService<UnityDbContext>();
//var sync = scope.ServiceProvider.GetRequiredService<UnitySyncService>();

//Console.WriteLine("Ensuring database is created...");
//await db.Database.EnsureCreatedAsync();

//Console.WriteLine("Syncing data from Unity API...");
//var result = await sync.SyncAsync();

//Console.WriteLine();
//Console.WriteLine($"  Groups:              {result.Groups}");
//Console.WriteLine($"  Meetings:            {result.Meetings}");
//Console.WriteLine($"  Positions:           {result.Positions}");
//Console.WriteLine($"  Members:             {result.Members}");
//Console.WriteLine($"  Intergroup Meetings: {result.IntergroupMeetings}");
//Console.WriteLine();

//// Quick verification queries via repositories
//var groupRepo = scope.ServiceProvider.GetRequiredService<IGroupRepository>();
//var meetingRepo = scope.ServiceProvider.GetRequiredService<IMeetingRepository>();
//var memberRepo = scope.ServiceProvider.GetRequiredService<IMemberRepository>();
//var positionRepo = scope.ServiceProvider.GetRequiredService<IPositionRepository>();
//var intergroupRepo = scope.ServiceProvider.GetRequiredService<IIntergroupMeetingRepository>();

//var allGroups = await groupRepo.GetAllAsync();
//var allMeetings = await meetingRepo.GetAllAsync();
//var gsrs = await memberRepo.GetGsrsAsync();
//var filledPositions = await positionRepo.GetFilledPositionsAsync();
//var vacantPositions = await positionRepo.GetVacantPositionsAsync();
//var onlineMeetings = await meetingRepo.GetOnlineMeetingsAsync();
//var allIntergroup = await intergroupRepo.GetAllAsync();

//Console.WriteLine("Verification (via repositories):");
//Console.WriteLine($"  Groups:              {allGroups.Count}");
//Console.WriteLine($"  Meetings:            {allMeetings.Count}");
//Console.WriteLine($"  GSR members:         {gsrs.Count}");
//Console.WriteLine($"  Filled positions:    {filledPositions.Count}");
//Console.WriteLine($"  Vacant positions:    {vacantPositions.Count}");
//Console.WriteLine($"  Online meetings:     {onlineMeetings.Count}");
//Console.WriteLine($"  Intergroup meetings: {allIntergroup.Count}");
//Console.WriteLine();
//Console.WriteLine("Done.");
