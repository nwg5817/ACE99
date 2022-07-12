using System;
using System.Collections.Generic;

namespace ACE.Database.Models.PKKills
{
    public partial class Kill
    {
        public uint Id { get; set; }
        public uint KillerId { get; set; }
        public uint VictimId { get; set; }

        public uint? KillerMonarchId { get; set; }

        public uint? VictimMonarchId { get; set; }

        public DateTime KillDateTime { get; set; }
    }
}
