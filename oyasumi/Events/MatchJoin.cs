﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using oyasumi.Enums;
using oyasumi.IO;
using oyasumi.Layouts;
using oyasumi.Managers;
using oyasumi.Objects;

namespace oyasumi.Events
{
    public class MatchJoin
    {
        [Packet(PacketType.ClientMultiMatchJoin)]
        public static void Handle(Packet p, Presence pr)
        {
            var reader = new SerializationReader(new MemoryStream(p.Data));
            var matchId = reader.ReadInt32();
            var password = reader.ReadString();

            if (MatchManager.Matches.TryGetValue(matchId, out var match))
                MatchManager.JoinMatch(pr, match, password);
            else
                pr.MatchJoinFail();
        }
    }
}