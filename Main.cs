using System.Collections.Generic;
using Wox.Plugin;

namespace Community.PowerToys.Run.Plugin.KeePassXC
{
    public class Main : IPlugin
    {
        public static string PluginID => "181EC9C9CE7E777243213D37E4ED8D7D";
        public string Name => "PingPong";
        public string Description => "PowerToys Ping Pong plugin description";


        private PluginInitContext? _context;

        
        // Инициализация плагина
        public void Init(PluginInitContext context)
        {
            _context = context;
        }

        // Реакция на запросы
        public List<Result> Query(Query query)
        {
            var results = new List<Result>();

            // Проверяем, что запрос - это "ping"
            if (query.Search.ToLower() == "ping")
            {
                results.Add(new Result
                {
                    Title = "pong",
                    SubTitle = "This is a reply from Ping Pong Plugin.",
                    IcoPath = "Images\\icon.png", // Путь к иконке (можно заменить на свой)
                    Action = _ =>
                    {
                        // Сообщение в консоли, что результат выбран
                        _context.API.ShowMsg("You selected 'pong'.", "Ping Pong Plugin");
                        return true;
                    }
                });
            }
            else
            {
                // Если запрос не совпадает, ничего не возвращаем
                results.Add(new Result
                {
                    Title = "Unknown command",
                    SubTitle = "Try typing 'ping'.",
                    IcoPath = "Images\\error.png"
                });
            }

            return results;
        }
    }
}