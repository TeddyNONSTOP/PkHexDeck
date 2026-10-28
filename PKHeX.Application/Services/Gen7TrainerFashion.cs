using PKHeX.Core;

namespace PKHeX.Application.Services;

public enum Gen7FashionMode { Default, Legal, All }

public static class Gen7TrainerFashion
{
    public static void Apply(SAV7 save, int gender, Gen7FashionMode mode)
    {
        if (gender is not (0 or 1) || !Enum.IsDefined(mode)) throw new ArgumentException("Unsupported fashion scope.");
        byte[]? payload = null;
        if (mode != Gen7FashionMode.Default)
        {
            string name = $"PKHeX.Application.Resources.Gen7Fashion.fashion_{(gender == 0 ? "m" : "f")}_{(save is SAV7USUM ? "uu" : "sm")}{(mode == Gen7FashionMode.All ? "_illegal" : string.Empty)}.bin";
            using var stream = typeof(Gen7TrainerFashion).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing upstream fashion payload.");
            using var memory = new MemoryStream(); stream.CopyTo(memory); payload = memory.ToArray();
        }
        save.Fashion.Clear();
        if (payload is not null) save.Fashion.ImportPayload(payload);
        else
        {
            byte original = save.Gender;
            try { save.Gender = (byte)gender; save.Fashion.Reset(); }
            finally { save.Gender = original; }
        }
    }
}
