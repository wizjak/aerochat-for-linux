using Avalonia.Threading;
using Aerochat.Linux.ViewModels;
using Aerochat.Linux.Views;

namespace Aerochat.Linux.Services;

/// <summary>
/// Keeps the WLM toast windows stacked above each other in the bottom-right corner and
/// routes their click back to the chat window that owns the message.
/// </summary>
public static class NotificationService
{
    private static readonly List<NotificationWindow> Active = new();

    public static void Show(NotificationViewModel viewModel, Action? onActivate = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Show(viewModel, onActivate));
            return;
        }

        _ = viewModel.LoadAvatarAsync();
        SoundService.PlayNotification();

        var window = new NotificationWindow(viewModel, onActivate);
        Active.Add(window);

        window.Closed += (_, _) =>
        {
            Active.Remove(window);
            Reposition();
        };

        window.Show();
    }

    public static void CloseAll()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(CloseAll);
            return;
        }

        foreach (var window in Active.ToList())
        {
            try
            {
                window.Close();
            }
            catch
            {
                // The notification may already be closing.
            }
        }

        Active.Clear();
    }

    internal static int GetSlot(NotificationWindow window)
    {
        var slot = 0;
        foreach (var other in Active)
        {
            if (ReferenceEquals(other, window))
                break;

            if (other.IsVisible)
                slot++;
        }

        return slot;
    }

    private static void Reposition()
    {
        var slot = 0;
        foreach (var window in Active.ToList())
        {
            if (!window.IsVisible)
                continue;

            window.MoveToSlot(slot++);
        }
    }
}
