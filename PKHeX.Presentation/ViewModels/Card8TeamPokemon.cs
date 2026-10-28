using System.Buffers.Binary;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Application.Abstractions;
using PKHeX.Core;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

public partial class Card8TeamPokemon : ViewModelBase
{
    private readonly SAV8SWSH _save;
    private readonly bool _title;
    private readonly ISpriteRenderer? _sprites;
    private readonly Func<bool> _canEdit;
    private readonly Action _changed;
    private readonly byte[] _original;
    private int Offset => _title ? Index * TitleScreen8Poke.SIZE : TrainerCard8.GetPokeOffset(Index);
    private Span<byte> Data => (_title ? _save.TitleScreen.Data : _save.TrainerCard.Data).Slice(Offset, _title ? TitleScreen8Poke.SIZE : TrainerCard8Poke.SIZE);
    public int Index { get; }
    public string SlotName => LocalizedStrings.Instance.Format("Card8Flow_Slot", Index + 1);
    public bool HasError => Species is < 0 or > ushort.MaxValue || Form is < 0 or > byte.MaxValue || Gender is < 0 or > byte.MaxValue;
    public string Error => HasError ? LocalizedStrings.Instance["Card8Flow_TeamError"] : string.Empty;
    public string UnknownValues => _title
        ? string.Join(" / ", new[] { _save.TitleScreen.ViewPoke(Index).Unknown18, _save.TitleScreen.ViewPoke(Index).Unknown1C, _save.TitleScreen.ViewPoke(Index).Unknown20, _save.TitleScreen.ViewPoke(Index).Unknown24 }.Select(value => value.ToString("X8")))
        : _save.TrainerCard.ViewPoke(Index).Unknown.ToString("X8");
    [ObservableProperty] private int _species;
    [ObservableProperty] private int _form;
    [ObservableProperty] private int _gender;
    [ObservableProperty] private bool _isShiny;
    [ObservableProperty] private uint _encryptionConstant;
    [ObservableProperty] private int _formArgument;
    public IReadOnlyList<ComboItem> SpeciesOptions { get; private set; } = [];
    public IReadOnlyList<ComboItem> GenderOptions { get; private set; } = [];
    public byte[]? Sprite { get; private set; }
    public string Summary { get; private set; } = string.Empty;
    public string FormName { get; private set; } = string.Empty;
    public bool HasFormName => !string.IsNullOrWhiteSpace(FormName);

    public Card8TeamPokemon(SAV8SWSH save, int index, bool title, ISpriteRenderer? sprites, Func<bool> canEdit, Action changed)
    {
        _save = save; Index = index; _title = title; _sprites = sprites; _canEdit = canEdit; _changed = changed;
        _original = Data.ToArray();
        _species = BinaryPrimitives.ReadUInt16LittleEndian(Data); _form = Data[4]; _gender = Data[8]; _isShiny = Data[0xC] != 0;
        _encryptionConstant = BinaryPrimitives.ReadUInt32LittleEndian(Data[0x10..]);
        _formArgument = BinaryPrimitives.ReadInt32LittleEndian(Data[(title ? 0x14 : 0x18)..]);
        RefreshLanguage();
    }
    partial void OnSpeciesChanged(int value)
    {
        if (_canEdit() && value is >= 0 and <= ushort.MaxValue) BinaryPrimitives.WriteUInt16LittleEndian(Data, (ushort)value);
        RefreshPreview(); _changed();
    }
    partial void OnFormChanged(int value)
    {
        if (_canEdit() && value is >= 0 and <= byte.MaxValue) Data[4] = (byte)value;
        RefreshPreview(); _changed();
    }
    partial void OnGenderChanged(int value)
    {
        if (_canEdit() && value is >= 0 and <= byte.MaxValue) Data[8] = (byte)value;
        RefreshPreview(); _changed();
    }
    partial void OnIsShinyChanged(bool value)
    {
        if (_canEdit()) Data[0xC] = value == (_original[0xC] != 0) ? _original[0xC] : value ? (byte)1 : (byte)0;
        RefreshPreview(); _changed();
    }
    partial void OnEncryptionConstantChanged(uint value)
    {
        if (_canEdit()) BinaryPrimitives.WriteUInt32LittleEndian(Data[0x10..], value);
        _changed();
    }
    partial void OnFormArgumentChanged(int value)
    {
        if (_canEdit()) BinaryPrimitives.WriteInt32LittleEndian(Data[(_title ? 0x14 : 0x18)..], value);
        RefreshPreview(); _changed();
    }
    public void RefreshLanguage()
    {
        var names = GameInfo.Strings.Species;
        var options = names.Take(_save.MaxSpeciesID + 1).Select((name, id) => new ComboItem(id == 0 ? LocalizedStrings.Instance["Card8Flow_EmptySlot"] : name, id)).ToList();
        if (Species > _save.MaxSpeciesID) options.Add(new(LocalizedStrings.Instance.Format("Card8Flow_UnknownSpecies", Species), Species));
        SpeciesOptions = options;
        var genders = new List<ComboItem>
        {
            new(LocalizedStrings.Instance["Card8Flow_Male"], 0), new(LocalizedStrings.Instance["Card8Flow_Female"], 1), new(LocalizedStrings.Instance["Card8Flow_Genderless"], 2),
        };
        if (Gender > 2) genders.Add(new(LocalizedStrings.Instance.Format("Card8Flow_UnknownValue", Gender), Gender));
        GenderOptions = genders;
        OnPropertyChanged(nameof(SpeciesOptions)); OnPropertyChanged(nameof(GenderOptions)); OnPropertyChanged(nameof(SlotName));
        RefreshPreview();
    }
    private void RefreshPreview()
    {
        var loc = LocalizedStrings.Instance;
        string speciesName = (uint)Species < GameInfo.Strings.Species.Count ? GameInfo.Strings.Species[Species] : loc.Format("Card8Flow_UnknownSpecies", Species);
        try { FormName = FormConverter.GetStringFromForm((ushort)Species, (byte)Form, GameInfo.Strings, GameInfo.GenderSymbolASCII, EntityContext.Gen8); }
        catch (ArgumentOutOfRangeException) { FormName = Form.ToString(); }
        catch (IndexOutOfRangeException) { FormName = Form.ToString(); }
        var genderName = GenderOptions.FirstOrDefault(option => option.Value == Gender)?.Text ?? loc.Format("Card8Flow_UnknownValue", Gender);
        Summary = Species == 0 ? loc["Card8Flow_EmptySlot"] : loc.Format("Card8Flow_TeamSummary", speciesName, Form, genderName, IsShiny ? loc["Card8Flow_Shiny"] : loc["Card8Flow_NotShiny"], FormArgument);
        Sprite = null;
        if (!HasError && _sprites is not null)
        {
            try { Sprite = Species == 0 ? _sprites.GetEmptySlot() : _sprites.GetSprite((ushort)Species, (byte)Form, (byte)Gender, (uint)Math.Max(0, FormArgument), IsShiny, EntityContext.Gen8); }
            catch (ArgumentOutOfRangeException) { }
            catch (IndexOutOfRangeException) { }
        }
        OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(Sprite)); OnPropertyChanged(nameof(FormName)); OnPropertyChanged(nameof(HasFormName)); OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(HasError));
    }
}
