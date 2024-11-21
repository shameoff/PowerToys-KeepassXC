using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wox.Plugin;

namespace PowerToys_KeePassXC
{
    public class PluginMain : IPlugin
    {
        private PluginInitContext _context;

        // Конфигурация: Путь к KeePassXC CLI, базе данных и ключевому файлу
        private readonly string _keepassCliPath = @"C:\Program Files\KeePassXC\keepassxc-cli.exe"; // TODO Учесть, если утилита в другом месте. Может, использовать значение из Path? 
        private readonly string _databasePath = @"C:\Users\shameoff\Desktop\Passwords.kdbx"; // TODO Путь до БД стоит настраивать из интерфейса. Разобраться, как это делать
        private readonly string _dbPassword = "0000"; // TODO Извлекать как-то более безопасно из настроек для повышения безопасности
        private readonly string _keyFilePath = @"C:\Path\To\KeyFile.key"; // TODO Включить поддержку keyFile auth
        private IPlugin _pluginImplementation;

        public void Init(PluginInitContext context)
        {
            _context = context;
        }

        public string Name { get; }
        public string Description { get; }

        public List<Result> Query(Query query)
        {
            var results = new List<Result>();

            try
            {
                // Выполнить поиск записей в KeePassXC
                var entries = SearchDatabase(query.Search);

                foreach (var entry in entries)
                {
                    results.Add(new Result
                    {
                        Title = entry,
                        SubTitle = "Perform actions with this entry",
                        IcoPath = "Images\\icon.png", // Путь к иконке
                        Action = e =>
                        {
                            _context.API.ShowMsg($"Entry Selected: {entry}", "Action triggered");
                            return true;
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                results.Add(new Result
                {
                    Title = "Error",
                    SubTitle = ex.Message,
                    IcoPath = "Images\\error.png"
                });
            }

            return results;
        }

        private List<string> SearchDatabase(string searchQuery)
        {
            var results = new List<string>();

            // Формирование аргументов для keepassxc-cli
            var args = new List<string>
            {
                "search",
                _databasePath,
                searchQuery
            };

            if (!string.IsNullOrEmpty(_keyFilePath))
            {
                args.Add("-k");
                args.Add(_keyFilePath);
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _keepassCliPath,
                        Arguments = string.Join(" ", args),
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                // Ввод пароля через stdin
                using (var writer = process.StandardInput)
                {
                    writer.WriteLine(_dbPassword);
                }

                // Чтение вывода
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = process.StandardOutput.ReadLine();
                    if (!string.IsNullOrEmpty(line))
                    {
                        results.Add(line.Trim());
                    }
                }

                process.WaitForExit();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error executing KeePassXC CLI: {ex.Message}");
                throw;
            }

            return results;
        }
    }
}