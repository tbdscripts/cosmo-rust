using System.Collections.Generic;
using Newtonsoft.Json;
using Oxide.Core.Database;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Cosmo", "Mex de Loo", "0.1.0")]
    public class Cosmo : RustPlugin
    {
        private PluginConfig _config;
        private Core.MySql.Libraries.MySql _mySql;
        private Connection _connection;

        void Init()
        {
            _config = Config.ReadObject<PluginConfig>();
            _mySql = GetLibrary<Core.MySql.Libraries.MySql>();

            var creds = _config.Database;
            _connection = _mySql.OpenDb(
                creds.Host, creds.Port, creds.Database, creds.Username, creds.Password,
                this
            );

            timer.Every(_config.CheckTime, () =>
            {
                CheckForPendingOrders();
                CheckForExpiredActions();
            });
        }

        private void CheckForPendingOrders()
        {
            DebugPuts("Checking for pending orders");
            Sql sql = Sql.Builder.Append(SqlQueries.CheckForPendingOrders, _config.ServerId);

            _mySql.Query(sql, _connection, list =>
            {
                if (list == null)
                {
                    return;
                }

                foreach (var entry in list)
                {
                    HandlePendingOrder(entry);
                }
            });
        }

        private void HandlePendingOrder(Dictionary<string, object> order)
        {
            var receiver = order["receiver"].ToString();
            if (receiver == null) return;
            
            var player = covalence.Players.FindPlayerById(receiver);
            if (player == null || !player.IsConnected) return;

            var sql = Sql.Builder.Append(SqlQueries.GetPendingOrderActions, order["id"].ToString());
            
            _mySql.Query(sql, _connection, actions =>
            {
                var hasFailed = false;

                foreach (var action in actions)
                {
                    var success = HandlePendingAction(player, action);
                    if (!success) hasFailed = true;
                }

                if (!hasFailed)
                {
                    _mySql.Update(Sql.Builder.Append(SqlQueries.DeliverOrder, order["id"]), _connection);
                }
            });
        }

        private bool HandlePendingAction(IPlayer player, Dictionary<string, object> action)
        {
            if (action["receiver"].ToString() != player.Id) return false;
            if (action["name"].ToString() != "console_command") return false;

            var data = JsonConvert.DeserializeObject<ConsoleCommandData>(action["data"].ToString());
            var cmdArr = data.cmd
                .Replace(":sid64", player.Id)
                .Replace(":nick", player.Name);

            Server.Command(cmdArr);

            var sql = Sql.Builder.Append(SqlQueries.CompleteAction, action["id"]);
            _mySql.Update(sql, _connection);
            
            return true;
        }

        private void CheckForExpiredActions()
        {
            DebugPuts("Checking for expired actions.");
            Sql sql = Sql.Builder.Append(SqlQueries.CheckForExpiredActions, _config.ServerId);

            _mySql.Query(sql, _connection, list =>
            {
                if (list == null)
                {
                    return;
                }

                foreach (var action in list)
                {
                    HandleExpiredAction(action);
                }
            });
        }

        private void HandleExpiredAction(Dictionary<string, object> action)
        {
            if (action["name"].ToString() != "console_command") return;
            
            var receiver = action["receiver"].ToString();
            if (receiver == null) return;

            var player = covalence.Players.FindPlayerById(receiver);
            if (player == null || !player.IsConnected) return;

            var data = JsonConvert.DeserializeObject<ConsoleCommandData>(action["data"].ToString());
            var cmdArr = data.expire_cmd
                .Replace(":sid64", player.Id)
                .Replace(":nick", player.Name);

            Server.Command(cmdArr);

            _mySql.Update(Sql.Builder.Append(SqlQueries.ExpireAction, action["id"]), _connection);
        }
        
        private void DebugPuts(string format, params object[] args)
        {
            if (_config.DebugMode)
            {
                Puts(format, args);
            }
        }

        #region structs
        
        private struct ConsoleCommandData
        {
            public string cmd, expire_cmd;
        }
        
        #endregion
        
        #region sql

        private class SqlQueries
        {
            public static string CheckForPendingOrders = @"
                SELECT o.id, o.receiver, p.name AS `package_name`
                FROM orders o
                    INNER JOIN packages p on o.package_id = p.id
                WHERE status = 'waiting_for_package'
                    AND @0 IN (SELECT packageable_id
                                FROM packageables pkg
                                WHERE packageable_type = 'App\\Models\\Index\\Server'
                                AND o.package_id = pkg.package_id);
            ";
            
            public static string CheckForExpiredActions = @"
                SELECT a.id, a.name, a.receiver, a.data
                FROM `actions` a
                  INNER JOIN orders o ON a.order_id = o.id
                WHERE `expires_at` < CURRENT_TIMESTAMP
                  AND `active` = TRUE
                  AND @0 IN (SELECT packageable_id
                              FROM packageables pkg
                              WHERE packageable_type = 'App\\Models\\Index\\Server'
                              AND o.package_id = pkg.package_id);
            ";

            public static string GetPendingOrderActions = @"
                SELECT `id`, `name`, `data`, `receiver`
                FROM `actions`
                WHERE `delivered_at` IS NULL AND `order_id` = @0 AND `active` = FALSE;            
            ";

            public static string CompleteAction = @"
                UPDATE `actions`
                SET `delivered_at` = CURRENT_TIMESTAMP(), `active` = TRUE
                WHERE `id` = @0;
            ";

            public static string DeliverOrder = @"
                UPDATE `orders`
                SET `status` = 'delivered'
                WHERE `id` = @0
            ";

            public static string ExpireAction = @"
                UPDATE `actions`
                SET `active` = FALSE
                WHERE `id` = @0;    
            ";
        }
        
        #endregion

        #region config

        protected override void LoadDefaultConfig()
        {
            Puts("Creating a new configuration file.");

            Config.WriteObject(PluginConfig.GetDefaultConfig(), true);
        }

        private class PluginConfig
        {
            public struct DatabaseCredentials
            {
                public string Host, Username, Password, Database;
                public int Port;
            }

            public bool DebugMode { get; set; }
            
            public int ServerId { get; set; }
            public float CheckTime { get; set; }
            public DatabaseCredentials Database { get; set; }

            public static PluginConfig GetDefaultConfig()
            {
                DatabaseCredentials creds;
                creds.Host = "localhost";
                creds.Username = "root";
                creds.Password = "Password1";
                creds.Database = "cosmo";
                creds.Port = 3306;

                return new PluginConfig
                {
                    DebugMode = false,
                    ServerId = 1,
                    CheckTime = 20f,
                    Database = creds
                };
            }
        }

        #endregion
    }
}
