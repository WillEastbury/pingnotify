using System.Windows.Automation;
using System.IO;

namespace PingNotify;

internal static class UiAutomationNotificationSource
{
    private static bool _unavailable;

    public static IReadOnlyDictionary<string, NotificationMetadata> GetKnownMetadata()
    {
        if (_unavailable)
            return new Dictionary<string, NotificationMetadata>();
        try
        {
            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.ListItem);
            var entries = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
            var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (AutomationElement entry in entries)
            {
                var name = entry.Current.Name;
                var app = name.Contains("Slack", StringComparison.OrdinalIgnoreCase)
                    ? "Slack"
                    : name.Contains("BurntToast", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("PowerShell", StringComparison.OrdinalIgnoreCase)
                        ? "Scout"
                        : null;
                name = string.Empty;
                if (app is not null)
                    counts[app] = counts.TryGetValue(app, out var count) ? count + 1 : 1;
            }

            return counts.ToDictionary(
                item => item.Key,
                item => new NotificationMetadata(item.Value, DateTimeOffset.UtcNow));
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
        catch (ElementNotEnabledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (TypeInitializationException)
        {
            _unavailable = true;
            return null;
        }
        catch (FileNotFoundException)
        {
            _unavailable = true;
            return null;
        }
        catch (FileLoadException)
        {
            _unavailable = true;
            return null;
        }
    }
}
