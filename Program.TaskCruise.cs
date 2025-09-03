using Sandbox.ModAPI.Ingame;
using System;
using System.Collections;
using System.Linq;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        struct CruiseTaskResult
        {
            public float Propulsion;
        }

        bool Cruise = false;
        float CruiseSpeed = 0;

        CruiseTaskResult CruiseResult = new CruiseTaskResult();

        IEnumerable CruiseTask(float cruiseSpeed = -1, Func<bool> cruiseWhile = null)
        {
            if (Cruise) yield break;

            var pid = new PID(_pidCruise);

            Cruise = true;
            CruiseSpeed = cruiseSpeed > -1 ? cruiseSpeed : (float)(Speed * 3.6);
            cruiseWhile = cruiseWhile ?? (() => UpDown == 0 && !Controllers.MainController.HandBrake);

            while (cruiseWhile())
            {
                if (cruiseSpeed == -1)
                {
                    CruiseSpeed = MathHelper.Clamp(CruiseSpeed + (float)ForwardBackward * -5f, 5, MyWheels.First().SpeedLimit);
                }
                var dt = TaskManager.CurrentTaskLastRun.TotalSeconds;
                var currentSpeedKmh = Speed * 3.6;
                var targetSpeed = CruiseSpeed;
                var error = targetSpeed - currentSpeedKmh;
                var propulsion = MathHelper.Clamp(pid.Signal(error, dt), -1, 1);

                CruiseResult.Propulsion = (float)propulsion;

                yield return null;
            }
            Cruise = false;
            CruiseSpeed = 0;
            CruiseResult.Propulsion = 0;
        }
    }
}
