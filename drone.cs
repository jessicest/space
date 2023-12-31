
bool _broken = false;

IMyShipConnector _dockingPort;
IMyShipController _remoteControl;
List<IMyThrust> _thrusters;
List<IMyBatteryBlock> _batteries = new List<IMyBatteryBlock>();
List<IMyGyro> _gyros = new List<IMyGyro>();
List<IMyCargoContainer> _cargo = new List<IMyCargoContainer>();
List<Action> _plan = new List<Action>();

MyWaypointInfo _mine;
MyWaypointInfo _home;

interface Action {
    void Begin();
    bool Step();
    List<Action> void End();
}

class FlyToWaypoint implements Action {
    public const MyWaypoint _target;
    public const bool _beFast;

    FlyToWaypoint(MyWaypoint target, bool beFast) {
        _target = target;
        _beFast = beFast;
    }

    void Begin() {
        _remoteControl.SpeedLimit = 20.0f;
        _remoteControl.AddWaypoint(_target);
        _remoteControl.SetDockingMode(!beFast);
        _remoteControl.SetCollisionAvoidance(beFast);
        _remoteControl.SetAutopilotEnabled(true);
        _remoteControl.FlightMode = FlightMode.OneWay;
        _remoteControl.WaitForFreeWay = false;
    }

    bool Step() {
        float desiredCloseness;
        if(_beFast) {
            desiredCloseness = 50.0f;
        } else {
            desiredCloseness = 0.0f;
        }
        ReportStatus(String.Format("FlyToWaypoint: {0}, {1}", _target, _beFast));
        return Vector3.Distance(_dockingPort.Position, _target.Coords) <= desiredCloseness;
    }

    List<Action> End() {
        _remoteControl.ClearWaypoints();
        return new List<Action>();
    }
}

class Dock implements Action {
    public const bool _towardMine;

    public Dock(bool towardMine) {
        _towardMine = towardMine;
    }

    void Begin() {
        Runtime.UpdateFrequency |= UpdateFrequency.Update10;
        _remoteControl.FlightMode = FlightMode.OneWay;
        DockingPort.Enabled = true;
        _remoteControl.Direction = Direction.Down;
        _remoteControl.SpeedLimit = 1.0f;
        _remoteControl.SetDockingMode(true);
        _remoteControl.SetCollisionAvoidance(false);
        _remoteControl.SetAutopilotEnabled(true);
    }

    bool Step() {
        ReportStatus(String.Format("Dock: {0}", _towardMine);
        return _dockingPort.Status == MyShipConnectorStatus.Unconnected; // when it becomes Connectable (or weirdly, if it becomes Connected), we end this action and assume we're docked
    }

    List<Action> End() {
        Runtime.UpdateFrequency &= ~UpdateFrequency.Update10;

        _dockingPort.Connect();
        _remoteControl.SetHandbrake(true);
        _remoteControl.SetAutopilotEnabled(false);

        List<Action> plan = new List<Action>();
        plan.Append(new SitAtDockingPort(_towardMine));
        return plan;
    }
}

class SitAtDockingPort implements Action {
    public const bool _atMine;
    public const List<IMyShipDrill> _drills;

    SitAtDockingPort() {
        IMyBlockGroup drillsGroup = GridTerminalSystem.GetBlockGroupWithName("Downer drills");
        _drills = new List<IMyShipDrill>();
        if(drillsGroup != null) {
            drillsGroup.GetBlocks(_drills);
            _atMine = true;
        } else {
            _atMine = false;
        }
    }

    void Begin() {
        for(var gyro in _gyros) {
            gyro.GyroOverride = false;
        }

        foreach (var thruster in _thrusters) {
            thruster.Enabled = false;
        }

        if(_atMine) {
            foreach (var drill in _drills) {
                drill.Enabled = true;
            }
        } else {
            foreach (IMyBatteryBlock battery in _batteries) {
                battery.ChargeMode = ChargeMode.Recharge;
            }
        }
    }

    bool Step() {
        ReportStatus(String.Format("Dock: {0}, {1}", _atMine, _drills.Count);
        if(_atMine) {
            return CargoFullness < 0.9f;
        } else {
            return CargoFullness() > 0.01f && _power < 0.9f;
        }
    }

    List<Action> End() {
        for(var gyro in _gyros) {
            gyro.GyroOverride = true;
            gyro.Pitch = 0;
            gyro.Roll = 0;
            gyro.Yaw = 0;
        }

        foreach (var thruster in _thrusters) {
            thruster.Enabled = true;
        }

        foreach (var drill in _drills) {
            drill.Enabled = false;
        }

        foreach (IMyBatteryBlock battery in _batteries) {
            battery.ChargeMode = ChargeMode.Auto;
        }

        _remoteControl.SetHandbrake(false);
        _dockingPort.Disconnect();
        _dockingPort.Enabled = false;

        List<Action> plan = new List<Action>();
        if(_atMine) {
            plan.Append(new FlyToWaypoint(_mine, false));
            plan.Append(new FlyToWaypoint(_home, true));
            plan.Append(new FlyToWaypoint(_home, false));
            plan.Append(new Dock(false);
        } else {
            plan.Append(new FlyToWaypoint(_home, false));
            plan.Append(new FlyToWaypoint(_mine, true));
            plan.Append(new FlyToWaypoint(_mine, false));
            plan.Append(new Dock(true);
        }
        return plan;
    }
}

// returns between 0.0f and 1.0f to represent the amount of cargo space that's full in our ship
private float CargoFullness() {
    long capacityMicrolitres = 0;
    long storedCargoMicrolitres = 0;

    for(var cargo in _cargo) {
        var inventory = cargo.GetInventory();
        capacityMicrolitres += inventory.MaxVolume.RawValue;
        storedCargoMicrolitres += inventory.CurrentVolume.RawValue;
    }

    return ((float)storedCargoMicrolitres) / ((float)capacityMicrolitres);
}

private void ReportStatus(string message) {
    Echo(message);
}

private void Breakdown(string message) {
    ReportStatus(message);
    _broken = true;
    Runtime.UpdateFrequency = 0;
}

private IMyTerminalBlock LoadBlock(string name) {
    var block = GridTerminalSystem.GetBlockWithName(name);
    if block == null {
        throw new Exception("Oh shit! I can't find block: '" + name + "'");
    }
    return block;
}

public Program()
{
    try {
        List<MyWaypointInfo> waypoints = new List<MyWaypointInfo>();
        MyWaypointInfo.FindAll(Me.CustomData, waypoints);
        if(waypoints.Count != 2) {
            throw new Exception("okay so you need to enter a home and a mine waypoint into custom data. Thanks!");
        }
        _home = waypoints[0];
        _mine = waypoints[1];

        Runtime.UpdateFrequency = UpdateFrequency.Update100;
        _remoteControl = (IMyRemoteControl) LoadBlock("Drone remote control");
        _dockingPort = (IMyShipConnector) LoadBlock("Drone connector");
        GridTerminalSystem.GetBlocksOfType(_cargo, block => block.IsSameConstructAs(Me) && block.HasInventory());
        GridTerminalSystem.GetBlocksOfType(_thrusters, block => block.IsSameConstructAs(Me));
        GridTerminalSystem.GetBlocksOfType(_batteries, block => block.IsSameConstructAs(Me));
        GridTerminalSystem.GetBlocksOfType(_gyros, block => block.IsSameConstructAs(Me));
    } catch(Exception e) {
        Breakdown(e.Message);
    }
}

public void Main(string argument, UpdateType updateSource)
{
    if _broken {
        return;
    }

    try {
        if(_plan.IsEmpty()) {
            if(_dockingPort.IsConnected()) {
                _plan.append(new SitAtDockingPort());
                _plan[0].Begin();
            } else {
                Breakdown("plz help me get to a docking port and then reboot me thx");
            }
        } else {
            var action = _plan[0];
            if(!action.Step()) {
                _plan.Remove(0);
                _plan.AddRange(action.End());
                if(!_plan.IsEmpty()) {
                    _plan[0].Begin();
                }
            }
        }
    } catch(Exception e) {
        Breakdown(e.Message);
    }
}
