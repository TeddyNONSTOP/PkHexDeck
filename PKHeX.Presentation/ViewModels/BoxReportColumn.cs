using System;
using CommunityToolkit.Mvvm.ComponentModel;
using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>Framework-free column choices and value projection for the report.</summary>
public sealed partial class BoxReportColumn : ObservableObject
{
    public string Id { get; }
    public string SortMember => Id is "HP" or "ATK" or "DEF" or "SPA" or "SPD" or "SPE" ? "Numeric" + Id : Id;
    public string Header => LocalizedStrings.Instance[_headerKey];
    private readonly string _headerKey;
    public double DefaultWidth { get; }
    public bool IsIdentity => Id is "Position" or "Species";
    public bool CanHide => !IsIdentity;
    public bool DefaultVisible { get; }
    public void RefreshHeader() => OnPropertyChanged(nameof(Header));
    public Func<BoxReportRow, object?> GetValue { get; }
    [ObservableProperty] private bool _isVisible;

    public BoxReportColumn(string id, string headerKey, double width, bool visible, Func<BoxReportRow, object?> getValue)
    {
        Id = id;
        _headerKey = headerKey;
        DefaultWidth = width;
        DefaultVisible = visible;
        _isVisible = visible;
        GetValue = getValue;
    }

    public static BoxReportColumn[] CreateDefaults() =>
    [
        new("Position", "BoxReportView_PositionHeader", 110, true, r => r.Position),
        new("Species", "BoxReportView_SpeciesHeader", 160, true, r => r.Species),
        new("Nickname", "BoxReportView_NicknameHeader", 160, true, r => r.Nickname),
        new("Level", "BoxReportView_LvlHeader", 90, true, r => r.Level),
        new("Nature", "BoxReportView_NatureHeader", 96, false, r => r.Nature),
        new("Gender", "BoxReportView_GenderHeader", 90, false, r => r.Gender),
        new("ESV", "BoxReportView_EsvHeader", 90, false, r => r.ESV),
        new("HP_Type", "BoxReportView_HpTypeHeader", 90, false, r => r.HP_Type),
        new("Ability", "BoxReportView_AbilityHeader", 160, true, r => r.Ability),
        new("HeldItem", "BoxReportView_ItemHeader", 160, true, r => r.HeldItem),
        new("Ball", "BoxReportView_BallHeader", 90, false, r => r.Ball),
        new("Move1", "BoxReportView_Move1Header", 160, false, r => r.Move1),
        new("Move2", "BoxReportView_Move2Header", 160, false, r => r.Move2),
        new("Move3", "BoxReportView_Move3Header", 160, false, r => r.Move3),
        new("Move4", "BoxReportView_Move4Header", 160, false, r => r.Move4),
        new("HP", "BoxReportView_HpHeader", 90, false, r => r.HP),
        new("ATK", "BoxReportView_AtkHeader", 90, false, r => r.ATK),
        new("DEF", "BoxReportView_DefHeader", 90, false, r => r.DEF),
        new("SPA", "BoxReportView_SpaHeader", 90, false, r => r.SPA),
        new("SPD", "BoxReportView_SpdHeader", 90, false, r => r.SPD),
        new("SPE", "BoxReportView_SpeHeader", 90, false, r => r.SPE),
        new("IVTotal", "BoxReportView_IvTotalHeader", 90, false, r => r.IVTotal),
        new("EVTotal", "BoxReportView_EvTotalHeader", 90, false, r => r.EVTotal),
        new("MetLoc", "BoxReportView_MetLocationHeader", 160, false, r => r.MetLoc),
        new("Version", "BoxReportView_OriginHeader", 90, false, r => r.Version),
        new("OT", "BoxReportView_OtHeader", 160, false, r => r.OT),
        new("IsShiny", "BoxReportView_ShinyHeader", 90, false, r => r.IsShiny),
        new("Legal", "BoxReportView_LegalHeader", 90, true, r => r.Legal),
        new("Checksum", "BoxReportView_ChecksumHeader", 90, false, r => r.Checksum),
    ];
}
