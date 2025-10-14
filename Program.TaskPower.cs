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

        void InitPower() {
            var blocks = Util.GetBlocks<IMyPowerProducer>();
            PowerProducersPower = new GridPower {
                MaxOutput = () => blocks.Sum(b => b.Enabled ? b.MaxOutput : 0),
                CurrentOutput = () => blocks.Sum(b => b.CurrentOutput)
            };
            PowerResult.MaxPowerPercent = 100;

            _PowerConsumptionTask.Restart();

            if (_power) _PowerTask.Pause(AllWheels.Count() == 0);
            _PowerConsumptionTask.Pause(AllWheels.Count() == 0);
        }

        struct PowerTaskResult
        {
            public float Power;
            public float WheelMaxPower;
            public float GridMaxPower;
            public float MaxPowerPercent;
        }
        PowerTaskResult PowerResult;

        IEnumerable PowerConsumptionTask() {
            var myWheels = MyWheels;
            var wheelPower = myWheels.Concat(SubWheels).Sum(w => w.MaxPower);
            float passivePower = 0;

            while (true) {
                if (Speed < 0.1) passivePower = PowerProducersPower.CurrentOutput();
                var vehicleMaxPower = PowerProducersPower.MaxOutput();
                var powerMaxPercent = MathHelper.Clamp((vehicleMaxPower - passivePower) * 100 / wheelPower, 0, 100);

                PowerResult.WheelMaxPower = (float)wheelPower;
                PowerResult.GridMaxPower = vehicleMaxPower;
                PowerResult.MaxPowerPercent = (float)powerMaxPercent;

                yield return null;
            }
        }

        IEnumerable PowerTask() {
            var PID = new PID(_pidPower);

            while (true) {
                var maxSpeed = MyWheels.First().SpeedLimit * 0.9f;
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var targetSpeed = Cruise ? CruiseSpeed : maxSpeed * Math.Abs(ForwardBackward);
                var error = (targetSpeed - Speed * 3.6) * 100 / maxSpeed;
                var power = MathHelper.Clamp(PID.Signal(error, dt), 5, PowerResult.MaxPowerPercent);

                PowerResult.Power = (float)power;
                yield return null;
            }
        }
    }
}
