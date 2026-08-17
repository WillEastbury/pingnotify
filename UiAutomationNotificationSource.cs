using System.Windows.Automation;

namespace PingNotify;

internal static class UiAutomationNotificationSource
{
    public static NotificationMetadata? GetSlackMetadata()
    {
        try
        {
            var condition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.ListItem);
            var entries = AutomationElement.RootElement.FindAll(TreeScope.Descendants, condition);
            var count = 0L;
            foreach (AutomationElement entry in entries)
            {
                var name = entry.Current.Name;
                var isSlack = name.Contains("Slack", StringComparison.OrdinalIgnoreCase);
                name = string.Empty;
                if (isSlack)
                    count++;
            }

            return count == 0
                ? null
                : new NotificationMetadata(count, DateTimeOffset.UtcNow);
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
    }
}
