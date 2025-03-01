using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
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
    public string Description => "PowerToys KeePassXC plugin description";


    private PluginInitContext? _context;

    public string KeePassXCPath { get; set; }
    public string DatabasePath { get; set; }
    public bool UseMasterPassword { get; set; }
    public string MasterPassword { get; set; }
    public bool AutoLogin { get; set; }

    /// <summary>
    /// AdditionalOptions describes options which can be set from the settings menu. 
    /// </summary>
    public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
    {
        new()
        {
            Key = nameof(KeePassXCPath),
            DisplayLabel = "KeePass CLI Path",
            DisplayDescription = "Path to the KeePassXC CLI executable",
            PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
            TextValue = KeePassXCPath,
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
        KeePassXCPath = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(KeePassXCPath))?.TextValue ?? string.Empty;
        MasterPassword = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(MasterPassword))?.TextValue ?? string.Empty;
        UseMasterPassword = settings.AdditionalOptions
            .SingleOrDefault(x => x.Key == nameof(UseMasterPassword))?.Value ?? false;
    }


    private PluginInitContext? Context { get; set; }

    private string? IconPath { get; set; }

    private bool Disposed { get; set; }


/// <summary>
/// Return a filtered list, based on the given query.
/// </summary>
/// <param name="query">The query to filter the list.</param>
/// <returns>A filtered list, can be empty when nothing was found.</returns>
public List<Result> Query(Query query)
{
    // Логируем входящий запрос
    Log.Info($"[KeePassXC] Query invoked with search: '{query.Search}'", GetType());

    var results = new List<Result>();

    // 1. Проверяем, введён ли пользовательский запрос
    string userSearch = query.Search?.Trim() ?? "";
    if (string.IsNullOrEmpty(userSearch))
    {
        Log.Warn("[KeePassXC] Empty search term, returning empty result list.", GetType());
        return results; // Не ищем, если пользователь ничего не ввел
    }

    // 2. Проверяем пути к CLI и БД
    if (string.IsNullOrEmpty(KeePassXCPath) || !File.Exists(KeePassXCPath))
    {
        Log.Warn($"[KeePassXC] KeePassXC CLI not found: '{KeePassXCPath}'", GetType());
        results.Add(new Result
        {
            Title = "keepassxc-cli not found",
            SubTitle = "Check KeePassXCPath in plugin settings",
            IcoPath = "error.png"
        });
        return results;
    }

    if (string.IsNullOrEmpty(DatabasePath) || !File.Exists(DatabasePath))
    {
        Log.Warn($"[KeePassXC] KeePass database not found: '{DatabasePath}'", GetType());
        results.Add(new Result
        {
            Title = "KeePassXC database not found",
            SubTitle = "Check DatabasePath in plugin settings",
            IcoPath = "error.png"
        });
        return results;
    }

    // 3. Формируем команду keepassxc-cli search
    string arguments = $"search --quiet \"{DatabasePath}\" \"{userSearch}\"";
    Log.Info($"[KeePassXC] Executing '{KeePassXCPath}' with args: {arguments}", GetType());

    var psi = new ProcessStartInfo
    {
        FileName = KeePassXCPath,
        Arguments = arguments,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = System.Text.Encoding.UTF8,
        StandardErrorEncoding = System.Text.Encoding.UTF8
    };

    psi.EnvironmentVariables["LANG"] = "en_US.UTF-8";
    psi.EnvironmentVariables["LC_ALL"] = "en_US.UTF-8";

    string output;
    string error;
    try
    {
        using (var process = Process.Start(psi))
        {
            if (process == null)
            {
                throw new Exception("[KeePassXC] Failed to start keepassxc-cli process.");
            }

            // Передаем пароль через stdin
            if (UseMasterPassword && !string.IsNullOrEmpty(MasterPassword))
            {
                process.StandardInput.WriteLine(MasterPassword);
                process.StandardInput.Flush();
                process.StandardInput.Close();
            }

            // Читаем stdout и stderr
            output = process.StandardOutput.ReadToEnd();
            error = process.StandardError.ReadToEnd();
            process.WaitForExit();
        }

        Log.Info($"[KeePassXC] CLI output:\n{output}", GetType());
        Log.Info($"[KeePassXC] CLI error:\n{error}", GetType());

        // 4. Проверяем ошибки
        if (!string.IsNullOrEmpty(error))
        {
            Log.Warn($"[KeePassXC] keepassxc-cli stderr: '{error}'", GetType());
            results.Add(new Result
            {
                Title = "Error in keepassxc-cli",
                SubTitle = error,
                IcoPath = "error.png"
            });
            return results;
        }

        // 5. Проверяем, есть ли результаты
        if (string.IsNullOrEmpty(output))
        {
            Log.Warn("[KeePassXC] No results from search", GetType());
            results.Add(new Result
            {
                Title = "No entries found",
                SubTitle = $"No results for '{userSearch}'",
                IcoPath = "warning.png"
            });
            return results;
        }

        // 6. Парсим строки: каждая строка содержит имя записи (без групп)
        var lines = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToList();

        Log.Info($"[KeePassXC] Parsed {lines.Count} result(s) from CLI output.", GetType());

        // 7. Создаем результаты
        foreach (var line in lines)
        {
            Log.Info($"[KeePassXC] Creating result for entry: '{line}'", GetType());

            results.Add(new Result
            {
                Title = line,
                SubTitle = "KeePassXC Entry",
                IcoPath = "Images\\key.png",
                Action = _ =>
                {
                    Log.Info($"[KeePassXC] User selected entry: '{line}'", GetType());
                    RetrieveAndCopy("password", line);
                    return true;
                },
                ContextData = line // Сохраняем имя записи для контекстного меню
            });
        }

        // 8. Если после поиска ничего не найдено
        if (!results.Any())
        {
            Log.Info($"[KeePassXC] No matching entries for '{userSearch}'", GetType());
            results.Add(new Result
            {
                Title = "No matching entries",
                SubTitle = $"No results for '{userSearch}'",
                IcoPath = "warning.png"
            });
        }
    }
    catch (Exception ex)
    {
        Log.Exception($"[KeePassXC] Exception during search: {ex.Message}", ex, ex.GetType());
        results.Add(new Result
        {
            Title = "Exception running keepassxc-cli",
            SubTitle = ex.Message,
            IcoPath = "error.png"
        });
    }

    // Логируем финальное количество результатов
    Log.Info($"[KeePassXC] Returning {results.Count} result(s) to PowerToys.", GetType());

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
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy Password",
                Glyph = "\xE8C8", // Иконка копирования
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ => RetrieveAndCopy("password", entryName)
            },
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy Username",
                Glyph = "\xE77B", // Иконка пользователя
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ => RetrieveAndCopy("username", entryName)
            },
            new ContextMenuResult
            {
                PluginName = Name,
                Title = "Copy OTP Code",
                Glyph = "\xE8D6", // Иконка OTP
                FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                Action = _ => RetrieveAndCopy("otp", entryName)
            }
        };
    }


    /// <summary>
    /// Creates setting panel.
    /// </summary>
    /// <returns>The control.</returns>
    /// <exception cref="NotImplementedException">method is not implemented.</exception>
    public Control CreateSettingPanel() => throw new NotImplementedException();

    /// <inheritdoc/>
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

        if (Context?.API != null)
        {
            Context.API.ThemeChanged -= OnThemeChanged;
        }

        Disposed = true;
    }

    private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite
        ? Context?.CurrentPluginMetadata.IcoPathLight
        : Context?.CurrentPluginMetadata.IcoPathDark;

    private void OnThemeChanged(Theme currentTheme, Theme newTheme) => UpdateIconPath(newTheme);

    private static bool CopyToClipboard(string? value)
    {
        if (value != null)
        {
            Clipboard.SetText(value);
        }

        return true;
    }
    
    
    private bool RetrieveAndCopy(string attribute, string entryName)
    {
        Log.Info($"[KeePassXC] Retrieving {attribute} for: '{entryName}'", GetType());

        var psi = new ProcessStartInfo
        {
            FileName = KeePassXCPath,
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

}