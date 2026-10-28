using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using PKHeX.Application.Abstractions;
using PKHeX.Application.Services;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>One entry in a Bank archive's <c>manifest.json</c>.</summary>
public sealed record BankArchiveManifestEntry(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>
/// A Bank archive: a loose folder of real <c>.pkN</c>/<c>.pbN</c>/etc. files (one per Pokémon, named
/// like the existing OS export convention) plus a schema-versioned <c>manifest.json</c> beside them.
/// This is the Bank's own interchange format for backing up/sharing a Bank's contents outside the
/// app — it works even without the manifest, since import re-scans the folder for recognized entity
/// files by extension.
/// </summary>
public sealed record BankArchiveManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("entries")] IReadOnlyList<BankArchiveManifestEntry> Entries);

public readonly record struct BankArchiveExportResult(int Exported, int Skipped, IReadOnlyList<string> Errors);
public readonly record struct BankArchiveImportResult(int Imported, int Duplicates, int Skipped, IReadOnlyList<string> Errors);

/// <summary>
/// Exports every current Bank entry as a loose folder of native entity files + a manifest. Reuses
/// the same decrypted-bytes convention as <see cref="ExportEntityToFileUseCase"/> (the Bank already
/// stores exactly that), just batched across the whole vault.
/// </summary>
public sealed class ExportBankArchiveUseCase
{
    private static readonly JsonSerializerOptions ManifestOptions = new() { WriteIndented = true };

    public BankArchiveExportResult Execute(IBankService bank, string folder)
    {
        Directory.CreateDirectory(folder);
        var manifestEntries = new List<BankArchiveManifestEntry>();
        var errors = new List<string>();
        var exported = 0;
        var skipped = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in bank.GetAll())
        {
            var raw = bank.GetRawData(entry.Id);
            if (raw is null)
            {
                skipped++;
                errors.Add($"{entry.Id}: raw data missing on disk, skipped.");
                continue;
            }

            var ext = string.IsNullOrEmpty(entry.Info.Format) ? "pkm" : entry.Info.Format;
            var baseName = PathUtil.CleanFileName(
                $"{entry.Info.Species:0000} - {entry.Info.Nickname} - {entry.Id:N}"[..Math.Min(120, $"{entry.Info.Species:0000} - {entry.Info.Nickname} - {entry.Id:N}".Length)]);
            var fileName = $"{baseName}.{ext}";
            var dedupe = 1;
            while (!usedNames.Add(fileName))
                fileName = $"{baseName} ({dedupe++}).{ext}";

            var path = Path.Combine(folder, fileName);
            try
            {
                File.WriteAllBytes(path, raw);
                manifestEntries.Add(new BankArchiveManifestEntry(entry.Id, fileName, Convert.ToHexString(SHA256.HashData(raw))));
                exported++;
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"{entry.Id}: {ex.Message}");
            }
        }

        try
        {
            var manifest = new BankArchiveManifest(1, manifestEntries);
            var json = JsonSerializer.Serialize(manifest, ManifestOptions);
            File.WriteAllText(Path.Combine(folder, "manifest.json"), json);
        }
        catch (Exception ex)
        {
            errors.Add($"manifest.json: {ex.Message}");
        }

        return new BankArchiveExportResult(exported, skipped, errors);
    }
}

/// <summary>
/// Imports a loose folder of entity files into the Bank. Idempotent: any file whose SHA-256 already
/// matches an existing Bank entry is treated as a duplicate and skipped rather than re-added, so
/// re-importing the same archive (or one that overlaps the current Bank) is safe to repeat. Works
/// even on a folder with no manifest — every recognized Pokémon entity file is considered.
/// </summary>
public sealed class ImportBankArchiveUseCase
{
    /// <param name="bank">Bank to import into.</param>
    /// <param name="folder">Folder of loose entity files (with or without a manifest).</param>
    /// <param name="sourceLabel">Provenance label recorded on every imported entry (shown in the Bank's tooltip).</param>
    public BankArchiveImportResult Execute(IBankService bank, string folder, string sourceLabel = "Archive import")
    {
        if (!Directory.Exists(folder))
            return new BankArchiveImportResult(0, 0, 0, ["Folder not found."]);

        var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in bank.GetAll())
        {
            var raw = bank.GetRawData(entry.Id);
            if (raw is not null)
                existingHashes.Add(Convert.ToHexString(SHA256.HashData(raw)));
        }

        var errors = new List<string>();
        var imported = 0;
        var duplicates = 0;
        var skipped = 0;

        foreach (var file in Directory.EnumerateFiles(folder).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            byte[] data;
            try
            {
                data = File.ReadAllBytes(file);
            }
            catch (Exception ex)
            {
                skipped++;
                errors.Add($"{name}: {ex.Message}");
                continue;
            }

            object? parsed;
            try
            {
                parsed = FileUtil.GetSupportedFile(data, Path.GetExtension(file));
            }
            catch
            {
                parsed = null;
            }

            if (parsed is not PKM pk || pk.Species == 0)
            {
                // Not a recognized Pokémon entity file (e.g. a stray readme) — silently skip.
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(data));
            if (!existingHashes.Add(hash))
            {
                duplicates++;
                continue;
            }

            var raw = BankEntryInfoFactory.GetRawData(pk);
            var info = BankEntryInfoFactory.CreateInfo(pk, sourceLabel);
            var result = bank.Add(raw, info);
            if (result.Success)
                imported++;
            else
            {
                skipped++;
                errors.Add($"{name}: {result.Error}");
            }
        }

        return new BankArchiveImportResult(imported, duplicates, skipped, errors);
    }
}
