using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Library;
using Wox.Plugin;
using Wox.Plugin.Logger;
namespace Community.PowerToys.Run.Plugin.KeePassXC;


public class Main : IPlugin, IContextMenu, ISettingProvider, IDisposable
{
    public static string PluginID => "181EC9C9CE7E777243213D37E4ED8D7D";
    public string Name => "Search KeePassXC";
    public string Description => "Search credentials in KeePassXC database";
    
    public string? KeePassXcPath { get; set; }
    public string? DatabasePath { get; set; }
    public bool UseMasterPassword { get; set; }
    public string? MasterPassword { get; set; }
    public bool AutoLogin { get; set; }

    /// <summary>
    /// AdditionalOptions describes options which can be set from the settings menu. 
    /// </summary>
    public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
    {
        new()
        {
            Key = nameof(KeePassXcPath),
            DisplayLabel = "KeePass CLI Path",
            DisplayDescription = "Path to the KeePassXC CLI executable",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = KeePassXcPath,
        },
        new()
        {
            Key = nameof(DatabasePath),
            DisplayLabel = "Database Path",
            DisplayDescription = "Path to the KeePass database file (.kdbx)",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = DatabasePath,
        },
        new()
        {
            Key = nameof(UseMasterPassword),
            DisplayLabel = "Use Master Password",
            DisplayDescription = "Enable to enter master password manually",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
            Value = UseMasterPassword,
        },
        new()
        {
            Key = nameof(MasterPassword),
            DisplayLabel = "Master Password",
            DisplayDescription = "Master password for the database (leave empty for prompt)",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = MasterPassword,
        },
        new()
        {
            Key = nameof(AutoLogin),
            DisplayLabel = "Auto Login",
            DisplayDescription = "Automatically login to the database on startup",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
            Value = AutoLogin,
        }
    };

    /// <summary>
    /// UpdateSettings does update of variables which are dependant on AdditionalOptions
    /// </summary>
    /// <param name="settings">The plugin settings.</param>
    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        Log.Info("UpdateSettings", GetType());

        DatabasePath = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(DatabasePath))?.TextValue ?? string.Empty;
        KeePassXcPath = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(KeePassXcPath))?.TextValue ?? string.Empty;
        MasterPassword = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(MasterPassword))?.TextValue ?? string.Empty;
        UseMasterPassword = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(UseMasterPassword))?.Value ?? false;
    }



    private string? IconPath { get; set; }

    private bool Disposed { get; set; }


    /// <summary>
    /// Processes the user's query, searches for matching KeePassXC entries, and returns results.
    /// </summary>
    /// <param name="query">The user query containing the search term.</param>
    /// <returns>A list of results found in the KeePassXC database.</returns>
    public List<Result> Query(Query query)
    {
        Log.Info($"[KeePassXC] Query invoked with search: '{query.Search}'", GetType());

        var results = new List<Result>();
        var keepassxcOutput = "";
        var keepassxcError = "";
        var userSearch = query.Search?.Trim() ?? "";
        if (string.IsNullOrEmpty(userSearch))
        {
            Log.Info("[KeePassXC] Empty search term, returning all result list.", GetType());
            keepassxcOutput = ExecuteKeePassSearch("", out keepassxcError);
            ParseSearchResults(keepassxcOutput, userSearch, results);

            return results;
        }

        if (!ValidatePaths(results)) return results;

        keepassxcOutput = ExecuteKeePassSearch(userSearch, out keepassxcError);
        if (!string.IsNullOrEmpty(keepassxcError))
        {
            results.Add(new Result
                { Title = "Error in keepassxc-cli", SubTitle = keepassxcError });
            return results;
        }

        ParseSearchResults(keepassxcOutput, userSearch, results);

        return results;
    }


    /// <summary>
    /// Initialize the plugin with the given <see cref="PluginInitContext"/>.
    /// </summary>
    /// <param name="context">The <see cref="PluginInitContext"/> for this plugin.</param>
    public void Init(PluginInitContext context)
    {
        Log.Info("Init", GetType());
        // Context = context ?? throw new ArgumentNullException(nameof(context));
        // Context.API.ThemeChanged += OnThemeChanged;
        // UpdateIconPath(Context.API.GetCurrentTheme());
    }

    /// <summary>
    /// Returns a list of context menu actions for a given search result.
    /// </summary>
    /// <param name="selectedResult">The selected result from PowerToys Run.</param>
    /// <returns>A list of context menu actions.</returns>
    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        Log.Info($"[KeePassXC] LoadContextMenus for: '{selectedResult.Title}'", GetType());

        string entryName = selectedResult.ContextData as string;
        if (string.IsNullOrEmpty(entryName))
        {
            return new List<ContextMenuResult>();
        }

        return new List<ContextMenuResult>
        {
            // --- COPY Actions (Visible in UI) ---
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy Password (Ctrl + P)",
                Glyph = "\xE8C8", // Copy icon
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.P, // Ctrl + P for password
                AcceleratorModifiers = ModifierKeys.Control,
                Action = _ => RetrieveAndCopy("password", entryName)
            },
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy Username (Ctrl + U)",
                Glyph = "\xE77B", // User icon
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.U, // Ctrl + U for username
                AcceleratorModifiers = ModifierKeys.Control,
                Action = _ => RetrieveAndCopy("username", entryName)
            },
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy TOTP Code (Ctrl + O)",
                Glyph = "\xE823", // Clocks icon
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                AcceleratorKey = Key.O, // Ctrl + O for TOTP
                AcceleratorModifiers = ModifierKeys.Control,
                Action = _ => RetrieveAndCopy("totp", entryName)
            },

            // --- INSERT Actions (Hidden in UI, Triggered by Hotkeys) ---
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Insert Username: Ctrl + Shift + U", // Hidden
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Glyph = "\xE77B", // User icon
                AcceleratorKey = Key.U, // Ctrl + Shift + U for inserting username
                AcceleratorModifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Action = _ => RetrieveAndInsert("username", entryName)
            },
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Insert OTP-pin: Ctrl + Shift + O", // Hidden
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Glyph = "\xE823", // OTP icon
                AcceleratorKey = Key.O, // Ctrl + Shift + O for inserting OTP
                AcceleratorModifiers = ModifierKeys.Control | ModifierKeys.Shift,
                Action = _ => RetrieveAndInsert("otp", entryName)
            }
        };
    }


    /// <summary>
    /// Creates setting panel.
    /// Not Implemented due to redundancy. Method "AdditionalOptions" does all the work 
    /// </summary>
    public Control CreateSettingPanel() => throw new NotImplementedException();

    public void Dispose()
    {
        Log.Info("Dispose", GetType());

        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Wrapper method for <see cref="Dispose()"/> that dispose additional objects and events form the plugin itself.
    /// </summary>
    /// <param name="disposing">Indicate that the plugin is disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (Disposed || !disposing)
        {
            return;
        }

        Disposed = true;
    }


    /// <summary>
    /// Retrieves a specified attribute (password, username, OTP) from a KeePassXC entry 
    /// and copies it to the clipboard.
    /// </summary>
    /// <param name="attribute">The attribute to retrieve (e.g., "password", "username", "otp").</param>
    /// <param name="entryName">The name of the entry to retrieve data from.</param>
    /// <returns>True if the operation was successful, otherwise false.</returns>
    private bool RetrieveAndCopy(string attribute, string entryName)
    {
        Log.Info($"[KeePassXC] Retrieving {attribute} for: '{entryName}'", GetType());
        var totpFlag = attribute == "otp" ? "--totp" : "";
        var psi = new ProcessStartInfo
        {
            FileName = KeePassXcPath,
            Arguments = $"show -s -q -a {attribute} {totpFlag} \"{DatabasePath}\" \"{entryName}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        string output;
        try
        {
            using (var process = Process.Start(psi))
            {
                if (process == null)
                {
                    throw new Exception("[KeePassXC] Failed to start keepassxc-cli process.");
                }

                // Вводим пароль, если нужно
                if (UseMasterPassword && !string.IsNullOrEmpty(MasterPassword))
                {
                    process.StandardInput.WriteLine(MasterPassword);
                    process.StandardInput.Flush();
                    process.StandardInput.Close();
                }

                output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
            }

            if (string.IsNullOrEmpty(output))
            {
                Log.Warn($"[KeePassXC] No output received for {attribute}.", GetType());
                return false;
            }

            // Копируем в буфер обмена
            Clipboard.SetText(output);
            Log.Info($"[KeePassXC] {attribute} copied to clipboard.", GetType());
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception($"[KeePassXC] Exception retrieving {attribute}: {ex.Message}", ex, ex.GetType());
            return false;
        }
    }


    /// <summary>
    /// Retrieves a specific attribute (e.g., "password", "username", "otp") from a KeePassXC entry
    /// and inserts it into the active window by simulating keyboard input using InputSimulator.
    /// </summary>
    /// <param name="attribute">The attribute to retrieve (for example, "password").</param>
    /// <param name="entryName">The name of the KeePassXC entry from which to retrieve the data.</param>
    /// <returns>True if the insertion was successful; otherwise, false.</returns>
    private bool RetrieveAndInsert(string attribute, string entryName)
    {
        Log.Info($"[KeePassXC] Retrieving {attribute} for insertion: '{entryName}'", GetType());

        var psi = new ProcessStartInfo
        {
            FileName = KeePassXcPath,
            Arguments = $"show -s -q -a {attribute} \"{DatabasePath}\" \"{entryName}\"",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        string output;
        try
        {
            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new Exception("[KeePassXC] Failed to start keepassxc-cli process.");

                // Pass the master password via stdin if required
                if (UseMasterPassword && !string.IsNullOrEmpty(MasterPassword))
                {
                    process.StandardInput.WriteLine(MasterPassword);
                    process.StandardInput.Flush();
                    process.StandardInput.Close();
                }

                output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
            }

            if (string.IsNullOrEmpty(output))
            {
                Log.Warn($"[KeePassXC] No output received for {attribute}.", GetType());
                return false;
            }

            // Insert the retrieved text into the active window
            KeyboardSender.SendTextToActiveWindow(output);
            Log.Info($"[KeePassXC] {attribute} inserted into active window.", GetType());
            return true;
        }
        catch (Exception ex)
        {
            Log.Exception($"[KeePassXC] Exception inserting {attribute}: {ex.Message}", ex, ex.GetType());
            return false;
        }
    }
    

    /// <summary>
    /// Inserts the given text into the currently focused control using UI Automation.
    /// Falls back to the clipboard method if the control does not support direct value setting.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    private void InsertTextUsingUiAutomation(string text)
    {
        var focusedElement = AutomationElement.FocusedElement;
        if (focusedElement != null)
        {
            // Try to get the ValuePattern from the focused element
            if (focusedElement.TryGetCurrentPattern(ValuePattern.Pattern, out object pattern))
            {
                ((ValuePattern)pattern).SetValue(text);
                Log.Info("Inserted text via UI Automation.", GetType());
            }
            else
            {
                // If the element does not support ValuePattern, fallback to clipboard method
                Log.Warn("Focused element does not support ValuePattern. Do nothing", GetType());
            }
        }
        else
        {
            Log.Warn("No focused element found. Cannot insert text.", GetType());
        }
    }


    /// <summary>
    /// Validates if the KeePassXC CLI path and database file exist.
    /// </summary>
    /// <param name="results">A list where error messages will be added if validation fails.</param>
    /// <returns>True if both paths are valid; otherwise, false.</returns>
    private bool ValidatePaths(List<Result> results)
    {
        if (string.IsNullOrEmpty(KeePassXcPath) || !File.Exists(KeePassXcPath))
        {
            Log.Warn($"[KeePassXC] KeePassXC CLI not found: '{KeePassXcPath}'", GetType());
            results.Add(new Result
            {
                Title = "keepassxc-cli not found", SubTitle = "Check KeePassXCPath in plugin settings",
            });
            return false;
        }

        if (string.IsNullOrEmpty(DatabasePath) || !File.Exists(DatabasePath))
        {
            Log.Warn($"[KeePassXC] KeePass database not found: '{DatabasePath}'", GetType());
            results.Add(new Result
            {
                Title = "KeePassXC database not found", SubTitle = "Check DatabasePath in plugin settings",
            });
            return false;
        }

        return true;
    }


    /// <summary>
    /// Executes the KeePassXC CLI search command and returns the output.
    /// </summary>
    /// <param name="searchTerm">The term to search for in the database.</param>
    /// <param name="error">An output parameter that captures any error messages.</param>
    /// <returns>The CLI output containing search results, or an empty string if an error occurs.</returns>
    private string ExecuteKeePassSearch(string searchTerm, out string error)
    {
        var arguments = $"search --quiet \"{DatabasePath}\" \"{searchTerm}\"";
        Log.Info($"[KeePassXC] Executing '{KeePassXcPath}' with args: {arguments}", GetType());

        var psi = new ProcessStartInfo
        {
            FileName = KeePassXcPath,
            Arguments = arguments,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            EnvironmentVariables =
            {
                ["LANG"] = "en_US.UTF-8",
                ["LC_ALL"] = "en_US.UTF-8"
            }
        };

        var output = "";
        error = "";

        try
        {
            using (var process = Process.Start(psi))
            {
                if (process == null)
                    throw new Exception("[KeePassXC] Failed to start keepassxc-cli process.");

                // Pass master password via stdin if required
                if (UseMasterPassword && !string.IsNullOrEmpty(MasterPassword))
                {
                    process.StandardInput.WriteLine(MasterPassword);
                    process.StandardInput.Flush();
                    process.StandardInput.Close();
                }

                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
            }

            Log.Info($"[KeePassXC] CLI output:\n{output}", GetType());
            if (!string.IsNullOrEmpty(error))
                Log.Warn($"[KeePassXC] CLI error: {error}", GetType());
        }
        catch (Exception ex)
        {
            Log.Exception($"[KeePassXC] Exception running keepassxc-cli: {ex.Message}", ex, ex.GetType());
            error = ex.Message;
        }

        return output.Trim();
    }

    /// <summary>
    /// Parses the search results from the CLI output and adds them to the results list.
    /// </summary>
    /// <param name="output">The CLI output containing search results.</param>
    /// <param name="searchTerm">The original search term provided by the user.</param>
    /// <param name="results">The list to which parsed results will be added.</param>
    private void ParseSearchResults(string output, string searchTerm, List<Result> results)
    {
        if (string.IsNullOrEmpty(output))
        {
            Log.Warn("[KeePassXC] No results from search", GetType());
            results.Add(new Result
                { Title = "No entries found", SubTitle = $"No results for '{searchTerm}'" });
            return;
        }

        var entryLines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();

        Log.Debug($"[KeePassXC] Parsed {entryLines.Count} result(s) from CLI output.", GetType());

        foreach (var entryLine in entryLines)
        {
            Log.Debug($"[KeePassXC] Creating result for entry: '{entryLine}'", GetType());

            results.Add(new Result
            {
                Title = entryLine,
                SubTitle = "KeePassXC Entry",
                Action = _ =>
                {
                    RetrieveAndInsert("password", entryLine);
                    return true;
                },
                ContextData = entryLine
            });
        }

        if (!results.Any())
        {
            results.Add(new Result
            {
                Title = "No matching entries", SubTitle = $"No results for '{searchTerm}'" 
            });
        }
    }
}




public static class NativeMethods
{
    public const int INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public int type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;         // виртуальный код клавиши (0 для UNICODE ввода)
        public ushort wScan;       // скан-код символа
        public uint dwFlags;       // флаги (KEYEVENTF_UNICODE для нажатия, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP для отпускания)
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

public static class KeyboardSender
{
    /// <summary>
    /// Sends the specified text to the active window by simulating keystrokes using the native WinAPI SendInput.
    /// </summary>
    /// <param name="text">The text to send.</param>
    public static void SendTextToActiveWindow(string text)
    {
        // Для каждого символа создаём два INPUT: для нажатия и для отпускания клавиши.
        int inputsCount = text.Length * 2;
        NativeMethods.INPUT[] inputs = new NativeMethods.INPUT[inputsCount];
        int index = 0;

        foreach (char ch in text)
        {
            // Создаем INPUT для нажатия клавиши (keydown)
            inputs[index] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0, // 0, так как мы используем UNICODE ввод
                        wScan = ch,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            index++;

            // Создаем INPUT для отпускания клавиши (keyup)
            inputs[index] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = ch,
                        dwFlags = NativeMethods.KEYEVENTF_UNICODE | NativeMethods.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            index++;
        }

        // Отправляем все события за один вызов
        uint sent = NativeMethods.SendInput((uint)inputsCount, inputs, Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        if (sent != inputsCount)
        {
            throw new Exception("SendInput failed with error: " + Marshal.GetLastWin32Error());
        }
    }
}
