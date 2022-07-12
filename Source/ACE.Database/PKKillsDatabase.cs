using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

using log4net;

using ACE.Database.Entity;
using ACE.Database.Models.PKKills;
using ACE.Entity.Enum;
using ACE.Entity.Enum.Properties;

namespace ACE.Database
{
    public class PKKillsDatabase
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public bool Exists(bool retryUntilFound)
        {
            var config = Common.ConfigManager.Config.MySql.PKKills;

            for (; ; )
            {
                using (var context = new PKKillsDbContext())
                {
                    if (((RelationalDatabaseCreator)context.Database.GetService<IDatabaseCreator>()).Exists())
                    {
                        log.Debug($"[DATABASE] Successfully connected to {config.Database} database on {config.Host}:{config.Port}.");
                        return true;
                    }
                }

                log.Error($"[DATABASE] Attempting to reconnect to {config.Database} database on {config.Host}:{config.Port} in 5 seconds...");

                if (retryUntilFound)
                    Thread.Sleep(5000);
                else
                    return false;
            }
        }

        public Kill CreateKill(uint victimId, uint killerId, uint? victimMonarchId, uint? killerMonarchId)
        {
            var kill = new Kill();

            try
            {
                kill.VictimId = victimId;
                kill.KillerId = killerId;
                kill.VictimMonarchId = victimMonarchId;
                kill.KillerMonarchId = killerMonarchId;
                kill.KillDateTime = DateTime.Now;

                using (var context = new PKKillsDbContext())
                {
                    context.Kills.Add(kill);
                    context.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                log.Error($"Exception in CreateKill saving kill data to PKKills DB. Ex: {ex}");
            }

            return kill;
        }

    }
}
