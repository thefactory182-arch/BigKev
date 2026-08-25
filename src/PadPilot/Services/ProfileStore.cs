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

    public async Task ExportProfileAsync(Profile profile, string path) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(profile, _json));

    public async Task ExportLibraryAsync(IEnumerable<Profile> profiles, string path)
    {
        var bundle = new ProfileBundle { Profiles = profiles.ToList() };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(bundle, _json));
    }

    public async Task ExportMacroAsync(MacroDefinition macro, string path) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new MacroBundle { Macro = macro }, _json));

    public async Task<MacroDefinition> ImportMacroAsync(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 1_000_000) throw new InvalidDataException("The macro file is missing or too large.");
        var bundle = JsonSerializer.Deserialize<MacroBundle>(await File.ReadAllTextAsync(path), _json);
        if (bundle?.Macro is null || bundle.Macro.Steps is null || bundle.Macro.Steps.Count > 500) throw new InvalidDataException("The file does not contain a valid BigKev macro.");
        bundle.Macro.Id = Guid.NewGuid();
        bundle.Macro.Name = string.IsNullOrWhiteSpace(bundle.Macro.Name) ? "Imported macro" : bundle.Macro.Name.Trim();
        return bundle.Macro;
    }

    public async Task<IReadOnlyList<Profile>> ImportAsync(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 5_000_000) throw new InvalidDataException("The settings file is missing or too large.");
        var text = await File.ReadAllTextAsync(path);
        using var document = JsonDocument.Parse(text);
        List<Profile> imported;
        if (document.RootElement.TryGetProperty("Profiles", out _))
            imported = JsonSerializer.Deserialize<ProfileBundle>(text, _json)?.Profiles ?? [];
        else
            imported = JsonSerializer.Deserialize<Profile>(text, _json) is { } profile ? [profile] : [];
        if (imported.Count is 0 or > 250) throw new InvalidDataException("The file does not contain valid BigKev profiles.");
        foreach (var profile in imported)
        {
            profile.Id = Guid.NewGuid();
            profile.Name = string.IsNullOrWhiteSpace(profile.Name) ? "Imported profile" : profile.Name.Trim();
            profile.Mappings ??= [];
            profile.Macros ??= [];
            await SaveAsync(profile);
        }
        return imported;
    }
}

public sealed class ProfileBundle
{
    public string Format { get; set; } = "BigKev profiles";
    public int Version { get; set; } = 1;
    public List<Profile> Profiles { get; set; } = [];
}

public sealed class MacroBundle
{
    public string Format { get; set; } = "BigKev macro";
    public int Version { get; set; } = 1;
    public MacroDefinition? Macro { get; set; }
}

