﻿using StarfallAfterlife.Bridge.Profiles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StarfallAfterlife.Bridge.Networking.MgrHandlers
{
    public class MgrHandler
    {
        public SfaGameProfile Profile { get; }

        public MgrHandler(SfaGameProfile profile)
        {
            Profile = profile;
        }
    }
}
