using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript {
    partial class Program {
        public class Dock : Action {
            public readonly Program _program;
            public readonly bool _towardMine;
            public readonly IMyRemoteControl _remoteControl;
            public readonly IMyShipConnector _dockingPort;

            public Dock(Program program, bool towardMine, IMyRemoteControl remoteControl, IMyShipConnector dockingPort) {
                _program = program;
                _towardMine = towardMine;
                _remoteControl = remoteControl;
                _dockingPort = dockingPort;
            }

            public void Begin() {
                _program.Runtime.UpdateFrequency |= UpdateFrequency.Update10;
                _remoteControl.FlightMode = FlightMode.OneWay;
                _dockingPort.Enabled = true;
                _remoteControl.Direction = Base6Directions.Direction.Down;
                _remoteControl.SpeedLimit = 1.0f;
                _remoteControl.SetDockingMode(true);
                _remoteControl.SetCollisionAvoidance(false);
                _remoteControl.SetAutoPilotEnabled(true);
            }

            public bool Step() {
                _program.ReportStatus(String.Format("Dock: {0}", _towardMine));
                return _dockingPort.Status == MyShipConnectorStatus.Unconnected; // when it becomes Connectable (or weirdly, if it becomes Connected), we end this action and assume we're docked
            }

            public List<Action> End() {
                _program.Runtime.UpdateFrequency &= ~UpdateFrequency.Update10;

                _dockingPort.Connect();
                _remoteControl.SetAutoPilotEnabled(false);

                List<Action> plan = new List<Action>();
                plan.Add(new SitAtDockingPort(_program,_towardMine, _dockingPort, _remoteControl, _program._gyros, _program._cargo, _program._thrusters, _program._batteries));
                return plan;
            }
        }

    }
}
