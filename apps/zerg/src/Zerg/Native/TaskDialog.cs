using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Zerg.Native;

/// <summary>
/// Windows' own message dialog (TaskDialogIndirect, common controls v6, which
/// app.manifest asks for): a headline, a sentence, and the technical details
/// folded away under "See details" instead of filling the screen.
/// </summary>
static class TaskDialog
{
    public enum Icon { Information, Warning, Error }

    /// <summary>Shows the dialog and waits. Owned by <paramref name="owner"/> when
    /// it has a window, so it opens over it rather than wherever Windows likes.</summary>
    public static void Show(string headline, string content, Icon icon,
                            string? details = null, string? footer = null, Window? owner = null)
    {
        var config = new Config
        {
            cbSize = (uint)Marshal.SizeOf<Config>(),
            hwndParent = owner is null ? 0 : new WindowInteropHelper(owner).Handle,
            dwFlags = AllowCancel | ExpandFooterArea | (owner is null ? 0 : PositionRelativeToWindow),
            dwCommonButtons = OkButton,
            pszWindowTitle = AppInfo.Name,
            hMainIcon = icon switch { Icon.Error => ErrorIcon, Icon.Warning => WarningIcon, _ => InformationIcon },
            pszMainInstruction = headline,
            pszContent = content,
            pszExpandedInformation = details,
            pszCollapsedControlText = details is null ? null : "See details",
            pszExpandedControlText = details is null ? null : "Hide details",
            pszFooter = footer,
        };
        try
        {
            int hr = TaskDialogIndirect(ref config, out _, out _, out _);
            if (hr >= 0) return;
        }
        // Only if the manifest didn't take (common controls v5 has no task
        // dialog): a plain message box still tells the player what happened.
        catch (EntryPointNotFoundException) { }

        var text = headline + "\n\n" + content + (footer is null ? "" : "\n\n" + footer);
        var image = icon switch { Icon.Error => MessageBoxImage.Error, Icon.Warning => MessageBoxImage.Warning, _ => MessageBoxImage.Information };
        if (owner is null) MessageBox.Show(text, AppInfo.Name, MessageBoxButton.OK, image);
        else MessageBox.Show(owner, text, AppInfo.Name, MessageBoxButton.OK, image);
    }

    // ------------------------------------------------------------- interop

    const uint AllowCancel = 0x0008;               // TDF_ALLOW_DIALOG_CANCELLATION: Esc and the X close it
    const uint ExpandFooterArea = 0x0040;          // TDF_EXPAND_FOOTER_AREA: details open below, not mid-dialog
    const uint PositionRelativeToWindow = 0x1000;  // TDF_POSITION_RELATIVE_TO_WINDOW
    const uint OkButton = 0x0001;                  // TDCBF_OK_BUTTON

    // MAKEINTRESOURCEW(-1/-2/-3): TD_WARNING_ICON, TD_ERROR_ICON, TD_INFORMATION_ICON.
    const nint WarningIcon = 0xFFFF;
    const nint ErrorIcon = 0xFFFE;
    const nint InformationIcon = 0xFFFD;

    /// <summary>TASKDIALOGCONFIG. commctrl.h declares it byte-packed (pshpack1),
    /// which matters on x64: 160 bytes, not the 168 natural alignment gives.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Unicode)]
    struct Config
    {
        public uint cbSize;
        public nint hwndParent;
        public nint hInstance;
        public uint dwFlags;
        public uint dwCommonButtons;
        public string? pszWindowTitle;
        public nint hMainIcon;
        public string? pszMainInstruction;
        public string? pszContent;
        public uint cButtons;
        public nint pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public nint pRadioButtons;
        public int nDefaultRadioButton;
        public string? pszVerificationText;
        public string? pszExpandedInformation;
        public string? pszExpandedControlText;
        public string? pszCollapsedControlText;
        public nint hFooterIcon;
        public string? pszFooter;
        public nint pfCallback;
        public nint lpCallbackData;
        public uint cxWidth;
    }

    [DllImport("comctl32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int TaskDialogIndirect(ref Config config, out int button, out int radioButton,
                                         [MarshalAs(UnmanagedType.Bool)] out bool verificationChecked);
}
