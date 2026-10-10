using Atrium.Core.Storage;
using Dapper;
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
    private readonly SemaphoreSlim _mutationLock = new(1, 1);

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
            return ParsePreference(key?.GetValue(Path.GetFullPath(exePath)) as string);
        }
        catch { }

        return null;
    }

    private static GpuPreference? ParsePreference(string? value)
    {
        foreach (var part in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2 && pair[0].Equals("GpuPreference", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(pair[1], out var number) && Enum.IsDefined(typeof(GpuPreference), number))
                return (GpuPreference)number;
        }
        return null;
    }

    public async Task<bool> SetHighPerformanceAsync(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath)) return false;
        var normalizedPath = Path.GetFullPath(exePath);
        await _mutationLock.WaitAsync();
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(_registryKeyPath);
            var existingValue = key.GetValue(normalizedPath) as string;
            // Do not take ownership of a preference already configured in Windows.
            if (ParsePreference(existingValue) == GpuPreference.HighPerformance) return false;

            using var conn = _database.CreateConnection();
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"
                INSERT INTO GpuPreferences (ExePath, PreviousValue, AppliedAt)
                VALUES (@Path, @Prev, @Now)
                ON CONFLICT(ExePath) DO NOTHING;",
                new { Path = normalizedPath, Prev = existingValue, Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });

            var otherSettings = (existingValue ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !part.Split('=', 2)[0].Trim().Equals("GpuPreference", StringComparison.OrdinalIgnoreCase));
            key.SetValue(normalizedPath, string.Join(';', otherSettings.Append("GpuPreference=2")) + ";");
            return true;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<bool> RevertAsync(string exePath)
    {
        var normalizedPath = Path.GetFullPath(exePath);
        await _mutationLock.WaitAsync();
        try
        {
            using var conn = _database.CreateConnection();
            await conn.OpenAsync();
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT PreviousValue FROM GpuPreferences WHERE ExePath = @Path",
                new { Path = normalizedPath });
            if (row == null) return false;

            string? previousValue = (string?)row.PreviousValue;
            using var key = Registry.CurrentUser.CreateSubKey(_registryKeyPath);
            if (previousValue != null)
                key.SetValue(normalizedPath, previousValue);
            else
                key.DeleteValue(normalizedPath, false);

            // A failed registry restore must leave the backup available for retry.
            await conn.ExecuteAsync("DELETE FROM GpuPreferences WHERE ExePath = @Path", new { Path = normalizedPath });
            return true;
        }
        finally
        {
            _mutationLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetModifiedExePathsAsync()
    {
        using var conn = _database.CreateConnection();
        await conn.OpenAsync();
        var paths = await conn.QueryAsync<string>("SELECT ExePath FROM GpuPreferences");
        return paths.ToList();
    }
}
