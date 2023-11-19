﻿using StarfallAfterlife.Bridge.Database;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StarfallAfterlife.Bridge.Server.Discovery.AI
{
    public class ScanAction : AIAction
    {
        public TimeSpan Duration { get; }

        public ScanAction(TimeSpan duration)
        {
            Duration = duration;
        }

        public override void Start()
        {
            base.Start();
            Fleet?.AddEffect(new()
            {
                Duration = (float)Duration.TotalSeconds,
                Logic = GameplayEffectType.Scan
            });
        }

        public override void Update()
        {
            base.Update();

            if (TotalTime > Duration)
                State = AIActionState.Completed;
        }

        public override void Stop()
        {
            base.Stop();

            if (State is AIActionState.Started)
                Fleet?.AddEffect(new() { Duration = 0, Logic = GameplayEffectType.Scan });
        }
    }
}
