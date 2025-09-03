using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Linq;
using System.Text;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {

        struct GridPower
        {
            public Func<float> MaxOutput;
            public Func<float> CurrentOutput;
        }
        GridPower PowerProducersPower;

        void InitPower()
        {
            var blocks = Util.GetBlocks<IMyPowerProducer>(b => b.IsSameConstructAs(Me));
            PowerProducersPower = new GridPower
            {
                MaxOutput = () => blocks.Sum(b => b.Enabled ? b.MaxOutput : 0),
                CurrentOutput = () => blocks.Sum(b => b.CurrentOutput)
            };
        }

        struct PowerTaskResult
        {
            public float Power;
            public float WheelMaxPower;
            public float GridMaxPower;
            public float MaxPowerPercent;
        }
        PowerTaskResult PowerResult;

        IEnumerable PowerTask()
        {
            var myWheels = MyWheels;
            var PID = new PID(_pidPower);
            var wheelPower = myWheels.Concat(SubWheels).Sum(w => w.MaxPower);
            float passivePower = 0;
            while (myWheels == MyWheels)
            {
                var powerProducersPower = PowerProducersPower;
                double speed = Speed;
                if (speed < 0.1) passivePower = powerProducersPower.CurrentOutput();
                var vehicleMaxPower = powerProducersPower.MaxOutput();
                var powerMaxPercent = MathHelper.Clamp((vehicleMaxPower - passivePower) / wheelPower, 0, 1);
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var targetSpeed = Cruise ? CruiseSpeed : (ForwardBackward != 0 ? MyWheels.First().SpeedLimit : 0);
                var error = targetSpeed - speed * 3.6;
                var power = MathHelper.Clamp(PID.Signal(error, dt), 5, 100 * powerMaxPercent);

                PowerResult.Power = (float)power;
                PowerResult.WheelMaxPower = (float)wheelPower;
                PowerResult.GridMaxPower = vehicleMaxPower;
                PowerResult.MaxPowerPercent = (float)powerMaxPercent;
                yield return null;
            }
        }
    }
}
