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

    public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption> { };
    private string KeePassXCPath => "C:\\Program Files\\KeePassXC\\keepassxc-cli.exe"; // Пример пути
    private string DatabasePath => "\"C:\\Users\\shameoff\\Sync\\backups\\Passwords.kdbx\"";
    private bool UseMasterPassword => false;
    private string MasterPassword => "511234"; // Если пустой — будет запрашиваться
    private bool AutoLogin => true;
    
    // public IEnumerable<PluginAdditionalOption> AdditionalOptions => new List<PluginAdditionalOption>
    // {
    //     new()
    //     {
    //         Key = nameof(KeePassCliPath),
    //         DisplayLabel = "KeePass CLI Path",
    //         DisplayDescription = "Path to the keepasscli executable",
    //         PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
    //         TextValue = KeePassCliPath,
    //     },
    //     new()
    //     {
    //         Key = nameof(DatabasePath),
    //         DisplayLabel = "Database Path",
    //         DisplayDescription = "Path to the KeePass database file (.kdbx)",
    //         PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
    //         TextValue = DatabasePath,
    //     },
    //     new()
    //     {
    //         Key = nameof(UseMasterPassword),
    //         DisplayLabel = "Use Master Password",
    //         DisplayDescription = "Enable to enter master password manually",
    //         PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
    //         Value = UseMasterPassword,
    //     },
    //     new()
    //     {
    //         Key = nameof(MasterPassword),
    //         DisplayLabel = "Master Password",
    //         DisplayDescription = "Master password for the database (leave empty for prompt)",
    //         PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Textbox,
    //         TextValue = MasterPassword,
    //     },
    //     new()
    //     {
    //         Key = nameof(AutoLogin),
    //         DisplayLabel = "Auto Login",
    //         DisplayDescription = "Automatically login to the database on startup",
    //         PluginOptionType = PluginAdditionalOption.AdditionalOptionType.Checkbox,
    //         Value = AutoLogin,
    //     }
    // };
        
    // public string KeePassCliPath { get; set; }
    // public string DatabasePath { get; set; }
    // public bool UseMasterPassword { get; set; }
    // public string MasterPassword { get; set; }
    // public bool AutoLogin { get; set; }

    
    
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
        Log.Info("Query: " + query.Search, GetType());

         var results = new List<Result>();

        // 1. Проверяем, введён ли пользовательский запрос (например, "keepass" + текст).
        //    Если он пустой (только "keepass"), тогда мы показываем ВСЕ записи.
        //    А если пользователь хочет ещё как-то фильтровать, можно учесть query.Search.
        string userSearch = query.Search?.Trim() ?? "";

        // 2. Убеждаемся, что путь к CLI и базе указан
        if (string.IsNullOrEmpty(KeePassXCPath) || !File.Exists(KeePassXCPath))
        {
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
            results.Add(new Result
            {
                Title = "KeePassXC database not found",
                SubTitle = "Check DatabasePath in plugin settings",
                IcoPath = "error.png"
            });
            return results;
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
                    throw new Exception("Failed to start keepassxc-cli process.");

                output = process.StandardOutput.ReadToEnd();
                error = process.StandardError.ReadToEnd();
                process.WaitForExit();
            }

            // 4. Проверяем ошибки
            if (!string.IsNullOrEmpty(error))
            {
                // Иногда keepassxc-cli пишет предупреждения в stderr, 
                // можно проверять код возврата, но для простоты тут считаем, что если есть stderr — это ошибка
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

            // 7. Фильтруем или сортируем
            //    - Сортируем по алфавиту
            //    - Если пользователь ввёл что-то после "keepass", 
            //      можем фильтровать (например, line.Contains(userSearch, StringComparison.OrdinalIgnoreCase)).
            if (!string.IsNullOrEmpty(userSearch))
            {
                lines = lines.Where(line => 
                    line.IndexOf(userSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();
            }

            lines.Sort(StringComparer.OrdinalIgnoreCase);

            // 8. Превращаем каждую строку в результат
            foreach (var line in lines)
            {
                // Можно разобрать группу/запись более подробно.
                // Здесь Title — это полное имя (путь) записи, 
                // SubTitle можно сделать статичным или добавлять деталь.
                results.Add(new Result
                {
                    Title = line,
                    SubTitle = "KeePassXC Entry",
                    IcoPath = "Images\\key.png", // или свой путь к иконке
                    // Можно добавить Action, чтобы при клике скопировать пароль или открыть запись:
                    Action = _ =>
                    {
                        // Для получения пароля понадобится ещё раз вызвать keepassxc-cli:
                        // keepassxc-cli show "DatabasePath" -p "MasterPassword" "line"
                        // Но учтите, что passwordArg может «утечь» в логи, 
                        // поэтому решайте, безопасно ли это.
                        // Ниже — пример копирования в буфер, но не гарантирует, что всё сработает.
                        
                        //Clipboard.SetText(password);
                        // И т.д.
                        return true;
                    }
                });
            }

            // Если после фильтрации нет строк — сообщаем
            if (!results.Any())
            {
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
            results.Add(new Result
            {
                Title = "Exception running keepassxc-cli",
                SubTitle = ex.Message,
                IcoPath = "error.png"
            });
        }

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
            return new List<ContextMenuResult> {
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
            return new List<ContextMenuResult> {
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

        return new List<ContextMenuResult>{};
    }

    /// <summary>
    /// Creates setting panel.
    /// </summary>
    /// <returns>The control.</returns>
    /// <exception cref="NotImplementedException">method is not implemented.</exception>
    public Control CreateSettingPanel() => throw new NotImplementedException();

    /// <summary>
    /// Updates settings.
    /// </summary>
    /// <param name="settings">The plugin settings.</param>
    public void UpdateSettings(PowerLauncherPluginSettings settings)
    {
        Log.Info("UpdateSettings", GetType());

        // CountSpaces = settings.AdditionalOptions.SingleOrDefault(x => x.Key == nameof(CountSpaces))?.Value ?? false; // TODO Удалить
    }

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

    private void UpdateIconPath(Theme theme) => IconPath = theme == Theme.Light || theme == Theme.HighContrastWhite ? Context?.CurrentPluginMetadata.IcoPathLight : Context?.CurrentPluginMetadata.IcoPathDark;

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