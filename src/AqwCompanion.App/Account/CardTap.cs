using System.Windows.Input;

namespace AqwCompanion.App.Account;

/// <summary>Shared by the card views: a tap on a card runs its open command, guarded by CanExecute.</summary>
internal static class CardTap
{
    // Executing a relay command ignores its CanExecute, so a card without data must be checked here.
    public static void Open(ICommand? command)
    {
        if (command?.CanExecute(null) == true)
        {
            command.Execute(null);
        }
    }
}
