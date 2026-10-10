using Atrium.Core.Storage;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace Atrium.Core.System;

public enum GpuPreference
{
    Default = 0,
    PowerSaving = 1,
    HighPerformance = 2
}

public sealed class GpuPreferenceService
{
    private const string DefaultRegistryKeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";
    private readonly string _registryKeyPath;
    private readonly LibraryDatabase _database;

    public GpuPreferenceService(LibraryDatabase database, string? customRegistryKeyPath = null)
    {
        _database = database;
        _registryKeyPath = customRegistryKeyPath ?? DefaultRegistryKeyPath;
    }

    public GpuPreference? GetPreference(string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath);
            var val = key?.GetValue(exePath) as string;
            if (string.IsNullOrEmpty(val)) return null;

            if (val.Contains("GpuPreference=2")) return GpuPreference.HighPerformance;
            if (val.Contains("GpuPreference=1")) return GpuPreference.PowerSaving;
            if (val.Contains("GpuPreference=0")) return GpuPreference.Default;
        }
        catch { }

        return null;
    }

    public async Task SetHighPerformanceAsync(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(exePath);

        // 1. Check existing registry value
        string? existingValue = null;
        using (var key = Registry.CurrentUser.OpenSubKey(_registryKeyPath))
        {
            existingValue = key?.GetValue(normalizedPath) as string;
        }

        // 2. Backup to database if not already backed up
        using (var conn = new SqliteConnection($"Data Source={_database.DatabasePath}"))
        {
            await conn.OpenAsync();
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM GpuPreferences WHERE ExePath = @Path",
                new { Path = normalizedPath });

            if (exists == 0)
            {
                await conn.ExecuteAsync(@"
                    INSERT INTO GpuPreferences (ExePath, PreviousValue, AppliedAt)
                    VALUES (@Path, @Prev, @Now)",
                    new
                    {
                        Path = normalizedPath,
                        Prev = existingValue,
                        Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    });
            }
        }

        // 3. Write HighPerformance setting (GpuPreference=2;)
        using (var writeKey = Registry.CurrentUser.CreateSubKey(_registryKeyPath))
        {
            writeKey.SetValue(normalizedPath, "GpuPreference=2;");
        }
    }

    public async Task RevertAsync(string exePath)
    {
        var normalizedPath = Path.GetFullPath(exePath);

        string? previousValue = null;
        bool hasBackup = false;

        using (var conn = new SqliteConnection($"Data Source={_database.DatabasePath}"))
        {
            await conn.OpenAsync();
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT PreviousValue FROM GpuPreferences WHERE ExePath = @Path",
                new { Path = normalizedPath });

            if (row != null)
            {
                hasBackup = true;
                previousValue = (string?)row.PreviousValue;
                await conn.ExecuteAsync("DELETE FROM GpuPreferences WHERE ExePath = @Path", new { Path = normalizedPath });
            }
        }

        // Restore or delete registry entry
        using var writeKey = Registry.CurrentUser.CreateSubKey(_registryKeyPath);
        if (hasBackup && !string.IsNullOrEmpty(previousValue))
        {
            writeKey.SetValue(normalizedPath, previousValue);
        }
        else
        {
            writeKey.DeleteValue(normalizedPath, false);
        }
    }

    public async Task<IReadOnlyList<string>> GetModifiedExePathsAsync()
    {
        using var conn = new SqliteConnection($"Data Source={_database.DatabasePath}");
        await conn.OpenAsync();
        var paths = await conn.QueryAsync<string>("SELECT ExePath FROM GpuPreferences");
        return paths.ToList();
    }
}
