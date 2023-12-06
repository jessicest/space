
bool _broken = false;

IMyShipConnector _dockingPort;
IMyShipController _remoteControl;
IMyBlockGroup _allThrusters;
List<IMyBatteryBlock> _batteries = new List<>();
List<IMyCargoContainer> _cargo = new List<>();

IMyWaypointInfo _mine;
IMyWaypointInfo _home;

interface Action {
    void Begin();
    bool Step();
    Action End();
}

class Fly implements Action {
    public const IMyWaypoint _target;
    public const float _desiredCloseness;
    public const bool _beFast;
    public const bool _towardMine;

    Fly(bool _towardMine, IMyWaypoint target, float desiredCloseness, bool beFast) {
        _target = target;
        _desiredCloseness = desiredCloseness;
        _beFast = beFast;
    }

    void Begin() {
        _remoteControl.AddWaypoint(_target);
        _remoteControl.SetDockingMode(!beFast);
        _remoteControl.SetCollisionAvoidance(beFast);
        _remoteControl.SetAutopilotEnabled(true);
        _remoteControl.FlightMode = FlightMode.OneWay;
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }

    bool Step() {
        return MeIsNear(Target, DesiredCloseness);
    }

    Action End() {
        _remoteControl.ClearWaypoints();

        var nextTarget = _towardMine ? _mine : _home;

        if(_target == nextTarget) {
            if(_beFast) {
                return new Fly(_towardMine, nextTarget, 0.0f, false);
            } else {
                return new Dock(_towardMine);
            }
        } else {
            return new Fly(_towardMine, nextTarget, 50.0f, true);
        }
    }
}

class Dock implements Action {
    public const bool _towardMine;

    public Dock(bool towardMine) {
        _towardMine = towardMine;
    }

    void Begin() {
        SetFastness(false);
        _remoteControl.SetDockingMode(true);
        _remoteControl.SetCollisionAvoidance(false);
        _remoteControl.SetAutopilotEnabled(true);
        _remoteControl.FlightMode = FlightMode.OneWay;
        DockingPort.Enabled = true;
        flyDown();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
    }

    bool Step() {
        return _dockingPort.Status == MyShipConnectorStatus.Unconnected; // when it becomes Connectable (or weirdly, if it becomes Connected), we end this action
    }

    Action End() {
        _dockingPort.Connect();
        _remoteControl.SetHandbrake(true);
        _remoteControl.SetAutopilotEnabled(false);
        return new SitAtDockingPort(_towardMine);
    }
}

class SitAtDockingPort implements Action {
    public const bool _atMine;
    public const List<IMyShipDrill> _drills;

    SitAtDockingPort() {
        IMyBlockGroup drillsGroup = GridTerminalSystem.GetBlockGroupWithName("Downer drills");
        _drills = new List<>();
        if(drillsGroup != null) {
            drillsGroup.GetBlocks(_drills);
            _atMine = true;
        } else {
            _atMine = false;
        }
    }

    void Begin() {
        foreach (var thruster in _allThrusters) {
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
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
    }

    bool Step() {
        if(_atMine) {
            return _cargo.gg < 1.0f;
        } else {
            return _bags > 0.0f && _power < 1.0f;
        }
    }

    Action End() {
        foreach (var thruster in _allThrusters) {
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
        if(_bags < 0.2f) {
            return new Fly(true, _home, 0.0f, false);
        }
    }
}

// returns between 0.0f and 1.0f -- the amount of cargo space that's full in our ship
private float GetCargoPercentage() {
    todo;
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
        Runtime.UpdateFrequency = UpdateFrequency.Update100;
        _remoteControl = (IMyRemoteControl) LoadBlock("Drone remote control");
        _dockingPort = LoadBlock("Drone connector");
        GridTerminalSystem.GetBlocksOfType(_cargo, block => block.IsSameConstructAs(Me) && block.HasInventory());
    } catch(Exception e) {
        Echo(e.Message);
        _broken = true;
        Runtime.UpdateFrequency = 0;
    }
}

public void Main(string argument, UpdateType updateSource)
{
    if _broken {
        return;
    }

    if(_action == null) {
        if(_dockingPort.IsConnected()) {
            _action = new SitAtDockingPort();
        } else {
            Echo("plz help me get to a docking port and then reboot me thx");
            Runtime.UpdateFrequency = 0;
            _broken = true;
        }
    }

    if(!_action.Step()) {
        _action = _action.End();
        _action.Begin();
    }
}
