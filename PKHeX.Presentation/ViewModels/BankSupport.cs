using PKHeX.Presentation.Localization;

namespace PKHeX.Presentation.ViewModels;

/// <summary>
/// The open save the Bank withdraws into. The Bank window lives for the whole app session while
/// the open save comes and goes, so the Bank never holds a <c>SaveFile</c> itself — it asks this
/// target (implemented by <see cref="MainWindowViewModel"/>, which owns the save, its slot-write
/// path and undo history) to perform the withdrawal.
/// </summary>
public interface IBankSaveTarget
{
    /// <summary>Localized description of where a withdrawal would go (game, trainer and box), or <see langword="null"/> when no save is open.</summary>
    string? WithdrawTargetDescription { get; }

    /// <summary>True when a save that can receive Pokémon in its boxes is open.</summary>
    bool CanReceiveWithdrawal { get; }

    /// <summary>Raised when the open save or its viewed box changes.</summary>
    event Action? TargetChanged;

    /// <summary>Withdraws one Bank entry into the open save's currently viewed box (hold until saved).</summary>
    Task WithdrawAsync(Guid entryId);

    /// <summary>
    /// Withdraws several Bank entries in one confirmed, undoable action, filling empty box slots from
    /// the viewed box onward (hold until saved). Returns the ids that were withdrawn.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> WithdrawManyAsync(IReadOnlyList<Guid> entryIds);
}

/// <summary>Localized wording for Bank failures, shared by the Bank window and the main window's deposit/withdraw paths.</summary>
public static class BankMessages
{
    public static string Describe(BankErrorKind kind, string? detail)
    {
        var loc = LocalizedStrings.Instance;
        return kind switch
        {
            BankErrorKind.ReadOnly => loc["Bank_Error_ReadOnly"],
            BankErrorKind.NotFound => loc["Bank_Error_NotFound"],
            BankErrorKind.InvalidSlot => loc["Bank_Error_InvalidSlot"],
            BankErrorKind.Occupied => loc["Bank_Error_Occupied"],
            BankErrorKind.PendingWithdrawal => loc["Bank_Error_Pending"],
            BankErrorKind.PendingDeposit => loc["Bank_Error_PendingDeposit"],
            BankErrorKind.Busy => loc["Bank_Error_Busy"],
            BankErrorKind.InvalidData => loc["Bank_Error_InvalidData"],
            _ => string.IsNullOrWhiteSpace(detail)
                ? loc["Bank_Error_Io"]
                : loc.Format("Bank_Error_IoDetail", detail),
        };
    }

    public static string Describe(BankOperationResult result) => Describe(result.Kind, result.Error);

    public static string Describe(BankAddResult result) => Describe(result.Kind, result.Error);
}
