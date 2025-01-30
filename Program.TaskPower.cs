using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;
using VRageRender;
using VRageRender.Animations;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        struct PowerTaskResult
        {
            public float Power;
            public float WheelMaxPower;
            public float GridMaxPower;
            public float MaxPowerPercent;

        }
        IEnumerable<PowerTaskResult> PowerTask()
        {
            var gridProps = this.gridProps;
            var ini = Config;
            var wheels = MyWheels;
            var PID = new PID(ini["PIDPower"].ToString("10/0/0/0"));
            var wheelPower = wheels.Concat(SubWheels).Sum(w => w.MaxPower);
            float passivePower = 0;
            while (ini.Equals(Config) && wheels.Equals(MyWheels))
            {
                var powerProducersPower = PowerProducersPower;
                double speed = gridProps.Speed;
                if (speed < 0.1)
                {
                    passivePower = powerProducersPower.CurrentOutput;
                }
                var vehicleMaxPower = powerProducersPower.MaxOutput;
                var powerMaxPercent = MathHelper.Clamp((vehicleMaxPower - passivePower) / wheelPower, 0, 1);
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var currentSpeedKmh = speed * 3.6;
                var targetSpeed = Cruise ? CruiseSpeed : (gridProps.ForwardBackward != 0 ? wheels.First().SpeedLimit : 0);
                var error = targetSpeed - currentSpeedKmh;
                var power = MathHelper.Clamp(PID.Signal(error, dt), 5, 100 * powerMaxPercent);
                yield return new PowerTaskResult
                {
                    Power = (float)power,
                    WheelMaxPower = (float)wheelPower,
                    GridMaxPower = vehicleMaxPower,
                    MaxPowerPercent = (float)powerMaxPercent
                };
            }
        }
    }
}