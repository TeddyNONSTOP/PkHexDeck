using System;
using System.Collections.Generic;
using System.IO;
using PKHeX.Application.Abstractions;
using PKHeX.Core;

namespace PKHeX.Application.UseCases;

/// <summary>Outcome of a bulk slot export: how many decrypted entity files were written vs. skipped.</summary>
public readonly record struct BulkExportResult(int Exported, int Skipped);

/// <summary>
/// Exports multiple marked box/party slots to individual decrypted entity files (e.g. ".pk9") in a
/// folder, reusing <see cref="ExportEntityToFileUseCase"/> per slot — the same naming convention as
/// Core's box dump (<see cref="DumpBoxesUseCase"/>). A slot that is empty, or whose file cannot be
/// written, is skipped and counted rather than aborting the whole export. Never overwrites a file:
/// a name already used in this batch or already present in the folder gets a " (2)", " (3)", ...
/// suffix instead.
/// </summary>
public sealed class BulkExportSlotsUseCase
{
    public BulkExportResult Execute(SaveFile sav, IReadOnlyList<SlotLocation> locations, string folder)
    {
        var exporter = new ExportEntityToFileUseCase();
        var exported = 0;
        var skipped = 0;
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var location in locations)
        {
            PKM pk;
            try
            {
                pk = location.IsParty ? sav.GetPartySlotAtIndex(location.Slot) : sav.GetBoxSlotAtIndex(location.Box, location.Slot);
            }
            catch
            {
                skipped++;
                continue;
            }

            var file = exporter.Execute(pk);
            if (file is not { } entity)
            {
                skipped++;
                continue;
            }

            var fileName = MakeUniqueFileName(folder, entity.FileName, usedNames);
            try
            {
                using var stream = new FileStream(Path.Combine(folder, fileName), FileMode.CreateNew, FileAccess.Write);
                stream.Write(entity.Data);
                exported++;
            }
            catch
            {
                skipped++;
            }
        }

        return new BulkExportResult(exported, skipped);
    }

    private static string MakeUniqueFileName(string folder, string fileName, HashSet<string> used)
    {
        if (IsFree(folder, fileName, used))
            return fileName;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; ; i++)
        {
            var candidate = $"{name} ({i}){ext}";
            if (IsFree(folder, candidate, used))
                return candidate;
        }
    }

    private static bool IsFree(string folder, string fileName, HashSet<string> used)
        => !File.Exists(Path.Combine(folder, fileName)) && used.Add(fileName);
}
