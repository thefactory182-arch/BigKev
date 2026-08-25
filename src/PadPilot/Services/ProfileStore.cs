using System.Text.Json;
using System.Text.Json.Serialization;
using System.IO;
using PadPilot.Models;

namespace PadPilot.Services;

public sealed class ProfileStore
{
    private readonly string _directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BigKev", "profiles");
    private readonly string _legacyDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PadPilot", "profiles");

    private readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<Profile>> LoadAsync()
    {
        Directory.CreateDirectory(_directory);
        var profiles = new List<Profile>();
        var files = Directory.EnumerateFiles(_directory, "*.json").ToList();
        if (Directory.Exists(_legacyDirectory))
            files.AddRange(Directory.EnumerateFiles(_legacyDirectory, "*.json"));
        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<Profile>(await File.ReadAllTextAsync(file), _json);
                if (profile is not null) profiles.Add(profile);
            }
            catch (JsonException) { /* A bad profile must not prevent startup. */ }
        }
        return profiles.OrderBy(p => p.Name).ToList();
    }

    public async Task SaveAsync(Profile profile)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, $"{profile.Id:N}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, _json));
    }

    public void Delete(Profile profile)
    {
        var path = Path.Combine(_directory, $"{profile.Id:N}.json");
        if (File.Exists(path)) File.Delete(path);
    }
}

