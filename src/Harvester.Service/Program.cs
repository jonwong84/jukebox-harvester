using Harvester.Service;
using Jukebox.Harvester.DataAccess.Contracts;
using Jukebox.Harvester.DataAccess.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

const string artist = "Radiohead";
const string album = "OK Computer";

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var connectionString = configuration.GetConnectionString("HarvesterStaging")
    ?? throw new InvalidOperationException("Connection string 'HarvesterStaging' was not found.");

using var musicBrainzClient = new MusicBrainzClient();
var rawPayload = await musicBrainzClient.SearchReleaseAsync(artist, album);

var stagingRecord = new StagingRecord
{
    Id = Guid.NewGuid(),
    Source = "MusicBrainz",
    ExtractedAt = DateTime.UtcNow,
    RawPayload = rawPayload,
    Isrc = null
};

var options = new DbContextOptionsBuilder<HarvesterDbContext>()
    .UseSqlServer(connectionString)
    .Options;

await using var dbContext = new HarvesterDbContext(options);
dbContext.StagingRecords.Add(stagingRecord);
await dbContext.SaveChangesAsync();

Console.WriteLine($"Staged MusicBrainz release data. StagingRecord Id: {stagingRecord.Id}");
