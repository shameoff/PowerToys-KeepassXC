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
        //    (например, "keepass" + текст).
        //    Если он пустой (только "keepass"), тогда мы показываем ВСЕ записи.
        //    А если пользователь хочет ещё как-то фильтровать, можно учесть query.Search.
        string userSearch = query.Search?.Trim() ?? "";
        Log.Info($"[KeePassXC] userSearch is: '{userSearch}'", GetType());

        // 2. Убеждаемся, что путь к CLI и базе указан
        if (string.IsNullOrEmpty(KeePassXCPath) || !File.Exists(KeePassXCPath))
        {
            Log.Warn($"[KeePassXC] KeePassXCPath is invalid or file does not exist: '{KeePassXCPath}'", GetType());
            results.Add(new Result
            {
                Title = "keepassxc-cli not found",
                SubTitle = "Check KeePassXCPath in plugin settings",
                IcoPath = "error.png"
            });
            return results;
        }
        else
        {
            Log.Info($"[KeePassXC] KeePassXC CLI found at: '{KeePassXCPath}'", GetType());
        }

        if (string.IsNullOrEmpty(DatabasePath) || !File.Exists(DatabasePath))
        {
            Log.Warn($"[KeePassXC] DatabasePath is invalid or file does not exist: '{DatabasePath}'", GetType());
            results.Add(new Result
            {
                Title = "KeePassXC database not found",
                SubTitle = "Check DatabasePath in plugin settings",
                IcoPath = "error.png"
            });
            return results;
        }
        else
        {
            Log.Info($"[KeePassXC] Using database path: '{DatabasePath}'", GetType());
        }

        // 3. Готовим аргументы для keepassxc-cli ls
        //    --recursive       - просмотреть записи рекурсивно во всех группах
        //    -p <пароль>       - если UseMasterPassword = true и пароль задан
        //    (иначе keepassxc-cli запросит его интерактивно, 
        //     что может не сработать под PowerToys, поэтому обычно лучше явно указывать)
        //    Можно добавить --show-protected, если хотите видеть защищенные поля
        //    Но учтите риски в логах/выводе.
        string passwordArg = (UseMasterPassword && !string.IsNullOrEmpty(MasterPassword))
            ? $"-p \"{MasterPassword}\""
            : "";

        string arguments = $"ls --recursive \"{DatabasePath}\" {passwordArg}";

        // Логируем построенную команду
        Log.Info($"[KeePassXC] Executing '{KeePassXCPath}' with args: {arguments}", GetType());

        var psi = new ProcessStartInfo
        {
            FileName = KeePassXCPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

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

                // Читаем весь stdout и stderr
                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
            }

            Log.Info($"[KeePassXC] CLI output:\n{output}", GetType());
            Log.Info($"[KeePassXC] CLI error:\n{error}", GetType());

            // 4. Проверяем ошибки
            if (!string.IsNullOrEmpty(error))
            {
                // Иногда keepassxc-cli пишет предупреждения в stderr,
                // можно проверять код возврата, но для простоты тут считаем, что если есть stderr — это ошибка
                Log.Warn($"[KeePassXC] keepassxc-cli stderr is not empty: '{error}'", GetType());

                results.Add(new Result
                {
                    Title = "Error in keepassxc-cli",
                    SubTitle = error,
                    IcoPath = "error.png"
                });
                return results;
            }

            // 5. Если вывод пуст — скорее всего нет записей или ошибка
            if (string.IsNullOrEmpty(output))
            {
                Log.Warn("[KeePassXC] keepassxc-cli returned empty output", GetType());

                results.Add(new Result
                {
                    Title = "No entries found",
                    SubTitle = "keepassxc-cli returned empty result",
                    IcoPath = "warning.png"
                });
                return results;
            }

            // 6. Парсим строки: каждая строка обычно содержит путь к записи или группе, например:
            //    "Internet/Gmail"
            //    "Internet/StackOverflow"
            //    Если есть группы, keepassxc-cli покажет их тоже.
            var lines = output
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .ToList();

            Log.Info($"[KeePassXC] Parsed {lines.Count} line(s) from CLI output.", GetType());

            // 7. Фильтруем или сортируем
            //    - Сортируем по алфавиту
            //    - Если пользователь ввёл что-то после "keepass", 
            //      можем фильтровать (например, line.Contains(userSearch, StringComparison.OrdinalIgnoreCase)).
            if (!string.IsNullOrEmpty(userSearch))
            {
                lines = lines
                    .Where(line => line.IndexOf(userSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

                Log.Info($"[KeePassXC] After filtering by '{userSearch}', {lines.Count} line(s) left.", GetType());
            }
            else
            {
                Log.Info($"[KeePassXC] No search filter applied; showing all entries.", GetType());
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);

            // 8. Превращаем каждую строку в результат
            foreach (var line in lines)
            {
                Log.Info($"[KeePassXC] Creating result for entry: '{line}'", GetType());

                results.Add(new Result
                {
                    Title = line,
                    SubTitle = "KeePassXC Entry",
                    IcoPath = "Images\\key.png", // или свой путь к иконке
                    Action = _ =>
                    {
                        // Логируем, что пользователь кликнул по записи
                        Log.Info($"[KeePassXC] User clicked entry: '{line}'", GetType());

                        // Для получения пароля понадобится ещё раз вызвать keepassxc-cli:
                        // keepassxc-cli show "DatabasePath" -p "MasterPassword" "line"
                        // Но учтите, что passwordArg может «утечь» в логи, 
                        // поэтому решайте, безопасно ли это.
                        // Ниже — пример копирования в буфер, но не гарантирует, что всё сработает.

                        //Clipboard.SetText(password);
                        // и т.д.
                        return true;
                    }
                });
            }

            // Если после фильтрации нет строк — сообщаем
            if (!results.Any())
            {
                Log.Info("[KeePassXC] No entries after filtering; adding 'No matching entries' result.", GetType());

                results.Add(new Result
                {
                    Title = "No matching entries",
                    SubTitle = $"for: {userSearch}",
                    IcoPath = "warning.png"
                });
            }
        }
        catch (Exception ex)
        {
            Log.Exception("[KeePassXC] Exception during request: " + ex.Message, ex, ex.GetType());
            results.Add(new Result
            {
                Title = "Exception running keepassxc-cli",
                SubTitle = ex.Message,
                IcoPath = "error.png"
            });
        }

        // Логируем финальное количество результатов, которое вернём в PowerToys
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

    /// <summary>
    /// Return a list context menu entries for a given <see cref="Result"/> (shown at the right side of the result).
    /// </summary>
    /// <param name="selectedResult">The <see cref="Result"/> for the list with context menu entries.</param>
    /// <returns>A list context menu entries.</returns>
    public List<ContextMenuResult> LoadContextMenus(Result selectedResult)
    {
        Log.Info("LoadContextMenus", GetType());

        if (selectedResult?.ContextData is (int words, TimeSpan transcription))
        {
            return new List<ContextMenuResult>
            {
                new ContextMenuResult
                {
                    PluginName = Name,
                    Title = "Copy (Enter)",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    Glyph = "\xE8C8", // Copy
                    AcceleratorKey = Key.Enter,
                    Action = _ => CopyToClipboard(words.ToString()),
                },
                new ContextMenuResult
                {
                    PluginName = Name,
                    Title = "Copy time (Ctrl+Enter)",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    Glyph = "\xE916", // Stopwatch
                    AcceleratorKey = Key.Enter,
                    AcceleratorModifiers = ModifierKeys.Control,
                    Action = _ => CopyToClipboard(transcription.ToString()),
                },
            };
        }

        if (selectedResult?.ContextData is int characters)
        {
            return new List<ContextMenuResult>
            {
                new ContextMenuResult
                {
                    PluginName = Name,
                    Title = "Copy (Enter)",
                    FontFamily = "Segoe Fluent Icons,Segoe MDL2 Assets",
                    Glyph = "\xE8C8", // Copy
                    AcceleratorKey = Key.Enter,
                    Action = _ => CopyToClipboard(characters.ToString()),
                },
            };
        }

        return new List<ContextMenuResult> { };
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
}