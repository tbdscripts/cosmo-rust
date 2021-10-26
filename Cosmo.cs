using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Oxide.Plugins
{
    [Info("Cosmo", "Mex de Loo <mex@zeodev.cc>", "1.0.0")]
    public class Cosmo : RustPlugin
    {
        #region Struct Definitions
        public struct PluginConfiguration
        {
            public string InstanceUrl { get; set; }
            public string ServerToken { get; set; }
            public int FetchInterval { get; set; }

            public static PluginConfiguration Default => new
            {
                InstanceUrl = "https://your.domain",
                ServerToken = "your secret server token",
                FetchInterval = 60
            };
        }

        public struct Order
        {
            public ulong Id { get; set; }
            public string Receiver { get; set; }
            public string PackageName { get; set; }

#nullable enable
            public List<Action> Actions { get; set; }
#nullable disable
        }

        public struct Action
        {
            public ulong Id { get; set; }
            public string Receiver { get; set; }
            public string Name { get; set; }
            public JsonElement Data { get; set; }

#nullable enable
            public Order Order { get; set; }
#nullable disable
        }

        public struct PendingStoreResponse
        {
            [JsonPropertyName("orders")] public List<Order> PendingOrders { get; set; }
            [JsonPropertyName("actions")] public List<Action> ExpiredActions { get; set; }
        }

        public struct ConsoleCommandData
        {
            [JsonPropertyName("cmd")] public string Command { get; set; }
            [JsonPropertyName("expire_cmd")] public string ExpireCommand { get; set; }
        }
        #endregion

        #region Initialization
        private void Init()
        {
            LoadConfig();
            StartStoreTimer();
        }
        #endregion

        #region Configuration Setup
        public PluginConfiguration Configuration { get; private set; }

        private void LoadConfig() {
            Configuration = Config.ReadObject<PluginConfiguration>();
        }

        protected override void LoadDefaultConfig() => Config.WriteObject(PluginConfiguration.Default, true);

        private void SaveConfig() => Config.WriteObject(Configuration, true);
        #endregion

        #region Http Requests
        private void MakeApiCall<T>(RequestMethod method, string endpoint, int expectedStatus = 200, Action<T> callback)
        {
            var url = Configuration.InstanceUrl.TrimRight('/') + "/api/game" + endpoint.TrimLeft('/');

            webrequest.Enqueue(url, null, (code, rawBody) => 
            {
                if (code != expectedStatus)
                {
                    throw new System.Exception("Something went wrong.");
                    return;
                }

                var body = JsonSerializer.Deserialize<T>(rawBody);

                callback(body);
            }, this, method);
        }
        #endregion

        #region Handling
        private void StartStoreTimer() => timer.Every(Configuration.FetchInterval, CheckForPending);

        public void CheckForPending()
        {
            MakeApiCall<PendingStoreResponse>(RequestMethod.GET, "store/pending", 200, result =>
            {
                foreach (var order in result.PendingOrders)
                {
                    HandlePendingOrder(order);
                }

                foreach (var action in result.ExpiredActions)
                {
                    HandleExpiredAction(action);
                }
            });
        }

        private void HandlePendingOrder(Order order)
        {
            if (order.Actions is null) return;

            LogWarning("Handling pending order: " + order.Id);

            var player = covalence.Players.FindPlayerById(order.Receiver);
            if (player is null || !player.IsConnected) return;

            var success = true;
            foreach (var action in order.Actions)
            {
                var result = HandlePendingAction(action, player, order);
                if (!result)
                {
                    success = false;
                }
            }

            if (!success) return;

            // deliver order
        }

        private bool HandlePendingAction(Action action, IPlayer player, Order order)
        {
            if (action.Receiver != order.Receiver || action.Name != "console_command") return false;

            var data = JsonSerializer.Deserialize<ConsoleCommandData>(order.Data.GetRawText());
            var command = data.Command
                .Replace(":sid64", player.Id)
                .Replace(":nick", player.Name);

            Server.Command(command);

            // complete action

            return true;
        }

        private void HandleExpiredAction(Action action)
        {
            if (action.Name != "console_command") return;

            var player = covalence.Players.FindPlayerById(action.Receiver);
            if (player is null || !player.IsConnected) return;

            var data = JsonSerializer.Deserialize<ConsoleCommandData>(action.Data);
            var command = data.Command
                .Replace(":sid64", player.Id)
                .Replace(":nick", player.Name);

            Server.Command(command);

            // expire action
        }
        #endregion
    }
}