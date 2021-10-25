using Oxide.Core.Libraries;
using System.Threading.Tasks;
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
            public object Data { get; set; }

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
        private Task<T> MakeApiCall<T>(RequestMethod method, string endpoint, int expectedStatus = 200)
        {
            return Task.Run(() => 
            {
                var completionSource = new TaskCompletionSource<T>();
                var url = Configuration.InstanceUrl.TrimRight('/') + "/api/game" + endpoint.TrimLeft('/');

                webrequest.Enqueue(url, null, (code, rawBody) => 
                {
                    if (code != expectedStatus)
                    {
                        completionSource.SetException(new System.Exception("Invalid stuf."));
                        return;
                    }

                    var body = JsonSerializer.Deserialize<T>(rawBody);

                    completionSource.SetResult(body);
                }, this, method);

                return completionSource.Task;
            });
        }

        private Task<PendingStoreResponse> GetPendingStore()
        {
            return await MakeApiCall<PendingStoreResponse>(RequestMethod.GET, "store/pending");
        }
        #endregion

        #region Handling
        private void StartStoreTimer() => 
            timer.Every(Configuration.FetchInterval, async () => await CheckForPending());

        public async Task CheckForPending()
        {
            var pending = await GetPendingStore();

            foreach (var order in pending.PendingOrders)
            {
                if (order.Actions is null) continue;

                LogWarning("Handling pending order: " + order.Id);

                foreach (var action in order.Actions)
                {
                    if (action.Receiver != order.Receiver || action.Name != "console_command") continue;

                    var data = (ConsoleCommandData)action.Data;

                    await CompleteAction(action.Id);
                }

                await DeliverOrder(order.Id);
            }

            foreach (var action in pending.ExpiredActions)
            {

            }
        }
        #endregion
    }
}