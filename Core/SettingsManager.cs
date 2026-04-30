using System;
using System.IO;
using System.Text.Json;
using Ermine.Models;

namespace Ermine.Core;

public static class SettingsManager
{
    private static readonly string FolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ermine");
    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    public static AppSettings Load()
    {
        if (!File.Exists(FilePath)) return new AppSettings();

        try
        {
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch 
        { 
            return new AppSettings(); 
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(FolderPath);
        
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(settings, options);
        
        File.WriteAllText(FilePath, json);
    }
}